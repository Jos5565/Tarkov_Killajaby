using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.SceneManagement;
using Type = PartyProtocol.Type;
using Member = PartyProtocol.Member;

// 파티 (네트워크 버전 전용). 방장 PC가 UDP 서버가 되고, 파티원은 초대 코드로 접속한다. 외부 서버 없음.
// 씬이 바뀌어도 유지되므로 로비로 나가도 파티는 그대로다. 나가는 건 [파티 나가기]/[파티 닫기] 또는 앱 종료 때뿐.
//
// 방장
//   - 무작위 포트(49152~65535)로 UDP 서버를 열고, 공유기에 포트 열기를 요청(UPnP, 1시간 유지 · 자동 연장)
//   - 초대 코드 = (공인 IP, 내부 IP, 포트, 초대 비밀값, 10분 뒤 만료). 만료되면 새 코드로
//   - 참가 요청이 초대 키로 서명돼 있으면 파티원마다 다른 세션 키를 나눠 준다
//   - 레이드가 시작되면(로그) 모든 파티원의 키를 새로 바꾼다 (이전 키는 30초 유예)
//   - 서명이 틀린 패킷은 해석하지 않고 응답도 하지 않는다. 주소별 초당 횟수 제한, 인증 5회 실패 시 10분 무시
// 파티원
//   - 상태(로비/맵, 위치)가 바뀌면, 그리고 10초마다 방장에게 보낸다 (공유기 길 유지 + 끊김 감지)
//   - 방장은 모은 파티 전체 상태를 파티원 각자에게 그 사람 키로 보낸다
//
// IP · 초대 코드 · 키는 로그(Player.log)에 남기지 않는다.
public class PartySession : MonoBehaviour
{
    public static PartySession Instance { get; private set; }

    public enum Role { None, Starting, Hosting, Joining, Joined }

    public Role CurrentRole { get; private set; }
    public bool IsInParty => CurrentRole == Role.Hosting || CurrentRole == Role.Joined;
    public bool IsHost => CurrentRole == Role.Hosting;

    // 화면에 보여 줄 상태 한 줄 (예: "포트 자동 열기 성공")과 정상 여부
    public string StatusText { get; private set; } = "";
    public bool StatusOk { get; private set; } = true;

    // 방장: 지금 초대 코드 (만료되면 null)
    public string InviteCode => inviteKeys != null && DateTime.UtcNow < inviteExpiresUtc ? inviteCode : null;
    public DateTime InviteExpiresUtc => inviteExpiresUtc;
    public bool ReachableFromInternet { get; private set; }

    public ushort MyId { get; private set; }

    // 나를 포함한 파티 전체 (방장이 정한 색 · 닉네임 · 상태)
    public IReadOnlyList<Member> Members => members;

    // 파티 구성, 상태, 역할이 바뀌면
    public event Action Changed;

    public const int InviteMinutes = 10;
    const int PortMin = 49152, PortMax = 65535;
    const int LeaseSeconds = 3600;
    const float SendInterval = 10f;          // 상태/파티 정보를 이 간격으로 다시 보냄
    const float DisconnectAfter = 45f;       // 이만큼 소식이 없으면 연결 끊김
    const float RemoveAfter = 120f;          // 이만큼 지나면 목록에서 뺌 (방장)
    const float RekeyGrace = 30f;            // 키를 바꾼 뒤 이전 키를 받아 주는 시간
    const int PacketsPerSecond = 20;         // 주소별
    const int MaxFailures = 5;
    const float BanSeconds = 600f;
    const string NicknamePref = "Party.Nickname";

    public string Nickname
    {
        get => PartyProtocol.CleanNickname(PlayerPrefs.GetString(NicknamePref, ""));
        set
        {
            PlayerPrefs.SetString(NicknamePref, PartyProtocol.CleanNickname(value));
            PlayerPrefs.Save();
        }
    }

    // ---- 내부 상태 ----

    class Peer
    {
        public Member member;
        public IPEndPoint endpoint;
        public PartyCrypto.Keys keys, previousKeys;
        public ushort epoch, previousEpoch;
        public float previousUntil;
        public ulong lastSeqIn, seqOut;
        public float lastHeard;
        public byte[] helloNonce;
        public byte[] welcome;          // 같은 Hello가 다시 오면 그대로 다시 보냄
        public bool rekeyPending;
        public byte[] rekeyPacket;
    }

    class AddressState
    {
        public float windowStart;
        public int count;
        public int failures;
        public float bannedUntil;
    }

    readonly List<Member> members = new List<Member>();
    readonly ConcurrentQueue<(byte[] data, IPEndPoint from)> inbox = new ConcurrentQueue<(byte[], IPEndPoint)>();
    UdpClient udp;
    int localPort;

    // 방장
    readonly Dictionary<ushort, Peer> peers = new Dictionary<ushort, Peer>();
    readonly Dictionary<IPAddress, AddressState> addresses = new Dictionary<IPAddress, AddressState>();
    PartyCrypto.Keys inviteKeys;
    string inviteCode;
    DateTime inviteExpiresUtc;
    UpnpPortMapper.Gateway gateway;
    IPAddress publicAddress, localAddress;
    int mappedLease = -1;
    float nextLeaseRenew;
    ushort nextId = 2, currentEpoch = 1;
    bool partyDirty;
    float nextPartySend;

    // 파티원
    Peer host;                      // 방장 연결 (파티원일 때)
    readonly List<IPEndPoint> hostCandidates = new List<IPEndPoint>();
    byte[] joinNonce;
    float joinStarted, nextHello;
    float nextStateSend;

    // 내 상태
    PartyProtocol.MemberState myState;
    bool myStateDirty = true;
    WhereIAM whereIAM;
    float sceneEnteredAt, poseAt = -1f;

    // ---- 생성 ----

    public static PartySession Ensure()
    {
        if (Instance != null) return Instance;
        var go = new GameObject(nameof(PartySession));
        DontDestroyOnLoad(go);
        Instance = go.AddComponent<PartySession>();
        return Instance;
    }

    void OnEnable()
    {
        SceneManager.sceneLoaded += OnSceneLoaded;
        TarkovLogWatcher.RaidStarted += OnRaidStarted;
        OnSceneLoaded(SceneManager.GetActiveScene(), LoadSceneMode.Single);
    }

    void OnDisable()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        TarkovLogWatcher.RaidStarted -= OnRaidStarted;
        if (whereIAM != null) whereIAM.PoseChanged -= OnPoseChanged;
    }

    void OnApplicationQuit() => Leave(waitForRouter: true);

    void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (whereIAM != null) whereIAM.PoseChanged -= OnPoseChanged;
        whereIAM = FindAnyObjectByType<WhereIAM>(FindObjectsInactive.Include);
        if (whereIAM != null) whereIAM.PoseChanged += OnPoseChanged;
        sceneEnteredAt = Time.realtimeSinceStartup;
        poseAt = -1f;
        myStateDirty = true;
    }

    // 맵에 들어온 직후의 위치는 이전 맵에서 찍은 스크린샷일 수 있어 보내지 않는다
    void OnPoseChanged(WhereIAM w)
    {
        if (Time.realtimeSinceStartup - sceneEnteredAt < 2f) return;
        poseAt = Time.realtimeSinceStartup;
        myStateDirty = true;
    }

    // ---- 방장 ----

    public async void Host()
    {
        if (CurrentRole != Role.None) return;
        if (!OpenSocket(fixedPort: true))
        {
            SetStatus("UDP 포트를 열지 못했어요", false);
            return;
        }
        CurrentRole = Role.Starting;
        SetStatus("공유기 포트 여는 중...", true);

        UpnpPortMapper.Gateway found = await UpnpPortMapper.DiscoverAsync();
        if (CurrentRole != Role.Starting) return;   // 그 사이에 닫음
        gateway = found;
        localAddress = gateway?.localAddress ?? GuessLocalAddress();

        string status;
        bool ok;
        if (gateway == null && UpnpPortMapper.IsUsablePublic(localAddress))
        {
            // 공유기 없이 인터넷에 바로 연결된 PC (PC 주소가 곧 공인 IP): 포트를 열 필요 없이 바로 접속된다
            publicAddress = localAddress;
            ReachableFromInternet = true;
            status = "공유기 없이 바로 연결된 PC예요 — Windows 방화벽 창이 뜨면 '허용'해 주세요";
            ok = true;
        }
        else if (gateway == null)
        {
            status = "공유기 포트를 자동으로 열지 못했어요 (UPnP 꺼짐) — 같은 공유기 안에서만 참가할 수 있어요";
            ok = false;
        }
        else
        {
            UpnpPortMapper.Gateway g = gateway;
            int port = localPort;
            int lease = await UpnpPortMapper.AddPortMappingAsync(g, port, LeaseSeconds, "TarkovKilaJaby Party");
            IPAddress external = lease >= 0 ? await UpnpPortMapper.GetExternalAddressAsync(g) : null;
            if (CurrentRole != Role.Starting)
            {
                // 그 사이에 파티를 닫았으면 방금 연 포트를 바로 닫는다
                if (lease >= 0) _ = DeleteMapping(g, port);
                return;
            }
            gateway = g;
            mappedLease = lease;
            publicAddress = external;
            if (mappedLease < 0)
            {
                status = "공유기가 포트 열기를 거절했어요 — 같은 공유기 안에서만 참가할 수 있어요";
                ok = false;
            }
            else if (!UpnpPortMapper.IsUsablePublic(publicAddress))
            {
                status = "공유기가 한 번 더 연결된 환경(이중 NAT)이라 밖에서는 참가할 수 없어요";
                ok = false;
            }
            else
            {
                status = "포트 자동 열기 성공";
                ok = true;
                ReachableFromInternet = true;
                nextLeaseRenew = mappedLease > 0 ? Time.realtimeSinceStartup + mappedLease * 0.5f : float.MaxValue;
            }
        }
        Debug.Log("[Party] 파티 만듦 · " + (!ReachableFromInternet ? "같은 네트워크 전용"
            : gateway == null ? "공유기 없음 (바로 연결된 PC, 포트 열기 안 함)" : "공유기 포트 자동 열기 성공"));

        CurrentRole = Role.Hosting;
        MyId = 1;
        members.Clear();
        members.Add(new Member { id = 1, color = 0, isHost = true, nickname = Nickname, state = myState });
        NewInvite();
        SetStatus(status, ok);
    }

    // 새 초대 코드 (이전 코드로는 더 참가할 수 없다. 이미 들어온 사람은 그대로)
    public void NewInvite()
    {
        if (CurrentRole != Role.Hosting) return;
        byte[] secret = PartyCrypto.RandomBytes(16);
        inviteKeys = PartyCrypto.InviteKeys(secret);
        inviteExpiresUtc = DateTime.UtcNow.AddMinutes(InviteMinutes);
        inviteCode = PartyCrypto.EncodeInvite(new PartyCrypto.Invite
        {
            publicAddress = ReachableFromInternet ? publicAddress : IPAddress.Any,
            localAddress = localAddress ?? IPAddress.Any,
            port = localPort,
            secret = secret,
            expiresUtc = inviteExpiresUtc,
        });
        Changed?.Invoke();
    }

    public void Kick(ushort id)
    {
        if (!IsHost || !peers.TryGetValue(id, out Peer p)) return;
        SendBye(p, PartyProtocol.ByeReason.Kicked);
        RemovePeer(id);   // 세션 키가 사람마다 달라서, 내보낸 사람의 키만 버리면 된다
    }

    // 레이드가 시작되면 모든 파티원의 키를 바꾼다
    void OnRaidStarted(string map)
    {
        if (!IsHost || peers.Count == 0) return;
        currentEpoch++;
        foreach (Peer p in peers.Values) Rekey(p);
        Debug.Log($"[Party] 레이드 시작 → 보안 키 갱신 (세대 {currentEpoch})");
        Changed?.Invoke();
    }

    void Rekey(Peer p)
    {
        byte[] master = PartyCrypto.RandomBytes(PartyCrypto.KeySize);
        ushort newEpoch = currentEpoch;
        byte[] body = PartyProtocol.Body(w =>
        {
            w.Write(newEpoch);
            w.Write(master);
        });
        // 이전 키로 봉인해서 보낸 뒤 새 키로 바꾼다. 받았다는 신호(새 세대로 온 패킷)가 올 때까지 파티 정보와 함께 다시 보냄
        p.rekeyPacket = Seal(p, Type.Rekey, body);
        p.previousKeys = p.keys;
        p.previousEpoch = p.epoch;
        p.previousUntil = Time.realtimeSinceStartup + RekeyGrace;
        p.keys = new PartyCrypto.Keys(master);
        p.epoch = newEpoch;
        p.rekeyPending = true;
        Send(p.rekeyPacket, p.endpoint);
    }

    void HostReceive(byte[] packet, IPEndPoint from, PartyProtocol.Header h)
    {
        if (h.type == Type.Hello)
        {
            HandleHello(packet, from);
            return;
        }
        if (!peers.TryGetValue(h.memberId, out Peer p))
        {
            Fail(from);   // 없는 참가자 번호: 맞히기 시도로 본다
            return;
        }
        // 있는 참가자의 패킷이 안 열리면(키 교체를 놓침, 중복 도착) 버리기만 한다.
        // 차단까지 하면 같은 집(같은 IP)에서 함께 참가한 사람도 막힌다
        if (!TryOpen(p, packet, h, out byte[] body)) return;
        p.endpoint = from;   // 공유기가 포트를 바꿔도 따라간다 (서명이 맞은 뒤에만)
        p.lastHeard = Time.realtimeSinceStartup;
        if (!p.member.connected) partyDirty = true;
        p.member.connected = true;
        if (h.epoch == p.epoch && p.rekeyPending) p.rekeyPending = false;

        try
        {
            using (var r = new BinaryReader(new MemoryStream(body)))
            {
                if (h.type == Type.State && PartyProtocol.TryRead(r, out PartyProtocol.MemberState s))
                {
                    p.member.state = s;
                    p.member.receivedAt = Time.realtimeSinceStartup;
                    partyDirty = true;
                }
                else if (h.type == Type.Bye) RemovePeer(p.member.id);
            }
        }
        catch (Exception)
        {
            // 잘린 본문: 무시
        }
    }

    void HandleHello(byte[] packet, IPEndPoint from)
    {
        if (inviteKeys == null || DateTime.UtcNow >= inviteExpiresUtc || packet.Length < PartyProtocol.HelloSize)
        {
            Fail(from);
            return;
        }
        byte[] body = PartyCrypto.Open(inviteKeys, packet, PartyProtocol.HeaderSize);
        if (body == null || body.Length < 17)
        {
            Fail(from);
            return;
        }

        byte[] nonce = body.Take(16).ToArray();
        string nickname;
        try
        {
            using (var r = new BinaryReader(new MemoryStream(body, 16, body.Length - 16)))
                nickname = PartyProtocol.ReadString(r, PartyProtocol.MaxNickname);
        }
        catch (Exception)
        {
            nickname = null;
        }
        if (nickname == null)
        {
            Fail(from);
            return;
        }

        // 같은 요청을 다시 보낸 것(응답 유실)이면 같은 응답을 다시 보낸다
        Peer same = peers.Values.FirstOrDefault(x => x.helloNonce != null && x.helloNonce.SequenceEqual(nonce));
        if (same != null)
        {
            if (same.endpoint.Equals(from)) Send(same.welcome, from);
            return;
        }

        if (peers.Count >= PartyProtocol.MaxMembers - 1)
        {
            byte[] full = PartyCrypto.Seal(inviteKeys,
                PartyProtocol.WriteHeader(new PartyProtocol.Header { type = Type.Bye }),
                new[] { (byte)PartyProtocol.ByeReason.Full });
            Send(full, from);
            return;
        }

        var member = new Member
        {
            id = nextId++,
            color = NextColor(),
            nickname = PartyProtocol.CleanNickname(nickname),
            receivedAt = Time.realtimeSinceStartup,
        };
        byte[] master = PartyCrypto.RandomBytes(PartyCrypto.KeySize);
        var p = new Peer
        {
            member = member,
            endpoint = from,
            keys = new PartyCrypto.Keys(master),
            epoch = currentEpoch,
            lastHeard = Time.realtimeSinceStartup,
            helloNonce = nonce,
        };
        byte[] welcomeBody = PartyProtocol.Body(w =>
        {
            w.Write(nonce);
            w.Write(member.id);
            w.Write(member.color);
            w.Write(currentEpoch);
            w.Write(master);
        });
        p.welcome = PartyCrypto.Seal(inviteKeys,
            PartyProtocol.WriteHeader(new PartyProtocol.Header { type = Type.Welcome, memberId = member.id, epoch = currentEpoch }),
            welcomeBody);
        peers.Add(member.id, p);
        members.Add(member);
        Send(p.welcome, from);
        partyDirty = true;
        Debug.Log($"[Party] 참가: {member.nickname}");
        Changed?.Invoke();
    }

    byte NextColor()
    {
        for (byte c = 0; c < 8; c++)
            if (members.All(m => m.color != c)) return c;
        return 0;
    }

    void RemovePeer(ushort id)
    {
        if (!peers.Remove(id)) return;
        members.RemoveAll(m => m.id == id);
        partyDirty = true;
        Changed?.Invoke();
    }

    void HostUpdate(float now)
    {
        // 연결 끊김 / 목록에서 빼기
        foreach (Peer p in peers.Values.ToList())
        {
            float silent = now - p.lastHeard;
            if (silent > RemoveAfter) RemovePeer(p.member.id);
            else if (silent > DisconnectAfter && p.member.connected)
            {
                p.member.connected = false;
                partyDirty = true;
                Changed?.Invoke();
            }
            if (p.previousKeys != null && now > p.previousUntil) p.previousKeys = null;
        }

        // 내 상태는 바로 반영
        if (myStateDirty)
        {
            myStateDirty = false;
            members[0].state = myState;
            members[0].nickname = Nickname;
            members[0].receivedAt = now;
            partyDirty = true;
        }

        if ((partyDirty && now >= nextPartySend - SendInterval + 0.3f) || now >= nextPartySend)
        {
            partyDirty = false;
            nextPartySend = now + SendInterval;
            byte[] body = PartyProtocol.WriteParty(Snapshot(now));
            foreach (Peer p in peers.Values)
            {
                if (p.rekeyPending && p.previousKeys != null) Send(p.rekeyPacket, p.endpoint);
                Send(Seal(p, Type.Party, body), p.endpoint);
            }
            Changed?.Invoke();
        }

        // 공유기 포트 유지 시간 연장
        if (gateway != null && mappedLease > 0 && now >= nextLeaseRenew)
        {
            nextLeaseRenew = now + mappedLease * 0.5f;
            _ = UpnpPortMapper.AddPortMappingAsync(gateway, localPort, LeaseSeconds, "TarkovKilaJaby Party");
        }
    }

    // 보낼 파티 정보: 위치 경과 시간을 지금 기준으로 맞춘다
    List<Member> Snapshot(float now)
    {
        return members.Select(m => new Member
        {
            id = m.id,
            color = m.color,
            isHost = m.isHost,
            connected = m.connected,
            nickname = m.nickname,
            state = WithAge(m.state, m.state.positionAge + (uint)Mathf.Max(0f, now - m.receivedAt)),
        }).ToList();
    }

    static PartyProtocol.MemberState WithAge(PartyProtocol.MemberState s, uint age)
    {
        s.positionAge = age;
        return s;
    }

    void Fail(IPEndPoint from)
    {
        AddressState a = Address(from.Address);
        if (++a.failures >= MaxFailures) a.bannedUntil = Time.realtimeSinceStartup + BanSeconds;
    }

    AddressState Address(IPAddress ip)
    {
        if (!addresses.TryGetValue(ip, out AddressState a))
        {
            if (addresses.Count > 1000) addresses.Clear();   // 기억할 주소 수 제한
            addresses.Add(ip, a = new AddressState());
        }
        return a;
    }

    // 주소별 횟수 제한 / 차단 중이면 false
    bool Allow(IPEndPoint from, float now)
    {
        AddressState a = Address(from.Address);
        if (now < a.bannedUntil) return false;
        if (now - a.windowStart >= 1f)
        {
            a.windowStart = now;
            a.count = 0;
        }
        return ++a.count <= PacketsPerSecond;
    }

    // ---- 파티원 ----

    public void Join(string code)
    {
        if (CurrentRole != Role.None) return;
        if (!PartyCrypto.TryDecodeInvite(code, out PartyCrypto.Invite invite))
        {
            SetStatus("초대 코드가 올바르지 않아요 (전체를 복사했는지 확인해 주세요)", false);
            return;
        }
        if (DateTime.UtcNow >= invite.expiresUtc)
        {
            SetStatus("초대 코드가 만료됐어요 — 방장에게 새 코드를 받아 주세요", false);
            return;
        }
        if (!OpenSocket(fixedPort: false))
        {
            SetStatus("UDP 포트를 열지 못했어요", false);
            return;
        }

        inviteKeys = PartyCrypto.InviteKeys(invite.secret);
        hostCandidates.Clear();
        // 같은 공유기 안이면 내부 주소로 먼저 붙는다
        if (UpnpPortMapper.IsPrivate(invite.localAddress)) hostCandidates.Add(new IPEndPoint(invite.localAddress, invite.port));
        if (UpnpPortMapper.IsUsablePublic(invite.publicAddress)) hostCandidates.Add(new IPEndPoint(invite.publicAddress, invite.port));
        if (hostCandidates.Count == 0)
        {
            SetStatus("초대 코드에 연결할 주소가 없어요", false);
            CloseSocket();
            return;
        }

        joinNonce = PartyCrypto.RandomBytes(16);
        joinStarted = Time.realtimeSinceStartup;
        nextHello = 0f;
        CurrentRole = Role.Joining;
        SetStatus("방장에게 연결하는 중...", true);
    }

    void SendHello()
    {
        string nick = Nickname;
        byte[] body = PartyProtocol.Body(w =>
        {
            w.Write(joinNonce);
            PartyProtocol.WriteString(w, nick, PartyProtocol.MaxNickname);
        });
        // 응답(Welcome)보다 크게 채운다: 남의 주소로 위장한 요청으로 증폭 공격을 못 하게
        int padding = Mathf.Max(0, PartyProtocol.HelloSize - PartyProtocol.HeaderSize - 16 - PartyCrypto.TagSize - body.Length - 16);
        Array.Resize(ref body, body.Length + padding);
        byte[] packet = PartyCrypto.Seal(inviteKeys, PartyProtocol.WriteHeader(new PartyProtocol.Header { type = Type.Hello }), body);
        foreach (IPEndPoint ep in hostCandidates) Send(packet, ep);
    }

    void ClientReceive(byte[] packet, IPEndPoint from, PartyProtocol.Header h)
    {
        if (CurrentRole == Role.Joining)
        {
            if (!hostCandidates.Contains(from) || inviteKeys == null) return;
            byte[] body = PartyCrypto.Open(inviteKeys, packet, PartyProtocol.HeaderSize);
            if (body == null) return;

            if (h.type == Type.Bye && body.Length == 1 && body[0] == (byte)PartyProtocol.ByeReason.Full)
            {
                ResetAll();
                SetStatus("파티가 가득 찼어요 (최대 5명)", false);
                return;
            }
            if (h.type != Type.Welcome || body.Length < 16 + 2 + 1 + 2 + PartyCrypto.KeySize) return;
            if (!body.Take(16).SequenceEqual(joinNonce)) return;

            using (var r = new BinaryReader(new MemoryStream(body, 16, body.Length - 16)))
            {
                MyId = r.ReadUInt16();
                r.ReadByte();   // 색 (파티 정보로 다시 받음)
                ushort epoch = r.ReadUInt16();
                byte[] master = r.ReadBytes(PartyCrypto.KeySize);
                host = new Peer
                {
                    endpoint = from,
                    keys = new PartyCrypto.Keys(master),
                    epoch = epoch,
                    lastHeard = Time.realtimeSinceStartup,
                    seqOut = 0,
                };
            }
            inviteKeys = null;   // 이제 필요 없음
            CurrentRole = Role.Joined;
            myStateDirty = true;
            nextStateSend = 0f;
            Debug.Log("[Party] 파티 참가함");
            SetStatus("참가함", true);
            return;
        }

        // 참가 후: 방장 주소에서 온, 내 번호로 된, 서명이 맞는 패킷만
        if (host == null || !from.Equals(host.endpoint) || h.memberId != MyId) return;
        if (!TryOpen(host, packet, h, out byte[] data)) return;
        host.lastHeard = Time.realtimeSinceStartup;
        if (StatusText != "참가함") SetStatus("참가함", true);

        try
        {
            if (h.type == Type.Party)
            {
                List<Member> list = PartyProtocol.ReadParty(data);
                if (list == null) return;
                float now = Time.realtimeSinceStartup;
                foreach (Member m in list)
                {
                    m.nickname = PartyProtocol.CleanNickname(m.nickname);
                    m.receivedAt = now;
                }
                members.Clear();
                members.AddRange(list);
                Changed?.Invoke();
            }
            else if (h.type == Type.Rekey)
            {
                using (var r = new BinaryReader(new MemoryStream(data)))
                {
                    ushort epoch = r.ReadUInt16();
                    byte[] master = r.ReadBytes(PartyCrypto.KeySize);
                    if (master.Length != PartyCrypto.KeySize || epoch == host.epoch) return;
                    host.previousKeys = host.keys;
                    host.previousEpoch = host.epoch;
                    host.previousUntil = Time.realtimeSinceStartup + RekeyGrace;
                    host.keys = new PartyCrypto.Keys(master);
                    host.epoch = epoch;
                }
                myStateDirty = true;   // 새 키로 바로 보내서 받았다고 알린다
                nextStateSend = 0f;
                Changed?.Invoke();
            }
            else if (h.type == Type.Bye && data.Length >= 1)
            {
                var reason = (PartyProtocol.ByeReason)data[0];
                ResetAll();
                SetStatus(reason == PartyProtocol.ByeReason.Kicked ? "방장이 파티에서 내보냈어요" : "방장이 파티를 닫았어요", false);
            }
        }
        catch (Exception)
        {
            // 잘린 본문: 무시
        }
    }

    void ClientUpdate(float now)
    {
        if (CurrentRole == Role.Joining)
        {
            if (now - joinStarted > 10f)
            {
                ResetAll();
                SetStatus("방장에게 연결할 수 없어요 (방장 공유기 포트가 닫혀 있거나 코드가 만료됐어요)", false);
                return;
            }
            if (now >= nextHello)
            {
                nextHello = now + 1f;
                SendHello();
            }
            return;
        }

        if (host.previousKeys != null && now > host.previousUntil) host.previousKeys = null;

        if ((myStateDirty && now >= nextStateSend - SendInterval + 0.5f) || now >= nextStateSend)
        {
            myStateDirty = false;
            nextStateSend = now + SendInterval;
            byte[] body = PartyProtocol.Body(w => PartyProtocol.Write(w, myState));
            Send(Seal(host, Type.State, body), host.endpoint);
        }

        if (now - host.lastHeard > DisconnectAfter && StatusText == "참가함")
            SetStatus("방장 연결 끊김 — 다시 연결되기를 기다리는 중", false);
    }

    // ---- 공통 ----

    public void Leave() => Leave(false);

    void Leave(bool waitForRouter)
    {
        if (CurrentRole == Role.None) return;
        if (IsHost)
            foreach (Peer p in peers.Values) SendBye(p, PartyProtocol.ByeReason.Closed);
        else if (CurrentRole == Role.Joined && host != null)
            SendBye(host, PartyProtocol.ByeReason.Left);

        Task remove = RemoveMapping();
        if (waitForRouter) remove.Wait(1500);   // 앱 종료 중: 공유기 포트를 닫을 때까지 잠깐 기다림
        ResetAll();
        SetStatus("", true);
        Debug.Log("[Party] 파티 나감");
    }

    // 공유기 포트 닫기 시작 (돌려준 Task는 스레드 풀에서 끝나므로 메인 스레드에서 Wait해도 된다)
    Task RemoveMapping()
    {
        UpnpPortMapper.Gateway g = gateway;
        int port = localPort;
        bool mapped = mappedLease >= 0;
        gateway = null;
        mappedLease = -1;
        return g != null && mapped ? DeleteMapping(g, port) : Task.CompletedTask;
    }

    // 스레드 풀에서 실행: 앱 종료 중 메인 스레드가 Wait로 기다려도 서로 멈추지 않게
    static Task DeleteMapping(UpnpPortMapper.Gateway g, int port) =>
        Task.Run(async () =>
        {
            try
            {
                await UpnpPortMapper.DeletePortMappingAsync(g, port).ConfigureAwait(false);
            }
            catch (Exception)
            {
            }
        });


    void ResetAll()
    {
        CloseSocket();
        _ = RemoveMapping();
        peers.Clear();
        members.Clear();
        addresses.Clear();
        hostCandidates.Clear();
        host = null;
        inviteKeys = null;
        inviteCode = null;
        publicAddress = localAddress = null;
        ReachableFromInternet = false;
        MyId = 0;
        nextId = 2;
        CurrentRole = Role.None;
        Changed?.Invoke();
    }

    void SendBye(Peer p, PartyProtocol.ByeReason reason) => Send(Seal(p, Type.Bye, new[] { (byte)reason }), p.endpoint);

    byte[] Seal(Peer p, Type type, byte[] body)
    {
        var h = new PartyProtocol.Header
        {
            type = type,
            memberId = IsHost ? p.member.id : MyId,
            epoch = p.epoch,
            seq = ++p.seqOut,
        };
        return PartyCrypto.Seal(p.keys, PartyProtocol.WriteHeader(h), body);
    }

    // 지금 키(또는 유예 중인 이전 키)로 열고, 순번이 커졌는지(재전송 아님) 확인
    static bool TryOpen(Peer p, byte[] packet, PartyProtocol.Header h, out byte[] body)
    {
        body = null;
        PartyCrypto.Keys keys = h.epoch == p.epoch ? p.keys
            : p.previousKeys != null && h.epoch == p.previousEpoch && Time.realtimeSinceStartup < p.previousUntil ? p.previousKeys
            : null;
        if (keys == null || h.seq <= p.lastSeqIn) return false;
        body = PartyCrypto.Open(keys, packet, PartyProtocol.HeaderSize);
        if (body == null) return false;
        p.lastSeqIn = h.seq;
        return true;
    }

    void SetStatus(string text, bool ok)
    {
        StatusText = text;
        StatusOk = ok;
        Changed?.Invoke();
    }

    // ---- 소켓 ----

    bool OpenSocket(bool fixedPort)
    {
        CloseSocket();
        var random = new System.Random();
        for (int attempt = 0; attempt < (fixedPort ? 10 : 1); attempt++)
        {
            try
            {
                int port = fixedPort ? random.Next(PortMin, PortMax + 1) : 0;
                udp = new UdpClient(new IPEndPoint(IPAddress.Any, port));
                // Windows: 상대가 없는 주소로 보냈을 때 받기가 오류로 끝나지 않게 (SIO_UDP_CONNRESET 끄기)
                try { udp.Client.IOControl(-1744830452, new byte[] { 0 }, null); } catch (Exception) { }
                localPort = ((IPEndPoint)udp.Client.LocalEndPoint).Port;
                UdpClient socket = udp;
                _ = ReceiveLoop(socket);
                return true;
            }
            catch (SocketException)
            {
                udp = null;
            }
        }
        return false;
    }

    async Task ReceiveLoop(UdpClient socket)
    {
        while (true)
        {
            try
            {
                UdpReceiveResult r = await socket.ReceiveAsync().ConfigureAwait(false);
                if (r.Buffer.Length <= PartyProtocol.MaxPacket && inbox.Count < 500) inbox.Enqueue((r.Buffer, r.RemoteEndPoint));
            }
            catch (ObjectDisposedException)
            {
                return;
            }
            catch (SocketException)
            {
                if (socket.Client == null) return;
            }
            catch (Exception)
            {
                return;
            }
        }
    }

    void CloseSocket()
    {
        try { udp?.Close(); } catch (Exception) { }
        udp = null;
        while (inbox.TryDequeue(out _)) { }
    }

    void Send(byte[] packet, IPEndPoint to)
    {
        if (udp == null || packet == null || to == null) return;
        try { udp.Send(packet, packet.Length, to); } catch (Exception) { }
    }

    static IPAddress GuessLocalAddress()
    {
        try
        {
            // UDP Connect는 패킷을 보내지 않는다. 기본 경로(인터넷 쪽)로 나갈 때 쓰는 이 PC의 주소만 알아낸다
            // (공유기 아래면 내부 IP, 공유기 없이 바로 연결된 PC면 공인 IP)
            using (var s = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp))
            {
                s.Connect(new IPEndPoint(IPAddress.Parse("8.8.8.8"), 53));
                return ((IPEndPoint)s.LocalEndPoint).Address;
            }
        }
        catch (Exception)
        {
            return IPAddress.Any;
        }
    }

    // ---- 매 프레임 ----

    void Update()
    {
        if (CurrentRole == Role.None || CurrentRole == Role.Starting)
        {
            while (inbox.TryDequeue(out _)) { }
            return;
        }

        UpdateMyState();
        float now = Time.realtimeSinceStartup;

        for (int i = 0; i < 200 && inbox.TryDequeue(out var item); i++)
        {
            if (!PartyProtocol.TryReadHeader(item.data, out PartyProtocol.Header h)) continue;
            if (IsHost)
            {
                if (Allow(item.from, now)) HostReceive(item.data, item.from, h);
            }
            else ClientReceive(item.data, item.from, h);
            if (CurrentRole == Role.None) return;
        }

        if (IsHost) HostUpdate(now);
        else ClientUpdate(now);
    }

    // 지금 씬과 내 위치로 내 상태를 만든다
    void UpdateMyState()
    {
        var s = new PartyProtocol.MemberState();
        bool inMap = whereIAM != null && GameSession.Map != null;
        if (inMap)
        {
            s.status = PartyProtocol.Status.InMap;
            s.map = GameSession.Map.normalizedName ?? "";
            s.side = (byte)(GameSession.Side == PlayerSide.Scav ? 1 : 0);
            if (whereIAM.HasPosition && poseAt >= 0f)
            {
                Vector3 p = whereIAM.GamePosition;
                s.hasPosition = true;
                s.x = p.x;
                s.y = p.y;
                s.z = p.z;
                s.yaw = whereIAM.GameRotation.eulerAngles.y;
                s.positionAge = (uint)Mathf.Max(0f, Time.realtimeSinceStartup - poseAt);
            }
        }
        else s.map = "";

        bool changed = s.status != myState.status || s.map != myState.map || s.side != myState.side ||
                       s.hasPosition != myState.hasPosition || s.x != myState.x || s.y != myState.y || s.z != myState.z || s.yaw != myState.yaw;
        myState = s;
        if (changed) myStateDirty = true;
    }

    // 화면 표시용: 이 파티원의 위치가 몇 초 전 것인지
    public static float PositionAge(Member m) =>
        m.state.positionAge + Mathf.Max(0f, Time.realtimeSinceStartup - m.receivedAt);
}

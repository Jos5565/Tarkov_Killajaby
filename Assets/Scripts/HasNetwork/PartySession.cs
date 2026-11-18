using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;
using Type = PartyProtocol.Type;
using Member = PartyProtocol.Member;

// 파티 (네트워크 버전 전용). Cloudflare 중계 서버(PartyRelay, Server/party-relay)의 방에 모두가 접속한다.
// 방장 PC에 포트를 열 필요가 없고, 파티원끼리 서로의 IP를 모른다. 중계 서버는 암호화된 봉투만 전달한다.
// 씬이 바뀌어도 유지되므로 로비로 나가도 파티는 그대로다. 나가는 건 [파티 나가기]/[파티 닫기] 또는 앱 종료 때뿐.
//
// 방장
//   - 무작위 방 ID(128비트)로 중계 방을 연다
//   - 초대 코드 = (방 ID, 초대 비밀값, 10분 뒤 만료). 만료되면 새 코드로 (방은 그대로, 이미 들어온 사람도 그대로)
//   - 참가 요청이 초대 키로 서명돼 있으면 파티원마다 다른 세션 키를 그 사람 연결에만 나눠 준다
//   - 레이드가 시작되면(로그) 모든 파티원의 키를 새로 바꾼다 (이전 키는 30초 유예)
//   - 서명이 틀린 패킷은 해석하지 않고 응답도 하지 않는다. 연결별 초당 횟수 제한, 인증 5회 실패 시 10분 무시
// 파티원
//   - 상태(로비/맵, 위치)가 바뀌면, 그리고 10초마다 방장에게 보낸다 (끊김 감지)
//   - 방장은 모은 파티 전체 상태를 파티원 각자에게 그 사람 키로 보낸다
// 중계 연결이 끊기면 자동으로 다시 붙는다. 연결 번호가 바뀌어도 서명이 맞는 패킷으로 새 번호를 따라간다.
// 내 상태에는 위치뿐 아니라 내가 그린 경로(QuestRoute.SharedPath)와 보스 상태 표시(BossMarks)도 들어간다.
// 받은 파티원의 보스 표시는 BossMarks에 넘기고, 경로는 PartyMapView가 Members에서 읽어 그린다.
//
// 초대 코드 · 키는 로그(Player.log)에 남기지 않는다.
public class PartySession : MonoBehaviour
{
    public static PartySession Instance { get; private set; }

    public enum Role { None, Starting, Hosting, Joining, Joined }

    public Role CurrentRole { get; private set; }
    public bool IsInParty => CurrentRole == Role.Hosting || CurrentRole == Role.Joined;
    public bool IsHost => CurrentRole == Role.Hosting;

    // 화면에 보여 줄 상태 한 줄 (예: "파티를 만들었어요")과 정상 여부
    public string StatusText { get; private set; } = "";
    public bool StatusOk { get; private set; } = true;

    // 방장: 지금 초대 코드 (만료되면 null)
    public string InviteCode => inviteKeys != null && DateTime.UtcNow < inviteExpiresUtc ? inviteCode : null;
    public DateTime InviteExpiresUtc => inviteExpiresUtc;

    public ushort MyId { get; private set; }

    // 나를 포함한 파티 전체 (방장이 정한 색 · 닉네임 · 상태)
    public IReadOnlyList<Member> Members => members;

    // 파티 구성, 상태, 역할이 바뀌면
    public event Action Changed;

    public const int InviteMinutes = 10;
    const float ConnectTimeout = 15f;        // 중계 서버 첫 연결 대기
    const float SendInterval = 10f;          // 상태/파티 정보를 이 간격으로 다시 보냄
    const float DisconnectAfter = 45f;       // 이만큼 소식이 없으면 연결 끊김
    const float RemoveAfter = 120f;          // 이만큼 지나면 목록에서 뺌 (방장)
    const float RekeyGrace = 30f;            // 키를 바꾼 뒤 이전 키를 받아 주는 시간
    const int PacketsPerSecond = 20;         // 연결별
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
        public uint conn;               // 중계 서버 연결 번호 (다시 접속하면 바뀐다)
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

    class ConnState
    {
        public float windowStart;
        public int count;
        public int failures;
        public float bannedUntil;
    }

    readonly List<Member> members = new List<Member>();
    readonly ConcurrentQueue<(byte[] data, uint from)> inbox = new ConcurrentQueue<(byte[], uint)>();
    PartyRelay relay;
    byte[] roomId;
    float connectStarted;

    // 방장
    readonly Dictionary<ushort, Peer> peers = new Dictionary<ushort, Peer>();
    readonly Dictionary<uint, ConnState> conns = new Dictionary<uint, ConnState>();
    PartyCrypto.Keys inviteKeys;
    string inviteCode;
    DateTime inviteExpiresUtc;
    ushort nextId = 2, currentEpoch = 1;
    bool partyDirty;
    float nextPartySend;

    // 파티원
    Peer host;                      // 방장 연결 (파티원일 때)
    byte[] joinNonce;
    float joinStarted, nextHello;
    float nextStateSend;

    // 내 상태
    PartyProtocol.MemberState myState;
    bool myStateDirty = true;
    WhereIAM whereIAM;
    QuestRoute questRoute;
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

    void OnApplicationQuit() => Leave(waitMs: 600);   // 나감 알림을 보낼 시간만 잠깐

    void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (whereIAM != null) whereIAM.PoseChanged -= OnPoseChanged;
        whereIAM = FindAnyObjectByType<WhereIAM>(FindObjectsInactive.Include);
        if (whereIAM != null) whereIAM.PoseChanged += OnPoseChanged;
        questRoute = FindAnyObjectByType<QuestRoute>();
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

    public void Host()
    {
        if (CurrentRole != Role.None) return;
        roomId = PartyCrypto.RandomBytes(16);
        relay = new PartyRelay(roomId, inbox);
        connectStarted = Time.realtimeSinceStartup;
        CurrentRole = Role.Starting;
        SetStatus("파티 서버에 연결하는 중...", true);
    }

    // 중계 서버에 처음 붙으면 방장 시작
    void StartHosting()
    {
        CurrentRole = Role.Hosting;
        MyId = 1;
        members.Clear();
        members.Add(new Member { id = 1, color = 0, isHost = true, nickname = Nickname, state = myState });
        NewInvite();
        Debug.Log("[Party] 파티 만듦");
        SetStatus("파티를 만들었어요 — 초대 코드를 복사해서 보내 주세요", true);
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
            roomId = roomId,
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
        Send(p.rekeyPacket, p.conn);
    }

    void HostReceive(byte[] packet, uint from, PartyProtocol.Header h)
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
        // 있는 참가자의 패킷이 안 열리면(키 교체를 놓침, 중복 도착) 버리기만 한다
        if (!TryOpen(p, packet, h, out byte[] body)) return;
        p.conn = from;   // 다시 접속해서 연결 번호가 바뀌어도 따라간다 (서명이 맞은 뒤에만)
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
                    ApplyRemoteMarks(p.member);
                }
                else if (h.type == Type.Bye) RemovePeer(p.member.id);
            }
        }
        catch (Exception)
        {
            // 잘린 본문: 무시
        }
    }

    void HandleHello(byte[] packet, uint from)
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
            if (same.conn == from) Send(same.welcome, from);
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
            conn = from,
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
        BossMarks.RemoveRemote(id);
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
                if (p.rekeyPending && p.previousKeys != null) Send(p.rekeyPacket, p.conn);
                Send(Seal(p, Type.Party, body), p.conn);
            }
            Changed?.Invoke();
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
        uint extra = age - s.positionAge;   // 받은 뒤 지난 시간
        s.positionAge = age;
        if (s.marks != null)
            s.marks = s.marks.Select(m => new PartyProtocol.MarkInfo
            {
                boss = m.boss,
                status = m.status,
                age = (ushort)Math.Min(ushort.MaxValue, m.age + extra),
            }).ToArray();
        return s;
    }

    // 파티원의 보스 표시 → BossMarks (같은 보스는 가장 최근 표시가 보인다)
    static void ApplyRemoteMarks(Member m)
    {
        float now = Time.realtimeSinceStartup;
        float age = Mathf.Max(0f, now - m.receivedAt);
        BossMarks.SetRemote(m.id, m.nickname, (m.state.marks ?? Array.Empty<PartyProtocol.MarkInfo>()).Select(x => new BossMarks.Mark
        {
            boss = x.boss,
            status = (BossMarks.Status)x.status,
            time = now - age - x.age,
        }));
    }

    void Fail(uint from)
    {
        ConnState c = Conn(from);
        if (++c.failures >= MaxFailures) c.bannedUntil = Time.realtimeSinceStartup + BanSeconds;
    }

    ConnState Conn(uint id)
    {
        if (!conns.TryGetValue(id, out ConnState c))
        {
            if (conns.Count > 1000) conns.Clear();   // 기억할 연결 수 제한
            conns.Add(id, c = new ConnState());
        }
        return c;
    }

    // 연결별 횟수 제한 / 차단 중이면 false
    bool Allow(uint from, float now)
    {
        ConnState c = Conn(from);
        if (now < c.bannedUntil) return false;
        if (now - c.windowStart >= 1f)
        {
            c.windowStart = now;
            c.count = 0;
        }
        return ++c.count <= PacketsPerSecond;
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
        inviteKeys = PartyCrypto.InviteKeys(invite.secret);
        roomId = invite.roomId;
        relay = new PartyRelay(roomId, inbox);
        connectStarted = Time.realtimeSinceStartup;

        joinNonce = PartyCrypto.RandomBytes(16);
        joinStarted = Time.realtimeSinceStartup;
        nextHello = 0f;
        CurrentRole = Role.Joining;
        SetStatus("파티 서버에 연결하는 중...", true);
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
        relay.Broadcast(packet);   // 방장의 연결 번호를 아직 모르므로 방 전체에 (방장만 열 수 있다)
    }

    void ClientReceive(byte[] packet, uint from, PartyProtocol.Header h)
    {
        if (CurrentRole == Role.Joining)
        {
            if (inviteKeys == null) return;
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
                    conn = from,
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

        // 참가 후: 내 번호로 된, 방장 키로 서명이 맞는 패킷만 (방장이 다시 접속해도 서명으로 알아본다)
        if (host == null || h.memberId != MyId) return;
        if (!TryOpen(host, packet, h, out byte[] data)) return;
        host.conn = from;
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
                foreach (Member m in list)
                    if (m.id != MyId) ApplyRemoteMarks(m);
                    else BossMarks.RemoveRemote(m.id);   // 내 표시는 내 것 그대로
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
            if (now - joinStarted > ConnectTimeout + 10f)
            {
                ResetAll();
                SetStatus("방장에게 연결할 수 없어요 (방장이 파티를 닫았거나 코드가 만료됐어요)", false);
                return;
            }
            if (relay.Status != PartyRelay.State.Connected) return;   // 서버에 붙은 뒤에 참가 요청
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
            Send(Seal(host, Type.State, body), host.conn);
        }

        if (now - host.lastHeard > DisconnectAfter && StatusText == "참가함")
            SetStatus("방장 연결 끊김 — 다시 연결되기를 기다리는 중", false);
    }

    // ---- 공통 ----

    public void Leave() => Leave(0);

    void Leave(int waitMs)
    {
        if (CurrentRole == Role.None) return;
        if (IsHost)
            foreach (Peer p in peers.Values) SendBye(p, PartyProtocol.ByeReason.Closed);
        else if (CurrentRole == Role.Joined && host != null)
            SendBye(host, PartyProtocol.ByeReason.Left);

        relay?.Close(waitMs);   // 나감 알림을 보낸 뒤 닫는다
        relay = null;
        ResetAll();
        SetStatus("", true);
        Debug.Log("[Party] 파티 나감");
    }

    void ResetAll()
    {
        relay?.Close();
        relay = null;
        while (inbox.TryDequeue(out _)) { }
        peers.Clear();
        members.Clear();
        conns.Clear();
        BossMarks.ClearRemote();
        host = null;
        roomId = null;
        inviteKeys = null;
        inviteCode = null;
        MyId = 0;
        nextId = 2;
        CurrentRole = Role.None;
        Changed?.Invoke();
    }

    void SendBye(Peer p, PartyProtocol.ByeReason reason) => Send(Seal(p, Type.Bye, new[] { (byte)reason }), p.conn);

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

    // ---- 중계 ----

    void Send(byte[] packet, uint to) => relay?.SendTo(to, packet);

    // ---- 매 프레임 ----

    void Update()
    {
        if (CurrentRole == Role.None)
        {
            while (inbox.TryDequeue(out _)) { }
            return;
        }
        float now = Time.realtimeSinceStartup;
        if (!UpdateConnection(now)) return;

        UpdateMyState();

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

    // 중계 연결 상태 처리. 계속 진행해도 되면 true
    bool UpdateConnection(float now)
    {
        if (relay == null) return false;
        if (CurrentRole == Role.Starting)
        {
            while (inbox.TryDequeue(out _)) { }
            if (relay.Status == PartyRelay.State.Connected && relay.MyConn != 0) StartHosting();
            else if (now - connectStarted > ConnectTimeout)
            {
                Debug.LogWarning("[Party] 파티 서버 연결 실패: " + relay.LastError);
                ResetAll();
                SetStatus("파티 서버에 연결할 수 없어요 — 인터넷 연결을 확인해 주세요", false);
            }
            return false;
        }

        // 나간 연결: 방장은 그 파티원을 바로 "연결 끊김"으로 (다시 접속하면 서명된 패킷으로 돌아온다)
        while (relay.TryGetLeft(out uint gone))
        {
            if (!IsHost) continue;
            foreach (Peer p in peers.Values)
                if (p.conn == gone && p.member.connected)
                {
                    p.member.connected = false;
                    partyDirty = true;
                    Changed?.Invoke();
                }
        }

        // 서버와 끊겨 다시 연결하는 중이면 안내 (자동으로 다시 붙는다)
        bool online = relay.Status == PartyRelay.State.Connected;
        if (!online && IsInParty && StatusOk) SetStatus("파티 서버와 연결이 끊겨 다시 연결하는 중...", false);
        else if (online && IsInParty && !StatusOk && StatusText.StartsWith("파티 서버와 연결이 끊겨"))
            SetStatus(IsHost ? "다시 연결됐어요" : "참가함", true);
        return true;
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
            s.route = MyRoute();
            float now = Time.realtimeSinceStartup;
            s.marks = BossMarks.Mine(PartyProtocol.MaxMarks).Select(m => new PartyProtocol.MarkInfo
            {
                boss = m.boss,
                status = (byte)m.status,
                age = (ushort)Mathf.Clamp(now - m.time, 0f, ushort.MaxValue),
            }).ToArray();
        }
        else s.map = "";

        bool changed = s.status != myState.status || s.map != myState.map || s.side != myState.side ||
                       s.hasPosition != myState.hasPosition || s.x != myState.x || s.y != myState.y || s.z != myState.z || s.yaw != myState.yaw ||
                       !SameRoute(s.route, myState.route) || !SameMarks(s.marks, myState.marks);
        myState = s;
        if (changed) myStateDirty = true;
    }

    // 내가 지금 그린 경로 (1m 단위, 앞에서부터 최대 MaxRoutePoints개)
    PartyProtocol.RoutePoint[] MyRoute()
    {
        if (questRoute == null || questRoute.SharedPath.Count < 2) return Array.Empty<PartyProtocol.RoutePoint>();
        return questRoute.SharedPath.Take(PartyProtocol.MaxRoutePoints).Select(p => new PartyProtocol.RoutePoint
        {
            x = (short)Mathf.Clamp(Mathf.RoundToInt(p.x), short.MinValue, short.MaxValue),
            z = (short)Mathf.Clamp(Mathf.RoundToInt(p.y), short.MinValue, short.MaxValue),
        }).ToArray();
    }

    static bool SameRoute(PartyProtocol.RoutePoint[] a, PartyProtocol.RoutePoint[] b)
    {
        a = a ?? Array.Empty<PartyProtocol.RoutePoint>();
        b = b ?? Array.Empty<PartyProtocol.RoutePoint>();
        if (a.Length != b.Length) return false;
        for (int i = 0; i < a.Length; i++)
            if (a[i].x != b[i].x || a[i].z != b[i].z) return false;
        return true;
    }

    // 경과 시간은 계속 변하므로 비교하지 않는다
    static bool SameMarks(PartyProtocol.MarkInfo[] a, PartyProtocol.MarkInfo[] b)
    {
        a = a ?? Array.Empty<PartyProtocol.MarkInfo>();
        b = b ?? Array.Empty<PartyProtocol.MarkInfo>();
        if (a.Length != b.Length) return false;
        for (int i = 0; i < a.Length; i++)
            if (a[i].boss != b[i].boss || a[i].status != b[i].status) return false;
        return true;
    }

    // 화면 표시용: 이 파티원의 위치가 몇 초 전 것인지
    public static float PositionAge(Member m) =>
        m.state.positionAge + Mathf.Max(0f, Time.realtimeSinceStartup - m.receivedAt);
}

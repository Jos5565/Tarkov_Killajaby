using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

// 파티 패킷 구조. 받은 데이터는 숫자와 짧은 글자로만 해석하고, 범위를 벗어나면 통째로 버린다.
//   헤더(16, 서명에 포함): 'K' 'J' | 버전 1 | 종류 1 | 참가자 번호 2 | 키 세대 2 | 순번 8
//   본문: PartyCrypto.Seal로 암호화 + 서명
//   Hello   (참가자→방장, 초대 키)  : 참가자 nonce 16 | 닉네임 | 빈 공간 (응답보다 크게 만들어 증폭 공격 방지)
//   Welcome (방장→참가자, 초대 키)  : 참가자 nonce 16 | 참가자 번호 | 색 | 키 세대 | 세션 키 32
//   State   (참가자→방장, 세션 키)  : 내 상태 (로비/맵, 위치)
//   Party   (방장→참가자, 세션 키)  : 파티 전체 상태
//   Rekey   (방장→참가자, 지금 키)  : 새 키 세대 | 새 세션 키 32   (레이드 시작마다)
//   Bye     (양쪽, 세션 키)         : 이유 (나감 / 파티 닫힘 / 내보냄)
public static class PartyProtocol
{
    public const int HeaderSize = 16;
    public const int MaxPacket = 1200;
    public const int HelloSize = 320;   // Hello는 패딩으로 이 크기 이상 (Welcome보다 크게)
    const byte Version = 1;

    public enum Type : byte { Hello = 1, Welcome = 2, State = 3, Party = 4, Rekey = 5, Bye = 6 }
    public enum ByeReason : byte { Left = 0, Closed = 1, Kicked = 2, Full = 3 }

    public struct Header
    {
        public Type type;
        public ushort memberId;
        public ushort epoch;
        public ulong seq;
    }

    public static byte[] WriteHeader(Header h)
    {
        var b = new byte[HeaderSize];
        b[0] = (byte)'K';
        b[1] = (byte)'J';
        b[2] = Version;
        b[3] = (byte)h.type;
        b[4] = (byte)(h.memberId >> 8);
        b[5] = (byte)h.memberId;
        b[6] = (byte)(h.epoch >> 8);
        b[7] = (byte)h.epoch;
        for (int i = 0; i < 8; i++) b[8 + i] = (byte)(h.seq >> (56 - i * 8));
        return b;
    }

    public static bool TryReadHeader(byte[] packet, out Header h)
    {
        h = default;
        if (packet == null || packet.Length < HeaderSize || packet.Length > MaxPacket) return false;
        if (packet[0] != 'K' || packet[1] != 'J' || packet[2] != Version) return false;
        if (packet[3] < 1 || packet[3] > 6) return false;
        h.type = (Type)packet[3];
        h.memberId = (ushort)((packet[4] << 8) | packet[5]);
        h.epoch = (ushort)((packet[6] << 8) | packet[7]);
        for (int i = 0; i < 8; i++) h.seq = (h.seq << 8) | packet[8 + i];
        return true;
    }

    // ---- 상태 ----

    public enum Status : byte { Lobby = 0, InMap = 1 }

    // 한 사람의 상태. 위치는 스크린샷을 찍을 때만 바뀐다
    public struct MemberState
    {
        public Status status;
        public string map;          // tarkov.dev normalizedName (로비면 빈 문자열)
        public byte side;           // 0 PMC, 1 스캐브
        public bool hasPosition;
        public float x, y, z, yaw;
        public uint positionAge;    // 위치를 찍은 지 몇 초 지났는지
    }

    public class Member
    {
        public ushort id;
        public byte color;
        public bool isHost;
        public bool connected = true;
        public string nickname = "";
        public MemberState state;
        public float receivedAt;    // (보내지 않음) 이 상태를 받은 시각 (Time.realtimeSinceStartup). 위치 경과 시간 계산용
    }

    public const int MaxNickname = 16;
    const int MaxMap = 32;
    const float MaxCoordinate = 20000f;

    public static void Write(BinaryWriter w, MemberState s)
    {
        w.Write((byte)s.status);
        WriteString(w, s.map, MaxMap);
        w.Write(s.side);
        w.Write(s.hasPosition);
        w.Write(s.x);
        w.Write(s.y);
        w.Write(s.z);
        w.Write(s.yaw);
        w.Write(s.positionAge);
    }

    public static bool TryRead(BinaryReader r, out MemberState s)
    {
        s = default;
        byte status = r.ReadByte();
        if (status > 1) return false;
        s.status = (Status)status;
        s.map = ReadString(r, MaxMap);
        s.side = r.ReadByte();
        s.hasPosition = r.ReadBoolean();
        s.x = r.ReadSingle();
        s.y = r.ReadSingle();
        s.z = r.ReadSingle();
        s.yaw = r.ReadSingle();
        s.positionAge = r.ReadUInt32();
        if (s.map == null || s.side > 1) return false;
        if (!Valid(s.x) || !Valid(s.y) || !Valid(s.z) || !Valid(s.yaw)) return false;
        foreach (char c in s.map)
            if (!(char.IsLetterOrDigit(c) || c == '-' || c == '_')) return false;
        return true;
    }

    static bool Valid(float v) => !float.IsNaN(v) && !float.IsInfinity(v) && Math.Abs(v) <= MaxCoordinate;

    public static byte[] WriteParty(IReadOnlyList<Member> members)
    {
        using (var ms = new MemoryStream())
        using (var w = new BinaryWriter(ms))
        {
            w.Write((byte)members.Count);
            foreach (Member m in members)
            {
                w.Write(m.id);
                w.Write(m.color);
                w.Write((byte)((m.isHost ? 1 : 0) | (m.connected ? 2 : 0)));
                WriteString(w, m.nickname, MaxNickname);
                Write(w, m.state);
            }
            return ms.ToArray();
        }
    }

    public const int MaxMembers = 5;

    public static List<Member> ReadParty(byte[] body)
    {
        try
        {
            using (var r = new BinaryReader(new MemoryStream(body)))
            {
                int count = r.ReadByte();
                if (count > MaxMembers) return null;
                var list = new List<Member>(count);
                for (int i = 0; i < count; i++)
                {
                    var m = new Member { id = r.ReadUInt16(), color = r.ReadByte() };
                    byte flags = r.ReadByte();
                    m.isHost = (flags & 1) != 0;
                    m.connected = (flags & 2) != 0;
                    m.nickname = ReadString(r, MaxNickname);
                    if (m.nickname == null || !TryRead(r, out m.state)) return null;
                    list.Add(m);
                }
                return list;
            }
        }
        catch (Exception)
        {
            return null;   // 잘린 데이터 등
        }
    }

    public static byte[] Body(Action<BinaryWriter> write)
    {
        using (var ms = new MemoryStream())
        using (var w = new BinaryWriter(ms))
        {
            write(w);
            return ms.ToArray();
        }
    }

    // 길이(1바이트) + UTF-8. 최대 글자 수를 넘으면 자른다
    public static void WriteString(BinaryWriter w, string s, int maxChars)
    {
        s = s ?? "";
        if (s.Length > maxChars) s = s.Substring(0, maxChars);
        byte[] b = Encoding.UTF8.GetBytes(s);
        if (b.Length > 255) Array.Resize(ref b, 255);
        w.Write((byte)b.Length);
        w.Write(b);
    }

    // 최대 글자 수를 넘거나 제어 문자가 있으면 null
    public static string ReadString(BinaryReader r, int maxChars)
    {
        int length = r.ReadByte();
        byte[] b = r.ReadBytes(length);
        if (b.Length != length) return null;
        string s = Encoding.UTF8.GetString(b);
        if (s.Length > maxChars) return null;
        foreach (char c in s)
            if (char.IsControl(c)) return null;
        return s;
    }

    // 화면에 그대로 찍을 닉네임: 앞뒤 공백 제거, 길이 제한, 서식 태그로 해석되지 않게
    public static string CleanNickname(string s)
    {
        s = (s ?? "").Trim();
        var sb = new StringBuilder();
        foreach (char c in s)
            if (!char.IsControl(c) && c != '<' && c != '>') sb.Append(c);
        s = sb.ToString();
        if (s.Length > MaxNickname) s = s.Substring(0, MaxNickname);
        return s.Length == 0 ? "플레이어" : s;
    }
}

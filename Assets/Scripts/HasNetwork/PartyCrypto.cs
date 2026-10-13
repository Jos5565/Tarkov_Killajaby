using System;
using System.Net;
using System.Security.Cryptography;
using System.Text;

// 파티 통신 암호화와 초대 코드 (.NET 기본 기능만 사용, 직접 만든 암호 방식 없음).
//   키      : 32바이트 마스터 키 → HMAC-SHA256으로 암호화 키(16)와 서명 키(32)를 나눠 만든다
//   봉인    : AES-128-CBC(메시지마다 새 IV)로 암호화한 뒤, 헤더+IV+암호문 전체를 HMAC-SHA256으로 서명 (앞 16바이트)
//   열기    : 서명을 먼저 확인(시간 차이 없는 비교)하고 맞을 때만 복호화. 틀리면 해석하지 않는다
//   초대 코드: 공인 IP · 내부 IP · 포트 · 초대 비밀값(무작위 16바이트) · 만료 시각을 Base32로 포장
//             비밀값이 무작위 128비트라 맞히기 공격이 불가능하므로 비밀번호용 느린 변환은 쓰지 않는다
public static class PartyCrypto
{
    public const int KeySize = 32;
    public const int TagSize = 16;
    const int IvSize = 16;

    public sealed class Keys
    {
        public readonly byte[] enc;
        public readonly byte[] mac;

        public Keys(byte[] master)
        {
            enc = new byte[16];
            Buffer.BlockCopy(Hmac(master, "kj-party-enc"), 0, enc, 0, 16);
            mac = Hmac(master, "kj-party-mac");
        }
    }

    public static byte[] RandomBytes(int count)
    {
        var bytes = new byte[count];
        using (var rng = RandomNumberGenerator.Create()) rng.GetBytes(bytes);
        return bytes;
    }

    // 초대 비밀값(16) → 초대용 마스터 키(32)
    public static Keys InviteKeys(byte[] inviteSecret) => new Keys(Hmac(inviteSecret, "kj-party-invite"));

    static byte[] Hmac(byte[] key, string label)
    {
        using (var h = new HMACSHA256(key)) return h.ComputeHash(Encoding.ASCII.GetBytes(label));
    }

    // header || IV || AES(plain) || tag
    public static byte[] Seal(Keys keys, byte[] header, byte[] plain)
    {
        byte[] iv = RandomBytes(IvSize);
        byte[] cipher;
        using (Aes aes = Aes.Create())
        {
            aes.Mode = CipherMode.CBC;
            aes.Padding = PaddingMode.PKCS7;
            aes.Key = keys.enc;
            aes.IV = iv;
            using (ICryptoTransform t = aes.CreateEncryptor()) cipher = t.TransformFinalBlock(plain, 0, plain.Length);
        }

        var packet = new byte[header.Length + IvSize + cipher.Length + TagSize];
        Buffer.BlockCopy(header, 0, packet, 0, header.Length);
        Buffer.BlockCopy(iv, 0, packet, header.Length, IvSize);
        Buffer.BlockCopy(cipher, 0, packet, header.Length + IvSize, cipher.Length);
        byte[] tag = Tag(keys, packet, packet.Length - TagSize);
        Buffer.BlockCopy(tag, 0, packet, packet.Length - TagSize, TagSize);
        return packet;
    }

    // 서명이 맞으면 복호화한 본문, 아니면 null (예외 없이 조용히 버린다)
    public static byte[] Open(Keys keys, byte[] packet, int headerSize)
    {
        int cipherLength = packet.Length - headerSize - IvSize - TagSize;
        if (keys == null || cipherLength < 16 || cipherLength % 16 != 0) return null;

        byte[] expected = Tag(keys, packet, packet.Length - TagSize);
        int diff = 0;
        for (int i = 0; i < TagSize; i++) diff |= expected[i] ^ packet[packet.Length - TagSize + i];
        if (diff != 0) return null;

        try
        {
            using (Aes aes = Aes.Create())
            {
                aes.Mode = CipherMode.CBC;
                aes.Padding = PaddingMode.PKCS7;
                aes.Key = keys.enc;
                var iv = new byte[IvSize];
                Buffer.BlockCopy(packet, headerSize, iv, 0, IvSize);
                aes.IV = iv;
                using (ICryptoTransform t = aes.CreateDecryptor())
                    return t.TransformFinalBlock(packet, headerSize + IvSize, cipherLength);
            }
        }
        catch (CryptographicException)
        {
            return null;
        }
    }

    static byte[] Tag(Keys keys, byte[] data, int length)
    {
        using (var h = new HMACSHA256(keys.mac))
        {
            byte[] full = h.ComputeHash(data, 0, length);
            var tag = new byte[TagSize];
            Buffer.BlockCopy(full, 0, tag, 0, TagSize);
            return tag;
        }
    }

    // ---- 초대 코드 ----

    public struct Invite
    {
        public IPAddress publicAddress;   // 없으면 0.0.0.0 (포트 자동 열기 실패: 같은 공유기 안에서만)
        public IPAddress localAddress;
        public int port;
        public byte[] secret;             // 16바이트
        public DateTime expiresUtc;
    }

    const byte InviteVersion = 1;
    const string Alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";   // 헷갈리는 글자(I, O, 0, 1)를 뺀 32자

    // 버전1 | 공인IP4 | 내부IP4 | 포트2 | 비밀값16 | 만료4 | 확인2  = 33바이트 → 5글자씩 묶은 Base32
    public static string EncodeInvite(Invite invite)
    {
        var data = new byte[33];
        data[0] = InviteVersion;
        Buffer.BlockCopy(V4(invite.publicAddress), 0, data, 1, 4);
        Buffer.BlockCopy(V4(invite.localAddress), 0, data, 5, 4);
        data[9] = (byte)(invite.port >> 8);
        data[10] = (byte)invite.port;
        Buffer.BlockCopy(invite.secret, 0, data, 11, 16);
        uint expires = (uint)new DateTimeOffset(invite.expiresUtc).ToUnixTimeSeconds();
        data[27] = (byte)(expires >> 24);
        data[28] = (byte)(expires >> 16);
        data[29] = (byte)(expires >> 8);
        data[30] = (byte)expires;
        byte[] check = Check(data, 31);
        data[31] = check[0];
        data[32] = check[1];

        string text = ToBase32(data);
        var sb = new StringBuilder("KJ-");
        for (int i = 0; i < text.Length; i++)
        {
            if (i > 0 && i % 5 == 0) sb.Append('-');
            sb.Append(text[i]);
        }
        return sb.ToString();
    }

    // 형식이 틀렸거나 확인값이 안 맞으면 false (오타, 잘린 붙여넣기)
    public static bool TryDecodeInvite(string code, out Invite invite)
    {
        invite = default;
        if (string.IsNullOrWhiteSpace(code)) return false;
        string text = code.Trim().ToUpperInvariant();
        if (text.StartsWith("KJ-")) text = text.Substring(3);
        text = text.Replace("-", "").Replace(" ", "");

        byte[] data = FromBase32(text);
        if (data == null || data.Length < 33 || data[0] != InviteVersion) return false;
        byte[] check = Check(data, 31);
        if (check[0] != data[31] || check[1] != data[32]) return false;

        invite.publicAddress = new IPAddress(new[] { data[1], data[2], data[3], data[4] });
        invite.localAddress = new IPAddress(new[] { data[5], data[6], data[7], data[8] });
        invite.port = (data[9] << 8) | data[10];
        invite.secret = new byte[16];
        Buffer.BlockCopy(data, 11, invite.secret, 0, 16);
        uint expires = ((uint)data[27] << 24) | ((uint)data[28] << 16) | ((uint)data[29] << 8) | data[30];
        invite.expiresUtc = DateTimeOffset.FromUnixTimeSeconds(expires).UtcDateTime;
        return invite.port > 0;
    }

    static byte[] V4(IPAddress address)
    {
        byte[] b = address?.GetAddressBytes();
        return b != null && b.Length == 4 ? b : new byte[4];
    }

    static byte[] Check(byte[] data, int length)
    {
        using (SHA256 sha = SHA256.Create()) return sha.ComputeHash(data, 0, length);
    }

    static string ToBase32(byte[] data)
    {
        var sb = new StringBuilder();
        int buffer = 0, bits = 0;
        foreach (byte b in data)
        {
            buffer = (buffer << 8) | b;
            bits += 8;
            while (bits >= 5)
            {
                sb.Append(Alphabet[(buffer >> (bits - 5)) & 31]);
                bits -= 5;
            }
        }
        if (bits > 0) sb.Append(Alphabet[(buffer << (5 - bits)) & 31]);
        return sb.ToString();
    }

    static byte[] FromBase32(string text)
    {
        var bytes = new System.Collections.Generic.List<byte>();
        int buffer = 0, bits = 0;
        foreach (char c in text)
        {
            int v = Alphabet.IndexOf(c);
            if (v < 0) return null;
            buffer = (buffer << 5) | v;
            bits += 5;
            if (bits >= 8)
            {
                bytes.Add((byte)(buffer >> (bits - 8)));
                bits -= 8;
            }
        }
        return bytes.ToArray();
    }
}

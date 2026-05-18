using System.Security.Cryptography;

namespace ContextOS.Storage;

/// <summary>Generates ULID strings: 48-bit timestamp + 80-bit random, Crockford base32.</summary>
public static class UlidHelper
{
    private const string Alphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";

    public static string NewUlid()
    {
        long ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        byte[] r = new byte[10];
        RandomNumberGenerator.Fill(r);

        char[] c = new char[26];

        // 48-bit timestamp encoded in 10 chars (5 bits each)
        c[0] = Alphabet[(int)((ts >> 45) & 0x1F)];
        c[1] = Alphabet[(int)((ts >> 40) & 0x1F)];
        c[2] = Alphabet[(int)((ts >> 35) & 0x1F)];
        c[3] = Alphabet[(int)((ts >> 30) & 0x1F)];
        c[4] = Alphabet[(int)((ts >> 25) & 0x1F)];
        c[5] = Alphabet[(int)((ts >> 20) & 0x1F)];
        c[6] = Alphabet[(int)((ts >> 15) & 0x1F)];
        c[7] = Alphabet[(int)((ts >> 10) & 0x1F)];
        c[8] = Alphabet[(int)((ts >> 5) & 0x1F)];
        c[9] = Alphabet[(int)(ts & 0x1F)];

        // 80-bit random encoded in 16 chars (5 bits each, packed across 10 bytes)
        c[10] = Alphabet[(r[0] >> 3) & 0x1F];
        c[11] = Alphabet[((r[0] & 0x07) << 2) | (r[1] >> 6)];
        c[12] = Alphabet[(r[1] >> 1) & 0x1F];
        c[13] = Alphabet[((r[1] & 0x01) << 4) | (r[2] >> 4)];
        c[14] = Alphabet[((r[2] & 0x0F) << 1) | (r[3] >> 7)];
        c[15] = Alphabet[(r[3] >> 2) & 0x1F];
        c[16] = Alphabet[((r[3] & 0x03) << 3) | (r[4] >> 5)];
        c[17] = Alphabet[r[4] & 0x1F];
        c[18] = Alphabet[(r[5] >> 3) & 0x1F];
        c[19] = Alphabet[((r[5] & 0x07) << 2) | (r[6] >> 6)];
        c[20] = Alphabet[(r[6] >> 1) & 0x1F];
        c[21] = Alphabet[((r[6] & 0x01) << 4) | (r[7] >> 4)];
        c[22] = Alphabet[((r[7] & 0x0F) << 1) | (r[8] >> 7)];
        c[23] = Alphabet[(r[8] >> 2) & 0x1F];
        c[24] = Alphabet[((r[8] & 0x03) << 3) | (r[9] >> 5)];
        c[25] = Alphabet[r[9] & 0x1F];

        return new string(c);
    }
}

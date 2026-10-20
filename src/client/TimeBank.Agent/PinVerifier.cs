using System.Security.Cryptography;

namespace WillBeyond.TimeBank.Agent;

/// <summary>本地 PIN 校验（与服务端同算法：PBKDF2-SHA256 100k 轮）。</summary>
internal static class PinVerifier
{
    public static bool Verify(string pin, string stored)
    {
        try
        {
            var parts = stored.Split(':');
            if (parts.Length != 2) return false;
            var salt = Convert.FromHexString(parts[0]);
            var expected = Convert.FromHexString(parts[1]);
            var actual = Rfc2898DeriveBytes.Pbkdf2(pin, salt, 100_000, HashAlgorithmName.SHA256, 32);
            return CryptographicOperations.FixedTimeEquals(actual, expected);
        }
        catch
        {
            return false;
        }
    }
}

using System.Security.Cryptography;
using System.Text;

namespace PosSystem.Helpers;

/// <summary>
/// PBKDF2-SHA256 passwords. Old MD5 passwords from the WinForms system still work once
/// and are upgraded automatically at login.
/// </summary>
public static class PasswordHasher
{
    private const int Iterations = 100_000;

    public static string Hash(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(16);
        var key = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, 32);
        return $"PBKDF2${Iterations}${Convert.ToBase64String(salt)}${Convert.ToBase64String(key)}";
    }

    public static bool Verify(string password, string? storedHash, out bool needsUpgrade)
    {
        needsUpgrade = false;
        if (string.IsNullOrEmpty(storedHash)) return false;

        if (storedHash.StartsWith("PBKDF2$", StringComparison.Ordinal))
        {
            var parts = storedHash.Split('$');
            if (parts.Length != 4 || !int.TryParse(parts[1], out var iter)) return false;
            var salt = Convert.FromBase64String(parts[2]);
            var expected = Convert.FromBase64String(parts[3]);
            var actual = Rfc2898DeriveBytes.Pbkdf2(password, salt, iter, HashAlgorithmName.SHA256, expected.Length);
            return CryptographicOperations.FixedTimeEquals(actual, expected);
        }

        if (storedHash.Length == 32) // legacy MD5
        {
            var md5 = Convert.ToHexString(MD5.HashData(Encoding.UTF8.GetBytes(password.Trim())));
            if (string.Equals(md5, storedHash, StringComparison.OrdinalIgnoreCase)) { needsUpgrade = true; return true; }
        }
        return false;
    }
}

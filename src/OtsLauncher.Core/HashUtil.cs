using System.Security.Cryptography;

namespace OtsLauncher.Core;

public static class HashUtil
{
    public static string Sha256Stream(Stream stream)
    {
        var hash = SHA256.HashData(stream);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    public static string Sha256File(string path)
    {
        using var stream = File.OpenRead(path);
        return Sha256Stream(stream);
    }

    public static string Sha256Bytes(byte[] data)
    {
        var hash = SHA256.HashData(data);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    public static bool EqualsHex(string a, string b) =>
        string.Equals(a.Trim(), b.Trim(), StringComparison.OrdinalIgnoreCase);
}

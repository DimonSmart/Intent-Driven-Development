using System.Security.Cryptography;

internal static class FileHasher
{
    public static string Sha256(byte[] content) =>
        Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant();
}

using System.Security.Cryptography;

namespace Oxce.FixtureSupport;

public sealed record FileDigest(long Size, string Sha256)
{
    public static FileDigest Calculate(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 64 * 1024,
            FileOptions.SequentialScan);
        var hash = SHA256.HashData(stream);
        return new FileDigest(stream.Length, Convert.ToHexStringLower(hash));
    }
}

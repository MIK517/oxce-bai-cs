namespace Oxce.FixtureSupport;

public static class FixtureFile
{
    public static byte[] ReadAllBytes(string path, int maximumBytes)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumBytes);
        using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 1,
            FileOptions.SequentialScan);
        if (stream.Length > maximumBytes)
        {
            throw new InvalidDataException($"File '{path}' exceeds the {maximumBytes}-byte limit.");
        }

        var bytes = GC.AllocateUninitializedArray<byte>(checked((int)stream.Length));
        stream.ReadExactly(bytes);
        return bytes;
    }
}

using Oxce.Formats.Audio;
using Oxce.Formats.Binary;
using Oxce.Formats.Containers;
using Xunit;

namespace Oxce.CompatibilityTests;

public sealed class PrivateGmCatCorpusTests
{
    [Fact]
    public void OwnedUfoGmCatEntriesConvertToBoundedMidiFiles()
    {
        var root = FindRepositoryRoot();
        var path = Path.Combine(root, "data", "UFO", "SOUND", "GM.CAT");
        Assert.SkipUnless(File.Exists(path), "Owned UFO GM.CAT is not available in this checkout.");
        var archive = CatArchive.Parse(BinaryDataReader.FromFile(path));

        Assert.NotEmpty(archive.Entries);
        foreach (var entry in archive.Entries)
        {
            var midi = GmCatMusicCodec.DecodeEntry(entry.Data.Span);
            Assert.True(midi.AsSpan().StartsWith("MThd"u8));
            Assert.InRange(midi.Length, 26, GmCatMusicCodec.DefaultMaximumMidiBytes);
        }
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Oxce.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new DirectoryNotFoundException("Could not locate the repository root.");
    }
}

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
        var root = Oxce.FixtureSupport.FixturePaths.FindRepositoryRoot();
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

}

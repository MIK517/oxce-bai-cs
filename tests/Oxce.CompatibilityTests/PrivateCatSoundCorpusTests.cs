using Oxce.Formats.Audio;
using Oxce.Formats.Binary;
using Oxce.Formats.Containers;
using Xunit;

namespace Oxce.CompatibilityTests;

public sealed class PrivateCatSoundCorpusTests
{
    [Fact]
    public void OwnedUfoAndTftdCatSoundsDecodeWithinBounds()
    {
        var root = FindRepositoryRoot();
        var ufoSound = Path.Combine(root, "data", "UFO", "SOUND");
        var tftdSound = Path.Combine(root, "data", "TFTD", "SOUND");
        Assert.SkipUnless(
            Directory.Exists(ufoSound) && Directory.Exists(tftdSound),
            "Owned UFO and TFTD sound assets are not available in this checkout.");

        var ufoDecoded = 0;
        var ufoSkipped = 0;
        foreach (var name in new[] { "SAMPLE.CAT", "SAMPLE2.CAT", "SAMPLE3.CAT", "SOUND1.CAT", "SOUND2.CAT" })
        {
            var archive = CatArchive.Parse(BinaryDataReader.FromFile(Path.Combine(ufoSound, name)));
            foreach (var entry in archive.Entries)
            {
                if (SoundLength(entry.Data.Span) < 12)
                {
                    ufoSkipped++;
                    continue;
                }

                var audio = CatSoundCodec.DecodeEntry(entry.Data.Span, XcomSoundVariant.Ufo);
                Assert.Equal(checked(audio.FrameCount * audio.Channels), audio.Samples.Length);
                ufoDecoded++;
            }
        }

        var tftdArchive = CatArchive.Parse(BinaryDataReader.FromFile(Path.Combine(tftdSound, "SAMPLE.CAT")));
        foreach (var entry in tftdArchive.Entries)
        {
            var audio = CatSoundCodec.DecodeEntry(entry.Data.Span, XcomSoundVariant.Tftd);
            Assert.Equal(checked(audio.FrameCount * audio.Channels), audio.Samples.Length);
        }

        Assert.Equal(168, ufoDecoded);
        Assert.Equal(2, ufoSkipped);
        Assert.Equal(123, tftdArchive.Entries.Count);
    }

    private static int SoundLength(ReadOnlySpan<byte> entry)
    {
        if (entry.IsEmpty || 1 + entry[0] > entry.Length)
        {
            return -1;
        }

        return entry.Length - 1 - entry[0];
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

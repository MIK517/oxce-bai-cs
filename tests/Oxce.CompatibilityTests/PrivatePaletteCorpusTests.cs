using Oxce.Formats.Binary;
using Oxce.Formats.Images;
using Xunit;

namespace Oxce.CompatibilityTests;

public sealed class PrivatePaletteCorpusTests
{
    [Fact]
    public void OwnedUfoAndTftdPaletteFilesMatchExpectedBlockLayout()
    {
        var root = FindRepositoryRoot();
        var ufo = Path.Combine(root, "data", "UFO", "GEODATA");
        var tftd = Path.Combine(root, "data", "TFTD", "GEODATA");
        Assert.SkipUnless(
            Directory.Exists(ufo) && Directory.Exists(tftd),
            "Owned UFO and TFTD assets are not available in this checkout.");

        var ufoBattle = DecodePalette(Path.Combine(ufo, "PALETTES.DAT"), paletteIndex: 4);
        var tftdBattle = DecodePalette(Path.Combine(tftd, "PALETTES.DAT"), paletteIndex: 4);
        var ufoBackgrounds = XcomPaletteCodec.Decode(
            BinaryDataReader.FromFile(Path.Combine(ufo, "BACKPALS.DAT")),
            colorCount: 128);
        var tftdBackgrounds = XcomPaletteCodec.Decode(
            BinaryDataReader.FromFile(Path.Combine(tftd, "BACKPALS.DAT")),
            colorCount: 128);

        Assert.Equal(255, ufoBattle.Count(color => color.Alpha == byte.MaxValue));
        Assert.All(tftdBattle, color => Assert.Equal(0, color.Alpha));
        Assert.Equal(127, ufoBackgrounds.Count(color => color.Alpha == byte.MaxValue));
        Assert.Equal(127, tftdBackgrounds.Count(color => color.Alpha == byte.MaxValue));
    }

    private static Oxce.Core.Graphics.Rgba32[] DecodePalette(string path, int paletteIndex) =>
        XcomPaletteCodec.Decode(
            BinaryDataReader.FromFile(path),
            XcomPaletteCodec.ColorsPerPalette,
            XcomPaletteCodec.GetPaletteOffset(paletteIndex));

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

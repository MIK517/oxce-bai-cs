using Oxce.Formats.Binary;
using Oxce.Formats.Images;
using Xunit;

namespace Oxce.CompatibilityTests;

public sealed class PrivatePckCorpusTests
{
    [Fact]
    public void OwnedUfoAndTftdSpriteSetsDecode()
    {
        var root = FindRepositoryRoot();
        var ufoRoot = Path.Combine(root, "data", "UFO", "UFOGRAPH");
        var tftdRoot = Path.Combine(root, "data", "TFTD", "UFOGRAPH");
        Assert.SkipUnless(
            Directory.Exists(ufoRoot) && Directory.Exists(tftdRoot),
            "Owned UFO and TFTD assets are not available in this checkout.");

        var ufoFrames = DecodePair(ufoRoot, "X1", width: 128, height: 64);
        var tftdFrames = DecodePair(tftdRoot, "CURSOR", width: 32, height: 40);

        Assert.Equal(8, ufoFrames.Count);
        Assert.Equal(17, tftdFrames.Count);
        Assert.Contains(ufoFrames, frame => frame.Any(value => value != 0));
        Assert.Contains(tftdFrames, frame => frame.Any(value => value != 0));
    }

    [Fact]
    public void TwoByteRosigmaTabDecodesOneFrame()
    {
        var root = FindRepositoryRoot();
        var terrainRoot = Path.Combine(root, "fixtures", "private", "mods", "rosigma", "TERRAIN");
        var pckPath = Path.Combine(terrainRoot, "GUARD_GRAV_DROP.PCK");
        var tabPath = Path.Combine(terrainRoot, "GUARD_GRAV_DROP.TAB");
        Assert.SkipUnless(
            File.Exists(pckPath) && File.Exists(tabPath),
            "The private Rosigma corpus is not available in this checkout.");

        var frames = PckSpriteSetCodec.Decode(
            BinaryDataReader.FromFile(pckPath),
            BinaryDataReader.FromFile(tabPath),
            width: 32,
            height: 40);

        Assert.Single(frames);
        Assert.Equal(32 * 40, frames[0].Length);
    }

    private static IReadOnlyList<byte[]> DecodePair(string directory, string name, int width, int height) =>
        PckSpriteSetCodec.Decode(
            BinaryDataReader.FromFile(Path.Combine(directory, name + ".PCK")),
            BinaryDataReader.FromFile(Path.Combine(directory, name + ".TAB")),
            width,
            height);

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

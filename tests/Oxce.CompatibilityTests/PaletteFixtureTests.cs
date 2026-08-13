using System.Text.Json;
using Oxce.Core.Graphics;
using Oxce.FixtureSupport;
using Oxce.Formats.Binary;
using Oxce.Formats.Images;
using Xunit;

namespace Oxce.CompatibilityTests;

public sealed class PaletteFixtureTests
{
    [Fact]
    public void OriginalPaletteRulesMatchCapturedCppReference()
    {
        var root = FindRepositoryRoot();
        var manifestPath = Path.Combine(root, "fixtures", "manifests", "xcom-palettes.json");
        var manifest = FixtureManifestLoader.Load(manifestPath);
        FixtureManifestVerifier.VerifyFiles(manifest, root);
        var fixture = File.ReadLines(Path.GetFullPath(manifest.Inputs[0].Path, root))
            .Where(line => line.Length != 0)
            .Select(line => line.Split('=', 2))
            .ToDictionary(values => values[0], values => values[1], StringComparer.Ordinal);
        var count = int.Parse(fixture["colorCount"], System.Globalization.CultureInfo.InvariantCulture);
        var first = Convert.FromHexString(fixture["first"]);
        var second = Convert.FromHexString(fixture["second"]);
        var data = new byte[XcomPaletteCodec.GetPaletteOffset(1) + second.Length];
        first.CopyTo(data, 0);
        second.CopyTo(data, XcomPaletteCodec.GetPaletteOffset(1));

        var actual = JsonSerializer.SerializeToUtf8Bytes(new
        {
            block14Offset = XcomPaletteCodec.GetColorBlockOffset(14),
            first = ToArrays(XcomPaletteCodec.Decode(new BinaryDataReader(data), count)),
            missing = ToArrays(XcomPaletteCodec.Decode(
                new BinaryDataReader(data),
                count,
                XcomPaletteCodec.GetPaletteOffset(2))),
            palette4Offset = XcomPaletteCodec.GetPaletteOffset(4),
            second = ToArrays(XcomPaletteCodec.Decode(
                new BinaryDataReader(data),
                count,
                XcomPaletteCodec.GetPaletteOffset(1))),
        });
        var expected = File.ReadAllBytes(Path.GetFullPath(manifest.Expected, root));

        Assert.True(CanonicalJson.SemanticallyEquals(expected, actual));
    }

    private static int[][] ToArrays(IEnumerable<Rgba32> colors) =>
        colors.Select(color => new[]
        {
            (int)color.Red,
            color.Green,
            color.Blue,
            color.Alpha,
        }).ToArray();

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

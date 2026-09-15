using System.Text.Json;
using Oxce.Core.Graphics;
using Oxce.FixtureSupport;
using Oxce.Formats.Binary;
using Oxce.Formats.Images;
using Oxce.TestSupport;
using Xunit;

namespace Oxce.CompatibilityTests;

public sealed class PaletteFixtureTests
{
    [Fact]
    public void OriginalPaletteRulesMatchCapturedCppReference()
    {
        var (root, manifest) = TestFixtures.LoadVerifiedManifest("xcom-palettes");
        var fixture = TestFixtures.ReadKeyValues(Path.GetFullPath(manifest.Inputs[0].Path, root));
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

        Assert.Equal(CanonicalJson.Normalize(expected), CanonicalJson.Normalize(actual));
    }

    private static int[][] ToArrays(IEnumerable<Rgba32> colors) =>
        colors.Select(color => new[]
        {
            (int)color.Red,
            color.Green,
            color.Blue,
            color.Alpha,
        }).ToArray();
}

using System.Text.Json;
using Oxce.FixtureSupport;
using Oxce.Formats.Binary;
using Oxce.Formats.Images;
using Oxce.TestSupport;
using Xunit;

namespace Oxce.CompatibilityTests;

public sealed class IndexedImageFixtureTests
{
    [Fact]
    public void PckTabSpritesMatchCapturedCppReference()
    {
        var (root, manifest) = TestFixtures.LoadVerifiedManifest("pck-tab-sprites");
        var fixture = TestFixtures.ReadKeyValues(Path.GetFullPath(manifest.Inputs[0].Path, root));
        var width = int.Parse(fixture["width"], System.Globalization.CultureInfo.InvariantCulture);
        var height = int.Parse(fixture["height"], System.Globalization.CultureInfo.InvariantCulture);
        var pck = Convert.FromHexString(fixture["pck"]);

        var noTab = PckSpriteSetCodec.Decode(new BinaryDataReader(pck), null, width, height);
        var tab16 = PckSpriteSetCodec.Decode(
            new BinaryDataReader(pck),
            Reader(fixture["tab16"]),
            width,
            height);
        var tab32 = PckSpriteSetCodec.Decode(
            new BinaryDataReader(pck),
            Reader(fixture["tab32"]),
            width,
            height);
        var actual = JsonSerializer.SerializeToUtf8Bytes(new
        {
            height,
            noTab = ToIntegers(noTab),
            tab16 = ToIntegers(tab16),
            tab32 = ToIntegers(tab32),
            width,
        });
        var expected = File.ReadAllBytes(Path.GetFullPath(manifest.Expected, root));

        Assert.Equal(CanonicalJson.Normalize(expected), CanonicalJson.Normalize(actual));
    }

    [Fact]
    public void ScreenCodecsMatchCapturedCppReference()
    {
        var (root, manifest) = TestFixtures.LoadVerifiedManifest("indexed-screen-codecs");
        var fixture = TestFixtures.ReadKeyValues(Path.GetFullPath(manifest.Inputs[0].Path, root));
        var width = int.Parse(fixture["width"], System.Globalization.CultureInfo.InvariantCulture);
        var height = int.Parse(fixture["height"], System.Globalization.CultureInfo.InvariantCulture);

        var raw = Decode(width, height, fixture["raw"], RawIndexedImageCodec.Decode);
        var spk = Decode(width, height, fixture["spk"], SpkImageCodec.Decode);
        var bdy = new byte[checked(width * height)];
        BdyImageCodec.Decode(Reader(fixture["bdy"]), bdy, width);
        var actual = JsonSerializer.SerializeToUtf8Bytes(new
        {
            bdy = bdy.Select(value => (int)value).ToArray(),
            height,
            raw = raw.Select(value => (int)value).ToArray(),
            spk = spk.Select(value => (int)value).ToArray(),
            width,
        });
        var expected = File.ReadAllBytes(Path.GetFullPath(manifest.Expected, root));

        Assert.Equal(CanonicalJson.Normalize(expected), CanonicalJson.Normalize(actual));
    }

    private static byte[] Decode(
        int width,
        int height,
        string hex,
        DecodeAction decode)
    {
        var pixels = new byte[checked(width * height)];
        decode(Reader(hex), pixels);
        return pixels;
    }

    private static BinaryDataReader Reader(string hex) => new(Convert.FromHexString(hex));

    private static int[][] ToIntegers(IReadOnlyList<byte[]> frames) =>
        frames.Select(frame => frame.Select(value => (int)value).ToArray()).ToArray();

    private delegate void DecodeAction(BinaryDataReader input, Span<byte> destination);
}

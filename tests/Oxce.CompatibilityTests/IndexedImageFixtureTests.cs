using System.Text.Json;
using Oxce.FixtureSupport;
using Oxce.Formats.Binary;
using Oxce.Formats.Images;
using Xunit;

namespace Oxce.CompatibilityTests;

public sealed class IndexedImageFixtureTests
{
    [Fact]
    public void ScreenCodecsMatchCapturedCppReference()
    {
        var root = FindRepositoryRoot();
        var manifestPath = Path.Combine(root, "fixtures", "manifests", "indexed-screen-codecs.json");
        var manifest = FixtureManifestLoader.Load(manifestPath);
        FixtureManifestVerifier.VerifyFiles(manifest, root);
        var fixturePath = Path.GetFullPath(manifest.Inputs[0].Path, root);
        var fixture = File.ReadLines(fixturePath)
            .Where(line => line.Length != 0)
            .Select(line => line.Split('=', 2))
            .ToDictionary(values => values[0], values => values[1], StringComparer.Ordinal);
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

        Assert.True(CanonicalJson.SemanticallyEquals(expected, actual));
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

    private delegate void DecodeAction(BinaryDataReader input, Span<byte> destination);

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

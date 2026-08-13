using System.Text.Json;
using Oxce.FixtureSupport;
using Oxce.Rendering;
using Xunit;

namespace Oxce.CompatibilityTests;

public sealed class IndexedSurfaceFixtureTests
{
    [Fact]
    public void ColorTransformsAndShadingMatchCapturedCppReference()
    {
        var root = FindRepositoryRoot();
        var manifestPath = Path.Combine(root, "fixtures", "manifests", "indexed-surface-operations.json");
        var manifest = FixtureManifestLoader.Load(manifestPath);
        FixtureManifestVerifier.VerifyFiles(manifest, root);
        var input = Convert.FromHexString(
            File.ReadAllText(Path.GetFullPath(manifest.Inputs[0].Path, root)).Trim());
        var transformed = CreateSurface(input.AsSpan(0, 4));
        transformed.OffsetColorBlocks(2, 16);
        transformed.InvertColors(0x28);
        var standardSource = CreateSurface(input.AsSpan(4, 6));
        var standard = new IndexedSurface(6, 1);
        standard.Clear(8);
        standard.BlitShaded(standardSource, 0, 0, shade: 2);
        var replacementSource = CreateSurface(input.AsSpan(10, 6));
        var replacement = new IndexedSurface(6, 1);
        replacement.Clear(8);
        replacement.BlitShaded(replacementSource, 0, 0, shade: 2, replacementColorGroup: 5);
        var actual = JsonSerializer.SerializeToUtf8Bytes(new
        {
            replacementShade = Convert.ToHexString(replacement.Pixels),
            standardShade = Convert.ToHexString(standard.Pixels),
            transformed = Convert.ToHexString(transformed.Pixels),
        });
        var expected = File.ReadAllBytes(Path.GetFullPath(manifest.Expected, root));

        Assert.True(CanonicalJson.SemanticallyEquals(expected, actual));
    }

    private static IndexedSurface CreateSurface(ReadOnlySpan<byte> pixels)
    {
        var surface = new IndexedSurface(pixels.Length, 1);
        pixels.CopyTo(surface.Pixels);
        return surface;
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

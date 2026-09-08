using System.Text.Json;
using Oxce.FixtureSupport;
using Oxce.Formats.Binary;
using Oxce.Formats.Containers;
using Xunit;

namespace Oxce.CompatibilityTests;

public sealed class CatFixtureTests
{
    [Fact]
    public void EntryBoundariesMatchCapturedCppReference()
    {
        var root = Oxce.FixtureSupport.FixturePaths.FindRepositoryRoot();
        var manifestPath = Path.Combine(root, "fixtures", "manifests", "cat-entries.json");
        var manifest = FixtureManifestLoader.Load(manifestPath);
        FixtureManifestVerifier.VerifyFiles(manifest, root);
        var fixturePath = Path.GetFullPath(manifest.Inputs[0].Path, root);
        var archive = CatArchive.Parse(
            new BinaryDataReader(Convert.FromHexString(File.ReadAllText(fixturePath).Trim())));
        var actual = JsonSerializer.SerializeToUtf8Bytes(new
        {
            entries = archive.Entries.Select(entry => new
            {
                data = Convert.ToHexString(entry.Data.Span),
                length = entry.Data.Length,
                offset = entry.Offset,
            }),
        });
        var expected = File.ReadAllBytes(Path.GetFullPath(manifest.Expected, root));

        Assert.True(CanonicalJson.SemanticallyEquals(expected, actual));
    }

}

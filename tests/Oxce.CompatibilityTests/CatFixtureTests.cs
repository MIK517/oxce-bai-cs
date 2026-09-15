using System.Text.Json;
using Oxce.FixtureSupport;
using Oxce.Formats.Binary;
using Oxce.Formats.Containers;
using Oxce.TestSupport;
using Xunit;

namespace Oxce.CompatibilityTests;

public sealed class CatFixtureTests
{
    [Fact]
    public void EntryBoundariesMatchCapturedCppReference()
    {
        var (root, manifest) = TestFixtures.LoadVerifiedManifest("cat-entries");
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

        Assert.Equal(CanonicalJson.Normalize(expected), CanonicalJson.Normalize(actual));
    }
}

using System.Text.Json;
using Oxce.FixtureSupport;
using Oxce.Mods.Files;
using Xunit;

namespace Oxce.CompatibilityTests;

public sealed class VirtualFileCatalogFixtureTests
{
    [Fact]
    public void LayeredCatalogMatchesCapturedCppReference()
    {
        var root = FindRepositoryRoot();
        var manifestPath = Path.Combine(root, "fixtures", "manifests", "vfs-layered-catalog.json");
        var manifest = FixtureManifestLoader.Load(manifestPath);
        FixtureManifestVerifier.VerifyFiles(manifest, root);
        var fixturePath = Path.GetFullPath(manifest.Inputs[0].Path, root);
        var rows = File.ReadLines(fixturePath)
            .Where(line => line.Length != 0)
            .Select(ParseRow)
            .ToArray();
        var layers = rows
            .GroupBy(row => (row.LayerId, row.ModId))
            .Select(group => VirtualFileLayer.FromEntries(
                new VirtualFileProvenance(
                    group.Key.LayerId,
                    group.Key.ModId.Length == 0 ? null : group.Key.ModId,
                    $"fixture:{group.Key.LayerId}"),
                group.Select(row => new VirtualFileSource(row.RelativePath, row.SourcePath))))
            .ToArray();
        var catalog = new VirtualFileCatalog(layers);
        var winner = catalog.GetRequired("GEOGRAPH/world.dat");
        var actual = JsonSerializer.SerializeToUtf8Bytes(new
        {
            language = catalog.List("language"),
            rulesets = catalog.Rulesets.Select(entry => entry.SourcePath).ToArray(),
            slice = catalog.GetSlice("GEOGRAPH/world.dat").Select(entry => entry?.SourcePath).ToArray(),
            winner = new
            {
                layer = winner.Provenance.LayerId,
                mod = winner.Provenance.ModId,
                source = winner.SourcePath,
            },
        });
        var expected = File.ReadAllBytes(Path.GetFullPath(manifest.Expected, root));

        Assert.True(CanonicalJson.SemanticallyEquals(expected, actual));
    }

    private static FixtureRow ParseRow(string line)
    {
        var values = line.Split('\t');
        if (values.Length != 4)
        {
            throw new InvalidDataException("VFS fixture rows must contain four tab-separated columns.");
        }

        return new FixtureRow(values[0], values[1], values[2], values[3]);
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

    private sealed record FixtureRow(string LayerId, string ModId, string RelativePath, string SourcePath);
}

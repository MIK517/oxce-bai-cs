using System.Text.Json;
using Oxce.Core.Geometry;
using Oxce.FixtureSupport;
using Oxce.TestSupport;
using Xunit;

namespace Oxce.CompatibilityTests;

public sealed class FixturePipelineTests
{
    [Fact]
    public void EveryManifestIsValidAndReferencesPinnedFiles()
    {
        var root = Oxce.FixtureSupport.FixturePaths.FindRepositoryRoot();
        var manifests = Directory.EnumerateFiles(
                Path.Combine(root, "fixtures", "manifests"),
                "*.json",
                SearchOption.TopDirectoryOnly)
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.NotEmpty(manifests);
        foreach (var path in manifests)
        {
            var manifest = FixtureManifestLoader.Load(path);
            FixtureManifestVerifier.VerifyFiles(manifest, root);
        }
    }

    [Fact]
    public void EveryExpectedOutputBelongsToExactlyOneManifest()
    {
        // An oracle without a manifest would be read without verifying its pinned inputs.
        var root = Oxce.FixtureSupport.FixturePaths.FindRepositoryRoot();
        var owners = Directory.EnumerateFiles(Path.Combine(root, "fixtures", "manifests"), "*.json")
            .Order(StringComparer.Ordinal)
            .Select(FixtureManifestLoader.Load)
            .GroupBy(static manifest => manifest.Expected, StringComparer.Ordinal)
            .ToDictionary(static group => group.Key, static group => group.Count(), StringComparer.Ordinal);
        var outputs = Directory.EnumerateFiles(Path.Combine(root, "fixtures", "expected"), "*", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(root, path).Replace('\\', '/'))
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.NotEmpty(outputs);
        Assert.All(outputs, output => Assert.Equal(1, owners.GetValueOrDefault(output)));
        Assert.All(owners.Keys, expected => Assert.Contains(expected, outputs));
    }

    [Fact]
    public void BootstrapFixtureNormalizesToExpectedOutput()
    {
        var (root, manifest) = TestFixtures.LoadVerifiedManifest("bootstrap-json");

        var input = File.ReadAllBytes(Path.GetFullPath(manifest.Inputs[0].Path, root));
        var expected = File.ReadAllText(Path.GetFullPath(manifest.Expected, root));

        Assert.Equal(expected, CanonicalJson.Normalize(input));
    }

    [Fact]
    public void PositionRulesMatchCapturedCppReference()
    {
        var (root, manifest) = TestFixtures.LoadVerifiedManifest("core-position");

        var tile = new Position3(2, -3, 4);
        var clipped = new Position3(-17, 31, -25).ClipVoxel();
        var containingTile = new Position3(-17, 31, -25).ToTile();
        var voxel = tile.ToVoxel();
        var actual = JsonSerializer.SerializeToUtf8Bytes(new
        {
            clipVoxel = new[] { (int)clipped.X, clipped.Y, clipped.Z },
            distance2d = Position3.Distance2D(new Position3(0, 0, 0), new Position3(2, 2, 0)),
            distanceSquared = Position3.DistanceSquared(new Position3(1, 2, 3), new Position3(4, 6, 3)),
            positionSize = sizeof(short) * 3,
            toTile = new[] { (int)containingTile.X, containingTile.Y, containingTile.Z },
            toVoxel = new[] { (int)voxel.X, voxel.Y, voxel.Z },
        });
        var expected = File.ReadAllText(Path.GetFullPath(manifest.Expected, root));

        Assert.Equal(expected, CanonicalJson.Normalize(actual));
    }
}

using System.Text.Json;
using Oxce.Core.Geometry;
using Oxce.FixtureSupport;
using Xunit;

namespace Oxce.CompatibilityTests;

public sealed class FixturePipelineTests
{
    [Fact]
    public void BootstrapFixtureNormalizesToExpectedOutput()
    {
        var root = FindRepositoryRoot();
        var manifestPath = Path.Combine(root, "fixtures", "manifests", "bootstrap-json.json");
        var manifest = FixtureManifestLoader.Load(manifestPath);
        FixtureManifestVerifier.VerifyFiles(manifest, root);

        var input = File.ReadAllBytes(Path.GetFullPath(manifest.Inputs[0].Path, root));
        var expected = File.ReadAllText(Path.GetFullPath(manifest.Expected, root));

        Assert.Equal(expected, CanonicalJson.Normalize(input));
    }

    [Fact]
    public void PositionRulesMatchCapturedCppReference()
    {
        var root = FindRepositoryRoot();
        var manifestPath = Path.Combine(root, "fixtures", "manifests", "core-position.json");
        var manifest = FixtureManifestLoader.Load(manifestPath);
        FixtureManifestVerifier.VerifyFiles(manifest, root);

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

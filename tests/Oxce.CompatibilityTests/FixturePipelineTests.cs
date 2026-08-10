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

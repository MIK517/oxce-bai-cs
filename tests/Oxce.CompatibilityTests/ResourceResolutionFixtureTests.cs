using System.Text.Json;
using Oxce.FixtureSupport;
using Oxce.Mods.Discovery;
using Oxce.Mods.Loading;
using Oxce.Mods.Resources;
using Oxce.Mods.Rulesets;
using Oxce.Mods.Rulesets.Content;
using Xunit;

namespace Oxce.CompatibilityTests;

public sealed class ResourceResolutionFixtureTests
{
    [Fact]
    public void MultiModResourcesResolveToPinnedDescriptorsAndOffsets()
    {
        var root = FindRepositoryRoot();
        var manifest = FixtureManifestLoader.Load(
            Path.Combine(root, "fixtures", "manifests", "resource-resolution.json"));
        FixtureManifestVerifier.VerifyFiles(manifest, root);
        var fixture = Path.Combine(root, "fixtures", "public", "mods", "resource-resolution");
        var discovery = ModDiscovery.ScanDirectory(fixture);
        var plan = ModLoadPlanner.Create(
            ModCatalog.Create(discovery.Mods),
            [new ModActivation("resource-master", true), new ModActivation("resource-addon", true)],
            "resource-master",
            new ModEngineIdentity("Extended", "8.6.1.0"));

        var snapshot = ContentSnapshotBuilder.Build(plan, options: new ContentSnapshotOptions
        {
            ResourceResolution = new ResourceResolutionOptions
            {
                SharedSpriteCounts = new Dictionary<string, int> { ["SET.PCK"] = 2 },
                SharedSoundCounts = new Dictionary<string, int> { ["SET.CAT"] = 1 },
            },
        });
        var actual = JsonSerializer.SerializeToUtf8Bytes(new
        {
            stage = snapshot.Capabilities.Has(ContentLoadStage.ResourcesResolved) ? "resources-resolved" : "failed",
            descriptors = snapshot.Content.Resources.Descriptors.Select(descriptor => new
            {
                descriptor.Id,
                kind = descriptor.Kind.ToString(),
                descriptor.CanonicalPath,
                descriptor.Provenance.ModId,
                policy = descriptor.LoadPolicy.ToString(),
                descriptor.RuntimeIndex,
                descriptor.Width,
                descriptor.Height,
                descriptor.OwnerSection,
                descriptor.OwnerId,
            }),
        });
        var expected = File.ReadAllBytes(Path.GetFullPath(manifest.Expected, root));

        Assert.DoesNotContain(snapshot.Diagnostics,
            item => item.Severity >= Oxce.Core.Diagnostics.DiagnosticSeverity.Error);
        Assert.Equal(CanonicalJson.Normalize(expected), CanonicalJson.Normalize(actual));
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

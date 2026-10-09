using Oxce.Mods.Bootstrap;
using Oxce.Mods.Rulesets.Content;
using Oxce.TestSupport;
using Xunit;

namespace Oxce.CompatibilityTests;

internal static class StrategicReadinessTestContent
{
    public static RuntimeContent Load(string overlay = "strategic-bases.rul", bool fromCache = false)
        => Load([overlay], fromCache);

    public static RuntimeContent Load(IReadOnlyList<string> overlays, bool fromCache = false, string? extraRules = null,
        bool extraRulesAfterOverlays = false)
    {
        var repository = Oxce.FixtureSupport.FixturePaths.FindRepositoryRoot();
        var root = Path.Combine(Path.GetTempPath(), $"oxce-readiness-content-{Guid.NewGuid():N}");
        try
        {
            TestFixtures.CopyDirectory(Path.Combine(repository, "fixtures/public/mods/strategic-logistics"), Path.Combine(root, "standard"));
            Directory.CreateDirectory(Path.Combine(root, "user/mods"));
            foreach (var overlay in overlays)
            {
                var targetName = $"aa-{Path.GetFileName(overlay).Replace("strategic-", "", StringComparison.Ordinal)}";
                File.Copy(Path.Combine(repository, "fixtures/public/savegames", overlay),
                    Path.Combine(root, "standard/logistics/Ruleset", targetName));
            }
            if (extraRules is not null)
                // The reference loads ruleset filenames in descending order.
                File.WriteAllText(Path.Combine(root, "standard/logistics/Ruleset",
                    extraRulesAfterOverlays ? "a0-generated-test.rul" : "zz-generated-test.rul"), extraRules);
            var request = InstallationLoadRequest.ForMasterAndAddOn(root, "logistics", "-", TestFixtures.Engine);
            var loaded = InstallationContentLoader.Load(request, cancellationToken: TestContext.Current.CancellationToken);
            if (fromCache)
                loaded = InstallationContentLoader.Load(request, cancellationToken: TestContext.Current.CancellationToken);
            Assert.True(loaded.IsSuccess, loaded.DescribeFailure());
            if (fromCache) Assert.Equal(CompiledContentCacheStatus.Hit, loaded.CacheStatus);
            return loaded.Content!;
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }
    }
}

using Oxce.Mods.Bootstrap;
using Oxce.Mods.Rulesets.Content;
using Oxce.TestSupport;
using Xunit;

namespace Oxce.CompatibilityTests;

internal static class StrategicReadinessTestContent
{
    public static RuntimeContent Load(string overlay = "strategic-bases.rul")
        => Load([overlay]);

    public static RuntimeContent Load(IReadOnlyList<string> overlays, string? extraRules = null,
        bool extraRulesAfterOverlays = false, string? finalRules = null, IReadOnlyDictionary<string, byte[]>? files = null)
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
            if (finalRules is not null)
                File.WriteAllText(Path.Combine(root, "standard/logistics/Ruleset", "a--final-test.rul"), finalRules);
            // Extra installation files (for example terrain data), by path below the master mod.
            foreach (var (path, data) in files ?? new Dictionary<string, byte[]>())
            {
                var target = Path.Combine(root, "standard/logistics", path);
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.WriteAllBytes(target, data);
            }
            var request = InstallationLoadRequest.ForMasterAndAddOn(root, "logistics", "-", TestFixtures.Engine);
            // Every load is built fresh, then restored from the compiled cache it wrote. The restored content
            // must be indistinguishable from the fresh build; tests then run against the restored content.
            var fresh = InstallationContentLoader.Load(request, cancellationToken: TestContext.Current.CancellationToken);
            Assert.True(fresh.IsSuccess, fresh.DescribeFailure());
            var loaded = InstallationContentLoader.Load(request, cancellationToken: TestContext.Current.CancellationToken);
            Assert.True(loaded.IsSuccess, loaded.DescribeFailure());
            Assert.Equal(CompiledContentCacheStatus.Hit, loaded.CacheStatus);
            AssertSameContent(fresh.Content!, loaded.Content!);
            return loaded.Content!;
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }
    }

    private static void AssertSameContent(RuntimeContent fresh, RuntimeContent restored)
    {
        var expected = ContentStateDump.Of(fresh);
        var actual = ContentStateDump.Of(restored);
        if (expected == actual) return;
        var index = 0;
        while (index < Math.Min(expected.Length, actual.Length) && expected[index] == actual[index]) index++;
        var start = Math.Max(0, index - 300);
        Assert.Fail($"Cached content differs from the fresh build at {index}:\nfresh:  {expected[start..Math.Min(expected.Length, index + 200)]}\ncached: {actual[start..Math.Min(actual.Length, index + 200)]}");
    }
}

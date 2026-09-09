using Oxce.Mods.Bootstrap;
using Oxce.Mods.Rulesets.Content;
using Xunit;

namespace Oxce.CompatibilityTests;

internal static class StrategicReadinessTestContent
{
    public static RuntimeContent Load(string overlay = "strategic-bases.rul", bool fromCache = false)
    {
        var repository = Oxce.FixtureSupport.FixturePaths.FindRepositoryRoot();
        var root = Path.Combine(Path.GetTempPath(), $"oxce-readiness-content-{Guid.NewGuid():N}");
        try
        {
            CopyDirectory(Path.Combine(repository, "fixtures/public/mods/strategic-logistics"), Path.Combine(root, "standard"));
            Directory.CreateDirectory(Path.Combine(root, "user/mods"));
            var targetName = $"aa-{Path.GetFileName(overlay).Replace("strategic-", "", StringComparison.Ordinal)}";
            File.Copy(Path.Combine(repository, "fixtures/public/savegames", overlay),
                Path.Combine(root, "standard/logistics/Ruleset", targetName));
            var request = InstallationLoadRequest.ForMasterAndAddOn(root, "logistics", "-", new("Extended", "8.6.1.0"));
            var loaded = InstallationContentLoader.Load(request, cancellationToken: TestContext.Current.CancellationToken);
            if (fromCache)
                loaded = InstallationContentLoader.Load(request, cancellationToken: TestContext.Current.CancellationToken);
            Assert.True(loaded.IsSuccess, loaded.DescribeFailure());
            if (fromCache) Assert.Equal(CompiledContentCacheStatus.Hit, loaded.CacheStatus);
            return loaded.Content!;
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }
    }

    private static void CopyDirectory(string source, string destination)
    {
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var target = Path.Combine(destination, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target);
        }
    }
}

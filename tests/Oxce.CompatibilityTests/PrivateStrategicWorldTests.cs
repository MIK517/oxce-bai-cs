using System.Text.Json;
using Oxce.Core.Random;
using Oxce.Gameplay.Campaigns;
using Oxce.Mods.Bootstrap;
using Oxce.Mods.Rulesets.Content;
using Oxce.Savegames.Oxce;
using Oxce.TestSupport;
using Xunit;

namespace Oxce.CompatibilityTests;

public sealed class PrivateStrategicWorldTests
{
    [Fact]
    public void ImportedWorldContinuationHasEquivalentFreshAndCachedClassifications()
    {
        var installation = TestFixtures.RepositoryPath("artifacts", "private-install");
        Assert.SkipUnless(File.Exists(Path.Combine(installation, ".oxce-private-install-manifest.json")),
            "Owned installation is not staged.");
        List<ContinuationResult> results = [];
        foreach (var (master, addon, family, count) in new[]
        {
            ("xcom1", "-", "vanilla-ufo", 7), ("xcom2", "-", "vanilla-tftd", 5),
            ("40k", "40k_ROSIGMA_edits", "modded/rosigma", 7),
        })
        {
            var request = InstallationLoadRequest.ForMasterAndAddOn(installation, master, addon, new("Extended", "8.6.1.0"));
            var fresh = InstallationContentLoader.Load(request, new() { Cache = new() { Enabled = false } },
                cancellationToken: TestContext.Current.CancellationToken);
            Assert.True(fresh.IsSuccess, fresh.DescribeFailure());
            var seed = InstallationContentLoader.Load(request, cancellationToken: TestContext.Current.CancellationToken);
            Assert.True(seed.IsSuccess, seed.DescribeFailure());
            var cached = InstallationContentLoader.Load(request, cancellationToken: TestContext.Current.CancellationToken);
            Assert.True(cached.IsSuccess, cached.DescribeFailure());
            Assert.Equal(CompiledContentCacheStatus.Hit, cached.CacheStatus);
            var first = Exercise(fresh.Content!, request, family);
            Assert.Equal(count, first.Length);
            Assert.Equivalent(first, Exercise(cached.Content!, request, family), strict: true);
            results.AddRange(first);
        }
        Assert.Equal(19, results.Count);
        Assert.Equal(6, results.Count(result => result.Outcome == "executable"));
        Assert.Equal(5, results.Count(result => result.Outcome == "blocked"));
        Assert.Equal(8, results.Count(result => result.Outcome == "preservation-only"));
        foreach (var result in results)
        {
            var expected = (result.Family, result.Name) switch
            {
                ("vanilla-ufo", "early/Begining.sav") => (125, "Alien mission wave spawning requires world simulation."),
                ("modded/rosigma", "early/Begining.sav") => (0, "Strategic event scheduling requires world simulation."),
                ("modded/rosigma", "early/Early.sav" or "early/Middle.sav" or "early/_autogeo_.asav") =>
                    (0, "UFO shield handling requires world simulation."),
                (_, var name) when name.StartsWith("battlescape/", StringComparison.Ordinal) =>
                    (0, "Active tactical battle is preserved; tactical continuation is unavailable."),
                _ => (360, (string?)null),
            };
            Assert.Equal(expected, (result.Ticks, result.Reason));
        }
        var path = TestFixtures.RepositoryPath("artifacts", "world-closure-corpus.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(results));
    }

    private static ContinuationResult[] Exercise(RuntimeContent content, InstallationLoadRequest request, string family)
    {
        var directory = TestFixtures.RepositoryPath("fixtures", "private", "saves", family);
        Assert.True(Directory.Exists(directory));
        var options = new OxceSaveLoadOptions(request.MasterId, request.ActiveMods.ToHashSet(StringComparer.Ordinal),
            new Oxce.Formats.Yaml.YamlReadOptions { MaxBytes = 32 * 1024 * 1024, MaxNodes = 2_000_000 });
        List<ContinuationResult> results = [];
        foreach (var file in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories)
            .Where(path => Path.GetExtension(path).Equals(".sav", StringComparison.OrdinalIgnoreCase) ||
                Path.GetExtension(path).Equals(".asav", StringComparison.OrdinalIgnoreCase)).Order(StringComparer.Ordinal))
        {
            var loaded = OxceSaveAdapter.LoadFile(file, content, new SplitMix64RandomSource(17), options);
            var initial = loaded.Campaign.Capture();
            var replay = OxceSaveAdapter.Load(OxceSaveAdapter.EmitLoadedCampaign(initial, loaded.Source),
                file, content, new SplitMix64RandomSource(17), options).Campaign;
            Assert.Equivalent(initial, replay.Capture(), strict: true);
            var ticks = 0;
            string? reason = null;
            while (ticks < 360)
            {
                var before = loaded.Campaign.Capture();
                var result = loaded.Campaign.Execute(new AdvanceCampaignTime(360 - ticks));
                Assert.Equivalent(result, replay.Execute(new AdvanceCampaignTime(360 - ticks)), strict: true);
                Assert.Equivalent(loaded.Campaign.Capture(), replay.Capture(), strict: true);
                var advanced = result.Events.OfType<CampaignTimeAdvanced>().SingleOrDefault()?.Summary.TickCount ?? 0;
                ticks += advanced;
                var blocked = result.Events.OfType<CampaignActionBlocked>().SingleOrDefault();
                if (blocked is null)
                {
                    Assert.True(advanced > 0, "Continuation neither advanced nor reported a guard.");
                    continue; // Resume ordinary notifications to complete the thirty-minute horizon.
                }
                reason = blocked.Reason;
                Assert.False(string.IsNullOrWhiteSpace(reason));
                var stopped = loaded.Campaign.Capture();
                if (advanced == 0) Assert.Equivalent(before, stopped, strict: true);
                var retry = loaded.Campaign.Execute(new AdvanceCampaignTime(1));
                Assert.Equal(reason, Assert.Single(retry.Events.OfType<CampaignActionBlocked>()).Reason);
                Assert.Equivalent(stopped, loaded.Campaign.Capture(), strict: true);
                break;
            }
            var after = loaded.Campaign.Capture();
            var rewritten = OxceSaveAdapter.EmitLoadedCampaign(after, loaded.Source);
            var restored = OxceSaveAdapter.Load(rewritten, file, content, new SplitMix64RandomSource(17), options);
            Assert.Equivalent(after, restored.Campaign.Capture(), strict: true);
            // Repeated loaded rewrites must retain opaque world/tactical sidecars too.
            Assert.Equal(rewritten, OxceSaveAdapter.EmitLoadedCampaign(restored.Campaign.Capture(), restored.Source));
            var outcome = reason is null ? "executable" : reason ==
                "Active tactical battle is preserved; tactical continuation is unavailable." ? "preservation-only" : "blocked";
            results.Add(new(family, Path.GetRelativePath(directory, file).Replace('\\', '/'), outcome, ticks, reason));
        }
        return results.ToArray();
    }

    private sealed record ContinuationResult(string Family, string Name, string Outcome, int Ticks, string? Reason);
}

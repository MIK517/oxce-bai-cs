using Oxce.Core.Random;
using Oxce.Gameplay.Campaigns;
using Oxce.Mods.Bootstrap;
using Oxce.Mods.Rulesets.Content;
using Oxce.Savegames.Oxce;
using Xunit;

namespace Oxce.CompatibilityTests;

public sealed class PrivateStrategicLogisticsTests
{
    private const string TacticalBattleBlocked = "Active tactical battle is preserved; tactical continuation is unavailable.";
    private const string ProjectsBlocked = "Active research/production needs staff and capacity accounting.";

    [Fact]
    public void OwnedContentHasEquivalentFreshAndCachedLogistics()
    {
        var root = Oxce.FixtureSupport.FixturePaths.FindRepositoryRoot();
        var installation = Path.Combine(root, "artifacts/private-install");
        Assert.SkipUnless(File.Exists(Path.Combine(installation, ".oxce-private-install-manifest.json")), "Owned installation is not staged.");
        foreach (var (master, addon) in new[] { ("xcom1", "-"), ("xcom2", "-"), ("40k", "40k_ROSIGMA_edits") })
        {
            var request = InstallationLoadRequest.ForMasterAndAddOn(installation, master, addon, new("Extended", "8.6.1.0"));
            var fresh = InstallationContentLoader.Load(request, new() { Cache = new() { Enabled = false } }, cancellationToken: TestContext.Current.CancellationToken);
            Assert.True(fresh.IsSuccess, fresh.DescribeFailure());
            var seed = InstallationContentLoader.Load(request, cancellationToken: TestContext.Current.CancellationToken);
            Assert.True(seed.IsSuccess, seed.DescribeFailure());
            var cached = InstallationContentLoader.Load(request, cancellationToken: TestContext.Current.CancellationToken);
            Assert.True(cached.IsSuccess, cached.DescribeFailure());
            Assert.Equal(CompiledContentCacheStatus.Hit, cached.CacheStatus);
            var first = Exercise(fresh.Content!, request);
            var second = Exercise(cached.Content!, request);
            Assert.Equivalent(first, second, strict: true);
            var family = master switch { "xcom1" => "vanilla-ufo", "xcom2" => "vanilla-tftd", _ => "modded/rosigma" };
            var imported = ExerciseImported(fresh.Content!, request, Path.Combine(root, "fixtures/private/saves", family));
            var cachedImported = ExerciseImported(cached.Content!, request, Path.Combine(root, "fixtures/private/saves", family));
            Assert.Equivalent(imported, cachedImported, strict: true);
            var expectedPurchase = master switch
            {
                "xcom1" => new ImportedResult("early/IronMode.sav", ImportedOutcome.Purchased, "STR_STINGRAY_LAUNCHER"),
                "xcom2" => new ImportedResult("early/IronMode.sav", ImportedOutcome.Purchased, "STR_AJAX_LAUNCHER"),
                _ => new ImportedResult("early/Begining.sav", ImportedOutcome.Purchased, "STR_STINGRAY_LAUNCHER"),
            };
            Assert.Equal(expectedPurchase, Assert.Single(imported, result => result.Outcome == ImportedOutcome.Purchased));
        }
    }

    private static object Exercise(RuntimeContent content, InstallationLoadRequest request)
    {
        var campaign = CampaignFactory.Create(content,
            new(new(Guid.Parse("9929e1b4-b21e-46f0-8c43-0d8c08f0bb21")), "Logistics corpus", request.MasterId, request.ActiveMods, CampaignDifficulty.Beginner),
            new SplitMix64RandomSource(42), new FixedClock());
        var site = campaign.QueryBaseSites(true)[0];
        Assert.IsType<StartingBasePlaced>(Assert.Single(campaign.Execute(new PlaceStartingBase(0, "Alpha", site.Longitude, site.Latitude)).Events));
        var initial = campaign.Capture();
        var quoteResult = campaign.Execute(new PrepareLogisticsQuote(initial.Bases[0].Id, LogisticsOperation.Purchase));
        var quote = Assert.IsType<LogisticsQuoted>(Assert.Single(quoteResult.Events)).Quote;
        var legalItems = quote.Rows.Where(r => r.Kind == CampaignTransferKind.Item && r.MaximumQuantity > 0).ToArray();
        Assert.NotEmpty(legalItems);
        var item = legalItems[0];
        Assert.IsType<LogisticsOrderCompleted>(Assert.Single(campaign.Execute(new SubmitLogisticsOrder(quote.Id, [new(item.Id, 1)])).Events));
        var recruitQuote = Assert.IsType<LogisticsQuoted>(Assert.Single(campaign.Execute(new PrepareLogisticsQuote(initial.Bases[0].Id, LogisticsOperation.Purchase)).Events)).Quote;
        var recruit = recruitQuote.Rows.Where(r => r.Kind == CampaignTransferKind.Soldier && r.MaximumQuantity > 0)
            .OrderByDescending(r => content.RuntimeRules.Soldiers[content.RuntimeRules.Soldiers.GetRequired(r.RuleId)].Value.SpawnedTemplate is not null).FirstOrDefault();
        if (recruit is not null)
            Assert.IsType<LogisticsOrderCompleted>(Assert.Single(campaign.Execute(new SubmitLogisticsOrder(recruitQuote.Id, [new(recruit.Id, 1)])).Events));
        var purchased = campaign.Capture();
        var loadOptions = new OxceSaveLoadOptions(request.MasterId, request.ActiveMods.ToHashSet(StringComparer.Ordinal));
        var loaded = OxceSaveAdapter.Load(OxceSaveAdapter.EmitNewCampaign(purchased), "new-logistics.sav", content, new SplitMix64RandomSource(0), loadOptions);
        Assert.Equivalent(purchased, loaded.Campaign.Capture(), strict: true);
        var saleQuote = Assert.IsType<LogisticsQuoted>(Assert.Single(loaded.Campaign.Execute(new PrepareLogisticsQuote(initial.Bases[0].Id, LogisticsOperation.Sell)).Events)).Quote;
        var sell = saleQuote.Rows.FirstOrDefault(r => r.Kind == CampaignTransferKind.Item && r.MaximumQuantity > 0);
        if (sell is not null)
            Assert.IsType<LogisticsOrderCompleted>(Assert.Single(loaded.Campaign.Execute(new SubmitLogisticsOrder(saleQuote.Id, [new(sell.Id, 1)])).Events));
        var advanced = loaded.Campaign.Execute(new AdvanceCampaignTime(2 * 24 * 720));
        var stopped = loaded.Campaign.Capture();
        Assert.Empty(advanced.Events.OfType<CampaignActionBlocked>());
        Assert.Equal(purchased.DaysPassed + 2, stopped.DaysPassed);
        var retry = loaded.Campaign.Execute(new AdvanceCampaignTime(1));
        Assert.DoesNotContain(retry.Events, e => e is CampaignActionBlocked);
        return new
        {
            soldiers = initial.Bases[0].Soldiers.Count,
            crafts = initial.Bases[0].Crafts.Count,
            assigned = initial.Bases[0].Soldiers.Count(s => s.Personal?.CraftType.Length > 0),
            legalPurchases = legalItems.Length,
            bought = item.RuleId,
            sold = sell?.RuleId,
            recruited = recruit?.RuleId,
            stop = "daily-readiness-complete",
            time = stopped.Time.ToString(),
            templates = content.RuntimeRules.Soldiers.Rules.Count(r => r.Value.SpawnedTemplate is not null),
            guardedTemplates = content.RuntimeRules.Soldiers.Rules.Count(r => r.Value.SpawnedTemplate is { UnsupportedFields.Count: > 0 }),
        };
    }

    private static ImportedResult[] ExerciseImported(RuntimeContent content, InstallationLoadRequest request, string directory)
    {
        Assert.True(Directory.Exists(directory));
        var results = new List<ImportedResult>();
        var options = new OxceSaveLoadOptions(request.MasterId, request.ActiveMods.ToHashSet(StringComparer.Ordinal),
            new Oxce.Formats.Yaml.YamlReadOptions { MaxBytes = 32 * 1024 * 1024, MaxNodes = 2_000_000 });
        foreach (var file in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories)
            .Where(f => Path.GetExtension(f).Equals(".sav", StringComparison.OrdinalIgnoreCase) ||
                Path.GetExtension(f).Equals(".asav", StringComparison.OrdinalIgnoreCase)).Order(StringComparer.Ordinal))
        {
            var loaded = OxceSaveAdapter.LoadFile(file, content, new SplitMix64RandomSource(0), options);
            var before = loaded.Campaign.Capture();
            var name = Path.GetRelativePath(directory, file).Replace('\\', '/');
            Assert.NotEmpty(before.Bases);
            var prepared = Assert.Single(loaded.Campaign.Execute(new PrepareLogisticsQuote(before.Bases[0].Id, LogisticsOperation.Purchase)).Events);
            if (prepared is CampaignActionBlocked blocked)
            {
                Assert.Contains(blocked.Reason, new[] { TacticalBattleBlocked, ProjectsBlocked });
                results.Add(new(name, ImportedOutcome.Blocked, blocked.Reason));
                Assert.Equivalent(before, loaded.Campaign.Capture(), strict: true);
                continue;
            }
            var quote = Assert.IsType<LogisticsQuoted>(prepared).Quote;
            var item = quote.Rows.FirstOrDefault(r => r.Kind == CampaignTransferKind.Item && r.MaximumQuantity > 0);
            Assert.NotNull(item);
            Assert.IsType<LogisticsOrderCompleted>(Assert.Single(loaded.Campaign.Execute(new SubmitLogisticsOrder(quote.Id, [new(item.Id, 1)])).Events));
            var after = loaded.Campaign.Capture();
            var rewritten = OxceSaveAdapter.EmitLoadedCampaign(after, loaded.Source);
            var restored = OxceSaveAdapter.Load(rewritten, file, content, new SplitMix64RandomSource(0), options);
            Assert.Equivalent(after, restored.Campaign.Capture(), strict: true);
            results.Add(new(name, ImportedOutcome.Purchased, item.RuleId));
        }
        Assert.NotEmpty(results);
        return results.ToArray();
    }

    private enum ImportedOutcome { Blocked, Purchased }

    private sealed record ImportedResult(string Name, ImportedOutcome Outcome, string Detail);

    private sealed class FixedClock : ICampaignClock
    {
        public DateTimeOffset UtcNow => new(2026, 9, 7, 0, 0, 0, TimeSpan.Zero);
    }
}

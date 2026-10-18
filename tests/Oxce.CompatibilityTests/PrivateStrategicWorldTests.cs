using System.Text.Json;
using Oxce.Core.Random;
using Oxce.Gameplay.Campaigns;
using Oxce.Gameplay.Campaigns.World;
using Oxce.Mods.Bootstrap;
using Oxce.Mods.Rulesets.Content;
using Oxce.Savegames.Oxce;
using Oxce.TestSupport;
using Xunit;

namespace Oxce.CompatibilityTests;

public sealed class PrivateStrategicWorldTests
{
    // Real mods run with the production forecast work bounds (branch 6 S3).
    public PrivateStrategicWorldTests() => Oxce.Gameplay.Campaigns.World.CampaignWorld.ProductionWorkBounds = true;

    [Fact]
    public void ImportedWorldContinuationHasEquivalentFreshAndCachedClassifications()
    {
        var installation = TestFixtures.RepositoryPath("artifacts", "private-install");
        Assert.SkipUnless(File.Exists(Path.Combine(installation, ".oxce-private-install-manifest.json")),
            "Owned installation is not staged.");
        var cacheDirectory = Directory.CreateTempSubdirectory("oxce-private-world-cache-");
        try
        {
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
                var options = new InstallationContentLoadOptions
                {
                    Cache = new() { DirectoryPath = cacheDirectory.FullName },
                };
                var seed = InstallationContentLoader.Load(request, options, cancellationToken: TestContext.Current.CancellationToken);
                Assert.True(seed.IsSuccess, seed.DescribeFailure());
                var cached = InstallationContentLoader.Load(request, options, cancellationToken: TestContext.Current.CancellationToken);
                Assert.True(cached.IsSuccess, cached.DescribeFailure());
                // A cache too large to publish leaves the previous family's file, which is then rejected.
                Assert.True(cached.CacheStatus == CompiledContentCacheStatus.Hit,
                    $"{master}/{addon}: {cached.CacheStatus} {cached.CacheRejectionReason}");
                var first = Exercise(fresh.Content!, request, family);
                Assert.Equal(count, first.Length);
                Assert.Equivalent(first, Exercise(cached.Content!, request, family), strict: true);
                results.AddRange(first);
            }
            // Retain the measured classifications even when an expectation fails, for review.
            var path = TestFixtures.RepositoryPath("artifacts", "world-closure-corpus.json");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, JsonSerializer.Serialize(results));
            Assert.Equal(19, results.Count);
            Assert.Equal(11, results.Count(result => result.Outcome == "executable"));
            Assert.Equal(0, results.Count(result => result.Outcome == "blocked"));
            Assert.Equal(8, results.Count(result => result.Outcome == "preservation-only"));
            foreach (var result in results)
            {
                var expected = (result.Family, result.Name) switch
                {
                    (_, var name) when name.StartsWith("battlescape/", StringComparison.Ordinal) =>
                        (0, "Active tactical battle is preserved; tactical continuation is unavailable."),
                    _ => (360, (string?)null),
                };
                Assert.Equal(expected, (result.Ticks, result.Reason));
            }
        }
        finally { cacheDirectory.Delete(recursive: true); }
    }

    /// <summary>
    /// Branch 6 S4: an imported save's soldier diaries and mission statistics are typed as the reference loads them,
    /// and a rewrite keeps the kill lists, mission IDs and statistics as they were.
    /// </summary>
    [Fact]
    public void ImportedDiariesAndMissionStatisticsAreTypedAndKept()
    {
        var installation = TestFixtures.RepositoryPath("artifacts", "private-install");
        Assert.SkipUnless(File.Exists(Path.Combine(installation, ".oxce-private-install-manifest.json")),
            "Owned installation is not staged.");
        var request = InstallationLoadRequest.ForMasterAndAddOn(installation, "xcom1", "-", new("Extended", "8.6.1.0"));
        var loaded = InstallationContentLoader.Load(request, new() { Cache = new() { Enabled = false } },
            cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(loaded.IsSuccess, loaded.DescribeFailure());
        var options = new OxceSaveLoadOptions(request.MasterId, request.ActiveMods.ToHashSet(StringComparer.Ordinal),
            new Oxce.Formats.Yaml.YamlReadOptions { MaxBytes = 32 * 1024 * 1024, MaxNodes = 2_000_000 });
        var path = TestFixtures.RepositoryPath("fixtures", "private", "saves", "vanilla-ufo", "early", "Middle.sav");
        var imported = OxceSaveAdapter.LoadFile(path, loaded.Content!, new SplitMix64RandomSource(17), options);
        var snapshot = imported.Campaign.Capture();

        var statistic = Assert.Single(snapshot.MissionStatistics);
        Assert.Equal(new MissionStatisticSnapshot(0, "STR_LANDING_SITE_", "STR_EUROPE", "STR_UK", "STR_UFO_GROUND_ASSAULT", "STR_MEDIUM_SCOUT",
            false, -330, 15, false, 1), statistic);
        var diaries = snapshot.Bases.SelectMany(static b => b.Soldiers).Concat(snapshot.DeadSoldiers.Select(static d => d.Soldier))
            .Select(static s => s.Personal!.Diary).ToArray();
        var killer = Assert.Single(diaries, static d => d.Kills.Any(static k => k.Id == 1_000_000));
        Assert.Equal(new SoldierKill("STR_LIVE_SOLDIER", "STR_SECTOID", "STR_RIFLE", "STR_RIFLE_CLIP", 6, 1, 0, 9, 1, 3)
        { Type = "STR_SECTOID_SOLDIER", Id = 1_000_000 }, Assert.Single(killer.Kills));
        Assert.Equal([0], killer.MissionIds);
        Assert.Equal((1, 1, 6, 1, 1), (killer.Counter("hitCounterTotal"), killer.Counter("lowAccuracyHitCounterTotal"),
            killer.Counter("shotsFiredCounterTotal"), killer.Counter("shotsLandedCounterTotal"), killer.Counter("killedInAction")));

        // The kill lists, mission IDs and statistics are written back from the source nodes; the writer chooses its
        // own YAML style, so they are compared after reading them back.
        var rewritten = OxceSaveAdapter.EmitLoadedCampaign(snapshot, imported.Source);
        var reparsed = OxceSaveAdapter.Load(rewritten, "rewritten.sav", loaded.Content!, new SplitMix64RandomSource(17), options).Campaign.Capture();
        Assert.Equivalent(snapshot.MissionStatistics, reparsed.MissionStatistics, strict: true);
        Assert.Equivalent(snapshot.Bases.SelectMany(static b => b.Soldiers), reparsed.Bases.SelectMany(static b => b.Soldiers), strict: true);
        Assert.Equivalent(snapshot.DeadSoldiers, reparsed.DeadSoldiers, strict: true);
        Assert.Contains(reparsed.DeadSoldiers.Select(static d => d.Soldier).Concat(reparsed.Bases.SelectMany(static b => b.Soldiers)),
            static s => s.Personal!.Diary.Kills.Any(static k => k.Id == 1_000_000));
    }

    /// <summary>
    /// Branch 6 S8: every staged save plays up to three months at the one-day speed, a headless player answering every
    /// window, saved and reloaded (and checked equivalent) after every monthly report. Each month every history grows by
    /// one up to twelve, and the world's populations stay bounded. The month tick is timed on its own; the measurements
    /// go to artifacts/private-campaign-cycle-measurements.json.
    /// </summary>
    [Fact]
    public void ImportedCampaignsPlayThreeMonthsAcrossReloadsAtEveryReport()
    {
        var installation = TestFixtures.RepositoryPath("artifacts", "private-install");
        Assert.SkipUnless(File.Exists(Path.Combine(installation, ".oxce-private-install-manifest.json")) &&
            Directory.Exists(TestFixtures.RepositoryPath("fixtures", "private", "saves")), "Owned installation or saves are not staged.");
        var cacheDirectory = Directory.CreateTempSubdirectory("oxce-private-cycle-cache-");
        List<object> measurements = [];
        try
        {
            foreach (var (master, addon, family) in new[]
            {
                ("xcom1", "-", "vanilla-ufo"), ("xcom2", "-", "vanilla-tftd"), ("40k", "40k_ROSIGMA_edits", "modded/rosigma"),
            })
            {
                var request = InstallationLoadRequest.ForMasterAndAddOn(installation, master, addon, new("Extended", "8.6.1.0"));
                var loaded = InstallationContentLoader.Load(request, new() { Cache = new() { DirectoryPath = cacheDirectory.FullName } },
                    cancellationToken: TestContext.Current.CancellationToken);
                Assert.True(loaded.IsSuccess, loaded.DescribeFailure());
                var content = loaded.Content!;
                var options = new OxceSaveLoadOptions(request.MasterId, request.ActiveMods.ToHashSet(StringComparer.Ordinal),
                    new Oxce.Formats.Yaml.YamlReadOptions { MaxBytes = 32 * 1024 * 1024, MaxNodes = 2_000_000 });
                var directory = TestFixtures.RepositoryPath("fixtures", "private", "saves", family);
                foreach (var file in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories)
                    .Where(path => Path.GetExtension(path) is ".sav" or ".asav").Order(StringComparer.Ordinal))
                {
                    var name = Path.GetRelativePath(directory, file).Replace('\\', '/');
                    if (name.StartsWith("battlescape/", StringComparison.Ordinal)) continue;
                    var label = family + "/" + name;
                    var imported = OxceSaveAdapter.LoadFile(file, content, new SplitMix64RandomSource(17), options);
                    var campaign = imported.Campaign;
                    var source = imported.Source;
                    var previous = campaign.Capture();
                    var months = 0;
                    string? stop = null;
                    List<object> reports = [];
                    List<double> tickMicroseconds = [], monthTickMilliseconds = [], reloadMilliseconds = [];
                    for (var step = 0; months < 3 && stop is null; step++)
                    {
                        Assert.True(step < 20_000, label + ": the campaign stopped making progress.");
                        // Up to the last tick before the month boundary, then the month tick on its own.
                        var time = campaign.Time;
                        var days = time.Month switch
                        {
                            2 when time.Year % 4 == 0 && (time.Year % 100 != 0 || time.Year % 400 == 0) => 29,
                            2 => 28,
                            4 or 6 or 9 or 11 => 30,
                            _ => 31,
                        };
                        var toMonth = ((days - time.Day) * 86400 + 86400 - (time.Hour * 3600 + time.Minute * 60 + time.Second)) / 5;
                        var count = toMonth <= 1 ? 1 : Math.Min(17280, toMonth - 1);
                        var watch = System.Diagnostics.Stopwatch.StartNew();
                        var result = campaign.Execute(new AdvanceCampaignTime(count) { SelectedSpeed = CampaignTimeSpeed.OneDay });
                        var elapsed = watch.Elapsed.TotalMilliseconds;
                        var advanced = result.Events.OfType<CampaignTimeAdvanced>().SingleOrDefault()?.Summary.TickCount ?? 0;
                        var report = result.Events.OfType<MonthlyReportReady>().SingleOrDefault()?.Report;
                        if (report is not null && count == 1) monthTickMilliseconds.Add(elapsed);
                        else if (advanced > 0) tickMicroseconds.Add(elapsed * 1000 / advanced);
                        var reason = result.Events.OfType<CampaignActionBlocked>().SingleOrDefault()?.Reason;
                        Assert.True(advanced > 0 || reason is not null, label + " neither advanced nor stopped.");
                        if (reason is not null && !AnswerBranchFiveChoices(campaign, reason))
                        {
                            stop = reason;
                            break;
                        }
                        if (report is null) continue;
                        // Answer the report (and the rest of its tick's windows), then save and reload the campaign.
                        while (campaign.Ending == 0 && (campaign.GetQuery<ICampaignMonthlyReportQuery>()!.QueryMonthlyReport() is not null ||
                               campaign.GetQuery<ICampaignEventWindowQuery>()!.QueryEventWindows().Count != 0))
                        {
                            var blocked = campaign.Execute(new AdvanceCampaignTime(1)).Events.OfType<CampaignActionBlocked>().Single().Reason;
                            Assert.True(AnswerBranchFiveChoices(campaign, blocked), label + ": " + blocked);
                        }
                        months++;
                        reports.Add(new { report.Month, report.Rating, report.GameOver });
                        var after = campaign.Capture();
                        AssertMonthInvariants(previous, after, label + " month " + months);
                        if (campaign.Ending != 0)
                        {
                            stop = "ended " + campaign.Ending.ToString(System.Globalization.CultureInfo.InvariantCulture);
                            break;
                        }
                        watch.Restart();
                        var snapshot = campaign.CaptureForSave(out var refusal);
                        Assert.True(snapshot is not null, label + ": " + refusal);
                        var saved = OxceSaveAdapter.EmitLoadedCampaign(snapshot, source);
                        var reloaded = OxceSaveAdapter.Load(saved, name, content, new SplitMix64RandomSource(17), options);
                        reloadMilliseconds.Add(watch.Elapsed.TotalMilliseconds);
                        // The month's state, its new missions, events and logs, survives the save.
                        Assert.Equivalent(snapshot, reloaded.Campaign.Capture(), strict: true);
                        campaign = reloaded.Campaign;
                        source = reloaded.Source;
                        previous = after;
                    }
                    // A month at least, then three months or the council's verdict; nothing else may stop a campaign.
                    Assert.True(months >= 1, label + ": no month was played.");
                    Assert.True(stop is null or "ended 2", label + ": " + stop);
                    measurements.Add(new
                    {
                        Family = family,
                        Save = name,
                        Months = months,
                        Stop = stop ?? "three months",
                        Reports = reports,
                        TickMicroseconds = Median(tickMicroseconds),
                        MonthTickMilliseconds = Median(monthTickMilliseconds),
                        MonthTicks = monthTickMilliseconds.Count,
                        SaveAndReloadMilliseconds = Median(reloadMilliseconds),
                        Reloads = reloadMilliseconds.Count,
                    });
                }
            }
            // The 19 staged saves: 11 play their months (positive), none is blocked, 8 battles stay preservation-only.
            Assert.Equal(11, measurements.Count);
            var record = TestFixtures.RepositoryPath("artifacts", "private-campaign-cycle-measurements.json");
            Directory.CreateDirectory(Path.GetDirectoryName(record)!);
            File.WriteAllText(record, JsonSerializer.Serialize(measurements, IndentedJson));
        }
        finally { cacheDirectory.Delete(recursive: true); }

        // The lower middle, so that one sample of two (often JIT warm-up) does not decide.
        static double Median(List<double> values) =>
            values.Count == 0 ? 0 : Math.Round(values.Order().ElementAt((values.Count - 1) / 2), 1);
    }

    /// <summary>
    /// SavedGame::monthlyFunding, Country::newMonth and Region::newMonth: every history grows by one month and keeps the
    /// last twelve; the world's populations stay bounded.
    /// </summary>
    private static void AssertMonthInvariants(CampaignSnapshot before, CampaignSnapshot after, string label)
    {
        static int Grown(int count) => Math.Min(count + 1, 12);
        Assert.True(after.Funds.Count == Grown(before.Funds.Count) && after.Maintenance.Count == Grown(before.Maintenance.Count) &&
            after.Incomes.Count == Grown(before.Incomes.Count) && after.Expenditures.Count == Grown(before.Expenditures.Count) &&
            after.ResearchScores.Count == Grown(before.ResearchScores.Count), label + ": the ledger did not grow by one month.");
        for (var index = 0; index < after.Countries.Count; index++)
        {
            var (old, country) = (before.Countries[index], after.Countries[index]);
            Assert.True(country.Funding.Count == Grown(old.Funding.Count) && country.ActivityXcom.Count == Grown(old.ActivityXcom.Count) &&
                country.ActivityAlien.Count == Grown(old.ActivityAlien.Count), label + ": " + country.RuleId + " did not grow by one month.");
        }
        for (var index = 0; index < after.Regions.Count; index++)
        {
            var (old, region) = (before.Regions[index], after.Regions[index]);
            Assert.True(region.ActivityXcom.Count == Grown(old.ActivityXcom.Count) && region.ActivityAlien.Count == Grown(old.ActivityAlien.Count),
                label + ": " + region.RuleId + " did not grow by one month.");
        }
        Assert.True(after.World.Missions.Count < 1_000 && after.World.Ufos.Count < 1_000 && after.World.MissionSites.Count < 1_000 &&
            after.World.AlienBases.Count < 1_000 && after.World.Events.Count < 1_000, label + ": a world population grew without bound.");
    }

    private const string ReportStop = "The monthly report is open.";
    private const string TacticalStop = "Active tactical battle is preserved; tactical continuation is unavailable.";
    private const string EventStop = "A strategic event window is open.";
    private const string LimitStop = "limit";

    /// <summary>
    /// At the one-day speed, resuming after every pause, each staged save runs until its named first stop. Branch 6
    /// S3: the month boundary runs and its report holds the clock; tactical saves stay preservation-only.
    /// </summary>
    [Fact]
    public void ImportedWorldRunsToItsNamedLongHorizonStop() => RunToLongHorizonStops(new()
    {
        [("vanilla-ufo", "early/Begining.sav")] = (525366, ReportStop),
        [("vanilla-ufo", "early/IronMode.sav")] = (527040, ReportStop),
        [("vanilla-ufo", "early/Middle.sav")] = (396542, ReportStop),
        [("vanilla-ufo", "early/_autogeo_.asav")] = (396559, ReportStop),
        [("vanilla-tftd", "early/Begining.sav")] = (526925, ReportStop),
        [("vanilla-tftd", "early/IronMode.sav")] = (527040, ReportStop),
        [("vanilla-tftd", "early/Middle.sav")] = (90175, ReportStop),
        // Branch 6 S5: the event pops at tick 580 and its window holds the clock from the next tick.
        [("modded/rosigma", "early/Begining.sav")] = (581, EventStop),
        // Branch 5 S6: the landing tick now runs and queues a confirmation, which then holds the clock.
        [("modded/rosigma", "early/Early.sav")] = (6290, "Landing confirmations stop the clock until they are answered."),
        [("modded/rosigma", "early/Middle.sav")] = (429682, ReportStop),
        [("modded/rosigma", "early/_autogeo_.asav")] = (429699, ReportStop),
    }, answer: null);

    /// <summary>
    /// A headless player declines every landing, disengages every interception, fires every base defense and
    /// acknowledges every monthly report and event window (branch 5 S9, branch 6 S3 and S5), so each staged save runs past those windows to the
    /// next stop that is not a player choice, or to the 40-day limit.
    /// </summary>
    [Fact]
    public void ImportedWorldRunsPastBranchFiveChoicesToItsNextStop() => RunToLongHorizonStops(new()
    {
        [("vanilla-ufo", "early/Begining.sav")] = (691200, LimitStop),
        [("vanilla-ufo", "early/IronMode.sav")] = (691200, LimitStop),
        [("vanilla-ufo", "early/Middle.sav")] = (691200, LimitStop),
        [("vanilla-ufo", "early/_autogeo_.asav")] = (691200, LimitStop),
        [("vanilla-tftd", "early/Begining.sav")] = (691200, LimitStop),
        [("vanilla-tftd", "early/IronMode.sav")] = (691200, LimitStop),
        // Superhuman with defeatScore -900: two months rated below -500 lose the campaign.
        [("vanilla-tftd", "early/Middle.sav")] = (591295, "This campaign has ended."),
        // Acknowledging every event window (branch 6 S5), Rosigma runs its months to the limit.
        [("modded/rosigma", "early/Begining.sav")] = (691200, LimitStop),
        [("modded/rosigma", "early/Early.sav")] = (691200, LimitStop),
        [("modded/rosigma", "early/Middle.sav")] = (691200, LimitStop),
        [("modded/rosigma", "early/_autogeo_.asav")] = (691200, LimitStop),
    }, AnswerBranchFiveChoices);

    private static bool AnswerBranchFiveChoices(CampaignState campaign, string reason)
    {
        // Branch 6 S3: the monthly report is acknowledged like any other window.
        if (reason == CampaignState.MonthlyReportOpen)
        {
            Assert.DoesNotContain(campaign.Execute(new AcknowledgeMonthlyReport()).Events, static evt => evt is CampaignActionBlocked);
            return true;
        }
        // Branch 6 S5: every event window is acknowledged.
        if (reason == CampaignState.EventWindowOpen)
        {
            while (campaign.GetQuery<ICampaignEventWindowQuery>()!.QueryEventWindows().Count != 0)
                Assert.DoesNotContain(campaign.Execute(new AcknowledgeCampaignEvent()).Events, static evt => evt is CampaignActionBlocked);
            return true;
        }
        if (reason == WorldCommandReasons.LandingConfirmationsOpen)
        {
            foreach (var landing in campaign.GetQuery<ICampaignLandingQuery>()!.QueryLandingConfirmations())
                Assert.DoesNotContain(campaign.Execute(new AnswerLanding(landing.Confirmation, LandingAnswer.No)).Events,
                    static evt => evt is CampaignActionBlocked);
            return true;
        }
        if (reason == WorldCommandReasons.BaseDefenseOpen)
        {
            var defense = campaign.GetQuery<ICampaignBaseDefenseQuery>()!.QueryBaseDefense()!;
            Assert.DoesNotContain(campaign.Execute(new ResolveBaseDefense(defense.BaseId, Fire: true)).Events,
                static evt => evt is CampaignActionBlocked);
            return true;
        }
        if (reason != WorldCommandReasons.InterceptionsOpen) return false;
        var interceptions = campaign.GetQuery<ICampaignInterceptionQuery>()!;
        foreach (var window in interceptions.QueryInterceptions())
            if (window.AvailableModes.Contains(InterceptionMode.Disengage))
                campaign.Execute(new SetInterceptionMode(window.Number, InterceptionMode.Disengage));
        for (var step = 0; step < 100_000 && interceptions.QueryInterceptions().Any(static window => !window.Minimized); step++)
            Assert.DoesNotContain(campaign.Execute(new AdvanceInterceptions()).Events, static evt => evt is CampaignActionBlocked);
        return true;
    }

    private static readonly System.Text.Json.JsonSerializerOptions IndentedJson = new() { WriteIndented = true };

    private static void RunToLongHorizonStops(Dictionary<(string, string), (int Ticks, string Reason)> expected,
        Func<CampaignState, string, bool>? answer)
    {
        var installation = TestFixtures.RepositoryPath("artifacts", "private-install");
        Assert.SkipUnless(File.Exists(Path.Combine(installation, ".oxce-private-install-manifest.json")),
            "Owned installation is not staged.");
        var cacheDirectory = Directory.CreateTempSubdirectory("oxce-private-horizon-cache-");
        try
        {
            var measured = new Dictionary<(string, string), (int Ticks, string Reason)>();
            foreach (var (master, addon, family) in new[]
            {
                ("xcom1", "-", "vanilla-ufo"), ("xcom2", "-", "vanilla-tftd"), ("40k", "40k_ROSIGMA_edits", "modded/rosigma"),
            })
            {
                var request = InstallationLoadRequest.ForMasterAndAddOn(installation, master, addon, new("Extended", "8.6.1.0"));
                var loaded = InstallationContentLoader.Load(request, new() { Cache = new() { DirectoryPath = cacheDirectory.FullName } },
                    cancellationToken: TestContext.Current.CancellationToken);
                Assert.True(loaded.IsSuccess, loaded.DescribeFailure());
                var options = new OxceSaveLoadOptions(request.MasterId, request.ActiveMods.ToHashSet(StringComparer.Ordinal),
                    new Oxce.Formats.Yaml.YamlReadOptions { MaxBytes = 32 * 1024 * 1024, MaxNodes = 2_000_000 });
                var directory = TestFixtures.RepositoryPath("fixtures", "private", "saves", family);
                foreach (var file in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories)
                    .Where(path => Path.GetExtension(path) is ".sav" or ".asav").Order(StringComparer.Ordinal))
                {
                    var name = Path.GetRelativePath(directory, file).Replace('\\', '/');
                    var campaign = OxceSaveAdapter.LoadFile(file, loaded.Content!, new SplitMix64RandomSource(17), options).Campaign;
                    const int Limit = 40 * 17280;
                    var ticks = 0;
                    string? reason = null;
                    while (reason is null && ticks < Limit)
                    {
                        var result = campaign.Execute(new AdvanceCampaignTime(Math.Min(Limit - ticks, 1440))
                        {
                            SelectedSpeed = CampaignTimeSpeed.OneDay,
                        });
                        var advanced = result.Events.OfType<CampaignTimeAdvanced>().SingleOrDefault()?.Summary.TickCount ?? 0;
                        ticks += advanced;
                        reason = result.Events.OfType<CampaignActionBlocked>().SingleOrDefault()?.Reason;
                        Assert.True(advanced > 0 || reason is not null, $"{family}/{name} neither advanced nor stopped.");
                        if (reason is not null && answer is not null && answer(campaign, reason)) reason = null;
                    }
                    if (name.StartsWith("battlescape/", StringComparison.Ordinal)) Assert.Equal((0, TacticalStop), (ticks, reason));
                    else measured[(family, name)] = (ticks, reason ?? "limit");
                }
            }
            // The measured stops, for updating the expectations when a slice moves them.
            var record = TestFixtures.RepositoryPath("artifacts", answer is null ? "private-horizon-stops.json" : "private-horizon-stops-answered.json");
            Directory.CreateDirectory(Path.GetDirectoryName(record)!);
            File.WriteAllText(record, System.Text.Json.JsonSerializer.Serialize(measured.OrderBy(pair => pair.Key)
                .Select(static pair => new { Family = pair.Key.Item1, Save = pair.Key.Item2, pair.Value.Ticks, pair.Value.Reason }),
                IndentedJson));
            Assert.Equal(expected.OrderBy(pair => pair.Key).ToArray(), measured.OrderBy(pair => pair.Key).ToArray());
        }
        finally { cacheDirectory.Delete(recursive: true); }
    }

    /// <summary>
    /// Branch 5 S6 acceptance: the Rosigma <c>Early.sav</c> campaign runs to its first landing, and Yes
    /// records the deployment request that then holds the campaign.
    /// </summary>
    [Fact]
    public void RosigmaEarlyReachesADeploymentRequest()
    {
        var installation = TestFixtures.RepositoryPath("artifacts", "private-install");
        Assert.SkipUnless(File.Exists(Path.Combine(installation, ".oxce-private-install-manifest.json")),
            "Owned installation is not staged.");
        var cacheDirectory = Directory.CreateTempSubdirectory("oxce-private-landing-cache-");
        try
        {
            var request = InstallationLoadRequest.ForMasterAndAddOn(installation, "40k", "40k_ROSIGMA_edits", new("Extended", "8.6.1.0"));
            var loaded = InstallationContentLoader.Load(request, new() { Cache = new() { DirectoryPath = cacheDirectory.FullName } },
                cancellationToken: TestContext.Current.CancellationToken);
            Assert.True(loaded.IsSuccess, loaded.DescribeFailure());
            var options = new OxceSaveLoadOptions(request.MasterId, request.ActiveMods.ToHashSet(StringComparer.Ordinal),
                new Oxce.Formats.Yaml.YamlReadOptions { MaxBytes = 32 * 1024 * 1024, MaxNodes = 2_000_000 });
            var file = TestFixtures.RepositoryPath("fixtures", "private", "saves", "modded/rosigma", "early", "Early.sav");
            var campaign = OxceSaveAdapter.LoadFile(file, loaded.Content!, new SplitMix64RandomSource(17), options).Campaign;
            LandingConfirmationRequested? landing = null;
            var ticks = 0;
            while (landing is null && ticks < 7000)
            {
                var result = campaign.Execute(new AdvanceCampaignTime(1440) { SelectedSpeed = CampaignTimeSpeed.OneDay });
                Assert.DoesNotContain(result.Events, static item => item is CampaignActionBlocked);
                ticks += result.Events.OfType<CampaignTimeAdvanced>().Single().Summary.TickCount;
                landing = result.Events.OfType<LandingConfirmationRequested>().FirstOrDefault();
            }
            Assert.NotNull(landing);
            Assert.Equal(6290, ticks);
            var answer = campaign.Execute(new AnswerLanding(landing.Confirmation, LandingAnswer.Yes)).Events;
            var deployment = Assert.IsType<DeploymentRequested>(Assert.Single(answer)).Request;
            Assert.Equal((landing.BaseId, landing.CraftTypeId, landing.CraftId), (deployment.BaseId, deployment.CraftTypeId, deployment.CraftId));
            // A terror site of the early Slaanesh cult, at night, with ten soldiers on board.
            Assert.Equal(("STR_TERROR_MISSION", "STR_TERROR_MISSION", "STR_SLAANESH_CULT_EARLY", 3, 3, 0, 10),
                (deployment.MissionType, deployment.DeploymentId, deployment.Race, deployment.MissionTexture, deployment.GlobeTexture,
                    deployment.Shade, deployment.Soldiers.Count));
            Assert.Equal(WorldCommandReasons.DeploymentRequestPending, Assert.IsType<CampaignActionBlocked>(
                campaign.Execute(new AdvanceCampaignTime(1)).Events[^1]).Reason);
        }
        finally { cacheDirectory.Delete(recursive: true); }
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
            if (family == "modded/rosigma" && Path.GetFileName(file) == "Begining.sav")
            {
                // This dummy wave previously stopped at tick 220. Reference spawnUfo
                // returns null for its undefined UFO; think advances without a site.
                var dummy = Assert.Single(initial.World.Missions, mission => mission.RuleId == "STR_DUMMY");
                var rule = content.RuntimeRules.AlienMissions[content.RuntimeRules.AlienMissions.GetRequired(dummy.RuleId)].Value;
                var wave = Assert.Single(rule.Waves);
                Assert.Null(wave.Ufo);
                Assert.False(wave.Objective);
                Assert.True(wave.Deployment is null ||
                    content.RuntimeRules.AlienDeployments[wave.Deployment.Value].Value.MarkerName.Length == 0);
            }
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
            if (family == "vanilla-ufo" && Path.GetFileName(file) == "Begining.sav")
            {
                // The site's first ordinary wave formerly stopped at tick 125:
                // regional candidates can reach an airborne first waypoint.
                var terror = Assert.Single(initial.World.Missions, mission => mission.RuleId == "STR_ALIEN_TERROR");
                var rule = content.RuntimeRules.AlienMissions[content.RuntimeRules.AlienMissions.GetRequired(terror.RuleId)].Value;
                Assert.Equal(Oxce.Mods.Rulesets.Runtime.RuntimeMissionObjective.Site, rule.Objective);
                Assert.Contains(after.World.Ufos, ufo => ufo.MissionId == terror.Id && ufo.MissionWaveNumber == 0);
            }
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

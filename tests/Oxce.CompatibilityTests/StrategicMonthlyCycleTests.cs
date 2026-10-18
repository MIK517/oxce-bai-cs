using Oxce.Core.Random;
using Oxce.Gameplay.Campaigns;
using Oxce.Gameplay.Campaigns.World;
using Xunit;
using static Oxce.CompatibilityTests.StrategicWorldTestSupport;

namespace Oxce.CompatibilityTests;

/// <summary>
/// Branch 6 S3: the month rollover runs inside the time advance in GeoscapeState::time1Month order (addMonth with the
/// purchase log, scheduling, monthly psi, funding, the report, the monthly alien-base roll), staged on a shadow copy
/// (ADR 0035). The report is modal: it holds the clock and every command but its acknowledgement.
/// </summary>
public sealed class StrategicMonthlyCycleTests
{
    private const string PsiLab = """
        defeatScore: -100000
        facilities:
          - type: PSI_LAB
            mapName: LOGISTICS
            psiLabs: 1
        """;

    private static readonly CampaignTime MonthEnd = new(1, 31, 1, 1999, 23, 59, 55);

    [Fact]
    public void TheMonthRunsInReferenceOrderAndTheReportHoldsTheClockUntilItIsAcknowledged()
    {
        var content = StrategicReadinessTestContent.Load(["strategic-world.rul"], PsiLab);
        var snapshot = CreateSnapshot(content, "Month") with
        {
            Time = MonthEnd,
            MonthlyPurchaseLog = new Dictionary<string, int>(StringComparer.Ordinal) { ["SUPPLY"] = 3 },
        };
        var campaign = CampaignState.Restore(snapshot, content, new SplitMix64RandomSource(11));

        var result = campaign.Execute(new AdvanceCampaignTime(5));

        Assert.Equal(1, Assert.IsType<CampaignTimeAdvanced>(result.Events[0]).Summary.TickCount);
        var report = Assert.Single(result.Events.OfType<MonthlyReportReady>()).Report;
        var after = campaign.Capture();
        Assert.Equal(1, after.MonthsPassed);
        Assert.Empty(after.MonthlyPurchaseLog);
        // monthlyFunding: one more month of every history, the funds credited with funding less maintenance.
        Assert.Equal(snapshot.Funds.Count + 1, after.Funds.Count);
        Assert.Equal(snapshot.Incomes.Count + 1, after.Incomes.Count);
        Assert.Equal(snapshot.ResearchScores.Count + 1, after.ResearchScores.Count);
        Assert.Equal((1, 1999), (report.Month, report.Year));
        Assert.Equal(after.Funds[^1], report.Balance);
        Assert.Equal(report, campaign.GetQuery<ICampaignMonthlyReportQuery>()!.QueryMonthlyReport());

        // Modal: time and every other command wait for the OK.
        Assert.Equal(CampaignState.MonthlyReportOpen, Assert.Single(campaign.Execute(new AdvanceCampaignTime(1)).Events.OfType<CampaignActionBlocked>()).Reason);
        Assert.Equal(1, campaign.Capture().MonthsPassed);
        Assert.Equal([new MonthlyReportAcknowledged(null), new CampaignSaveRequested(CampaignSaveKind.Autosave)],
            campaign.Execute(new AcknowledgeMonthlyReport()).Events);
        Assert.Null(campaign.GetQuery<ICampaignMonthlyReportQuery>()!.QueryMonthlyReport());
        AdvanceUnblocked(campaign, 3);
        // The report is not saved; a reload has none.
        Assert.Null(Reload(content, campaign, 11, "after-month.sav").GetQuery<ICampaignMonthlyReportQuery>()!.QueryMonthlyReport());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MonthlyPsiTrainingRunsOnlyWithoutAnytimePsiTraining(bool anytime)
    {
        var content = StrategicReadinessTestContent.Load(["strategic-world.rul"], PsiLab);
        var snapshot = CreateSnapshot(content, "Psi").WithFacility("PSI_LAB") with
        {
            Time = MonthEnd,
            Options = new CampaignOptions(AnytimePsiTraining: anytime),
        };
        var owner = snapshot.Bases[0];
        snapshot = snapshot with
        {
            Bases = [owner with { Soldiers = [owner.Soldiers[0] with { Personal = owner.Soldiers[0].Personal! with { PsiTraining = true } }, .. owner.Soldiers.Skip(1)] },
                .. snapshot.Bases.Skip(1)],
        };
        var campaign = CampaignState.Restore(snapshot, content, new SplitMix64RandomSource(5));

        campaign.Execute(new AdvanceCampaignTime(1));

        var trained = campaign.Capture().Bases[0].Soldiers[0].Personal!;
        // Soldier::trainPsi below the rule's maximum psi skill improves by maximum to 1.5 times maximum.
        if (anytime) Assert.Equal(0, trained.Improvement);
        else Assert.InRange(trained.Improvement, 50, 75);
        Assert.All(campaign.Capture().Bases[0].Soldiers.Skip(1), soldier => Assert.Equal(0, soldier.Personal!.Improvement));
    }

    private const string ServiceMedal = PsiLab + """

        commendations:
          - type: STR_MEDAL_SERVICE
            criteria: {totalMonthlyService: [1, 2]}

        """;

    [Fact]
    public void TheReportsOkAddsServiceAwardsCommendationsAndRequestsTheAutosave()
    {
        var content = StrategicReadinessTestContent.Load(["strategic-world.rul"], ServiceMedal);
        var snapshot = CreateSnapshot(content, "Service").WithFacility("PSI_LAB") with { Time = MonthEnd };
        var campaign = CampaignState.Restore(snapshot, content, new SplitMix64RandomSource(5));
        Assert.Null(campaign.QuerySaveRefusal());
        Assert.Single(campaign.Execute(new AdvanceCampaignTime(1)).Events.OfType<MonthlyReportReady>());
        // D2: the OK's effects would be lost by a save made under the report.
        Assert.Equal(CampaignState.MonthlyReportOpen, campaign.QuerySaveRefusal());

        var events = campaign.Execute(new AcknowledgeMonthlyReport()).Events;

        var soldiers = campaign.Capture().Bases.SelectMany(static b => b.Soldiers).ToArray();
        Assert.NotEmpty(soldiers);
        // The logistics fixture's STR_MEDAL_ORIGINAL8_NAME has no criteria and is never awarded here (ADR 0036).
        Assert.Equal<ICampaignEvent>([new MonthlyReportAcknowledged(null),
            new SoldiersCommended([.. soldiers.Select(static s => new CommendationAward(s.Id, "STR_MEDAL_SERVICE", "noNoun", 0))]),
            new PsiTrainingReminder(), new CampaignSaveRequested(CampaignSaveKind.Autosave)], events, new EventComparer());
        Assert.All(soldiers, static soldier =>
        {
            Assert.Equal(1, soldier.Personal!.Diary.Counter("monthsService"));
            Assert.Contains(new SoldierCommendation("STR_MEDAL_SERVICE", "noNoun", 0), soldier.Personal.Commendations);
        });
        Assert.Null(campaign.QuerySaveRefusal());
        // The service and the commendation are saved and read back.
        Reload(content, campaign, 5, "service.sav");
    }

    [Theory]
    [InlineData(false, true, CampaignSaveKind.Autosave)]
    [InlineData(false, false, null)]
    [InlineData(true, false, CampaignSaveKind.Ironman)]
    public void TheReportsOkRequestsAnIronmanSaveOrAnAutosave(bool ironman, bool autosave, CampaignSaveKind? kind)
    {
        var content = StrategicReadinessTestContent.Load(["strategic-world.rul"], "defeatScore: -100000\n");
        var snapshot = CreateSnapshot(content, "Save requests");
        snapshot = snapshot with
        {
            Time = MonthEnd,
            Identity = snapshot.Identity with { Ironman = ironman },
            Options = snapshot.Options with { Autosave = autosave },
        };
        var campaign = CampaignState.Restore(snapshot, content, new SplitMix64RandomSource(5));
        campaign.Execute(new AdvanceCampaignTime(1));

        var requests = campaign.Execute(new AcknowledgeMonthlyReport()).Events.OfType<CampaignSaveRequested>();

        Assert.Equal(kind is { } expected ? [new CampaignSaveRequested(expected)] : [], requests);
    }

    private sealed class EventComparer : IEqualityComparer<ICampaignEvent>
    {
        public bool Equals(ICampaignEvent? x, ICampaignEvent? y) =>
            x is SoldiersCommended left && y is SoldiersCommended right ? left.Awards.SequenceEqual(right.Awards) : object.Equals(x, y);

        public int GetHashCode(ICampaignEvent obj) => 0;
    }

    [Fact]
    public void ACountryScriptAdjustsTheFundingAndAPactSchedulesItsEvent()
    {
        var content = StrategicReadinessTestContent.Load(["strategic-world.rul"], """
            countries:
              - type: COUNTRY
                signedPactEvent: STR_PACT_EVENT
                scripts:
                  newMonthCountry: |
                    var int funding;
                    country.getCurrentFunding funding;
                    set fundingChange 7000;
                    set formPact 1;
                    return;
            events:
              - name: STR_PACT_EVENT
                timer: 60
            defeatScore: -100000
            """, extraRulesAfterOverlays: true);
        var snapshot = CreateSnapshot(content, "Country script") with { Time = MonthEnd };
        var campaign = CampaignState.Restore(snapshot, content, new SplitMix64RandomSource(5));

        var report = Assert.Single(campaign.Execute(new AdvanceCampaignTime(1)).Events.OfType<MonthlyReportReady>()).Report;

        var country = Assert.Single(campaign.Capture().Countries);
        // formPact: the pact is signed, so the funding is zero whatever the script's change.
        Assert.True(country.Pact);
        Assert.Equal(0, country.Funding[^1]);
        Assert.Equal(["COUNTRY"], report.SignedPacts);
        Assert.Contains(campaign.Capture().World.Events, scheduled => scheduled.RuleId == "STR_PACT_EVENT");
    }

    [Fact]
    public void ALaterCountryScriptSeesAnEarlierCountrysNewPact()
    {
        // Country::newMonth sets the pact before the next country's script runs.
        var content = StrategicReadinessTestContent.Load(["strategic-world.rul"], """
            countries:
              - type: COUNTRY
                scripts:
                  newMonthCountry: |
                    set formPact 1;
                    return;
              - type: COUNTRY_B
                scripts:
                  newMonthCountry: |
                    var int pacts;
                    var int value;
                    loop var other geoscape_game.getCountries.list;
                      other.getPact value;
                      add pacts value;
                    end;
                    if eq pacts 1;
                      set formPact 1;
                    end;
                    return;
            defeatScore: -100000
            """, extraRulesAfterOverlays: true);
        var snapshot = CreateSnapshot(content, "Pact order");
        snapshot = snapshot with { Time = MonthEnd, Countries = [.. snapshot.Countries, snapshot.Countries[0] with { RuleId = "COUNTRY_B" }] };
        var campaign = CampaignState.Restore(snapshot, content, new SplitMix64RandomSource(5));

        var report = Assert.Single(campaign.Execute(new AdvanceCampaignTime(1)).Events.OfType<MonthlyReportReady>()).Report;

        Assert.Equal(["COUNTRY", "COUNTRY_B"], report.SignedPacts);
        Assert.All(campaign.Capture().Countries, static country => Assert.True(country.Pact));
    }

    [Fact]
    public void AReloadDropsAnOpenReportAndKeepsTheMonthsEffects()
    {
        var content = StrategicReadinessTestContent.Load(["strategic-world.rul"], PsiLab);
        var campaign = CampaignState.Restore(CreateSnapshot(content, "Open report") with { Time = MonthEnd }, content,
            new SplitMix64RandomSource(11));
        Assert.Single(campaign.Execute(new AdvanceCampaignTime(1)).Events.OfType<MonthlyReportReady>());
        var month = campaign.Capture();

        // The reference keeps popups unsaved: the reloaded campaign has no report and its clock runs (ADR 0036).
        var reloaded = Reload(content, campaign, 11, "open-report.sav");
        Assert.Null(reloaded.GetQuery<ICampaignMonthlyReportQuery>()!.QueryMonthlyReport());
        Assert.Equal(month.MonthsPassed, reloaded.Capture().MonthsPassed);
        Assert.Equal(month.Funds, reloaded.Capture().Funds);
        AdvanceUnblocked(reloaded, 3);
    }

    [Fact]
    public void ACountryScriptThePortCannotRunRefusesTheMonthBeforeAnythingChanges()
    {
        var content = StrategicReadinessTestContent.Load(["strategic-world.rul"], """
            countries:
              - type: COUNTRY
                scripts:
                  newMonthCountry: |
                    var int offset;
                    rules.getSoundOffsetGeo offset 1;
                    return;
            """, extraRulesAfterOverlays: true);
        var snapshot = CreateSnapshot(content, "Unsupported country script") with { Time = MonthEnd };
        var campaign = CampaignState.Restore(snapshot, content, new SplitMix64RandomSource(5));
        var before = campaign.Capture();

        var result = campaign.Execute(new AdvanceCampaignTime(1));

        Assert.Equal(0, Assert.IsType<CampaignTimeAdvanced>(result.Events[0]).Summary.TickCount);
        Assert.Equal("Country script requires unsupported binding RuleMod.getSoundOffsetGeo.",
            Assert.Single(result.Events.OfType<CampaignActionBlocked>()).Reason);
        Assert.Equivalent(before, campaign.Capture(), strict: true);
    }

    [Fact]
    public void TwoBadMonthsEndTheCampaignWhenTheLoseRatingVideoLosesTheGame()
    {
        var content = StrategicReadinessTestContent.Load(["strategic-world.rul"], "cutscenes:\n  - type: loseGame\n");
        var snapshot = CreateSnapshot(content, "Bad months");
        snapshot = snapshot with { Identity = snapshot.Identity with { Ironman = true } };
        var region = snapshot.Regions[0];
        // Last month and this month both rate below the threshold: X-COM nothing, the aliens 900 each month.
        snapshot = snapshot with
        {
            Time = MonthEnd,
            MonthsPassed = 3,
            Funds = [100_000, 100_000],
            Maintenance = [0, 0],
            Incomes = [0, 0],
            Expenditures = [0, 0],
            ResearchScores = [0, 0],
            Regions = [region with { ActivityXcom = [0, 0], ActivityAlien = [900, 900] }, .. snapshot.Regions.Skip(1)],
        };
        var campaign = CampaignState.Restore(snapshot, content, new SplitMix64RandomSource(5));

        var report = Assert.Single(campaign.Execute(new AdvanceCampaignTime(1)).Events.OfType<MonthlyReportReady>()).Report;

        Assert.Equal(1, report.GameOver);
        Assert.Equal("STR_YOU_HAVE_NOT_SUCCEEDED", report.CouncilText);
        // The failed month gives no service or commendations; an ironman campaign saves after the cutscene.
        var acknowledged = campaign.Execute(new AcknowledgeMonthlyReport()).Events;
        Assert.Equal([new MonthlyReportAcknowledged("loseGame"), new CampaignEnded(2), new CampaignSaveRequested(CampaignSaveKind.Ironman)],
            acknowledged);
        Assert.Equal(2, campaign.Ending);
        Assert.All(campaign.Capture().Bases.SelectMany(static b => b.Soldiers), static s => Assert.Equal(0, s.Personal!.Diary.Counter("monthsService")));
    }

    [Fact]
    public void AnUndefinedLoseVideoLetsPlayGoOn()
    {
        // Mod::getVideo throws for the undefined video; the port plays nothing (ADR 0036).
        var content = StrategicReadinessTestContent.Load(["strategic-world.rul"], "");
        var snapshot = CreateSnapshot(content, "No lose video");
        var region = snapshot.Regions[0];
        snapshot = snapshot with
        {
            Time = MonthEnd,
            MonthsPassed = 3,
            Funds = [100_000, 100_000],
            Maintenance = [0, 0],
            Incomes = [0, 0],
            Expenditures = [0, 0],
            ResearchScores = [0, 0],
            Regions = [region with { ActivityXcom = [0, 0], ActivityAlien = [900, 900] }, .. snapshot.Regions.Skip(1)],
        };
        var campaign = CampaignState.Restore(snapshot, content, new SplitMix64RandomSource(5));

        Assert.Equal(1, Assert.Single(campaign.Execute(new AdvanceCampaignTime(1)).Events.OfType<MonthlyReportReady>()).Report.GameOver);
        Assert.Equal([new MonthlyReportAcknowledged(null)], campaign.Execute(new AcknowledgeMonthlyReport()).Events);
        Assert.Equal(0, campaign.Ending);
        AdvanceUnblocked(campaign, 3);
    }

    [Fact]
    public void AMissionWithoutWavesStartsItsOperationAndEndsOnItsNextPass()
    {
        // Rosigma starts alien bases with missions that have no waves (STR_ORK_BASE_START). processCommand starts the
        // mission with the script's delay, so AlienMission::start reads no wave; think has nothing to spawn and the
        // mission is over on its next half-hour pass, after its start chose the region's existing base.
        var content = StrategicReadinessTestContent.Load(["strategic-world.rul"], """
            alienMissions:
              - type: ZERO_WAVE_OPERATION
                operationType: 1
                raceWeights: {0: {RACE_A: 1}}
            missionScripts:
              - type: ZERO_WAVE_SCRIPT
                firstMonth: 3
                lastMonth: 3
                startDelay: 600
                missionWeights: {0: {ZERO_WAVE_OPERATION: 1}}
                regionWeights: {0: {REGION: 1}}
            defeatScore: -100000
            """, extraRulesAfterOverlays: true);
        var snapshot = CreateSnapshot(content, "Zero-wave operation") with { MonthsPassed = 3 };
        snapshot = snapshot with
        {
            Time = new CampaignTime(1, 1, 3, 1999, 0, 29, 55),
            World = snapshot.World with { AlienBases = [new AlienBaseSnapshot(1, "ALIEN_BASE_DEPLOY", "RACE_A", 0.45, -0.3) { Discovered = true }] },
        };
        var campaign = CampaignState.Restore(snapshot, content, new SplitMix64RandomSource(5));

        Assert.Empty(campaign.Execute(new ScheduleMonthlyWorldMissions(0, 0)).Events);
        var mission = Assert.Single(campaign.Capture().World.Missions, static m => m.RuleId == "ZERO_WAVE_OPERATION");
        Assert.Equal(600, mission.SpawnCountdown);
        Assert.Equal(1, mission.AlienBase?.Id);
        Reload(content, campaign, 5, "zero-wave-mission.sav");

        // The next half-hour pass removes the finished mission; the base stays.
        AdvanceUnblocked(campaign, 1);
        Assert.DoesNotContain(campaign.Capture().World.Missions, static m => m.RuleId == "ZERO_WAVE_OPERATION");
        Assert.Single(campaign.Capture().World.AlienBases);
    }

    [Fact]
    public void TheMonthlyAlienBaseRollFindsTheFirstUndiscoveredBase()
    {
        var content = StrategicReadinessTestContent.Load(["strategic-world.rul"], "chanceToDetectAlienBaseEachMonth: 100\ndefeatScore: -100000\n");
        var snapshot = CreateSnapshot(content, "Base detection") with { Time = MonthEnd };
        // In the fixture's region, where no X-COM facility or craft can see them.
        snapshot = snapshot with
        {
            World = snapshot.World with
            {
                AlienBases = [new AlienBaseSnapshot(1, "ALIEN_BASE_DEPLOY", "RACE_A", 0.45, -0.3) { Discovered = true },
                    new AlienBaseSnapshot(2, "ALIEN_BASE_DEPLOY", "RACE_A", 0.46, -0.3),
                    new AlienBaseSnapshot(3, "ALIEN_BASE_DEPLOY", "RACE_A", 0.47, -0.3)],
            },
        };
        var campaign = CampaignState.Restore(snapshot, content, new SplitMix64RandomSource(5));

        var events = campaign.Execute(new AdvanceCampaignTime(1)).Events;

        // Monthly upgrades in determineAlienMissions run first and may change the deployment.
        Assert.Equal(2, Assert.Single(events.OfType<AlienBaseDiscovered>()).Id);
        Assert.Equal([true, true, false], campaign.Capture().World.AlienBases.Select(static b => b.Discovered));
        // GeoscapeState::time1Month opens the report before the base window.
        Assert.True(events.ToList().FindIndex(static e => e is MonthlyReportReady) < events.ToList().FindIndex(static e => e is AlienBaseDiscovered));
    }

}

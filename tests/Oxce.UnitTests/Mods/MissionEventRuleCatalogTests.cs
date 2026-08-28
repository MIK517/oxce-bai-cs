using Oxce.Core.Diagnostics;
using Oxce.Formats.Yaml;
using Oxce.Mods;
using Oxce.Mods.Discovery;
using Oxce.Mods.Loading;
using Oxce.Mods.Rulesets;
using Oxce.Mods.Rulesets.CampaignStart;
using Oxce.Mods.Rulesets.EquipmentProduction;
using Oxce.Mods.Rulesets.Items;
using Oxce.Mods.Rulesets.MissionEvents;
using Oxce.Mods.Rulesets.PersonnelTactical;
using Oxce.Mods.Rulesets.TerrainDeployment;
using Xunit;

namespace Oxce.UnitTests.Mods;

public sealed class MissionEventRuleCatalogTests
{
    [Fact]
    public void LoadsMissionEventFamiliesAndUfopaediaUpdates()
    {
        const string first = """
            ufoTrajectories:
              - id: TRAJ
                groundTimer: 7
                waypoints: [[1, 2, 3]]
            alienMissions:
              - type: MISSION
                waves: [{ufo: UFO, count: 2, trajectory: TRAJ, timer: 30, objective: true}]
                missionWeights: {0: 5}
                raceWeights: {0: {RACE: 2}}
                regionWeights: {0: {REGION: 3}}
                multiUfoRetaliationExtra: true
            arcScripts:
              - type: ARC
                sequentialArcs: [RESEARCH]
                randomArcs: {RESEARCH: 2}
            events:
              - name: EVENT
                description: DESCRIPTION
                everyItemList: [ITEM]
                spawnedSoldier: {name: FIRST, stats: {tu: 40}}
            eventScripts:
              - type: EVENT_SCRIPT
                oneTimeSequentialEvents: [EVENT]
                eventWeights: {0: {EVENT: 4}}
                missionMinRuns: 2
                counterMin: 3
            missionScripts:
              - type: SCRIPT
                varName: runs
                maxRuns: 2
                missionWeights: {0: {MISSION: 5}}
                raceWeights: {0: {RACE: 2}}
                regionWeights: {0: {REGION: 2}}
            adhocScripts:
              - type: ADHOC
                adhocMissionScriptTags: [TAG]
            ufopaedia:
              - id: ITEM
                type_id: 4
                section: STR_ITEMS
                title: TITLE
                text: FIRST
            """;
        const string patch = """
            arcScripts:
              - update: ARC
                randomArcs: {RESEARCH: 0, RESEARCH_2: 3}
            alienMissions:
              - update: MISSION
                raceWeights: {0: {RACE: 0, RACE_2: 4}}
            events:
              - update: EVENT
                spawnedSoldier: {name: PATCH}
                weightedItemList: {OLD: 2}
              - update: EVENT
                weightedItemList: {OLD: 0, NEW: 3}
            ufopaedia:
              - id: ITEM
                text: PATCHED
                pages: [{title: PAGE_1}, {text: PAGE_2}]
              - id: DELETED
                type_id: 8
              - delete: DELETED
            """;
        using var fixture = new TemporaryMissionMod(("20-base.rul", first), ("10-patch.rul", patch));
        var diagnostics = new DiagnosticCollector();

        var catalog = MissionEventRuleCatalog.Load(CreatePlan(fixture.Root), diagnostics);

        var trajectory = Assert.Single(catalog.UfoTrajectories.Rules).Value;
        Assert.Equal(7, trajectory.GroundTimer); Assert.Equal(new(1, 2, 3), Assert.Single(trajectory.Waypoints));
        var mission = Assert.Single(catalog.AlienMissions.Rules).Value;
        Assert.True(mission.Booleans["multiUfoRetaliation"]); Assert.Equal(2ul, Assert.Single(mission.Waves).Count);
        Assert.Equal("RACE_2", Assert.Single(Assert.Single(mission.RaceWeights).Weights).Key);
        Assert.Equal(3, Assert.Single(catalog.EventScripts.Rules).Value.Integers["counterMin"]);
        Assert.Equal(3ul, Assert.Single(Assert.Single(catalog.ArcScripts.Rules).Value.Random).Value);
        Assert.Equal(5ul, Assert.Single(Assert.Single(catalog.MissionScripts.Rules).Value.MissionWeights).Weights["MISSION"]);
        var spawned = Assert.Single(catalog.Events.Rules).Value.SpawnedSoldier!;
        Assert.Equal("PATCH", YamlValueReader.ReadString(spawned.Entries.Single(entry => entry.ScalarKey == "name").Value));
        Assert.True(spawned.TryGet("stats", out _));
        Assert.Equal(3ul, Assert.Single(Assert.Single(catalog.Events.Rules).Value.WeightedItems).Value);
        var article = Assert.Single(catalog.Ufopaedia).Value;
        Assert.Equal(100, article.ListOrder); Assert.Equal(2, article.Pages.Count);
        Assert.Equal("PAGE_1", article.Pages[0].Title); Assert.Equal("PATCHED", article.Pages[0].Text);
        Assert.Equal("PAGE_2", article.Pages[1].Text); Assert.DoesNotContain("DELETED", catalog.Ufopaedia.Keys);
        Assert.DoesNotContain(diagnostics.Snapshot(), item => item.Code == ModDiagnosticCodes.UnconsumedRuleProperty);
        Assert.DoesNotContain(diagnostics.Snapshot(), item => item.Severity >= DiagnosticSeverity.Error);
    }

    [Theory]
    [InlineData("ufoTrajectories: [{id: T, waypoints: [[1, 2]]}]")]
    [InlineData("missionScripts: [{type: S, maxRuns: 1}]")]
    [InlineData("events: [{name: E, spawnedSoldier: NAME}]")]
    [InlineData("ufopaedia: [{id: A}]")]
    [InlineData("ufopaedia: [{id: A, type_id: 99}]")]
    [InlineData("ufopaedia: [{id: A, type_id: 8, pages: PAGE}]")]
    public void RejectsMalformedMissionEventProperties(string yaml)
    { using var fixture = new TemporaryMissionMod(("fixture.rul", yaml)); Assert.Throws<YamlFormatException>(() => MissionEventRuleCatalog.Load(CreatePlan(fixture.Root))); }

    [Fact]
    public void ReportsMissingStrategicReferences()
    {
        const string yaml = """
            alienMissions:
              - type: MISSION
                waves: [{ufo: MISSING_UFO, trajectory: MISSING_TRAJECTORY}]
                raceWeights: {0: {MISSING_RACE: 1}}
            missionScripts:
              - type: SCRIPT
                missionWeights: {0: {MISSING_MISSION: 1}}
                researchTriggers: {MISSING_RESEARCH: true}
            events:
              - name: EVENT
                everyItemList: [MISSING_ITEM]
                regionList: [MISSING_REGION]
            ufopaedia:
              - id: MISSING_ARTICLE_ITEM
                type_id: 4
            """;
        using var fixture = new TemporaryMissionMod(("fixture.rul", yaml)); var plan = CreatePlan(fixture.Root);
        var catalog = MissionEventRuleCatalog.Load(plan); var diagnostics = new DiagnosticCollector();

        var validation = catalog.ValidateRelationships(CampaignStartRuleCatalog.Load(plan), ItemRuleCatalog.Load(plan),
            EquipmentProductionRuleCatalog.Load(plan), PersonnelTacticalRuleCatalog.Load(plan),
            TerrainDeploymentRuleCatalog.Load(plan), diagnostics);

        Assert.False(validation.IsValid);
        Assert.DoesNotContain(validation.Issues, issue => issue.Property == "waves.ufo");
        Assert.Contains(validation.Issues, issue => issue.Property == "missionWeights");
        Assert.Contains(validation.Issues, issue => issue.Property == "items");
        Assert.Contains(validation.Issues, issue => issue.RuleId == "MISSING_ARTICLE_ITEM");
        Assert.Contains(diagnostics.Snapshot(), item => item.Code == ModDiagnosticCodes.MissingRuleReference);
    }

    [Fact]
    public void AcceptsReferenceCompatibleMissionWaveAndUfopaediaTargets()
    {
        const string yaml = """
            ufoTrajectories:
              - id: TRAJECTORY
                waypoints: [[0, 0, 0]]
            alienMissions:
              - type: DIRECT_SITE
                waves: [{ufo: DEPLOYMENT, trajectory: TRAJECTORY}]
              - type: OBJECTIVE_SITE
                objective: 3
                waves: [{ufo: RUNTIME_SELECTED_DEPLOYMENT, trajectory: TRAJECTORY, objective: true}]
              - type: NO_SPAWN
                waves: [{ufo: INTENTIONALLY_MISSING, trajectory: TRAJECTORY}]
            alienDeployments:
              - type: DEPLOYMENT
                markerName: STR_SITE
            ufopaedia:
              - id: GENERIC_TFTD_ARTICLE
                type_id: 10
              - id: MISSING_TFTD_CRAFT
                type_id: 11
              - id: MISSING_TFTD_USO
                type_id: 17
            """;
        using var fixture = new TemporaryMissionMod(("fixture.rul", yaml));
        var plan = CreatePlan(fixture.Root);
        var catalog = MissionEventRuleCatalog.Load(plan);
        var diagnostics = new DiagnosticCollector();

        var validation = catalog.ValidateRelationships(
            CampaignStartRuleCatalog.Load(plan),
            ItemRuleCatalog.Load(plan),
            EquipmentProductionRuleCatalog.Load(plan),
            PersonnelTacticalRuleCatalog.Load(plan),
            TerrainDeploymentRuleCatalog.Load(plan),
            diagnostics);

        Assert.DoesNotContain(validation.Issues, issue =>
            (issue.RuleId is "DIRECT_SITE" or "OBJECTIVE_SITE" or "NO_SPAWN") &&
            issue.Property == "waves.ufo");
        Assert.DoesNotContain(validation.Issues, issue => issue.RuleId == "GENERIC_TFTD_ARTICLE");
        Assert.Contains(validation.Issues, issue => issue.RuleId == "MISSING_TFTD_CRAFT");
        Assert.Contains(validation.Issues, issue => issue.RuleId == "MISSING_TFTD_USO");
    }

    private static ModLoadPlan CreatePlan(string root)
    { var discovery = ModDiscovery.ScanDirectory(root); return ModLoadPlanner.Create(ModCatalog.Create(discovery.Mods), [new ModActivation("fixture", true)], "fixture", new ModEngineIdentity("Extended", "8.6.1.0")); }
    private sealed class TemporaryMissionMod : IDisposable
    {
        public TemporaryMissionMod(params (string Name, string Yaml)[] rulesets)
        { Root = Path.Combine(Path.GetTempPath(), $"oxce-mission-test-{Guid.NewGuid():N}"); var mod = Path.Combine(Root, "fixture"); var rules = Path.Combine(mod, "Ruleset"); Directory.CreateDirectory(rules); File.WriteAllText(Path.Combine(mod, "metadata.yml"), "id: fixture\nname: Fixture\nversion: 1.0\nisMaster: true\nreservedSpace: 1000\n"); foreach (var item in rulesets) File.WriteAllText(Path.Combine(rules, item.Name), item.Yaml); }
        public string Root { get; }
        public void Dispose() => Directory.Delete(Root, true);
    }
}

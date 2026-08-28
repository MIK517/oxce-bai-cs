using System.Text.Json;
using Oxce.Core.Diagnostics;
using Oxce.Formats.Yaml;
using Oxce.Mods;
using Oxce.Mods.Discovery;
using Oxce.Mods.Loading;
using Oxce.Mods.Rulesets.MissionEvents;
using Xunit;

namespace Oxce.CompatibilityTests;

public sealed class MissionEventRulesFixtureTests
{
    [Fact]
    public void MissionEventRulesMatchPinnedReferenceFixture()
    {
        var root = FindRepositoryRoot(); var fixture = Path.Combine(root, "fixtures", "public", "mods", "mission-event-rules");
        using var expected = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "fixtures", "expected", "mods", "mission-event-rules.expected.json")));
        var diagnostics = new DiagnosticCollector(); var discovery = ModDiscovery.ScanDirectory(fixture, diagnostics);
        var plan = ModLoadPlanner.Create(ModCatalog.Create(discovery.Mods, diagnostics), [new ModActivation("fixture", true)], "fixture", new ModEngineIdentity("Extended", "8.6.1.0"), diagnostics);
        var actual = MissionEventRuleCatalog.Load(plan, diagnostics); var document = expected.RootElement;
        var trajectory = Assert.Single(actual.UfoTrajectories.Rules).Value; var waypoint = Assert.Single(trajectory.Waypoints);
        Assert.Equal(document.GetProperty("trajectory").EnumerateArray().Select(x => x.GetInt32()), new[] { trajectory.GroundTimer, trajectory.Waypoints.Count, waypoint.Zone, waypoint.Altitude, waypoint.Speed });
        var mission = Assert.Single(actual.AlienMissions.Rules).Value; var wave = Assert.Single(mission.Waves); var missionExpected = document.GetProperty("alienMission").EnumerateArray().ToArray();
        Assert.Equal(missionExpected[0].GetUInt64(), wave.Count); Assert.Equal(missionExpected[1].GetUInt64(), wave.Timer); Assert.Equal(missionExpected[2].GetBoolean(), wave.Objective);
        Assert.Equal(missionExpected[3].GetInt32(), mission.MissionWeights[0]); Assert.Equal(missionExpected[4].GetBoolean(), mission.Booleans["multiUfoRetaliation"]);
        Assert.Equal(missionExpected[5].GetUInt64(), Assert.Single(mission.RaceWeights).Weights["RACE_2"]); Assert.Equal(missionExpected[6].GetUInt64(), Assert.Single(mission.RegionWeights).Weights["REGION"]);
        var arc = Assert.Single(actual.ArcScripts.Rules).Value; Assert.Equal(document.GetProperty("arc").EnumerateArray().Select(x => x.GetInt32()), new[] { arc.Integers["firstMonth"], arc.Integers["lastMonth"], (int)arc.Random["RESEARCH"] });
        var eventRule = Assert.Single(actual.Events.Rules).Value; var eventExpected = document.GetProperty("event").EnumerateArray().ToArray();
        Assert.Equal(eventExpected[0].GetString(), eventRule.Strings["description"]); Assert.Equal(eventExpected[1].GetInt32(), eventRule.EveryItems.Count);
        Assert.True(eventRule.SpawnedSoldier!.TryGet("name", out var name)); Assert.Equal(eventExpected[2].GetString(), YamlValueReader.ReadString(name!)); Assert.Equal(eventExpected[3].GetBoolean(), eventRule.SpawnedSoldier.TryGet("stats", out _));
        var eventScript = Assert.Single(actual.EventScripts.Rules).Value; Assert.Equal(document.GetProperty("eventScript").EnumerateArray().Select(x => x.GetInt32()), new[] { eventScript.Integers["counterMin"], (int)Assert.Single(eventScript.EventWeights).Weights["EVENT"] });
        var missionScript = Assert.Single(actual.MissionScripts.Rules).Value; Assert.Equal(document.GetProperty("missionScript").EnumerateArray().Select(x => x.GetInt32()), new[] { missionScript.Integers["maxRuns"], (int)Assert.Single(missionScript.MissionWeights).Weights["MISSION"] });
        Assert.Equal(document.GetProperty("adhoc")[0].GetInt32(), Assert.Single(actual.AdhocScripts.Rules).Value.Tags.Count);
        var article = Assert.Single(actual.Ufopaedia).Value; var articleExpected = document.GetProperty("article").EnumerateArray().ToArray();
        Assert.Equal(articleExpected[0].GetInt32(), article.TypeId); Assert.Equal(articleExpected[1].GetInt32(), article.ListOrder); Assert.Equal(articleExpected[2].GetInt32(), article.Pages.Count);
        Assert.Equal(articleExpected[3].GetString(), article.Pages[0].Title); Assert.Equal(articleExpected[4].GetString(), article.Pages[0].Text); Assert.Equal(articleExpected[5].GetString(), article.Pages[1].Text);
        Assert.DoesNotContain(diagnostics.Snapshot(), item => item.Severity >= DiagnosticSeverity.Error);
    }
    private static string FindRepositoryRoot() { var directory = new DirectoryInfo(AppContext.BaseDirectory); while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Oxce.slnx"))) directory = directory.Parent; return directory?.FullName ?? throw new DirectoryNotFoundException(); }
}

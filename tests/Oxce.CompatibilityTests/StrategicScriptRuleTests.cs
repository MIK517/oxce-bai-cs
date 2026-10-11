using System.Text;
using System.Text.Json;
using Oxce.Core.Random;
using Oxce.FixtureSupport;
using Oxce.Gameplay.Campaigns.World;
using Oxce.Mods.Rulesets.Runtime;
using Oxce.TestSupport;
using Xunit;

namespace Oxce.CompatibilityTests;

public sealed class StrategicScriptRuleTests
{
    [Fact]
    public void SchedulingRulesRetainOrderedLinkedDataAcrossCache()
    {
        TestFixtures.LoadVerifiedManifest("strategic-script-rules");
        var rules = StrategicReadinessTestContent.Load("strategic-script-rules.rul").RuntimeRules;
        Assert.Equal(["Z_COMMAND", "A_DEFAULT", "SITE_COMMAND"], rules.MissionScripts.Rules.Select(rule => rule.Id));
        var mission = rules.MissionScripts[rules.MissionScripts.GetRequired("Z_COMMAND")].Value;
        Assert.Equal("history", mission.VariableName);
        Assert.Equal(7, mission.Label);
        Assert.Equal([3, -2, 3], mission.Conditionals);
        Assert.Equal(3, mission.MaximumRuns);
        Assert.Equal(2, mission.AvoidRepeats);
        Assert.Equal(30, mission.StartDelay);
        Assert.Equal(60, mission.RandomDelay);
        Assert.Equal(25, mission.TargetBaseOdds);
        Assert.False(mission.UseTable);
        Assert.False(mission.SiteType);
        Assert.Equal(-1, mission.Criteria.FirstMonth);
        Assert.Equal(12, mission.Criteria.LastMonth);
        Assert.Equal(-5000000000L, mission.Criteria.MinimumFunds);
        Assert.Equal(6000000000L, mission.Criteria.MaximumFunds);
        Assert.Equal("generated", mission.Criteria.MissionVariable);
        Assert.Equal("marker", mission.Criteria.MissionMarker);
        Assert.Equal(2, mission.Criteria.CounterMinimum);
        Assert.Equal(9, mission.Criteria.CounterMaximum);
        Assert.Equal([0UL, 6UL, 2UL], mission.Missions.Select(entry => entry.FirstMonth));
        Assert.Equal(["M_A", "M_B"], mission.Missions[0].Choices.Select(choice => choice.Rule.Id));
        Assert.Equal("M_A", rules.AlienMissions.GetExternalId(mission.Missions[0].Choices[0].Rule.Handle!.Value));
        Assert.Equal("RACE", rules.AlienRaces.GetExternalId(mission.Races[0].Choices[0].Rule.Handle!.Value));
        Assert.Equal("REGION", rules.Regions.GetExternalId(mission.Regions[0].Choices[0].Rule.Handle!.Value));
        Assert.Equal(["MISSING_RESEARCH", "UNLOCK"], mission.Criteria.Research.Select(trigger => trigger.Rule.Id));
        Assert.Null(mission.Criteria.Research[0].Rule.Handle);
        Assert.False(mission.Criteria.Research[0].Expected);
        Assert.True(mission.Criteria.Research[1].Expected);
        Assert.NotNull(mission.Criteria.Research[1].Rule.Handle);
        Assert.True(Assert.Single(mission.Criteria.Items).Expected);
        Assert.False(Assert.Single(mission.Criteria.Facilities).Expected);
        Assert.Null(Assert.Single(mission.Criteria.SoldierTypes).Rule.Handle);
        Assert.NotNull(Assert.Single(mission.Criteria.BaseRegions).Rule.Handle);
        Assert.False(Assert.Single(mission.Criteria.BaseCountries).Expected);
        Assert.True(Assert.Single(mission.Criteria.PactCountries).Expected);
        Assert.True(rules.MissionScripts[rules.MissionScripts.GetRequired("SITE_COMMAND")].Value.SiteType);
        Assert.Equal(7, rules.MissionScripts[rules.MissionScripts.GetRequired("SITE_COMMAND")].Value.Label);
        var adhoc = Assert.Single(rules.AdhocScripts.Rules).Value;
        Assert.Equal(["first", "second", "first"], adhoc.AdhocTags);
        Assert.NotNull(adhoc.Missions[0].Choices[0].Rule.Handle);
        var arc = rules.ArcScripts[rules.ArcScripts.GetRequired("ARC")].Value;
        Assert.Equal(4, arc.MaximumArcs);
        Assert.Equal(["UNLOCK", "ARC_RESEARCH", "UNLOCK"], arc.Sequential.Select(rule => rule.Id));
        Assert.Equal(["ARC_RESEARCH", "UNLOCK"], arc.Random.Select(choice => choice.Rule.Id));
        Assert.All(arc.Random, choice => Assert.NotNull(choice.Rule.Handle));
        var events = rules.EventScripts[rules.EventScripts.GetRequired("EVENT")].Value;
        Assert.Equal(["M_A", "M_A", "M_B"], events.Sequential.Select(rule => rule.Id));
        Assert.All(events.Sequential, rule => Assert.NotNull(rule.Handle));
        Assert.True(events.AffectsGameProgression);
        Assert.Equal(8, events.Criteria.CounterMinimum);
        Assert.Equal(10, events.Criteria.CounterMaximum);
        Assert.Throws<NotSupportedException>(() => ((IList<RuntimeScriptTimeline<AlienMissionRuleFamily>>)mission.Missions).Clear());
        Assert.Throws<NotSupportedException>(() => ((IList<int>)mission.Conditionals).Clear());

        var defaults = rules.MissionScripts[rules.MissionScripts.GetRequired("A_DEFAULT")].Value;
        Assert.Equal(0, defaults.Criteria.FirstMonth);
        Assert.Equal(-1, defaults.Criteria.LastMonth);
        Assert.Equal(100, defaults.Criteria.ExecutionOdds);
        Assert.Equal(0, defaults.Criteria.MinimumDifficulty);
        Assert.Equal(4, defaults.Criteria.MaximumDifficulty);
        Assert.Equal(int.MinValue, defaults.Criteria.MinimumScore);
        Assert.Equal(int.MaxValue, defaults.Criteria.MaximumScore);
        Assert.Equal(long.MinValue, defaults.Criteria.MinimumFunds);
        Assert.Equal(long.MaxValue, defaults.Criteria.MaximumFunds);
        Assert.Equal(-1, defaults.MaximumRuns);
        Assert.True(defaults.UseTable);
        Assert.False(defaults.SiteType);
        Assert.Empty(defaults.Missions);
        Assert.Equal(-1, rules.ArcScripts[rules.ArcScripts.GetRequired("ARC_DEFAULT")].Value.MaximumArcs);
        Assert.False(rules.EventScripts[rules.EventScripts.GetRequired("EVENT_DEFAULT")].Value.AffectsGameProgression);
    }

    [Fact]
    public void TimelineSelectionMatchesExtractedReference()
    {
        var rules = StrategicReadinessTestContent.Load("strategic-script-rules.rul").RuntimeRules;
        var mission = rules.MissionScripts[rules.MissionScripts.GetRequired("Z_COMMAND")].Value;
        var events = rules.EventScripts[rules.EventScripts.GetRequired("EVENT")].Value;
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-script-rules");
        foreach (var row in TestFixtures.Rows(oracle.RootElement, "rows"))
        {
            var month = row[0].GetUInt64();
            var roll = row[1].GetInt32();
            var random = new ScriptRoll(roll);
            var selectedMission = WorldScriptSelection.Generate(mission.Missions, month, random);
            var selectedEvent = WorldScriptSelection.Generate(events.Events, month, random, allowEmptyTimeline: true);
            var actual = JsonSerializer.SerializeToUtf8Bytes(new object?[] { month, roll, selectedMission?.Id, selectedEvent?.Id, random.Calls });
            Assert.Equal(CanonicalJson.Normalize(Encoding.UTF8.GetBytes(row.GetRawText())), CanonicalJson.Normalize(actual));
        }
        var names = WorldScriptSelection.Available(mission.Missions, -1).Select(choice => choice.Rule.Id).ToArray();
        Assert.Equal(CanonicalJson.Normalize(Encoding.UTF8.GetBytes(oracle.RootElement.GetProperty("beforeFirst").GetRawText())),
            CanonicalJson.Normalize(JsonSerializer.SerializeToUtf8Bytes(names)));
        var empty = rules.EventScripts[rules.EventScripts.GetRequired("EVENT_DEFAULT")].Value;
        var noDraw = new ScriptRoll(1);
        Assert.Equal(oracle.RootElement.GetProperty("emptyEvent").GetString(),
            WorldScriptSelection.Generate(empty.Events, 0, noDraw, allowEmptyTimeline: true)?.Id ?? "");
        Assert.Equal(oracle.RootElement.GetProperty("emptyCalls").GetInt32(), noDraw.Calls);
        foreach (var row in TestFixtures.Rows(oracle.RootElement, "delayRows"))
        {
            var random = new ScriptRoll(1);
            var script = mission with { StartDelay = row[0].GetInt32(), RandomDelay = row[1].GetInt32() };
            var actual = JsonSerializer.SerializeToUtf8Bytes(new[]
            { script.StartDelay, script.RandomDelay, WorldMissionScriptSelection.Delay(script, random), random.Calls });
            Assert.Equal(CanonicalJson.Normalize(Encoding.UTF8.GetBytes(row.GetRawText())), CanonicalJson.Normalize(actual));
        }
    }

    private sealed class ScriptRoll(int roll) : IRandomSource
    {
        public int Calls { get; private set; }
        public int NextInclusive(int minimum, int maximum) { Calls++; Assert.InRange(roll, minimum, maximum); return roll; }
        public int NextExclusive(int maximum) => throw new InvalidOperationException();
        public double NextUnit() => throw new InvalidOperationException();
    }
}

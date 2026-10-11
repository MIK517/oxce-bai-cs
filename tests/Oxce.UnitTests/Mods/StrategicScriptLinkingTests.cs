using Oxce.FixtureSupport;
using Oxce.Mods.Rulesets;
using Oxce.Mods.Rulesets.Content;
using Oxce.TestSupport;
using Xunit;

namespace Oxce.UnitTests.Mods;

public sealed class StrategicScriptLinkingTests
{
    [Theory]
    [InlineData("missionScripts", "MISSING", "does not exist")]
    [InlineData("adhocScripts", "MISSING", "does not exist")]
    [InlineData("missionScripts", "SITE", "cannot mix")]
    [InlineData("adhocScripts", "SITE", "cannot mix")]
    public void InvalidMissionSelectionsFailLinking(string family, string second, string reason)
    {
        using var fixture = new TemporaryModFixture($$"""
            alienMissions:
              - type: ORDINARY
              - type: SITE
                objective: 3
            {{family}}:
              - type: SCRIPT
                missionWeights:
                  0: {ORDINARY: 1}
                  6: { {{second}}: 1}
            """);
        var snapshot = ContentSnapshotBuilder.Build(TestFixtures.CreatePlan(fixture.Root));
        Assert.False(snapshot.Capabilities.Has(ContentLoadStage.RuntimeLinked));
        Assert.Contains(snapshot.Diagnostics, diagnostic => diagnostic.Message.Contains(reason, StringComparison.Ordinal));
    }

    [Fact]
    public void MissingLaterMissionRemainsUnresolvedAsInReferenceValidation()
    {
        // Mod::loadAll only rejects a missing first ordinal mission; later missing IDs are skipped.
        using var fixture = new TemporaryModFixture("""
            alienMissions: [{type: A_SITE, objective: 3}]
            missionScripts:
              - type: SCRIPT
                missionWeights: {0: {Z_MISSING: 1}, 6: {A_SITE: 1}}
            """);
        var snapshot = ContentSnapshotBuilder.Build(TestFixtures.CreatePlan(fixture.Root));
        Assert.True(snapshot.Capabilities.Has(ContentLoadStage.RuntimeLinked));
        var rule = Assert.Single(snapshot.Content.RuntimeRules.MissionScripts.Rules).Value;
        Assert.True(rule.SiteType);
        Assert.Null(Assert.Single(rule.Missions[0].Choices).Rule.Handle);
    }

    [Fact]
    public void RepeatedRuleLoadsAppendTimelinesAndReplaceTriggerMaps()
    {
        using var fixture = new TemporaryModFixture("""
            alienMissions: [{type: A}, {type: B}]
            missionScripts:
              - type: SCRIPT
                missionWeights: {0: {A: 1}}
                researchTriggers: {OLD: true}
              - type: SCRIPT
                missionWeights: {0: {B: 1}}
                researchTriggers: {NEW: false}
            """);
        var snapshot = ContentSnapshotBuilder.Build(TestFixtures.CreatePlan(fixture.Root));
        Assert.True(snapshot.Capabilities.Has(ContentLoadStage.RuntimeLinked));
        var rule = Assert.Single(snapshot.Content.RuntimeRules.MissionScripts.Rules).Value;
        Assert.Equal(["A", "B"], rule.Missions.Select(entry => Assert.Single(entry.Choices).Rule.Id));
        Assert.Equal("NEW", Assert.Single(rule.Criteria.Research).Rule.Id);
        Assert.False(Assert.Single(rule.Criteria.Research).Expected);
    }

    [Fact]
    public void OptionalEventSelectionsKeepMissingReferencesForTheCaller()
    {
        using var fixture = new TemporaryModFixture("""
            eventScripts:
              - type: SCRIPT
                eventWeights: {0: {MISSING: 1}}
            """);
        var snapshot = ContentSnapshotBuilder.Build(TestFixtures.CreatePlan(fixture.Root));
        Assert.True(snapshot.Capabilities.Has(ContentLoadStage.RuntimeLinked));
        var choice = Assert.Single(Assert.Single(snapshot.Content.RuntimeRules.EventScripts.Rules).Value.Events[0].Choices);
        Assert.Equal("MISSING", choice.Rule.Id);
        Assert.Null(choice.Rule.Handle);
    }
}

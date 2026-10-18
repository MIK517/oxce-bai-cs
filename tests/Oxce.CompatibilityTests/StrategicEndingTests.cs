using Oxce.Core.Random;
using Oxce.Gameplay.Campaigns;
using Oxce.Gameplay.Campaigns.World;
using Oxce.Mods.Rulesets.Content;
using Xunit;
using static Oxce.CompatibilityTests.StrategicWorldTestSupport;

namespace Oxce.CompatibilityTests;

/// <summary>
/// Branch 6 S6 (plan D4): one ending rule. A research discovery's cutscene (GeoscapeState::time1Day), then its free
/// topic's, ends an unended campaign by its video's <c>winGame</c> or <c>loseGame</c>; event cutscenes and the monthly
/// failure use the same rule on their acknowledgement (StrategicEventPayloadTests, StrategicMonthlyCycleTests). An
/// ended campaign refuses every command and keeps its ending across a reload.
/// </summary>
public sealed class StrategicEndingTests
{
    private const string Rules = """
        defeatScore: -100000
        facilities:
          - type: TEST_LAB
            mapName: LOGISTICS
            labs: 10
        cutscenes:
          - type: winVideo
            winGame: true
          - type: loseVideo
            loseGame: true
          - type: plainVideo
        research:
          - name: TOPIC_WIN
            cost: 10
            cutscene: winVideo
          - name: TOPIC_PLAIN_FREE_LOSE
            cost: 10
            cutscene: plainVideo
            getOneFree: [FREE_LOSE]
          - name: FREE_LOSE
            cost: 10
            cutscene: loseVideo
          - name: TOPIC_WIN_FREE_LOSE
            cost: 10
            cutscene: winVideo
            getOneFree: [FREE_LOSE]
          - name: TOPIC_UNDEFINED
            cost: 10
            cutscene: noSuchVideo
        """;

    private static readonly Lazy<RuntimeContent> Content = new(() =>
        StrategicReadinessTestContent.Load(["strategic-world.rul"], Rules, extraRulesAfterOverlays: true));

    private static CampaignState Researching(string topic)
    {
        var snapshot = CreateSnapshot(Content.Value, "Ending").WithFacility("TEST_LAB");
        var owner = snapshot.Bases[0];
        return CampaignState.Restore(snapshot with
        {
            Time = new(1, 1, 1, 1999, 23, 59, 55),
            // The fixture's zero-cost UNLOCK is known, so no grant discovers it.
            CompletedResearch = ["UNLOCK"],
            Bases = [owner with { Research = [new ResearchProjectSnapshot(topic, 1, 9, 10)] }, .. snapshot.Bases.Skip(1)],
        }, Content.Value, new SplitMix64RandomSource(5));
    }

    [Theory]
    [InlineData("TOPIC_WIN", new[] { "winVideo" }, 1)]
    [InlineData("TOPIC_PLAIN_FREE_LOSE", new[] { "plainVideo", "loseVideo" }, 2)]
    [InlineData("TOPIC_WIN_FREE_LOSE", new[] { "winVideo", "loseVideo" }, 1)]
    [InlineData("TOPIC_UNDEFINED", new string[0], 0)]
    public void AResearchCutsceneEndsAnUnendedCampaignByItsVideo(string topic, string[] played, int ending)
    {
        var campaign = Researching(topic);

        var events = campaign.Execute(new AdvanceCampaignTime(1)).Events;

        // The topic's video, then its free topic's; the first that wins or loses decides, the second plays on.
        Assert.Equal(played, events.OfType<CampaignCutscenePlayed>().Select(static e => e.Cutscene));
        Assert.Equal(ending == 0 ? [] : [new CampaignEnded(ending)], events.OfType<CampaignEnded>());
        Assert.Equal(ending, campaign.Ending);
        Assert.Contains(events, e => e is CampaignResearchCompleted { RuleId: var id } && id == topic);
        if (ending == 0)
        {
            AdvanceUnblocked(campaign, 1);
            return;
        }
        Assert.Equal("This campaign has ended.", Assert.IsType<CampaignActionBlocked>(Assert.Single(
            campaign.Execute(new AdvanceCampaignTime(1)).Events)).Reason);
        // The ending is saved; the reloaded campaign is still over.
        var reloaded = Reload(Content.Value, campaign, 5, "ended.sav");
        Assert.Equal(ending, reloaded.Ending);
        Assert.Equal("This campaign has ended.", Assert.IsType<CampaignActionBlocked>(Assert.Single(
            reloaded.Execute(new AdvanceCampaignTime(1)).Events)).Reason);
    }
}

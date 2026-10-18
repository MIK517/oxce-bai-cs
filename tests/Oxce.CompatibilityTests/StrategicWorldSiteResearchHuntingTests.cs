using Oxce.Core.Random;
using Oxce.Gameplay.Campaigns;
using Oxce.Gameplay.Campaigns.World;
using Oxce.Mods.Rulesets.Content;
using Oxce.TestSupport;
using Xunit;
using static Oxce.CompatibilityTests.StrategicWorldSiteResearchTests;
using static Oxce.CompatibilityTests.StrategicWorldTestSupport;

namespace Oxce.CompatibilityTests;

public sealed class StrategicWorldSiteResearchHuntingTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SiteResearchPrecedesHunterSelectionAndItsRetargetedFlight(bool uncertain)
    {
        TestFixtures.LoadVerifiedManifest("strategic-hunting");
        TestFixtures.LoadVerifiedManifest("strategic-research-diary-execution");
        var content = Content(uncertain);
        var snapshot = Snapshot(content);
        // An idle hunter can begin at its ordinary waypoint: retargeting to a
        // distant craft takes precedence over the uncertain ordinary arrival.
        var ufo = snapshot.World.Ufos[0];
        snapshot = snapshot with
        {
            World = snapshot.World with
            { Ufos = [ufo with { Destination = WorldTargetReference.ForWaypoint(0, ufo.Position) }] }
        };
        var campaign = CampaignState.Restore(snapshot, content, new SplitMix64RandomSource(17));
        var result = campaign.Execute(new AdvanceCampaignTime(1));
        Assert.Empty(result.Events.OfType<CampaignActionBlocked>());
        Assert.Single(result.Events.OfType<UfoStartedHunting>());
        var after = campaign.Capture();
        var hunter = Assert.Single(after.World.Ufos);
        Assert.True(hunter.Hunting);
        Assert.Equal(WorldTargetKind.Craft, hunter.Destination!.Kind);
        Assert.Equal("SHIP", hunter.Destination.TypeId);
        Assert.Equal(3300, hunter.Speed);
        Assert.NotEqual(ufo.Longitude, hunter.Longitude);
        Assert.Empty(after.World.MissionSites);
        Assert.Equal(uncertain ? 2 : 1, after.ResearchDiary.Count);
        Assert.Equal(!uncertain || after.CompletedResearch.Contains("BONUS"), Assert.Single(after.World.Missions).Interrupted);
        campaign = ReloadRepeatedly(content, campaign, 17, "site-research-hunter.sav", 3, after);
    }

    internal static RuntimeContent Content(bool uncertain = false, string extraRules = "", string[]? ruleFiles = null, string? finalRules = null) =>
        StrategicReadinessTestContent.Load(ruleFiles ?? ["strategic-world.rul", "strategic-hunting.rul"], $$"""
            research:
              - {name: UNLOCK, cost: 1}
              - {name: CORE, cost: 1{{(uncertain ? ", getOneFree: [BONUS, OTHER]" : "")}}}
              - {name: BONUS, cost: 1}
              - {name: OTHER, cost: 1}
            alienMissions:
              - {type: MISSION_SCOUT, interruptResearch: {{(uncertain ? "BONUS" : "CORE")}}}
            """ + "\n" + Deployment + "\n" + extraRules, finalRules: finalRules);

    internal static CampaignSnapshot Snapshot(RuntimeContent content)
    {
        var initial = CreateUfoTransitCampaign(content, new(1, 1, 1, 1999, 1, 29, 55), "HUNTER", 2200).Capture();
        initial = initial with
        {
            World = initial.World with
            {
                Ufos = [initial.World.Ufos[0] with { HunterKiller = true }],
                MissionSites = StrategicWorldSiteResearchTests.Snapshot(content).World.MissionSites,
            }
        };
        return initial.WithCraft("SHIP", craft => craft with
        {
            Logistics = craft.Logistics! with
            {
                Status = "STR_OUT",
                Takeoff = 0,
                Fuel = 100,
                Speed = 0,
                Longitude = 0.45,
                Latitude = 0.21,
                Destination = null,
            }
        });
    }
}

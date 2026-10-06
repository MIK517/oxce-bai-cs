using Oxce.Core.Random;
using Oxce.Gameplay.Campaigns;
using Oxce.Gameplay.Campaigns.World;
using Oxce.TestSupport;
using Xunit;
using static Oxce.CompatibilityTests.StrategicWorldTestSupport;

namespace Oxce.CompatibilityTests;

public sealed partial class StrategicWorldHuntingTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MixedHunterModesReplaceCandidatesBetweenActorsTicksAndReloads(bool cache)
    {
        TestFixtures.LoadVerifiedManifest("strategic-hunting");
        var content = Load(cache, "crafts: [{type: HUNT_SLOW, speedMax: 2000, damageMax: 100, fuelMax: 300}, {type: HUNT_FAST, speedMax: 6000, damageMax: 100, fuelMax: 300}]\n");
        var initial = HuntingSnapshot(content);
        var owner = initial.Bases[0];
        var template = owner.Crafts.Single(craft => craft.RuleId == "SHIP");
        initial = initial with
        {
            Bases = [owner with { Soldiers = [], Crafts = [template with { RuleId = "HUNT_SLOW", Id = 1,
                Logistics = template.Logistics! with { Weapons = [] } }, template with { RuleId = "HUNT_FAST", Id = 2,
                Logistics = template.Logistics! with { Weapons = [] } }] }, .. initial.Bases.Skip(1)],
            World = initial.World with
            {
                Missions = [initial.World.Missions[0] with { LiveUfos = 3 }],
                Ufos = [.. Enumerable.Range(0, 3).Select(index => initial.World.Ufos[0] with
                    { UniqueId = 9 + index, Id = 3 + index, HuntMode = index % 2 })],
            },
        };
        var campaign = CampaignState.Restore(initial, content, new SplitMix64RandomSource(81));
        int[] expectedTargets = [2, 1, 2];
        for (var tick = 0; tick < 6; tick++)
        {
            AdvanceUnblocked(campaign, 1);
            var after = campaign.Capture();
            Assert.Equal(expectedTargets, after.World.Ufos.Select(ufo => ufo.Destination!.Id));
            Assert.All(after.World.Ufos, ufo => Assert.True(ufo.Hunting));
            if (tick == 2) campaign = Reload(content, campaign, 81, "mixed-hunter-modes.sav");
        }
        Assert.DoesNotContain(campaign.Execute(new RecallCraft(owner.Id, "HUNT_FAST", 2)).Events,
            item => item is CampaignActionBlocked);
        var recalled = campaign.Capture().Bases[0].Crafts.Single(craft => craft.RuleId == "HUNT_FAST").Logistics!;
        Assert.False(recalled.MissionComplete);
        AdvanceUnblocked(campaign, 1);
        // Manual recall does not set the native HK ignore flags. The current target
        // remains selected, but its coordinates must come from this pass.
        Assert.Equal(expectedTargets, campaign.Capture().World.Ufos.Select(ufo => ufo.Destination!.Id));
        var moved = campaign.Capture().Bases[0].Crafts.Single(craft => craft.RuleId == "HUNT_FAST").Logistics!;
        Assert.NotEqual(new WorldPosition(recalled.Longitude, recalled.Latitude), new(moved.Longitude, moved.Latitude));
        Assert.Equal(new WorldPosition(moved.Longitude, moved.Latitude),
            campaign.Capture().World.Ufos[0].Destination!.Position);
        Reload(content, campaign, 81, "mixed-hunter-recall.sav");
    }
}

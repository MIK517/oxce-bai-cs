using Oxce.Core.Random;
using Oxce.Gameplay.Campaigns;
using Oxce.Gameplay.Campaigns.World;
using Oxce.Mods.Rulesets.Content;
using Oxce.TestSupport;
using Xunit;
using static Oxce.CompatibilityTests.StrategicWorldTestSupport;

namespace Oxce.CompatibilityTests;

public sealed class StrategicWorldIgnoredContactTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void IgnoredContactsRediscoverWithoutAlertsAndResetOnReload(bool ignore)
    {
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-ignored-contacts");
        var expected = Assert.Single(TestFixtures.Rows(oracle.RootElement, "rows"), row =>
            row[0].GetArrayLength() == 1 && row[0][0].GetInt32() == (ignore ? 3 : 0) &&
            row[1].GetInt32() == 3 && row[2].GetInt32() == 0);
        var content = StrategicReadinessTestContent.Load("strategic-world.rul");
        var campaign = Create(content);
        var before = campaign.Capture();
        if (ignore)
        {
            Assert.IsType<UfoContactIgnored>(Assert.Single(campaign.Execute(new IgnoreUfoContact(9, 3)).Events));
            Assert.IsType<UfoContactIgnored>(Assert.Single(campaign.Execute(new IgnoreUfoContact(9, 3)).Events));
            Assert.Equivalent(before, campaign.Capture(), strict: true);
        }
        Assert.Equal(ignore, Target(campaign).DetectionAlertIgnored);
        AdvanceOne(campaign); // No radar: lose the tracked contact at 01:30.
        Assert.False(Assert.Single(campaign.Capture().World.Ufos).Detected);
        Assert.IsType<CampaignActionBlocked>(Assert.Single(campaign.Execute(new IgnoreUfoContact(9, 3)).Events));
        Assert.IsType<CampaignFacilityChanged>(Assert.Single(campaign.Execute(
            new BuildCampaignFacility(0, "RADAR_HYPER_TEST", 1, 0)).Events));
        AdvanceUnblocked(campaign, 359);
        var reloaded = Reload(content, campaign, 17, "ignored-ufo-contact.sav");
        var result = campaign.Execute(new AdvanceCampaignTime(1));
        Assert.Equal(expected[4].GetInt32(), result.Events.OfType<UfoContactDetected>().Count());
        Assert.True(Assert.Single(campaign.Capture().World.Ufos).HyperDetected);
        Assert.Equal(ignore, Target(campaign).DetectionAlertIgnored);
        var reloadResult = reloaded.Execute(new AdvanceCampaignTime(1));
        Assert.Single(reloadResult.Events.OfType<UfoContactDetected>());
        Assert.False(Target(reloaded).DetectionAlertIgnored);
        Assert.Equivalent(campaign.Capture(), reloaded.Capture(), strict: true);
    }

    [Fact]
    public void IgnoredLandedRediscoveryDoesNotReserveOrAllocateAnAlertLandingMarker()
    {
        var content = StrategicReadinessTestContent.Load("strategic-world.rul");
        var initial = Create(content).Capture().WithNextId("STR_LANDING_SITE", int.MaxValue);
        var campaign = CampaignState.Restore(initial with
        {
            World = initial.World with
            {
                Ufos = [initial.World.Ufos[0] with
                { Status = UfoStatus.Landed, Altitude = WorldAltitudes.Ground, TrajectoryPoint = 2, SecondsRemaining = 10_000 }],
            },
        }, content, new SplitMix64RandomSource(17));
        Assert.IsType<UfoContactIgnored>(Assert.Single(campaign.Execute(new IgnoreUfoContact(9, 3)).Events));
        AdvanceOne(campaign);
        Assert.False(campaign.Capture().World.Ufos[0].Detected);
        Assert.IsType<CampaignFacilityChanged>(Assert.Single(campaign.Execute(
            new BuildCampaignFacility(0, "RADAR_HYPER_TEST", 1, 0)).Events));
        AdvanceUnblocked(campaign, 359);
        var result = campaign.Execute(new AdvanceCampaignTime(1));
        Assert.DoesNotContain(result.Events, item => item is UfoContactDetected or CampaignActionBlocked);
        var after = campaign.Capture();
        Assert.True(after.World.Ufos[0].Detected);
        Assert.Equal(0, after.World.Ufos[0].LandId);
        Assert.Equal(int.MaxValue, after.NextIds["STR_LANDING_SITE"]);
    }

    [Theory]
    [InlineData(0, 3)]
    [InlineData(10, 3)]
    [InlineData(9, 0)]
    [InlineData(9, 4)]
    public void StaleAndAnonymousIgnoreCommandsDoNotMutateState(int uniqueId, int contactId)
    {
        var content = StrategicReadinessTestContent.Load("strategic-world.rul");
        var campaign = Create(content);
        var before = campaign.Capture();
        Assert.IsType<CampaignActionBlocked>(Assert.Single(campaign.Execute(new IgnoreUfoContact(uniqueId, contactId)).Events));
        Assert.Equivalent(before, campaign.Capture(), strict: true);
        Assert.False(Target(campaign).DetectionAlertIgnored);
    }

    private static CampaignState Create(RuntimeContent content)
    {
        var snapshot = CreateLifecycleSnapshot(content);
        return CampaignState.Restore(snapshot with
        {
            Time = new CampaignTime(1, 1, 1, 1999, 1, 29, 55),
            World = snapshot.World with
            {
                Ufos = [snapshot.World.Ufos[0] with
                {
                    Detected = true, HyperDetected = false, Status = UfoStatus.Flying,
                    Altitude = WorldAltitudes.High, TrajectoryPoint = 0, Longitude = 0.4, Latitude = 0.2,
                    Speed = 0, SecondsRemaining = 0, Destination = WorldTargetReference.ForWaypoint(0, new(1, 0.6)),
                }],
            },
        }, content, new SplitMix64RandomSource(17));
    }

    private static CampaignWorldTarget Target(CampaignState campaign) =>
        Assert.Single(campaign.GetQuery<ICampaignWorldQuery>()!.QueryWorld().Targets, target => target.Kind == WorldTargetKind.Ufo);
}

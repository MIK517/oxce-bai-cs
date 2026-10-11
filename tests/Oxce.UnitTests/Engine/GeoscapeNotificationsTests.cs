using Oxce.Engine.Geoscape;
using Oxce.Gameplay.Campaigns;
using Oxce.Gameplay.Campaigns.World;
using Xunit;

namespace Oxce.UnitTests.Engine;

public sealed class GeoscapeNotificationsTests
{
    private static readonly Dictionary<string, string> Texts = new(StringComparer.Ordinal)
    {
        ["STR_PRODUCTION_OF_ITEM_AT_BASE_IS_COMPLETE"] = "Production of {0} at {1} is complete",
        ["STR_NOT_ENOUGH_MONEY_TO_PRODUCE_ITEM_AT_BASE"] = "No money for {0} at {1}",
        ["STR_NOT_ENOUGH_ITEM_TO_REFUEL_CRAFT_AT_BASE"] = "Not enough {0} to refuel {1} at {2}",
        ["STR_CRAFT_IS_READY"] = "{0} at {1} is ready",
        ["STR_NOT_ENOUGH_ITEM_TO_REARM_FACILITY_AT_BASE"] = "Not enough {0} to rearm {1} at {2}",
        ["STR_FUEL_ITEM"] = "Elerium",
        ["STR_LASER_DEFENSE"] = "Laser Defense",
    };

    private static readonly CampaignWorldOverview World = new(
    [
        new(WorldTargetKind.Ufo, 7, "STR_SMALL_SCOUT", "UFO-7", 0.5, 0.2)
        {
            Detected = true,
            Reference = new(WorldTargetKind.Ufo, WorldTargetReference.UfoType, 7, 0.5, 0.2) { UniqueId = 70 },
        },
    ], 0, 0)
    {
        Bases = [new(1, "Alpha", 0.1, 0.1)],
        Crafts = [new(1, "STR_INTERCEPTOR", 2, "INTERCEPTOR-2", "STR_OUT", 0.2, 0.2, 10, null, null, null)],
    };

    private static string Tr(string key) => Texts.GetValueOrDefault(key, key);

    private static GeoscapePopup? Describe(ICampaignEvent notification) =>
        GeoscapeNotifications.Describe(notification, World, Tr);

    [Fact]
    public void ProductionPopsOnlyWhenAProjectEndsWithItsFormattedMessage()
    {
        Assert.Null(Describe(new CampaignProductionProgress(1, "STR_LASER", 3, null)));
        Assert.Equal("Production of STR_LASER at Alpha is complete",
            Describe(new CampaignProductionProgress(1, "STR_LASER", 1, "STR_PRODUCTION_COMPLETE"))!.Title);
        Assert.Equal("No money for STR_LASER at Alpha",
            Describe(new CampaignProductionProgress(1, "STR_LASER", 0, "STR_NOT_ENOUGH_MONEY"))!.Title);
    }

    [Fact]
    public void ServiceMessagesNameTheirItemCraftFacilityAndBase()
    {
        Assert.Equal("Not enough Elerium to refuel INTERCEPTOR-2 at Alpha", Describe(new CraftArrivalServiceMessage(1,
            "STR_INTERCEPTOR", 2, "STR_NOT_ENOUGH_ITEM_TO_REFUEL_CRAFT_AT_BASE")
        { ItemId = "STR_FUEL_ITEM" })!.Title);
        Assert.Equal("INTERCEPTOR-2 at Alpha is ready",
            Describe(new CraftArrivalServiceMessage(1, "STR_INTERCEPTOR", 2, "STR_CRAFT_IS_READY"))!.Title);
        Assert.Equal("Not enough Elerium to rearm Laser Defense at Alpha", Describe(new FacilityServiceMessage(1, 2, 3,
            "STR_NOT_ENOUGH_ITEM_TO_REARM_FACILITY_AT_BASE")
        { ItemId = "STR_FUEL_ITEM", FacilityRuleId = "STR_LASER_DEFENSE" })!.Title);
    }

    [Fact]
    public void ButtonsBindTheReferenceKeysAndSpeedResets()
    {
        // LowFuelState: Enter is "OK - 5 seconds" (timerReset), Esc is OK.
        var fuel = Describe(new CraftLowFuel(1, "STR_INTERCEPTOR", 2))!;
        Assert.Equal("INTERCEPTOR-2", fuel.Title);
        Assert.Equal([(false, false, true), (true, true, false)],
            fuel.Actions.Select(action => (action.ResetSpeed, action.OkKey, action.CancelKey)));

        // UfoDetectedState binds only Cancel; Centre resets the speed and centres on the UFO.
        var detected = Describe(new UfoContactDetected(70, Hyperwave: true))!;
        Assert.Equal("UFO-7", detected.Title);
        Assert.Contains("STR_HYPER_WAVE_TRANSMISSIONS_ARE_DECODED", detected.Lines);
        Assert.Equal(new WorldPosition(0.5, 0.2), detected.Actions[0].Center);
        Assert.True(detected.Actions[0].ResetSpeed);
        Assert.DoesNotContain(detected.Actions, action => action.OkKey);
        Assert.True(detected.Actions[1].CancelKey);

        Assert.True(Describe(new CampaignResearchCompleted(1, "STR_LASER", []))!.ResetsSpeedWhenShown);
        var lost = Describe(new UfoContactLost(70))!;
        Assert.True(lost.Actions[0].OkKey && lost.Actions[0].CancelKey);
        Assert.Null(Describe(new CraftDestinationChanged(1, "STR_INTERCEPTOR", 2, null)));
        Assert.Null(Describe(new CampaignArcUnlocked("STR_ARC", "STR_ARTICLE")));
        // SavedGame::spawnEvent only schedules: the popup and reset come when the event fires.
        Assert.Null(Describe(new CampaignStrategicEventRequested("STR_EVENT", "research")));
        Assert.Null(Describe(new CampaignTransformationEventSelected("STR_EVENT")));
    }
}

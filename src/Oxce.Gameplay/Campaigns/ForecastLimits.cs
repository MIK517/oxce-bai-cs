namespace Oxce.Gameplay.Campaigns;

/// <summary>
/// The campaign forecasts' work budget and the refusals that several forecasts share when a bound is reached (the
/// refusal inventory's port work and range bounds). The world's globe-scaled budget is
/// <c>CampaignWorld.MaximumWorldWork</c>.
/// </summary>
internal static class ForecastLimits
{
    /// <summary>Work one campaign forecast pass may spend before it refuses the tick.</summary>
    public const long Work = 1_000_000;

    public const string AlienBaseMissionWeightsRange = "Alien base mission weights exceed the supported range.";
    public const string AlienMissionActivityForecastsWork = "Alien mission activity forecasts exceed the supported work limit.";
    public const string AlienMissionIdsOrPopulationLimit = "Alien mission IDs or population exceed the supported limit.";
    public const string AlienMissionLandSelectionWork = "Alien mission land selection exceeds the supported work limit.";
    public const string AlienMissionSpawnGeometryWork = "Alien mission spawn geometry exceeds the supported work limit.";
    public const string ArcScriptsWork = "Arc scripts exceed the supported work limit.";
    public const string BaseObjectiveCreationWork = "Base objective creation exceeds the supported work limit.";
    public const string CraftFlightFuelRefundsWork = "Craft flight fuel refunds exceed the supported work limit.";
    public const string CraftProductionIdForecastsWork = "Craft production ID forecasts exceed the supported work limit.";
    public const string CraftProductionWork = "Craft production exceeds the supported work limit.";
    public const string CraftServiceCapacityRange = "Craft service capacity exceeds the supported range.";
    public const string CraftServiceRateRange = "Craft service rate exceeds the supported range.";
    public const string CraftTargetRemovalWork = "Craft target removal exceeds the supported work limit.";
    public const string CraftWeaponBonusesCapacityRange = "Craft weapon bonuses exceed the supported capacity range.";
    public const string CraftWeaponServiceValuesRange = "Craft weapon service values exceed the supported range.";
    public const string DestroyedUfoCleanupWork = "Destroyed UFO cleanup exceeds the supported work limit.";
    public const string DestroyedUfoFollowerCleanupWork = "Destroyed UFO follower cleanup exceeds the supported work limit.";
    public const string DetectionConsumedFieldForecastWork = "Detection consumed-field forecast exceeds the supported work limit.";
    public const string DetectionDamageQueryForecastWork = "Detection damage query forecast exceeds the supported work limit.";
    public const string DetectionInventoryCostForecastWork = "Detection inventory cost forecast exceeds the supported work limit.";
    public const string DetectionNumericSafetyForecastWork = "Detection numeric safety forecast exceeds the supported work limit.";
    public const string DetectionScriptInstructions = "Detection script exceeds the supported instruction limit.";
    public const string DetectionUfoStatisticsRange = "Detection UFO statistics exceed the supported range.";
    public const string DirectMissionSiteCreationWork = "Direct mission site creation exceeds the supported work limit.";
    public const string EventScriptsWork = "Event scripts exceed the supported work limit.";
    public const string InfiltrationCountrySearchWork = "Infiltration country search exceeds the supported work limit.";
    public const string MissionOperationBaseActivityRange = "Mission operation base activity exceeds the supported range.";
    public const string MissionOperationBaseCreationWork = "Mission operation base creation exceeds the supported work limit.";
    public const string MissionResearchDiscoveryWork = "Mission research exceeds the supported discovery work limit.";
    public const string MissionResearchOutcomes = "Mission research exceeds the supported outcome limit.";
    public const string MissionResearchScoreRange = "Mission research score exceeds the supported range.";
    public const string MissionScriptsWork = "Mission scripts exceed the supported work limit.";
    public const string MissionSiteActivityForecastsWork = "Mission site activity forecasts exceed the supported work limit.";
    public const string MissionSiteMarkerForecastsWork = "Mission site marker forecasts exceed the supported work limit.";
    public const string MissionSiteResearchItemStocksRange = "Mission site research item stocks exceed the supported range.";
    public const string NewBaseHuntingRegionForecastsWork = "New base hunting region forecasts exceed the supported work limit.";
    public const string NewUfoEscortSelectionWork = "New UFO escort selection exceeds the supported work limit.";
    public const string ProducedCraftServiceValuesRange = "Produced craft service values exceed the supported range.";
    public const string ProductionAccountingRange = "Production accounting exceeds the supported range.";
    public const string ProductionArrivalsQuantityRange = "Production arrivals exceed the supported quantity range.";
    public const string ProductionArrivalsWork = "Production arrivals exceed the supported work limit.";
    public const string ProductionCountryActivityForecastsWork = "Production country activity forecasts exceed the supported work limit.";
    public const string ProductionOutputsStockRange = "Production outputs exceed the supported stock range.";
    public const string ProductionPriceDaysRange = "Production price days exceed the supported range.";
    public const string ProductionPriceForecastsWork = "Production price forecasts exceed the supported work limit.";
    public const string ProductionPriceResearchForecastsWork = "Production price research forecasts exceed the supported work limit.";
    public const string ProductionProgressRange = "Production progress exceeds the supported range.";
    public const string RandomProductionHistoryWork = "Random production history exceeds the supported work limit.";
    public const string RandomProductionWeightsRange = "Random production weights exceed the supported range.";
    public const string ResearchAndProductionScoresRange = "Research and production scores exceed the supported range.";
    public const string ResearchArrivalsStockRange = "Research arrivals exceed the supported stock range.";
    public const string ResearchDiaryForecastsWork = "Research diary forecasts exceed the supported work limit.";
    public const string ResearchRecoveryWork = "Research recovery exceeds the supported work limit.";
    public const string ResearchRefundsStockRange = "Research refunds exceed the supported stock range.";
    public const string ResearchScoreForecastsWork = "Research score forecasts exceed the supported work limit.";
    public const string RetaliationMissionWeightsRange = "Retaliation mission weights exceed the supported range.";
    public const string SoldierProductionWork = "Soldier production exceeds the supported work limit.";
    public const string StrategicEventWeightsRange = "Strategic event weights exceed the supported range.";
    public const string StrategicScriptWeightsRange = "Strategic script weights exceed the supported range.";
    public const string UfoAlienActivityRange = "UFO alien activity exceeds the supported range.";
    public const string UfoEscortSpeedRange = "UFO escort speed exceeds the supported range.";
    public const string UfoHuntSpeedRange = "UFO hunt speed exceeds the supported range.";
    public const string UfoHuntingStatsRange = "UFO hunting stats exceed the supported range.";
    public const string UfoRetargetingWork = "UFO retargeting exceeds the supported work limit.";
    public const string UfoShieldArithmeticRange = "UFO shield arithmetic exceeds the supported range.";
    public const string UfoSiteConversionWork = "UFO site conversion exceeds the supported work limit.";
    public const string UfoWaypointLandSelectionWork = "UFO waypoint land selection exceeds the supported work limit.";
    public const string WorldMissionSitePopulationLimit = "World mission site population exceeds the supported limit.";
    public const string XcomActivityRange = "X-COM activity exceeds the supported range.";
}

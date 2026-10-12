namespace Oxce.Mods.Rulesets.CampaignStart;

/// <summary>
/// Global rules read by interception, landing and base defense (<c>Mod::loadFile</c>), with the
/// <c>Mod::Mod</c> defaults. Scalars and the fixed five- and three-slot arrays layer per mod; each
/// <c>difficultyCoefficientOverrides</c> list replaces the previous one and, as in its callers, is used
/// only when it has an entry for the campaign's difficulty.
/// </summary>
public sealed record CampaignInterceptionSettings(
    int ChanceToStopRetaliation,
    bool LessAliensDuringBaseDefense,
    int UfoGlancingHitThreshold,
    int UfoBeamWidthParameter,
    IReadOnlyList<int> UfoTractorBeamSizeModifiers,
    bool EscortsJoinFightAgainstHK,
    int CrewEmergencyEvacuationSurvivalChance,
    int PilotsEmergencyEvacuationSurvivalChance,
    int PilotAccuracyZeroPoint,
    int PilotAccuracyRange,
    int PilotReactionsZeroPoint,
    int PilotReactionsRange,
    IReadOnlyList<int> PilotBraveryThresholds,
    bool ShowDogfightDistanceInKm,
    IReadOnlyList<int> MonthlyRatingThresholds,
    IReadOnlyList<int> UfoFiringRateCoefficients,
    IReadOnlyList<int> UfoEscapeCountdownCoefficients,
    IReadOnlyList<int> RetaliationTriggerOdds,
    IReadOnlyList<int> RetaliationBaseRegionOdds,
    IReadOnlyList<int> AliensFacingCraftOdds,
    IReadOnlyList<int> DifficultyCoefficients,
    IReadOnlyList<int> DifficultyBasedRetaliationDelay)
{
    /// <summary>SavedGame::getDifficultyCoefficient: <c>Mod::DIFFICULTY_COEFFICIENT[min(difficulty, 4)]</c>.</summary>
    public int DifficultyCoefficient(int difficulty) => DifficultyCoefficients[Math.Min(difficulty, 4)];

    public static CampaignInterceptionSettings Default { get; } = new CampaignInterceptionSettingsBuilder().Build();
}

internal sealed class CampaignInterceptionSettingsBuilder
{
    public int ChanceToStopRetaliation { get; set; }
    public bool LessAliensDuringBaseDefense { get; set; }
    public int UfoGlancingHitThreshold { get; set; }
    public int UfoBeamWidthParameter { get; set; } = 1000;
    public int[] UfoTractorBeamSizeModifiers { get; } = [400, 200, 100, 50, 25];
    public bool EscortsJoinFightAgainstHK { get; set; } = true;
    public int CrewEmergencyEvacuationSurvivalChance { get; set; } = 100;
    public int PilotsEmergencyEvacuationSurvivalChance { get; set; } = 100;
    public int PilotAccuracyZeroPoint { get; set; } = 55;
    public int PilotAccuracyRange { get; set; } = 40;
    public int PilotReactionsZeroPoint { get; set; } = 55;
    public int PilotReactionsRange { get; set; } = 60;
    public int[] PilotBraveryThresholds { get; } = [90, 80, 30];
    public bool ShowDogfightDistanceInKm { get; set; }
    public List<int> MonthlyRatingThresholds { get; set; } = [];
    public List<int> UfoFiringRateCoefficients { get; set; } = [];
    public List<int> UfoEscapeCountdownCoefficients { get; set; } = [];
    public List<int> RetaliationTriggerOdds { get; set; } = [];
    public List<int> RetaliationBaseRegionOdds { get; set; } = [];
    public List<int> AliensFacingCraftOdds { get; set; } = [];
    // Mod::DIFFICULTY_COEFFICIENT and Mod::DIFFICULTY_BASED_RETAL_DELAY: process-wide statics in the
    // reference, reset by Mod::resetGlobalStatics before every mod load (Game::loadMods).
    public int[] DifficultyCoefficients { get; } = [0, 1, 2, 3, 4];
    public int[] DifficultyBasedRetaliationDelay { get; } = [0, 0, 0, 0, 0];

    public CampaignInterceptionSettings Build() => new(
        ChanceToStopRetaliation,
        LessAliensDuringBaseDefense,
        UfoGlancingHitThreshold,
        UfoBeamWidthParameter,
        Array.AsReadOnly((int[])UfoTractorBeamSizeModifiers.Clone()),
        EscortsJoinFightAgainstHK,
        CrewEmergencyEvacuationSurvivalChance,
        PilotsEmergencyEvacuationSurvivalChance,
        PilotAccuracyZeroPoint,
        PilotAccuracyRange,
        PilotReactionsZeroPoint,
        PilotReactionsRange,
        Array.AsReadOnly((int[])PilotBraveryThresholds.Clone()),
        ShowDogfightDistanceInKm,
        Array.AsReadOnly(MonthlyRatingThresholds.ToArray()),
        Array.AsReadOnly(UfoFiringRateCoefficients.ToArray()),
        Array.AsReadOnly(UfoEscapeCountdownCoefficients.ToArray()),
        Array.AsReadOnly(RetaliationTriggerOdds.ToArray()),
        Array.AsReadOnly(RetaliationBaseRegionOdds.ToArray()),
        Array.AsReadOnly(AliensFacingCraftOdds.ToArray()),
        Array.AsReadOnly((int[])DifficultyCoefficients.Clone()),
        Array.AsReadOnly((int[])DifficultyBasedRetaliationDelay.Clone()));
}

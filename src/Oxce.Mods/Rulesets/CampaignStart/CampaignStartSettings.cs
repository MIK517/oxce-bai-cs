using System.Collections.ObjectModel;
using Oxce.Formats.Yaml;

namespace Oxce.Mods.Rulesets.CampaignStart;

public enum StartingBaseVariant
{
    Default,
    Beginner,
    Experienced,
    Veteran,
    Genius,
    Superhuman,
}

public sealed record CampaignStartTime(int Weekday, int Day, int Month, int Year, int Hour, int Minute, int Second);

public sealed class CampaignStartSettings
{
    internal CampaignStartSettings(CampaignStartSettingsBuilder builder)
    {
        StartingBases = new ReadOnlyDictionary<StartingBaseVariant, YamlMappingNode>(
            new Dictionary<StartingBaseVariant, YamlMappingNode>(builder.StartingBases));
        StartingTime = new CampaignStartTime(
            builder.Weekday, builder.Day, builder.Month, builder.Year,
            builder.Hour, builder.Minute, builder.Second);
        StartingDifficulty = builder.StartingDifficulty;
        CostHireEngineer = builder.CostHireEngineer;
        CostHireScientist = builder.CostHireScientist;
        CostEngineer = builder.CostEngineer;
        CostScientist = builder.CostScientist;
        PersonnelTransferTime = builder.PersonnelTransferTime;
        HireByCountryOdds = builder.HireByCountryOdds;
        HireByRegionOdds = builder.HireByRegionOdds;
        InitialFunding = builder.InitialFunding;
        GlobalTransferCostMultiplier = builder.GlobalTransferCostMultiplier;
        GlobalTransferCostDivisor = builder.GlobalTransferCostDivisor;
        PsiUnlockResearch = builder.PsiUnlockResearch;
        FakeUnderwaterBaseUnlockResearch = builder.FakeUnderwaterBaseUnlockResearch;
        NewBaseUnlockResearch = builder.NewBaseUnlockResearch;
        HireScientistsUnlockResearch = builder.HireScientistsUnlockResearch;
        HireEngineersUnlockResearch = builder.HireEngineersUnlockResearch;
        HireScientistsRequiredBaseFunctions = builder.HireScientistsRequiredBaseFunctions.AsReadOnly();
        HireEngineersRequiredBaseFunctions = builder.HireEngineersRequiredBaseFunctions.AsReadOnly();
        DestroyedFacility = builder.DestroyedFacility;
        DefeatScore = builder.DefeatScore;
        DefeatFunds = builder.DefeatFunds;
        DifficultyDemigod = builder.DifficultyDemigod;
        BaseNamesFirst = builder.BaseNamesFirst.AsReadOnly();
        BaseNamesMiddle = builder.BaseNamesMiddle.AsReadOnly();
        BaseNamesLast = builder.BaseNamesLast.AsReadOnly();
        OperationNamesFirst = builder.OperationNamesFirst.AsReadOnly();
        OperationNamesLast = builder.OperationNamesLast.AsReadOnly();
    }

    [System.Text.Json.Serialization.JsonConstructor]
    internal CampaignStartSettings(
        IReadOnlyDictionary<StartingBaseVariant, YamlMappingNode> startingBases,
        CampaignStartTime startingTime,
        int startingDifficulty,
        int costHireEngineer,
        int costHireScientist,
        int costEngineer,
        int costScientist,
        int personnelTransferTime,
        int hireByCountryOdds,
        int hireByRegionOdds,
        int initialFunding,
        int globalTransferCostMultiplier,
        int globalTransferCostDivisor,
        string psiUnlockResearch,
        string fakeUnderwaterBaseUnlockResearch,
        string newBaseUnlockResearch,
        string hireScientistsUnlockResearch,
        string hireEngineersUnlockResearch,
        IReadOnlyList<string> hireScientistsRequiredBaseFunctions,
        IReadOnlyList<string> hireEngineersRequiredBaseFunctions,
        string destroyedFacility,
        int defeatScore,
        int defeatFunds,
        bool difficultyDemigod,
        IReadOnlyList<string> baseNamesFirst,
        IReadOnlyList<string> baseNamesMiddle,
        IReadOnlyList<string> baseNamesLast,
        IReadOnlyList<string> operationNamesFirst,
        IReadOnlyList<string> operationNamesLast)
    {
        StartingBases = new ReadOnlyDictionary<StartingBaseVariant, YamlMappingNode>(
            new Dictionary<StartingBaseVariant, YamlMappingNode>(startingBases));
        StartingTime = startingTime;
        StartingDifficulty = startingDifficulty;
        CostHireEngineer = costHireEngineer;
        CostHireScientist = costHireScientist;
        CostEngineer = costEngineer;
        CostScientist = costScientist;
        PersonnelTransferTime = personnelTransferTime;
        HireByCountryOdds = hireByCountryOdds;
        HireByRegionOdds = hireByRegionOdds;
        InitialFunding = initialFunding;
        GlobalTransferCostMultiplier = globalTransferCostMultiplier;
        GlobalTransferCostDivisor = globalTransferCostDivisor;
        PsiUnlockResearch = psiUnlockResearch;
        FakeUnderwaterBaseUnlockResearch = fakeUnderwaterBaseUnlockResearch;
        NewBaseUnlockResearch = newBaseUnlockResearch;
        HireScientistsUnlockResearch = hireScientistsUnlockResearch;
        HireEngineersUnlockResearch = hireEngineersUnlockResearch;
        HireScientistsRequiredBaseFunctions = Array.AsReadOnly(hireScientistsRequiredBaseFunctions.ToArray());
        HireEngineersRequiredBaseFunctions = Array.AsReadOnly(hireEngineersRequiredBaseFunctions.ToArray());
        DestroyedFacility = destroyedFacility;
        DefeatScore = defeatScore;
        DefeatFunds = defeatFunds;
        DifficultyDemigod = difficultyDemigod;
        BaseNamesFirst = Array.AsReadOnly(baseNamesFirst.ToArray());
        BaseNamesMiddle = Array.AsReadOnly(baseNamesMiddle.ToArray());
        BaseNamesLast = Array.AsReadOnly(baseNamesLast.ToArray());
        OperationNamesFirst = Array.AsReadOnly(operationNamesFirst.ToArray());
        OperationNamesLast = Array.AsReadOnly(operationNamesLast.ToArray());
    }

    public IReadOnlyDictionary<StartingBaseVariant, YamlMappingNode> StartingBases { get; }
    public CampaignStartTime StartingTime { get; }
    public int StartingDifficulty { get; }
    public int CostHireEngineer { get; }
    public int CostHireScientist { get; }
    public int CostEngineer { get; }
    public int CostScientist { get; }
    public int PersonnelTransferTime { get; }
    public int HireByCountryOdds { get; }
    public int HireByRegionOdds { get; }
    public int InitialFunding { get; }
    public int GlobalTransferCostMultiplier { get; }
    public int GlobalTransferCostDivisor { get; }
    public string PsiUnlockResearch { get; }
    public string FakeUnderwaterBaseUnlockResearch { get; }
    public string NewBaseUnlockResearch { get; }
    public string HireScientistsUnlockResearch { get; }
    public string HireEngineersUnlockResearch { get; }
    public IReadOnlyList<string> HireScientistsRequiredBaseFunctions { get; }
    public IReadOnlyList<string> HireEngineersRequiredBaseFunctions { get; }
    public string DestroyedFacility { get; }
    public int DefeatScore { get; }
    public int DefeatFunds { get; }
    public bool DifficultyDemigod { get; }
    public IReadOnlyList<string> BaseNamesFirst { get; }
    public IReadOnlyList<string> BaseNamesMiddle { get; }
    public IReadOnlyList<string> BaseNamesLast { get; }
    public IReadOnlyList<string> OperationNamesFirst { get; }
    public IReadOnlyList<string> OperationNamesLast { get; }

    public YamlMappingNode? GetStartingBase(StartingBaseVariant variant) =>
        variant != StartingBaseVariant.Default && StartingBases.TryGetValue(variant, out var specific)
            ? specific
            : StartingBases.GetValueOrDefault(StartingBaseVariant.Default);
}

internal sealed class CampaignStartSettingsBuilder
{
    public Dictionary<StartingBaseVariant, YamlMappingNode> StartingBases { get; } = [];
    public int Weekday { get; set; } = 6;
    public int Day { get; set; } = 1;
    public int Month { get; set; } = 1;
    public int Year { get; set; } = 1999;
    public int Hour { get; set; } = 12;
    public int Minute { get; set; }
    public int Second { get; set; }
    public int StartingDifficulty { get; set; }
    public int CostHireEngineer { get; set; }
    public int CostHireScientist { get; set; }
    public int CostEngineer { get; set; }
    public int CostScientist { get; set; }
    public int PersonnelTransferTime { get; set; }
    public int HireByCountryOdds { get; set; }
    public int HireByRegionOdds { get; set; }
    public int InitialFunding { get; set; }
    public int GlobalTransferCostMultiplier { get; set; } = 1;
    public int GlobalTransferCostDivisor { get; set; } = 1;
    public string PsiUnlockResearch { get; set; } = string.Empty;
    public string FakeUnderwaterBaseUnlockResearch { get; set; } = string.Empty;
    public string NewBaseUnlockResearch { get; set; } = string.Empty;
    public string HireScientistsUnlockResearch { get; set; } = string.Empty;
    public string HireEngineersUnlockResearch { get; set; } = string.Empty;
    public List<string> HireScientistsRequiredBaseFunctions { get; } = [];
    public List<string> HireEngineersRequiredBaseFunctions { get; } = [];
    public string DestroyedFacility { get; set; } = string.Empty;
    public int DefeatScore { get; set; }
    public int DefeatFunds { get; set; }
    public bool DifficultyDemigod { get; set; }
    public List<string> BaseNamesFirst { get; } = [];
    public List<string> BaseNamesMiddle { get; } = [];
    public List<string> BaseNamesLast { get; } = [];
    public List<string> OperationNamesFirst { get; } = [];
    public List<string> OperationNamesLast { get; } = [];
}

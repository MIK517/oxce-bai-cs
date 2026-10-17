using Oxce.Core.Diagnostics;
using Oxce.Formats.Yaml;
using Oxce.Mods.Loading;

namespace Oxce.Mods.Rulesets.CampaignStart;

internal static class CampaignStartSettingsComposer
{
    private static readonly IReadOnlyDictionary<string, StartingBaseVariant> BaseKeys =
        new Dictionary<string, StartingBaseVariant>(StringComparer.Ordinal)
        {
            ["startingBase"] = StartingBaseVariant.Default,
            ["startingBaseBeginner"] = StartingBaseVariant.Beginner,
            ["startingBaseExperienced"] = StartingBaseVariant.Experienced,
            ["startingBaseVeteran"] = StartingBaseVariant.Veteran,
            ["startingBaseGenius"] = StartingBaseVariant.Genius,
            ["startingBaseSuperhuman"] = StartingBaseVariant.Superhuman,
        };

    public static CampaignStartSettings Compose(ModLoadPlan plan, RulesetCompositionOptions options)
        => Compose(RulesetDocumentCatalog.Parse(plan, options), options);

    public static CampaignStartSettings Compose(RulesetDocumentCatalog documents, RulesetCompositionOptions options)
    {
        var builder = new CampaignStartSettingsBuilder();
        foreach (var document in documents.Documents)
        {
            Apply(builder, document.Root);
        }
        return new CampaignStartSettings(builder);
    }

    private static void Apply(CampaignStartSettingsBuilder b, YamlMappingNode root)
    {
        if (root.TryGet("globe", out var globe))
            b.GlobeLayers.Add(globe as YamlMappingNode ?? throw new InvalidDataException("Globe rules require a mapping."));
        b.BuildTimeReductionScaling = Read(root, "buildTimeReductionScaling", b.BuildTimeReductionScaling);
        b.CustomTrainingFactor = Read(root, "customTrainingFactor", b.CustomTrainingFactor);
        b.EscortRange = Read(root, "escortRange", b.EscortRange);
        b.DrawEnemyRadarCircles = Read(root, "drawEnemyRadarCircles", b.DrawEnemyRadarCircles);
        b.MaximumViewDistance = Read(root, "maxViewDistance", b.MaximumViewDistance);
        b.MaximumDarknessToSeeUnits = Read(root, "maxDarknessToSeeUnits", b.MaximumDarknessToSeeUnits);
        b.HunterKillerFastRetarget = Read(root, "hunterKillerFastRetarget", b.HunterKillerFastRetarget);
        b.AllowAlienBasesOnWrongTextures = Read(root, "allowAlienBasesOnWrongTextures", b.AllowAlienBasesOnWrongTextures);
        b.AllowCountriesToCancelAlienPact = Read(root, "allowCountriesToCancelAlienPact", b.AllowCountriesToCancelAlienPact);
        b.BuildInfiltrationBaseCloseToTheCountry = Read(root, "buildInfiltrationBaseCloseToTheCountry", b.BuildInfiltrationBaseCloseToTheCountry);
        b.InfiltrateRandomCountryInTheRegion = Read(root, "infiltrateRandomCountryInTheRegion", b.InfiltrateRandomCountryInTheRegion);
        if (root.TryGet("mana", out var mana))
            b.ManaWoundThreshold = Read(mana as YamlMappingNode ?? throw new InvalidDataException("Mana settings require a mapping."), "woundThreshold", b.ManaWoundThreshold);
        if (root.TryGet("health", out var health))
            b.HealthWoundThreshold = Read(health as YamlMappingNode ?? throw new InvalidDataException("Health settings require a mapping."), "woundThreshold", b.HealthWoundThreshold);
        foreach (var pair in BaseKeys)
        {
            if (!root.TryGet(pair.Key, out var node)) continue;
            if (node is not YamlMappingNode mapping)
                throw new YamlFormatException($"Global rule '{pair.Key}' must be a mapping.", node!.Span);
            b.StartingBases[pair.Value] = b.StartingBases.TryGetValue(pair.Value, out var previous)
                ? Overlay(mapping, previous)
                : mapping;
        }
        if (root.TryGet("startingTime", out var time))
        {
            if (time is not YamlMappingNode mapping)
                throw new YamlFormatException("Global rule 'startingTime' must be a mapping.", time!.Span);
            b.Second = Read(mapping, "second", b.Second);
            b.Minute = Read(mapping, "minute", b.Minute);
            b.Hour = Read(mapping, "hour", b.Hour);
            b.Weekday = Read(mapping, "weekday", b.Weekday);
            b.Day = Read(mapping, "day", b.Day);
            b.Month = Read(mapping, "month", b.Month);
            b.Year = Read(mapping, "year", b.Year);
        }
        b.StartingDifficulty = Read(root, "startingDifficulty", b.StartingDifficulty);
        b.CostHireEngineer = Read(root, "costHireEngineer", b.CostHireEngineer);
        b.CostHireScientist = Read(root, "costHireScientist", b.CostHireScientist);
        b.CostEngineer = Read(root, "costEngineer", b.CostEngineer);
        b.CostScientist = Read(root, "costScientist", b.CostScientist);
        b.PersonnelTransferTime = Read(root, "timePersonnel", b.PersonnelTransferTime);
        b.HireByCountryOdds = Read(root, "hireByCountryOdds", b.HireByCountryOdds);
        b.HireByRegionOdds = Read(root, "hireByRegionOdds", b.HireByRegionOdds);
        b.InitialFunding = Read(root, "initialFunding", b.InitialFunding);
        if (root.TryGet("performanceBonusFactor", out var bonus))
        {
            b.PerformanceBonusFactor = YamlValueReader.ReadDouble(bonus!);
            if (!double.IsFinite(b.PerformanceBonusFactor))
                throw new YamlFormatException("performanceBonusFactor must be finite.", bonus!.Span);
        }
        ApplyCoefficients(root, "buyPriceCoefficient", b.BuyPriceCoefficients);
        ApplyCoefficients(root, "sellPriceCoefficient", b.SellPriceCoefficients);
        if (root.TryGet("transferCosts", out var transfer))
        {
            if (transfer is not YamlMappingNode mapping)
                throw new YamlFormatException("Global rule 'transferCosts' must be a mapping.", transfer!.Span);
            b.GlobalTransferCostMultiplier = Read(mapping, "globalCostMult", b.GlobalTransferCostMultiplier);
            b.GlobalTransferCostDivisor = Read(mapping, "globalCostDiv", b.GlobalTransferCostDivisor);
        }
        b.PsiUnlockResearch = Read(root, "psiUnlockResearch", b.PsiUnlockResearch);
        b.FakeUnderwaterBaseUnlockResearch = Read(root, "fakeUnderwaterBaseUnlockResearch", b.FakeUnderwaterBaseUnlockResearch);
        b.NewBaseUnlockResearch = Read(root, "newBaseUnlockResearch", b.NewBaseUnlockResearch);
        b.HireScientistsUnlockResearch = Read(root, "hireScientistsUnlockResearch", b.HireScientistsUnlockResearch);
        b.HireEngineersUnlockResearch = Read(root, "hireEngineersUnlockResearch", b.HireEngineersUnlockResearch);
        ApplyNames(root, "hireScientistsRequiresBaseFunc", b.HireScientistsRequiredBaseFunctions, true);
        ApplyNames(root, "hireEngineersRequiresBaseFunc", b.HireEngineersRequiredBaseFunctions, true);
        b.DestroyedFacility = Read(root, "destroyedFacility", b.DestroyedFacility);
        b.DefeatScore = Read(root, "defeatScore", b.DefeatScore);
        b.DefeatFunds = Read(root, "defeatFunds", b.DefeatFunds);
        b.DifficultyDemigod = Read(root, "difficultyDemigod", b.DifficultyDemigod);
        ApplyNames(root, "baseNamesFirst", b.BaseNamesFirst, false);
        ApplyNames(root, "baseNamesMiddle", b.BaseNamesMiddle, false);
        ApplyNames(root, "baseNamesLast", b.BaseNamesLast, false);
        ApplyNames(root, "operationNamesFirst", b.OperationNamesFirst, false);
        ApplyNames(root, "operationNamesLast", b.OperationNamesLast, false);
        if (root.TryGet("fixedUserOptions", out var fixedOptions))
            CampaignStartYaml.ApplyEditableNameMap(b.FixedUserOptions, fixedOptions!, "fixedUserOptions");
        ApplyInterception(root, b.Interception);
    }

    // Mod::loadFile order and defaults; see CampaignInterceptionSettings.
    private static void ApplyInterception(YamlMappingNode root, CampaignInterceptionSettingsBuilder b)
    {
        b.ChanceToStopRetaliation = Read(root, "chanceToStopRetaliation", b.ChanceToStopRetaliation);
        b.LessAliensDuringBaseDefense = Read(root, "lessAliensDuringBaseDefense", b.LessAliensDuringBaseDefense);
        b.UfoGlancingHitThreshold = Read(root, "ufoGlancingHitThreshold", b.UfoGlancingHitThreshold);
        b.UfoBeamWidthParameter = Read(root, "ufoBeamWidthParameter", b.UfoBeamWidthParameter);
        ApplyCoefficients(root, "ufoTractorBeamSizeModifiers", b.UfoTractorBeamSizeModifiers);
        b.EscortsJoinFightAgainstHK = Read(root, "escortsJoinFightAgainstHK", b.EscortsJoinFightAgainstHK);
        b.CrewEmergencyEvacuationSurvivalChance = Read(root, "crewEmergencyEvacuationSurvivalChance", b.CrewEmergencyEvacuationSurvivalChance);
        b.PilotsEmergencyEvacuationSurvivalChance = Read(root, "pilotsEmergencyEvacuationSurvivalChance", b.PilotsEmergencyEvacuationSurvivalChance);
        b.PilotAccuracyZeroPoint = Read(root, "pilotAccuracyZeroPoint", b.PilotAccuracyZeroPoint);
        b.PilotAccuracyRange = Read(root, "pilotAccuracyRange", b.PilotAccuracyRange);
        b.PilotReactionsZeroPoint = Read(root, "pilotReactionsZeroPoint", b.PilotReactionsZeroPoint);
        b.PilotReactionsRange = Read(root, "pilotReactionsRange", b.PilotReactionsRange);
        ApplyCoefficients(root, "pilotBraveryThresholds", b.PilotBraveryThresholds);
        b.ShowDogfightDistanceInKm = Read(root, "showDogfightDistanceInKm", b.ShowDogfightDistanceInKm);
        ApplyCoefficients(root, "difficultyCoefficient", b.DifficultyCoefficients);
        ApplyCoefficients(root, "difficultyBasedRetaliationDelay", b.DifficultyBasedRetaliationDelay);
        b.DamageRange = Read(root, "damageRange", b.DamageRange);
        b.ExplosiveDamageRange = Read(root, "explosiveDamageRange", b.ExplosiveDamageRange);
        ApplyCoefficients(root, "fireDamageRange", b.FireDamageRange);
        if (!root.TryGet("difficultyCoefficientOverrides", out var node)) return;
        if (node is not YamlMappingNode overrides)
            throw new YamlFormatException("Global rule 'difficultyCoefficientOverrides' must be a mapping.", node!.Span);
        b.MonthlyRatingThresholds = ReadList(overrides, "monthlyRatingThresholds", b.MonthlyRatingThresholds);
        b.UfoFiringRateCoefficients = ReadList(overrides, "ufoFiringRateCoefficients", b.UfoFiringRateCoefficients);
        b.UfoEscapeCountdownCoefficients = ReadList(overrides, "ufoEscapeCountdownCoefficients", b.UfoEscapeCountdownCoefficients);
        b.RetaliationTriggerOdds = ReadList(overrides, "retaliationTriggerOdds", b.RetaliationTriggerOdds);
        b.RetaliationBaseRegionOdds = ReadList(overrides, "retaliationBaseRegionOdds", b.RetaliationBaseRegionOdds);
        b.AliensFacingCraftOdds = ReadList(overrides, "aliensFacingCraftOdds", b.AliensFacingCraftOdds);
    }

    // YamlNodeReader::tryRead into a std::vector replaces the whole list.
    private static List<int> ReadList(YamlMappingNode mapping, string key, List<int> current)
    {
        if (!mapping.TryGet(key, out var node)) return current;
        if (node is not YamlSequenceNode sequence)
            throw new YamlFormatException($"Global rule '{key}' must be a sequence.", node!.Span);
        return [.. sequence.Items.Select(YamlValueReader.ReadInt32)];
    }

    private static YamlMappingNode Overlay(YamlMappingNode current, YamlMappingNode defaults)
    {
        var entries = current.Entries.ToList();
        foreach (var entry in defaults.Entries)
            if (entry.ScalarKey is null || !current.TryGet(entry.ScalarKey, out _)) entries.Add(entry);
        return new YamlMappingNode(current.Span, entries, current.Tag, current.Anchor);
    }

    private static void ApplyCoefficients(YamlMappingNode root, string key, int[] destination)
    {
        if (!root.TryGet(key, out var node)) return;
        if (node is not YamlSequenceNode sequence)
            throw new YamlFormatException($"Global rule '{key}' must be a sequence.", node!.Span);
        // Mod::loadFile indexes exactly the destination's slots; absent tail values retain earlier layers.
        for (var index = 0; index < Math.Min(destination.Length, sequence.Items.Count); index++)
            destination[index] = YamlValueReader.ReadInt32(sequence.Items[index]);
    }

    private static void ApplyNames(YamlMappingNode root, string key, List<string> destination, bool unique)
    {
        if (root.TryGet(key, out var node)) CampaignStartYaml.ApplyEditableNames(destination, node!, unique);
    }

    private static int Read(YamlMappingNode mapping, string key, int current) =>
        mapping.TryGet(key, out var node) ? YamlValueReader.ReadInt32(node!) : current;
    private static bool Read(YamlMappingNode mapping, string key, bool current) =>
        mapping.TryGet(key, out var node) ? YamlValueReader.ReadBoolean(node!) : current;
    private static string Read(YamlMappingNode mapping, string key, string current) =>
        mapping.TryGet(key, out var node) ? YamlValueReader.ReadString(node!) : current;
}

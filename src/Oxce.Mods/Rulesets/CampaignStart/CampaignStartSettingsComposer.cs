using System.Collections.ObjectModel;
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
        if (root.TryGet("ai", out var ai) && ai is YamlMappingNode aiSettings)
        {
            // Mod::loadAll's ai section: pickUpWeaponsMoreActively and pickUpWeaponsMoreActivelyCiv.
            b.AiPickUpWeaponsMoreActively = Read(aiSettings, "pickUpWeaponsMoreActively", b.AiPickUpWeaponsMoreActively);
            b.AiPickUpWeaponsMoreActivelyCivilians = Read(aiSettings, "pickUpWeaponsMoreActivelyCiv", b.AiPickUpWeaponsMoreActivelyCivilians);
        }
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
        b.ExtendedHwpLoadOrder = Read(root, "extendedHwpLoadOrder", b.ExtendedHwpLoadOrder);
        b.ExtendedForceSpawn = Read(root, "extendedForceSpawn", b.ExtendedForceSpawn);
        b.ExtendedInventorySlotSorting = Read(root, "extendedInventorySlotSorting", b.ExtendedInventorySlotSorting);
        b.ExtendedItemReloadCost = Read(root, "extendedItemReloadCost", b.ExtendedItemReloadCost);
        b.ExtendedRunningCost = Read(root, "extendedRunningCost", b.ExtendedRunningCost);
        b.ExtendedMovementCostRounding = Read(root, "extendedMovementCostRounding", b.ExtendedMovementCostRounding);
        b.ExtendedMeleeReactions = Read(root, "extendedMeleeReactions", b.ExtendedMeleeReactions);
        b.TooMuchSmokeThreshold = Read(root, "tooMuchSmokeThreshold", b.TooMuchSmokeThreshold);
        if (root.TryGet("lighting", out var lighting))
        {
            var lightingSettings = lighting as YamlMappingNode ?? throw new InvalidDataException("Lighting settings require a mapping.");
            b.MaximumStaticLightDistance = Read(lightingSettings, "maxStatic", b.MaximumStaticLightDistance);
            b.MaximumDynamicLightDistance = Read(lightingSettings, "maxDynamic", b.MaximumDynamicLightDistance);
            b.EnhancedLighting = Read(lightingSettings, "enhanced", b.EnhancedLighting);
        }
        b.ShareAmmoCategories = Read(root, "shareAmmoCategories", b.ShareAmmoCategories);
        b.BaseDefenseMapFromLocation = Read(root, "baseDefenseMapFromLocation", b.BaseDefenseMapFromLocation);
        b.BughuntMinTurn = Read(root, "bughuntMinTurn", b.BughuntMinTurn);
        b.BughuntMaxEnemies = Read(root, "bughuntMaxEnemies", b.BughuntMaxEnemies);
        b.BughuntRank = Read(root, "bughuntRank", b.BughuntRank);
        b.BughuntLowMorale = Read(root, "bughuntLowMorale", b.BughuntLowMorale);
        b.BughuntTimeUnitsLeft = Read(root, "bughuntTimeUnitsLeft", b.BughuntTimeUnitsLeft);
        // std::pair reads from a two-element sequence; anything else leaves the pair as it was.
        if (root.TryGet("alienFuel", out var fuel) && fuel is YamlSequenceNode { Items.Count: 2 } fuelPair)
        {
            b.AlienFuelName = YamlValueReader.ReadString(fuelPair.Items[0]);
            b.AlienFuelQuantity = YamlValueReader.ReadInt32(fuelPair.Items[1]);
        }
        if (root.TryGet("alienItemLevels", out var itemLevels))
        {
            if (itemLevels is not YamlSequenceNode levels)
                throw new YamlFormatException("Global rule 'alienItemLevels' must be a sequence.", itemLevels!.Span);
            b.AlienItemLevels = [.. levels.Items.Select(static month => month is YamlSequenceNode values
                ? values.Items.Select(YamlValueReader.ReadInt32).ToList()
                : throw new YamlFormatException("Each alien item level month must be a sequence.", month.Span))];
        }
        ApplyStatAdjustments(root, b.StatAdjustments);
        ApplyNames(root, "baseNamesFirst", b.BaseNamesFirst, false);
        ApplyNames(root, "baseNamesMiddle", b.BaseNamesMiddle, false);
        ApplyNames(root, "baseNamesLast", b.BaseNamesLast, false);
        ApplyNames(root, "operationNamesFirst", b.OperationNamesFirst, false);
        ApplyNames(root, "operationNamesLast", b.OperationNamesLast, false);
        if (root.TryGet("fixedUserOptions", out var fixedOptions))
            CampaignStartYaml.ApplyEditableNameMap(b.FixedUserOptions, fixedOptions!, "fixedUserOptions");
        ApplyInterception(root, b.Interception);
        ApplyMonthly(root, b.Monthly);
        b.Combat = ApplyCombat(root, b.Combat);
        // Mod::loadFile: the constants section, a mapping or (the backward compatible form) a sequence of mappings.
        if (root.TryGet("constants", out var constants))
            foreach (var node in constants is YamlSequenceNode list ? list.Items : [constants!])
                if (node is YamlMappingNode mapping) ApplyConstants(mapping, b);
    }

    // Mod::loadConstants: only the keys of the constants section that the battle reads.
    private static void ApplyConstants(YamlMappingNode constants, CampaignStartSettingsBuilder b)
    {
        b.Interception.DamageRange = Read(constants, "damageRange", b.Interception.DamageRange);
        b.Interception.ExplosiveDamageRange = Read(constants, "explosiveDamageRange", b.Interception.ExplosiveDamageRange);
        ApplyCoefficients(constants, "fireDamageRange", b.Interception.FireDamageRange);
        b.Combat = b.Combat with
        {
            ExtendedExperienceAwardSystem = Read(constants, "extendedExperienceAwardSystem", b.Combat.ExtendedExperienceAwardSystem),
            ExtendedSpotOnHitForSniping = Read(constants, "extendedSpotOnHitForSniping", b.Combat.ExtendedSpotOnHitForSniping),
            ExtendedTerrainMelee = Read(constants, "extendedTerrainMelee", b.Combat.ExtendedTerrainMelee),
            ExtendedUnderwaterThrowFactor = Read(constants, "extendedUnderwaterThrowFactor", b.Combat.ExtendedUnderwaterThrowFactor),
        };
    }

    // Mod::loadFile's combat globals. The legacy turnAIUseGrenade and turnAIUseBlaster come before the ai section, which wins.
    private static CampaignCombatSettings ApplyCombat(YamlMappingNode root, CampaignCombatSettings current)
    {
        var combat = current with
        {
            AiUseDelayGrenade = Read(root, "turnAIUseGrenade", current.AiUseDelayGrenade),
            AiUseDelayBlaster = Read(root, "turnAIUseBlaster", current.AiUseDelayBlaster),
        };
        if (root.TryGet("ai", out var ai) && ai is YamlMappingNode aiSettings)
        {
            combat = combat with
            {
                AiUseDelayBlaster = Read(aiSettings, "useDelayBlaster", combat.AiUseDelayBlaster),
                AiUseDelayFirearm = Read(aiSettings, "useDelayFirearm", combat.AiUseDelayFirearm),
                AiUseDelayGrenade = Read(aiSettings, "useDelayGrenade", combat.AiUseDelayGrenade),
                AiUseDelayProxy = Read(aiSettings, "aiUseDelayProxy", combat.AiUseDelayProxy),
                AiUseDelayMelee = Read(aiSettings, "useDelayMelee", combat.AiUseDelayMelee),
                AiUseDelayPsionic = Read(aiSettings, "useDelayPsionic", combat.AiUseDelayPsionic),
                AiUseDelayMedikit = Read(aiSettings, "useDelayMedikit", combat.AiUseDelayMedikit),
                ReactionFireThreshold = Read(aiSettings, "reactionFireThreshold", combat.ReactionFireThreshold),
                ReactionFireThresholdCivilians = Read(aiSettings, "reactionFireThresholdCiv", combat.ReactionFireThresholdCivilians),
            };
        }
        combat = combat with
        {
            KneelBonusGlobal = Read(root, "kneelBonusGlobal", combat.KneelBonusGlobal),
            OneHandedPenaltyGlobal = Read(root, "oneHandedPenaltyGlobal", combat.OneHandedPenaltyGlobal),
            EnableCloseQuartersCombat = Read(root, "enableCloseQuartersCombat", combat.EnableCloseQuartersCombat),
            CloseQuartersAccuracyGlobal = Read(root, "closeQuartersAccuracyGlobal", combat.CloseQuartersAccuracyGlobal),
            CloseQuartersTuCostGlobal = Read(root, "closeQuartersTuCostGlobal", combat.CloseQuartersTuCostGlobal),
            CloseQuartersEnergyCostGlobal = Read(root, "closeQuartersEnergyCostGlobal", combat.CloseQuartersEnergyCostGlobal),
            CloseQuartersSneakUpGlobal = Read(root, "closeQuartersSneakUpGlobal", combat.CloseQuartersSneakUpGlobal),
            NoLosAccuracyPenaltyGlobal = Read(root, "noLOSAccuracyPenaltyGlobal", combat.NoLosAccuracyPenaltyGlobal),
            ExplodeInventoryGlobal = Read(root, "explodeInventoryGlobal", combat.ExplodeInventoryGlobal),
            StunningImprovesMorale = Read(root, "stunningImprovesMorale", combat.StunningImprovesMorale),
            StatisticalBulletConservation = Read(root, "statisticalBulletConservation", combat.StatisticalBulletConservation),
            TuRecoveryWakeUpNewTurn = Read(root, "tuRecoveryWakeUpNewTurn", combat.TuRecoveryWakeUpNewTurn),
        };
        return root.TryGet("hitChancesTable", out var table) ? combat with { HitChancesTable = ReadHitChances(table!) } : combat;
    }

    private const int HitChanceTableSize = 40 * 61;

    // Mod::loadFile: the table is cleared, then each entry with a unitSize and exactly 40 rows of 61 values is kept.
    private static ReadOnlyDictionary<int, IReadOnlyList<int>> ReadHitChances(YamlNode node)
    {
        var tables = new Dictionary<int, IReadOnlyList<int>>();
        var entries = node switch
        {
            YamlSequenceNode sequence => sequence.Items,
            YamlMappingNode mapping => [.. mapping.Entries.Select(static entry => entry.Value)],
            _ => [],
        };
        foreach (var entry in entries)
        {
            if (entry is not YamlMappingNode table || !table.TryGet("unitSize", out var sizeNode) ||
                !YamlValueReader.TryReadInt32(sizeNode!, out var size)) continue;
            var values = new List<int>();
            if (table.TryGet("distances", out var distances) && distances is YamlMappingNode rows)
                foreach (var row in rows.Entries)
                    values.AddRange(row.Value is YamlSequenceNode items ? items.Items.Select(YamlValueReader.ReadInt32) : [YamlValueReader.ReadInt32(row.Value)]);
            if (values.Count == HitChanceTableSize) tables[size] = Array.AsReadOnly(values.ToArray());
        }
        return new ReadOnlyDictionary<int, IReadOnlyList<int>>(tables);
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

    // Mod::loadFile order and defaults; see CampaignMonthlySettings.
    private static void ApplyMonthly(YamlMappingNode root, CampaignMonthlySettingsBuilder b)
    {
        b.ChanceToDetectAlienBaseEachMonth = Read(root, "chanceToDetectAlienBaseEachMonth", b.ChanceToDetectAlienBaseEachMonth);
        // A gameOver node that is not a mapping has no children to read (YamlNodeReader::getChildNode).
        if (root.TryGet("gameOver", out var gameOver) && gameOver is YamlMappingNode mapping)
        {
            b.LoseMoneyCutscene = Read(mapping, "loseMoney", b.LoseMoneyCutscene);
            b.LoseRatingCutscene = Read(mapping, "loseRating", b.LoseRatingCutscene);
            b.LoseDefeatCutscene = Read(mapping, "loseDefeat", b.LoseDefeatCutscene);
        }
        // The reference's std::map read clears the map and emplaces each child, so the first value of a repeated
        // key stays and a node without children (null or a scalar) leaves no ratings.
        if (root.TryGet("monthlyRatings", out var ratings))
            b.MonthlyRatings = YamlValueReader.ReadMap(ratings!, YamlValueReader.ReadInt32, YamlValueReader.ReadString);
    }

    // YamlNodeReader::tryRead into a std::vector replaces the whole list.
    // Mod::loadFile: each difficulty's entry is read in place, so a short list or a partial stat map keeps the rest.
    private static void ApplyStatAdjustments(YamlMappingNode root, StatAdjustmentBuilder[] adjustments)
    {
        void PerDifficulty(string key, Action<StatAdjustmentBuilder, YamlNode> apply)
        {
            if (!root.TryGet(key, out var node)) return;
            if (node is not YamlSequenceNode sequence) throw new YamlFormatException($"Global rule '{key}' must be a sequence.", node!.Span);
            for (var index = 0; index < Math.Min(adjustments.Length, sequence.Items.Count); index++) apply(adjustments[index], sequence.Items[index]);
        }
        PerDifficulty("aimAndArmorMultipliers", static (adjustment, node) =>
            adjustment.AimMultiplier = adjustment.ArmorMultiplier = YamlValueReader.ReadDouble(node));
        PerDifficulty("aimMultipliers", static (adjustment, node) => adjustment.AimMultiplier = YamlValueReader.ReadDouble(node));
        PerDifficulty("armorMultipliers", static (adjustment, node) => adjustment.ArmorMultiplier = YamlValueReader.ReadDouble(node));
        PerDifficulty("armorMultipliersAbs", static (adjustment, node) => adjustment.ArmorMultiplierAbsolute = YamlValueReader.ReadInt32(node));
        PerDifficulty("statGrowthMultipliersAbs", static (adjustment, node) => ReadStats(node, adjustment.StatGrowthAbsolute));
        if (root.TryGet("statGrowthMultipliers", out var growth))
        {
            ReadStats(growth!, adjustments[0].StatGrowth);
            foreach (var adjustment in adjustments.Skip(1))
            {
                adjustment.StatGrowth.Clear();
                foreach (var pair in adjustments[0].StatGrowth) adjustment.StatGrowth[pair.Key] = pair.Value;
            }
        }
    }

    private static readonly string[] StatKeys =
        ["tu", "stamina", "health", "bravery", "reactions", "firing", "throwing", "strength", "psiStrength", "psiSkill", "melee", "mana"];

    private static void ReadStats(YamlNode node, Dictionary<string, short> stats)
    {
        if (node is not YamlMappingNode map) throw new YamlFormatException("Unit stats must be a mapping.", node.Span);
        foreach (var key in StatKeys)
            if (map.TryGet(key, out var value)) stats[key] = checked((short)YamlValueReader.ReadInt32(value!));
    }

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

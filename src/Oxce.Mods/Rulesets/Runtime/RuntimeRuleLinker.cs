using System.Collections.Frozen;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Text;
using Oxce.Core.Diagnostics;
using Oxce.Formats.Yaml;
using Oxce.Mods.Resources;
using Oxce.Mods.Rulesets.CampaignStart;
using Oxce.Mods.Rulesets.Content;
using Oxce.Mods.Rulesets.EquipmentProduction;
using Oxce.Mods.Rulesets.Items;
using Oxce.Mods.Rulesets.Phase3;
using Oxce.Mods.Rulesets.PersonnelTactical;
using Oxce.Mods.Rulesets.Presentation;
using Oxce.Mods.Rulesets.TerrainDeployment;
using AlienMissionRule = Oxce.Mods.Rulesets.MissionEvents.AlienMissionRule;
using EventRule = Oxce.Mods.Rulesets.MissionEvents.EventRule;
using UfoTrajectoryRule = Oxce.Mods.Rulesets.MissionEvents.UfoTrajectoryRule;
using WeightedTimelineEntry = Oxce.Mods.Rulesets.MissionEvents.WeightedTimelineEntry;
using StrategicScriptRule = Oxce.Mods.Rulesets.MissionEvents.StrategicScriptRule;

namespace Oxce.Mods.Rulesets.Runtime;

public sealed record RuntimeRuleLinkOptions
{
    public Oxce.Mods.Files.VirtualFileCatalog? Files { get; init; }
    public CancellationToken CancellationToken { get; init; }
    public IReadOnlyDictionary<string, IReadOnlyList<RuntimeSoldierNamePool>> SoldierNamePools { get; init; } =
        new ReadOnlyDictionary<string, IReadOnlyList<RuntimeSoldierNamePool>>(new Dictionary<string, IReadOnlyList<RuntimeSoldierNamePool>>());
}

/// <summary>
/// Compiles compatibility-facing typed rules into the dense, generation-scoped form used by gameplay.
/// Reference behavior: RuleCountry.cpp/RuleRegion.cpp/RuleBaseFacility.cpp/RuleCraft.cpp/RuleItem.cpp/
/// RuleSoldier.cpp afterLoad methods and Mod.cpp newSave/getStartingBase at reference commit 4df3a5e.
/// </summary>
public static class RuntimeRuleLinker
{
    public static RuntimeRuleLinkResult Link(
        Phase3ContentCatalog content,
        ResolvedResourceCatalog resources,
        IReadOnlyList<ContentScriptArtifact>? scripts = null,
        IDiagnosticSink? diagnostics = null,
        RuntimeRuleLinkOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(resources);
        scripts ??= [];
        diagnostics ??= NullDiagnosticSink.Instance;
        options ??= new RuntimeRuleLinkOptions();
        var cancellationToken = options.CancellationToken;
        cancellationToken.ThrowIfCancellationRequested();
        var generation = resources.Generation;
        var issues = new List<RuntimeRuleLinkIssue>();

        var countryHandles = Handles<CountryRuleFamily, CountryRule>(generation, content.CampaignStart.Countries);
        var regionHandles = Handles<RegionRuleFamily, RegionRule>(generation, content.CampaignStart.Regions);
        var facilityHandles = Handles<FacilityRuleFamily, BaseFacilityRule>(generation, content.CampaignStart.Facilities);
        var craftHandles = Handles<CraftRuleFamily, CraftRule>(generation, content.EquipmentProduction.Crafts);
        var craftWeaponHandles = Handles<CraftWeaponRuleFamily, CraftWeaponRule>(
            generation, content.EquipmentProduction.CraftWeapons);
        var itemHandles = Handles<ItemRuleFamily, ItemRule>(generation, content.Items.Items);
        var soldierHandles = Handles<SoldierRuleFamily, SoldierRule>(generation, content.PersonnelTactical.Soldiers);
        var armorHandles = Handles<ArmorRuleFamily, ArmorRule>(generation, content.PersonnelTactical.Armors);
        var unitHandles = Handles<UnitRuleFamily, UnitRule>(generation, content.PersonnelTactical.Units);
        var skillHandles = Handles<SkillRuleFamily, SkillRule>(generation, content.PersonnelTactical.Skills);
        var researchHandles = Handles<ResearchRuleFamily, ResearchRule>(generation, content.EquipmentProduction.Research);
        var manufactureHandles = Handles<ManufactureRuleFamily, ManufactureRule>(generation, content.EquipmentProduction.Manufacture);
        var eventHandles = Handles<EventRuleFamily, EventRule>(generation, content.MissionEvents.Events);
        var missionHandles = Handles<AlienMissionRuleFamily, AlienMissionRule>(generation, content.MissionEvents.AlienMissions);
        var raceHandles = Handles<AlienRaceRuleFamily, AlienRaceRule>(generation, content.TerrainDeployment.AlienRaces);
        var ufoHandles = Handles<UfoRuleFamily, UfoRule>(generation, content.EquipmentProduction.Ufos);
        var trajectoryHandles = Handles<UfoTrajectoryRuleFamily, UfoTrajectoryRule>(
            generation, content.MissionEvents.UfoTrajectories);
        var deploymentHandles = Handles<AlienDeploymentRuleFamily, AlienDeploymentRule>(
            generation, content.TerrainDeployment.AlienDeployments);
        var scriptBuild = BuildScripts(generation, scripts, cancellationToken);

        cancellationToken.ThrowIfCancellationRequested();
        var countries = BuildFamily<CountryRuleFamily, CountryRule, RuntimeCountryRule>(
            generation, content.CampaignStart.Countries, rule => new RuntimeCountryRule(
                rule.Value.FundingBase,
                rule.Value.FundingCap,
                rule.Value.LabelLongitude,
                rule.Value.LabelLatitude,
                rule.Value.LabelColor,
                rule.Value.ZoomLevel,
                rule.Value.Areas,
                rule.Value.ProvidedBaseFunctions,
                rule.Value.ForbiddenBaseFunctions,
                rule.Value.SignedPactEvent,
                OptionalRequired(eventHandles, rule.Value.SignedPactEvent, "countries", rule, "signedPactEvent"),
                rule.Value.RejoinedXcomEvent,
                OptionalRequired(eventHandles, rule.Value.RejoinedXcomEvent, "countries", rule, "rejoinedXcomEvent"),
                ScriptsFor(scriptBuild.ByOwner, "countries", rule.Id)));

        cancellationToken.ThrowIfCancellationRequested();
        var regions = BuildFamily<RegionRuleFamily, RegionRule, RuntimeRegionRule>(
            generation, content.CampaignStart.Regions, rule => new RuntimeRegionRule(
                rule.Value.BaseCost,
                rule.Value.Areas,
                rule.Value.MissionZones,
                rule.Value.MissionWeights,
                rule.Value.RegionWeight,
                rule.Value.MissionRegion,
                OptionalRuntime(regionHandles, rule.Value.MissionRegion),
                rule.Value.ProvidedBaseFunctions,
                rule.Value.ForbiddenBaseFunctions));

        cancellationToken.ThrowIfCancellationRequested();
        var facilities = BuildFamily<FacilityRuleFamily, BaseFacilityRule, RuntimeFacilityRule>(
            generation, content.CampaignStart.Facilities, rule => new RuntimeFacilityRule(
                rule.Value.SizeX,
                rule.Value.SizeY,
                rule.Value.Lift,
                rule.Value.BuildCost,
                rule.Value.RefundValue,
                rule.Value.BuildTime,
                rule.Value.MonthlyCost,
                rule.Value.Storage,
                rule.Value.Personnel,
                rule.Value.Aliens,
                rule.Value.Crafts,
                rule.Value.Laboratories,
                rule.Value.Workshops,
                rule.Value.PsiLaboratories,
                rule.Value.ListOrder,
                rule.Value.MapName,
                References(researchHandles, rule.Value.Requires),
                RequiredMany(facilityHandles, rule.Value.BuildOverFacilities, "facilities", rule,
                    "buildOverFacilities"),
                RequiredMany(facilityHandles, rule.Value.LeavesBehindOnSell, "facilities", rule,
                    "leavesBehindOnSell"),
                OptionalRequired(facilityHandles, rule.Value.DestroyedFacility, "facilities", rule,
                    "destroyedFacility"),
                OptionalRequired(itemHandles, rule.Value.AmmoItem, "facilities", rule, "ammoItem"),
                Array.AsReadOnly(rule.Value.BuildCostItems.Select(pair => new RuntimeFacilityItemCost(
                    pair.Key,
                    OptionalRuntime(itemHandles, pair.Key),
                    pair.Value.Build,
                    pair.Value.Refund)).ToArray()),
                Resource(rule.Value.SpriteShape, "BASEBITS.PCK", ResourceKind.Sprite, resources),
                Resource(rule.Value.SpriteFacility, "BASEBITS.PCK", ResourceKind.Sprite, resources),
                Resource(rule.Value.FireSound, "GEO.CAT", ResourceKind.Sound, resources),
                Resource(rule.Value.HitSound, "GEO.CAT", ResourceKind.Sound, resources),
                Resource(rule.Value.PlaceSound, "GEO.CAT", ResourceKind.Sound, resources))
            {
                PrisonType = rule.Value.PrisonType,
                AmmoMaximum = rule.Value.AmmoMaximum,
                RearmRate = rule.Value.RearmRate,
                CanBeBuiltOver = rule.Value.CanBeBuiltOver,
                FakeUnderwater = rule.Value.FakeUnderwater,
                UpgradeOnly = rule.Value.UpgradeOnly,
                RemovalTime = rule.Value.RemovalTime,
                MaximumAllowedPerBase = rule.Value.MaximumAllowedPerBase,
                TrainingRooms = rule.Value.TrainingRooms,
                ManaRecoveryPerDay = rule.Value.ManaRecoveryPerDay,
                HealthRecoveryPerDay = rule.Value.HealthRecoveryPerDay,
                SickBayAbsoluteBonus = rule.Value.SickBayAbsoluteBonus,
                SickBayRelativeBonus = rule.Value.SickBayRelativeBonus,
                RequiredBaseFunctions = rule.Value.RequiredBaseFunctions,
                ForbiddenBaseFunctions = rule.Value.ForbiddenBaseFunctions,
                HangarType = rule.Value.HangarType,
                ProvidedBaseFunctions = rule.Value.ProvidedBaseFunctions,
                SightRange = rule.Value.SightRange,
                SightChance = rule.Value.SightChance,
                RadarRange = rule.Value.RadarRange,
                RadarChance = rule.Value.RadarChance,
                Hyperwave = rule.Value.HyperWave,
                MindShield = rule.Value.MindShield,
                MindShieldPower = rule.Value.MindPower,
            });

        cancellationToken.ThrowIfCancellationRequested();
        var crafts = BuildFamily<CraftRuleFamily, CraftRule, RuntimeCraftRule>(
            generation, content.EquipmentProduction.Crafts, rule => new RuntimeCraftRule(
                rule.Value.Integers["listOrder"],
                rule.Value.Integers["costBuy"],
                rule.Value.Integers["costRent"],
                rule.Value.Integers["costSell"],
                rule.Value.Integers["transferTime"],
                rule.Value.Stats.Get("fuelMax"),
                rule.Value.Stats.Get("damageMax"),
                rule.Value.Stats.Get("speedMax"),
                rule.Value.Stats.Get("soldiers"),
                rule.Value.Stats.Get("vehicles"),
                rule.Value.EffectiveMaximumUnits,
                rule.Value.EffectiveMaximumVehiclesAndLargeSoldiers,
                rule.Value.Booleans["allowLanding"],
                References(researchHandles, rule.Value.Requirements),
                OptionalRequired(itemHandles, rule.Value.Strings["refuelItem"], "crafts", rule, "refuelItem"),
                RequiredMany(craftWeaponHandles, rule.Value.FixedWeapons.Where(static id => id.Length != 0),
                    "crafts", rule, "fixedWeapons"),
                ScriptsFor(scriptBuild.ByOwner, "crafts", rule.Id))
            {
                Type = rule.Id,
                Purchase = new(rule.Value.Integers["monthlyBuyLimit"], rule.Value.Strings["monthlyBuyLimitMessage"],
                    rule.Value.Strings["requiresBuyCountry"], rule.Value.RequiredBuyBaseFunctions, []),
                DefaultStats = rule.Value.Stats.Integers,
                HangarType = rule.Value.Integers["hangarType"],
                WeaponSlots = rule.Value.Integers["weapons"],
                RefuelRate = rule.Value.Integers["refuelRate"],
                RepairRate = rule.Value.Integers["repairRate"],
                ShieldCapacity = rule.Value.Stats.Get("shieldCapacity"),
                ShieldRechargeInGeoscape = rule.Value.Stats.Get("shieldRechargeInGeoscape"),
                ShieldRechargeAtBase = rule.Value.Integers["shieldRechargedAtBase"],
                SightRange = rule.Value.Stats.Get("sightRange"),
                RadarRange = rule.Value.Stats.Get("radarRange"),
                RadarChance = rule.Value.Stats.Get("radarChance"),
                MaximumItems = rule.Value.Stats.Get("maxItems"),
                MaximumStorageSpace = rule.Value.Stats.MaximumStorageSpace,
                NotifyWhenRefueled = rule.Value.Booleans["notifyWhenRefueled"],
                Undetectable = rule.Value.Booleans["undetectable"],
                PatrolWithoutFuel = rule.Value.Booleans["patrolWithoutFuel"],
                AutoPatrol = rule.Value.Booleans["autoPatrol"],
                FixedWeaponSlots = rule.Value.FixedWeapons,
                Pilots = rule.Value.Integers["pilots"],
                MaximumSoldiers = rule.Value.Integers["maxSoldiers"],
                MaximumVehicles = rule.Value.Integers["maxVehicles"],
                MaximumSmallSoldiers = rule.Value.Integers["maxSmallSoldiers"],
                MaximumLargeSoldiers = rule.Value.Integers["maxLargeSoldiers"],
                MaximumSmallVehicles = rule.Value.Integers["maxSmallVehicles"],
                MaximumLargeVehicles = rule.Value.Integers["maxLargeVehicles"],
                MaximumSmallUnits = rule.Value.Integers["maxSmallUnits"],
                MaximumLargeUnits = rule.Value.Integers["maxLargeUnits"],
                OnlyOneSoldierGroupAllowed = rule.Value.Booleans["onlyOneSoldierGroupAllowed"],
                AllowedSoldierGroups = rule.Value.AllowedSoldierGroups,
                AllowedArmorGroups = rule.Value.AllowedArmorGroups,
                ArmorGroupLimits = rule.Value.ArmorGroupLimits,
                PilotMinimumStats = rule.Value.PilotMinimumStats,
                RequiredPilotBonuses = rule.Value.RequiredPilotBonuses,
            });

        cancellationToken.ThrowIfCancellationRequested();
        var categoryReplacements = ItemCategoryReplacements(content);
        var items = BuildFamily<ItemRuleFamily, ItemRule, RuntimeItemRule>(
            generation, content.Items.Items, rule => new RuntimeItemRule(
                rule.Value.Values.GetString("name"),
                rule.Value.Values.GetInteger("listOrder"),
                rule.Value.Values.GetInteger("costBuy"),
                rule.Value.Values.GetInteger("costSell"),
                rule.Value.Values.GetInteger("transferTime"),
                rule.Value.Values.GetInteger("weight"),
                rule.Value.Values.Real("size"),
                rule.Value.Values.GetInteger("battleType"),
                rule.Value.Values.GetInteger("clipSize"),
                RequiredMany(researchHandles, rule.Value.Requirements, "items", rule, "requires"),
                Array.AsReadOnly(rule.Value.CompatibleAmmo
                    .Select(group => RequiredMany(itemHandles, group, "items", rule, "compatibleAmmo"))
                    .ToArray()),
                ScriptsFor(scriptBuild.ByOwner, "items", rule.Id))
            {
                Type = rule.Id,
                Damage = RuntimeItemDamageProjection.Create(rule.Value.Damage),
                MeleeDamage = RuntimeItemDamageProjection.Create(rule.Value.MeleeDamage),
                Power = rule.Value.Values.GetInteger("power"),
                MeleePower = rule.Value.Values.GetInteger("meleePower"),
                ArmorValue = rule.Value.Values.GetInteger("armor"),
                Waypoints = rule.Value.Values.GetInteger("waypoints"),
                TwoHanded = rule.Value.Values.Boolean("twoHanded"),
                BlockingBothHands = rule.Value.Values.Boolean("blockBothHands"),
                WaterOnly = rule.Value.Values.Boolean("underwaterOnly"),
                Categories = rule.Value.Categories.Select(category => categoryReplacements.GetValueOrDefault(category, category))
                    .ToFrozenSet(StringComparer.Ordinal),
                Accuracy = new(rule.Value.Actions["Aimed"].Accuracy, rule.Value.Actions["Auto"].Accuracy,
                    rule.Value.Actions["Snap"].Accuracy, rule.Value.Actions["Melee"].Accuracy,
                    rule.Value.Values.GetInteger("accuracyUse"), rule.Value.Values.GetInteger("accuracyMindControl"),
                    rule.Value.Values.GetInteger("accuracyPanic"), rule.Value.Values.GetInteger("accuracyThrow")),
                Purchase = new(rule.Value.Values.GetInteger("monthlyBuyLimit"), rule.Value.Values.GetString("monthlyBuyLimitMessage"),
                    rule.Value.Values.GetString("requiresBuyCountry"), rule.Value.RequiredBuyBaseFunctions, rule.Value.BuyRequirements),
                IsAlien = rule.Value.Values.Boolean("liveAlien"),
                Recoverable = rule.Value.Values.Boolean("recover"),
                CorpseRecoverable = rule.Value.Values.Boolean("recoverCorpse"),
                VehicleFixedAmmoSlot = rule.Value.Values.GetInteger("vehicleFixedAmmoSlot"),
                VehicleArmor = rule.Value.Values.Boolean("fixedWeapon") && content.PersonnelTactical.Units.TryGet(rule.Id, out var vehicleUnit)
                    ? OptionalRuntime(armorHandles, vehicleUnit!.Value.Strings["armor"]) : null,
                PrisonType = rule.Value.Values.GetInteger("prisonType"),
                MonthlyMaintenance = rule.Value.Values.GetInteger("monthlyMaintenance"),
                MonthlySalary = rule.Value.Values.GetInteger("monthlySalary"),
            });

        cancellationToken.ThrowIfCancellationRequested();
        var armors = BuildFamily<ArmorRuleFamily, ArmorRule, RuntimeArmorRule>(
            generation, content.PersonnelTactical.Armors, rule => new RuntimeArmorRule(
                rule.Value.Integers["listOrder"],
                rule.Value.Integers["size"],
                rule.Value.EffectiveSpaceOccupied,
                rule.Value.Strings["storeItem"],
                OptionalItem(itemHandles, rule.Value.Strings["storeItem"]))
            {
                Type = rule.Id,
                Group = rule.Value.Integers["group"],
                CorpseGeoscape = OptionalItem(itemHandles, rule.Value.Strings["corpseGeo"]),
                Stats = rule.Value.Stats.Values,
                Vision = new(
                    rule.Value.Integers["visibilityAtDark"],
                    rule.Value.Integers["visibilityAtDay"],
                    rule.Value.Integers["camouflageAtDark"],
                    rule.Value.Integers["camouflageAtDay"],
                    rule.Value.Integers["antiCamouflageAtDark"],
                    rule.Value.Integers["antiCamouflageAtDay"],
                    rule.Value.Integers["heatVision"],
                    rule.Value.Integers["visibilityThroughFire"],
                    rule.Value.Integers["personalLight"],
                    rule.Value.Integers["personalLightHostile"],
                    rule.Value.Integers["personalLightNeutral"]),
            });

        cancellationToken.ThrowIfCancellationRequested();
        var soldiers = BuildFamily<SoldierRuleFamily, SoldierRule, RuntimeSoldierRule>(
            generation, content.PersonnelTactical.Soldiers, rule => new RuntimeSoldierRule(
                rule.Value.Integers["listOrder"],
                rule.Value.Integers["group"],
                rule.Value.Integers["costBuy"],
                rule.Value.Integers["costSalary"],
                rule.Value.Integers["transferTime"],
                rule.Value.Booleans["allowPromotion"],
                rule.Value.Booleans["allowPiloting"],
                RequiredRule(armorHandles, rule.Value.Strings["armor"], "soldiers", rule, "armor"),
                References(researchHandles, rule.Value.Requirements),
                RequiredMany(skillHandles, rule.Value.Skills, "soldiers", rule, "skills"),
                ScriptsFor(scriptBuild.ByOwner, "soldiers", rule.Id))
            {
                Purchase = new(rule.Value.Integers["monthlyBuyLimit"], rule.Value.Strings["monthlyBuyLimitMessage"],
                    rule.Value.Strings["requiresBuyCountry"], rule.Value.RequiredBuyBaseFunctions, []),
                MinimumStats = rule.Value.MinimumStats.Values,
                MaximumStats = rule.Value.MaximumStats.Values,
                StatCaps = rule.Value.StatCaps.Values,
                TrainingStatCaps = rule.Value.TrainingStatCaps.Values,
                FemaleFrequency = rule.Value.Integers["femaleFrequency"],
                NamePools = options.SoldierNamePools.GetValueOrDefault(rule.Id) ?? [],
                SpawnedTemplate = RuntimeSoldierTemplateLoader.Read(rule.Value.SpawnedSoldierTemplate),
                RankCount = rule.Value.RankStrings.Count,
                Salaries = Array.AsReadOnly(new[] { rule.Value.Integers["costSalary"], rule.Value.Integers["costSalarySquaddie"],
                    rule.Value.Integers["costSalarySergeant"], rule.Value.Integers["costSalaryCaptain"],
                    rule.Value.Integers["costSalaryColonel"], rule.Value.Integers["costSalaryCommander"] }),
            });

        cancellationToken.ThrowIfCancellationRequested();
        var settings = BuildCampaignSettings(content.CampaignStart.Settings) with
        {
            Globe = RuntimeGlobeLoader.Load(content.CampaignStart.Settings.GlobeLayers, options.Files),
            BuildTimeReductionScaling = content.CampaignStart.Settings.BuildTimeReductionScaling,
            CustomTrainingFactor = content.CampaignStart.Settings.CustomTrainingFactor,
            ManaWoundThreshold = content.CampaignStart.Settings.ManaWoundThreshold,
            HealthWoundThreshold = content.CampaignStart.Settings.HealthWoundThreshold,
            EscortRange = content.CampaignStart.Settings.EscortRange,
            MaximumViewDistance = content.CampaignStart.Settings.MaximumViewDistance,
            MaximumDarknessToSeeUnits = content.CampaignStart.Settings.MaximumDarknessToSeeUnits,
            HunterKillerFastRetarget = content.CampaignStart.Settings.HunterKillerFastRetarget,
            AllowAlienBasesOnWrongTextures = content.CampaignStart.Settings.AllowAlienBasesOnWrongTextures,
            AllowCountriesToCancelAlienPact = content.CampaignStart.Settings.AllowCountriesToCancelAlienPact,
            BuildInfiltrationBaseCloseToTheCountry = content.CampaignStart.Settings.BuildInfiltrationBaseCloseToTheCountry,
            InfiltrateRandomCountryInTheRegion = content.CampaignStart.Settings.InfiltrateRandomCountryInTheRegion,
        };
        var compatibility = new RuntimeRuleCompatibilitySidecar(CompatibilityEntries(content));
        var catalog = new RuntimeRuleCatalog(
            generation,
            countries,
            regions,
            facilities,
            crafts,
            BuildFamily<CraftWeaponRuleFamily, CraftWeaponRule, RuntimeCraftWeaponRule>(generation,
                content.EquipmentProduction.CraftWeapons, rule => new RuntimeCraftWeaponRule(rule.Value.Integers["ammoMax"],
                    rule.Value.Integers["rearmRate"], rule.Value.Launcher, rule.Value.Clip, rule.Value.Stats.Integers)
                {
                    StatisticalBulletSaving = rule.Value.Booleans["bulletSaving"],
                    BonusStorageSpace = rule.Value.Stats.MaximumStorageSpace,
                }),
            items,
            soldiers,
            armors,
            BuildFamily<UnitRuleFamily, UnitRule, RuntimeUnitRule>(generation,
                content.PersonnelTactical.Units, rule => new(rule.Id)
                {
                    // The typed catalog already rejects missing unit armor.
                    Armor = OptionalRuntime(armorHandles, rule.Value.Strings["armor"]),
                    SpawnUnit = OptionalRequired(unitHandles, rule.Value.Strings["spawnUnit"], "units", rule, "spawnUnit"),
                    LiveAlien = rule.Value.Strings["liveAlien"] == "\0"
                        ? OptionalItem(itemHandles, rule.Id)
                        : rule.Value.Strings["liveAlien"] is "" or "STR_NONE" ? null
                        : OptionalRequired(itemHandles, rule.Value.Strings["liveAlien"], "units", rule, "liveAlien"),
                    CivilianRecoveryItem = rule.Value.Strings["civilianRecoveryType"] is "" or "\0" or "STR_NONE" or "STR_ENGINEER" or "STR_SCIENTIST" ||
                        soldierHandles.ContainsKey(rule.Value.Strings["civilianRecoveryType"]) ? null
                        : OptionalRequired(itemHandles, rule.Value.Strings["civilianRecoveryType"], "units", rule, "civilianRecoveryType"),
                }),
            BuildFamily<InventoryRuleFamily, InventoryRule, RuntimeInventoryRule>(generation,
                content.PersonnelTactical.Inventories, rule => new(rule.Id, rule.Value.Type, rule.Value.Hand)
                {
                    Costs = rule.Value.Costs.ToFrozenDictionary(StringComparer.Ordinal),
                }),
            BuildFamily<SoldierBonusRuleFamily, SoldierBonusRule, RuntimeSoldierBonusRule>(generation,
                content.PersonnelTactical.Bonuses, rule => new(rule.Value.ListOrder, rule.Value.Stats.Values)),
            BuildFamily<SoldierTransformationRuleFamily, SoldierTransformationRule, RuntimeSoldierTransformationRule>(generation,
                content.PersonnelTactical.Transformations, rule => new(rule.Value.Strings, rule.Value.Integers,
                    rule.Value.Booleans, rule.Value.Requirements, rule.Value.RequiredBaseFunctions,
                    rule.Value.AllowedSoldierTypes, rule.Value.RequiredPreviousTransformations,
                    rule.Value.ForbiddenPreviousTransformations, rule.Value.RemovedTransformations,
                    rule.Value.RequiredItems, rule.Value.RequiredCommendations,
                    new ReadOnlyDictionary<string, IReadOnlyDictionary<string, short>>(rule.Value.StatSets.ToDictionary(
                        pair => pair.Key, pair => pair.Value.Values, StringComparer.Ordinal)), rule.Value.Events)),
            BuildFamily<CommendationRuleFamily, CommendationRule, RuntimeCommendationRule>(generation,
                content.PersonnelTactical.Commendations, rule => new(rule.Value.SoldierBonusTypes)),
            IdentityFamily<SkillRuleFamily, SkillRule>(generation, content.PersonnelTactical.Skills),
            BuildFamily<ResearchRuleFamily, ResearchRule, RuntimeResearchRule>(generation,
                content.EquipmentProduction.Research, rule => new(
                    rule.Value.EffectiveLookup(rule.Id), rule.Value.SpawnedItem, rule.Value.SpawnedItemCount,
                    rule.Value.SpawnedItemList, rule.Value.DecreaseCounters,
                    rule.Value.IncreaseCounters, rule.Value.SpawnedEvent,
                    rule.Value.Cost, rule.Value.Points, rule.Value.Dependencies, rule.Value.Unlocks,
                    rule.Value.Disables, rule.Value.Reenables, rule.Value.GetOneFree,
                    rule.Value.SequentialGetOneFree,
                    rule.Value.GetOneFreeProtected, rule.Value.Requirements,
                    rule.Value.RequiredBaseFunctions,
                    // RuleResearch::afterLoad treats an empty neededItem like a missing one.
                    rule.Value.NeededItem is { Length: > 0 } neededItem ? neededItem
                        : rule.Value.NeedItem && itemHandles.ContainsKey(rule.Id) ? rule.Id : null,
                    rule.Value.NeedItem,
                    rule.Value.DestroyItem, rule.Value.ReturnsItem, rule.Value.Repeatable,
                    rule.Value.ListOrder, rule.Value.Events)),
            BuildFamily<ManufactureRuleFamily, ManufactureRule, RuntimeManufactureRule>(generation,
                content.EquipmentProduction.Manufacture, rule => new(
                    rule.Value.Category, rule.Value.Requirements, rule.Value.RequiredBaseFunctions,
                    rule.Value.Space, rule.Value.Time, rule.Value.Cost, rule.Value.Points,
                    rule.Value.Refund,
                    Array.AsReadOnly(rule.Value.RequiredItems.Select(pair => RequiredMaterial(pair.Key, pair.Value)).ToArray()),
                    Array.AsReadOnly((rule.Value.Category == "STR_CRAFT" ? rule.Value.EffectiveProducedItems().Take(1) : rule.Value.EffectiveProducedItems()).Select(pair => ProducedMaterial(
                            pair.Key, pair.Value, rule.Value.Category == "STR_CRAFT")).ToArray()),
                    rule.Value.RandomProducedItems, rule.Value.SpawnedPersonType,
                    rule.Value.SpawnedPersonName,
                    RuntimeSoldierTemplateLoader.Read(rule.Value.SpawnedSoldierTemplate),
                    rule.Value.TransferTimes, rule.Value.ListOrder,
                    rule.Value.Events)),
            BuildFamily<EventRuleFamily, EventRule, RuntimeEventRule>(generation, content.MissionEvents.Events,
                static rule => new(rule.Value.Integers["timer"], rule.Value.Integers["timerRandom"],
                    rule.Value.Strings["interruptResearch"],
                    Array.AsReadOnly(rule.Value.AdhocMissionScriptTags.ToArray()))),
            BuildFamily<UfoRuleFamily, UfoRule, RuntimeUfoRule>(generation, content.EquipmentProduction.Ufos,
                rule => BuildUfo(rule.Id, rule.Value)),
            BuildFamily<UfoTrajectoryRuleFamily, UfoTrajectoryRule, RuntimeUfoTrajectoryRule>(generation,
                content.MissionEvents.UfoTrajectories, static rule => new(rule.Value.GroundTimer,
                    Array.AsReadOnly(rule.Value.Waypoints.Select(static waypoint => new RuntimeTrajectoryWaypoint(
                        waypoint.Zone, waypoint.Altitude, waypoint.Speed)).ToArray()))),
            BuildFamily<AlienMissionRuleFamily, AlienMissionRule, RuntimeAlienMissionRule>(generation,
                content.MissionEvents.AlienMissions, rule => BuildAlienMission(rule.Id, rule.Value)),
            BuildFamily<AlienDeploymentRuleFamily, AlienDeploymentRule, RuntimeAlienDeploymentRule>(generation,
                content.TerrainDeployment.AlienDeployments, rule => BuildAlienDeployment(rule.Id, rule.Value)),
            IdentityFamily<AlienRaceRuleFamily, AlienRaceRule>(generation, content.TerrainDeployment.AlienRaces),
            BuildFamily<MissionScriptRuleFamily, StrategicScriptRule, RuntimeMissionScriptRule>(generation,
                content.MissionEvents.MissionScripts, rule => BuildMissionScript(rule, "missionScripts")),
            BuildFamily<AdhocScriptRuleFamily, StrategicScriptRule, RuntimeMissionScriptRule>(generation,
                content.MissionEvents.AdhocScripts, rule => BuildMissionScript(rule, "adhocScripts")),
            BuildFamily<ArcScriptRuleFamily, StrategicScriptRule, RuntimeArcScriptRule>(generation,
                content.MissionEvents.ArcScripts, rule => new(BuildScriptCriteria(rule.Value), rule.Value.Integers["maxArcs"],
                    References(researchHandles, rule.Value.Sequential), ScriptWeights(researchHandles, rule.Value.Random))),
            BuildFamily<EventScriptRuleFamily, StrategicScriptRule, RuntimeEventScriptRule>(generation,
                content.MissionEvents.EventScripts, rule => new(BuildScriptCriteria(rule.Value), rule.Value.Booleans["affectsGameProgression"],
                    References(eventHandles, rule.Value.Sequential), ScriptWeights(eventHandles, rule.Value.Random),
                    ScriptTimeline(eventHandles, rule.Value.EventWeights))),
            scriptBuild.Family,
            settings);
        cancellationToken.ThrowIfCancellationRequested();
        return new RuntimeRuleLinkResult(catalog, compatibility, Array.AsReadOnly(issues.ToArray()));

        RuntimeMissionScriptRule BuildMissionScript(TypedRule<StrategicScriptRule> owner, string family)
        {
            var value = owner.Value;
            bool? siteType = null;
            var firstMission = true;
            // Mod::loadAll derives the objective from the first ordinal ID across all months.
            // It rejects a missing first ID, but skips later missing IDs during consistency checks.
            foreach (var id in value.MissionWeights.SelectMany(entry => entry.Weights.Keys).Distinct(StringComparer.Ordinal)
                         .Order(StringComparer.Ordinal))
            {
                if (!content.MissionEvents.AlienMissions.TryGet(id, out var mission))
                {
                    if (firstMission)
                        Report(family, owner.Id, "missionWeights", id, "Referenced alien mission does not exist.", owner.LastUpdateSource);
                    firstMission = false;
                    continue;
                }
                firstMission = false;
                var isSite = mission!.Value.Integers["objective"] == (int)RuntimeMissionObjective.Site;
                if (siteType is { } previous && previous != isSite)
                    Report(family, owner.Id, "missionWeights", id,
                        "A mission script cannot mix site and non-site objectives.", owner.LastUpdateSource);
                siteType ??= isSite;
            }
            return new(BuildScriptCriteria(value), value.Strings["varName"], value.Integers["label"],
                value.Integers["targetBaseOdds"], value.Integers["maxRuns"], value.Integers["avoidRepeats"],
                value.Integers["startDelay"], value.Integers["randomDelay"], value.Booleans["useTable"], siteType ?? false,
                Array.AsReadOnly(value.Conditionals.ToArray()), Array.AsReadOnly(value.Tags.ToArray()),
                ScriptTimeline(missionHandles, value.MissionWeights), ScriptTimeline(raceHandles, value.RaceWeights),
                ScriptTimeline(regionHandles, value.RegionWeights));
        }

        RuntimeScriptCriteria BuildScriptCriteria(StrategicScriptRule value) => new(
            value.Integers["firstMonth"], value.Integers["lastMonth"], value.Integers["executionOdds"],
            value.Integers["minDifficulty"], value.Integers["maxDifficulty"],
            value.Integers["minScore"], value.Integers["maxScore"], value.Longs["minFunds"], value.Longs["maxFunds"],
            value.Strings["missionVarName"], value.Strings["missionMarkerName"], value.Integers["counterMin"], value.Integers["counterMax"],
            ScriptTriggers(researchHandles, value.Triggers["researchTriggers"]),
            ScriptTriggers(itemHandles, value.Triggers["itemTriggers"]),
            ScriptTriggers(facilityHandles, value.Triggers["facilityTriggers"]),
            ScriptTriggers(soldierHandles, value.Triggers["soldierTypeTriggers"]),
            ScriptTriggers(regionHandles, value.Triggers["xcomBaseInRegionTriggers"]),
            ScriptTriggers(countryHandles, value.Triggers["xcomBaseInCountryTriggers"]),
            ScriptTriggers(countryHandles, value.Triggers["pactCountryTriggers"]));

        RuntimeUfoRule BuildUfo(string id, UfoRule source)
        {
            var stats = UfoStats(source.Stats);
            var bonuses = source.RaceBonuses.ToDictionary(
                static pair => pair.Key, pair => UfoStats(pair.Value), StringComparer.Ordinal);
            return new RuntimeUfoRule(
                source.Size,
                // RuleUfo::getDefaultVisibility falls back to the vanilla 15*(3-size) ladder.
                source.Integers["visibility"] != 0 ? source.Integers["visibility"] : source.Size switch
                {
                    "STR_VERY_SMALL" => -30,
                    "STR_SMALL" => -15,
                    "STR_MEDIUM_UC" => 0,
                    "STR_LARGE" => 15,
                    "STR_VERY_LARGE" => 30,
                    _ => 0,
                },
                source.Integers["missionScore"],
                source.Integers["fakeWaterLandingChance"],
                source.Integers["hunterKillerPercentage"],
                source.Integers["huntMode"],
                source.Integers["huntSpeed"],
                source.Integers["huntBehavior"],
                source.Booleans["unmanned"],
                source.Booleans["instaHyper"],
                source.Booleans["noAlert"],
                source.Marker.Index,
                source.LandedMarker.Index,
                source.CrashedMarker.Index,
                stats,
                new ReadOnlyDictionary<string, RuntimeUfoStats>(bonuses),
                ScriptsFor(scriptBuild.ByOwner, "ufos", id))
            {
                Radius = source.EffectiveRadius,
                WeaponRange = source.Integers["range"],
                WeaponPower = source.Integers["power"],
                WeaponReload = source.Integers["reload"],
            };
        }

        static RuntimeUfoStats UfoStats(UfoStats source) => new(
            source.Craft.Get("speedMax"),
            source.Craft.Get("accel"),
            source.Craft.Get("damageMax"),
            source.Craft.Get("radarRange"),
            source.Craft.Get("radarChance"),
            source.Craft.Get("sightRange"),
            source.Craft.Get("shieldCapacity"),
            source.Craft.Get("shieldRecharge"),
            source.Craft.Get("shieldRechargeInGeoscape"),
            source.MissionCustomDeployment)
        {
            FuelMaximum = source.Craft.Get("fuelMax"),
            HitBonus = source.Craft.Get("hitBonus"),
            AvoidBonus = source.Craft.Get("avoidBonus"),
            AvoidBonus2 = source.Craft.Get("avoidBonus2"),
            PowerBonus = source.Craft.Get("powerBonus"),
            Armor = source.Craft.Get("armor"),
            ShieldBleedThrough = source.Craft.Get("shieldBleedThrough"),
            MaximumUnits = source.Craft.Get("soldiers"),
            MaximumVehicles = source.Craft.Get("vehicles"),
            MaximumItems = source.Craft.Get("maxItems"),
        };

        RuntimeAlienMissionRule BuildAlienMission(string id, AlienMissionRule source)
        {
            var objective = source.Integers["objective"];
            if (!Enum.IsDefined((RuntimeMissionObjective)objective))
                issues.Add(new RuntimeRuleLinkIssue("alienMissions", id, "objective", objective.ToString(CultureInfo.InvariantCulture),
                    $"Mission objective {objective} is not supported.", null));
            var operation = source.Integers["operationType"];
            if (!Enum.IsDefined((RuntimeMissionOperationType)operation))
                issues.Add(new RuntimeRuleLinkIssue("alienMissions", id, "operationType", operation.ToString(CultureInfo.InvariantCulture),
                    $"Mission operation type {operation} is not supported.", null));
            var waves = source.Waves.Select(wave => new RuntimeMissionWave(
                wave.Ufo,
                OptionalRuntime(ufoHandles, wave.Ufo),
                OptionalRuntime(deploymentHandles, wave.Ufo),
                wave.Count,
                wave.Trajectory,
                OptionalRuntime(trajectoryHandles, wave.Trajectory),
                wave.Timer,
                wave.Objective,
                wave.ObjectiveOnTheLandingSite,
                wave.ObjectiveOnXcomBase,
                wave.HunterKillerPercentage,
                wave.HuntMode,
                wave.HuntBehavior,
                wave.Escort,
                wave.InterruptPercentage)).ToArray();
            return new RuntimeAlienMissionRule(
                (RuntimeMissionObjective)objective,
                source.Integers["points"],
                source.Integers["spawnZone"],
                source.Integers["retaliationOdds"],
                source.Integers["targetBaseOdds"],
                (RuntimeMissionOperationType)operation,
                source.Integers["operationSpawnZone"],
                source.Strings["operationBaseType"],
                OptionalRuntime(deploymentHandles, source.Strings["operationBaseType"]),
                source.Strings["spawnUfo"],
                OptionalRuntime(ufoHandles, source.Strings["spawnUfo"]),
                source.Strings["siteType"],
                OptionalRuntime(deploymentHandles, source.Strings["siteType"]),
                source.Strings["interruptResearch"],
                OptionalRuntime(researchHandles, source.Strings["interruptResearch"]),
                source.Booleans["skipScoutingPhase"],
                source.Booleans["endlessInfiltration"],
                source.Booleans["multiUfoRetaliation"],
                source.Booleans["multiUfoRetaliationExtra"],
                source.Booleans["ignoreBaseDefenses"],
                source.Booleans["instaHyper"],
                source.Booleans["despawnEvenIfTargeted"],
                source.Booleans["respawnUfoAfterSiteDespawn"],
                source.Booleans["showAlienBase"],
                Array.AsReadOnly(waves),
                source.MissionWeights,
                Timeline(source.RaceWeights),
                Timeline(source.RegionWeights));
        }

        RuntimeAlienDeploymentRule BuildAlienDeployment(string id, AlienDeploymentRule source)
        {
            var evolution = source.AlienRaceEvolution
                .Select(node => ReadEvolution(id, node))
                .Where(static entry => entry is not null)
                .Select(static entry => entry!)
                .ToArray();
            return new RuntimeAlienDeploymentRule(
                source.Strings["markerName"],
                source.Integers["markerIcon"],
                source.Duration.Count > 0 ? source.Duration[0] : 0,
                source.Duration.Count > 1 ? source.Duration[1] : 0,
                source.Integers["points"],
                source.Integers["despawnPenalty"],
                source.Booleans["alienBase"],
                source.Booleans["finalDestination"],
                source.Integers["fakeUnderwaterSpawnChance"],
                source.Integers["baseDetectionRange"],
                source.Integers["baseDetectionChance"],
                source.Integers["huntMissionMaxFrequency"],
                source.Integers["genMissionFreq"],
                source.Integers["genMissionLimit"],
                source.Booleans["genMissionRaceFromAlienBase"],
                source.Booleans["huntMissionRaceFromAlienBase"],
                source.Booleans["resetAlienBaseAgeAfterUpgrade"],
                source.Booleans["resetAlienBaseAge"],
                source.Strings["upgradeRace"],
                source.Strings["baseSelfDestructCode"],
                OptionalRuntime(researchHandles, source.Strings["baseSelfDestructCode"]),
                source.Strings["unlockedResearchOnDespawn"],
                OptionalRuntime(researchHandles, source.Strings["unlockedResearchOnDespawn"]),
                source.Strings["counterDespawn"],
                source.Strings["counterFailure"],
                source.Strings["counterAll"],
                source.Strings["decreaseCounterDespawn"],
                source.Strings["decreaseCounterFailure"],
                source.Strings["decreaseCounterAll"],
                Weights(id, "genMission", source.GenMission),
                Timeline(source.HuntMissionWeights),
                Timeline(source.AlienBaseUpgrades),
                Array.AsReadOnly(evolution),
                Weights(id, "despawnEvents", source.DespawnEvents))
            { StartingCondition = source.Strings["startingCondition"] };
        }

        RuntimeAlienRaceEvolution? ReadEvolution(string id, YamlNode node)
        {
            if (node is not YamlSequenceNode sequence || sequence.Items.Count < 3)
            {
                issues.Add(new RuntimeRuleLinkIssue("alienDeployments", id, "alienRaceEvolution", string.Empty,
                    "Alien race evolution requires [month, from race, to race].", null));
                return null;
            }
            return new RuntimeAlienRaceEvolution(
                YamlValueReader.ReadInt32(sequence.Items[0]),
                YamlValueReader.ReadString(sequence.Items[1]),
                YamlValueReader.ReadString(sequence.Items[2]));
        }

        IReadOnlyDictionary<string, ulong> Weights(string id, string property, YamlNode? node)
        {
            if (node is null) return EmptyWeights;
            if (node is not YamlMappingNode mapping)
            {
                issues.Add(new RuntimeRuleLinkIssue("alienDeployments", id, property, string.Empty,
                    $"{property} must be a mapping.", null));
                return EmptyWeights;
            }
            var weights = new Dictionary<string, ulong>(StringComparer.Ordinal);
            foreach (var entry in mapping.Entries)
                weights[entry.ScalarKey ?? string.Empty] = YamlValueReader.ReadUInt64(entry.Value);
            return new ReadOnlyDictionary<string, ulong>(weights);
        }

        RuntimeManufactureMaterial RequiredMaterial(string id, int quantity)
        {
            var item = OptionalRuntime(itemHandles, id);
            return new(id, quantity, item, item is null ? OptionalRuntime(craftHandles, id) : null);
        }

        RuntimeManufactureMaterial ProducedMaterial(string id, int quantity, bool craft) => craft
            ? new(id, quantity, null, OptionalRuntime(craftHandles, id))
            : new(id, quantity, OptionalRuntime(itemHandles, id), null);

        RuntimeCampaignSettings BuildCampaignSettings(CampaignStartSettings source)
        {
            var templates = source.StartingBases
                .OrderBy(static pair => pair.Key)
                .Select(pair => StartingBase(pair.Key, pair.Value))
                .ToArray();
            return new RuntimeCampaignSettings(
                source.StartingTime,
                source.StartingDifficulty,
                source.InitialFunding,
                source.CostHireEngineer,
                source.CostHireScientist,
                source.CostEngineer,
                source.CostScientist,
                source.PersonnelTransferTime,
                source.GlobalTransferCostMultiplier,
                source.GlobalTransferCostDivisor,
                OptionalRuntime(researchHandles, source.PsiUnlockResearch),
                OptionalRuntime(researchHandles, source.FakeUnderwaterBaseUnlockResearch),
                OptionalRuntime(researchHandles, source.NewBaseUnlockResearch),
                OptionalRuntime(researchHandles, source.HireScientistsUnlockResearch),
                OptionalRuntime(researchHandles, source.HireEngineersUnlockResearch),
                OptionalRuntime(facilityHandles, source.DestroyedFacility),
                Array.AsReadOnly(templates))
            {
                BuyPriceCoefficients = source.BuyPriceCoefficients,
                SellPriceCoefficients = source.SellPriceCoefficients,
                HireScientistsBaseFunctions = source.HireScientistsRequiredBaseFunctions,
                HireEngineersBaseFunctions = source.HireEngineersRequiredBaseFunctions,
                HireByCountryOdds = source.HireByCountryOdds,
                HireByRegionOdds = source.HireByRegionOdds,
                PerformanceBonusFactor = source.PerformanceBonusFactor,
                FixedUserOptions = source.FixedUserOptions,
            };
        }

        RuntimeStartingBaseTemplate StartingBase(StartingBaseVariant variant, YamlMappingNode node)
        {
            var soldierRules = content.PersonnelTactical.Soldiers.Rules;
            var defaultSoldier = soldierRules.Count == 0 ? string.Empty : soldierRules[0].Id;
            var facilities = Sequence(node, "facilities").Select(entry => new RuntimeStartingFacility(
                RequiredId(facilityHandles, Type(entry), "startingBase", variant.ToString(), "facilities"),
                Integer(entry, "x"), Integer(entry, "y"), Integer(entry, "buildTime"))
            {
                Ammo = Integer(entry, "ammo"),
                AmmoMissingReported = Flag(entry, "ammoMissingReported"),
                Disabled = Flag(entry, "disabled"),
                HadPreviousFacility = Flag(entry, "hadPreviousFacility"),
            }).ToArray();
            var crafts = Sequence(node, "crafts").Select(entry => new RuntimeStartingCraft(
                RequiredId(craftHandles, Type(entry), "startingBase", variant.ToString(), "crafts"),
                Integer(entry, "id"))
            { Template = RuntimeCraftTemplateLoader.Read(entry as YamlMappingNode) }).ToArray();
            var soldiers = Sequence(node, "soldiers").Select(entry => new RuntimeStartingSoldier(
                RequiredId(soldierHandles, Type(entry, defaultSoldier), "startingBase", variant.ToString(),
                    "soldiers"), Integer(entry, "id"))
            {
                Template = RuntimeSoldierTemplateLoader.Read(entry as YamlMappingNode),
                CraftType = entry is YamlMappingNode soldierMap && soldierMap.TryGet("craft", out var craftNode) ? Type(craftNode!) : "",
                CraftId = entry is YamlMappingNode soldierMap2 && soldierMap2.TryGet("craft", out var craftNode2) ? Integer(craftNode2!, "id") : 0,
            }).ToArray();
            var items = Mapping(node, "items").Select(entry => new RuntimeStartingItem(
                RequiredId(itemHandles, entry.ScalarKey ?? string.Empty, "startingBase", variant.ToString(), "items"),
                ScalarInteger(entry.Value))).ToArray();
            var randomCount = 0;
            var random = Array.Empty<RuntimeStartingSoldierBatch>();
            if (node.TryGet("randomSoldiers", out var randomNode))
            {
                if (randomNode is YamlMappingNode randomMap)
                {
                    random = randomMap.Entries.OrderBy(entry => entry.ScalarKey, StringComparer.Ordinal).Select(entry => new RuntimeStartingSoldierBatch(
                        RequiredId(soldierHandles, entry.ScalarKey ?? string.Empty, "startingBase", variant.ToString(),
                            "randomSoldiers"), ScalarInteger(entry.Value))).ToArray();
                }
                else
                {
                    randomCount = ScalarInteger(randomNode!);
                }
            }

            return new RuntimeStartingBaseTemplate(
                variant,
                Array.AsReadOnly(facilities),
                Array.AsReadOnly(crafts),
                Array.AsReadOnly(soldiers),
                Array.AsReadOnly(items),
                randomCount,
                Array.AsReadOnly(random),
                Integer(node, "scientists"),
                Integer(node, "engineers"))
            { AssignRandomSoldiers = node.TryGet("randomSoldiers", out _) };
        }

        RuleHandle<TFamily> RequiredRule<TFamily, TRule>(
            Dictionary<string, RuleHandle<TFamily>> handles,
            string id,
            string family,
            TypedRule<TRule> owner,
            string property)
            where TRule : notnull => RequiredId(handles, id, family, owner.Id, property, owner.LastUpdateSource);

        RuleHandle<TFamily> RequiredId<TFamily>(
            Dictionary<string, RuleHandle<TFamily>> handles,
            string id,
            string family,
            string ownerId,
            string property,
            RuleOperationSource? source = null)
        {
            if (handles.TryGetValue(id, out var handle)) return handle;
            Report(family, ownerId, property, id, "Referenced runtime rule does not exist.", source);
            return default;
        }

        RuleHandleList<TFamily> RequiredMany<TFamily, TRule>(
            Dictionary<string, RuleHandle<TFamily>> handles,
            IEnumerable<string> ids,
            string family,
            TypedRule<TRule> owner,
            string property)
            where TRule : notnull => new(ids.Select(id => RequiredRule(handles, id, family, owner, property)));

        RuleHandle<TFamily>? OptionalRequired<TFamily, TRule>(
            Dictionary<string, RuleHandle<TFamily>> handles,
            string id,
            string family,
            TypedRule<TRule> owner,
            string property)
            where TRule : notnull => id.Length == 0 ? null : RequiredRule(handles, id, family, owner, property);

        void Report(
            string family,
            string ownerId,
            string property,
            string id,
            string message,
            RuleOperationSource? source)
        {
            issues.Add(new RuntimeRuleLinkIssue(family, ownerId, property, id, message, source));
            diagnostics.Report(new DiagnosticEvent(
                ModDiagnosticCodes.InvalidRuntimeRuleLink,
                DiagnosticSeverity.Error,
                $"Runtime {family} rule '{ownerId}' property '{property}' ('{id}'): {message}",
                source?.Span,
                new DiagnosticContext(source?.LayerId, source?.ModId, family, ownerId, id)));
        }
    }

    private static Dictionary<string, RuleHandle<TFamily>> Handles<TFamily, TRule>(
        ContentGenerationId generation,
        TypedRuleSection<TRule> source)
        where TRule : notnull => source.Rules.Select((rule, index) => new
        {
            rule.Id,
            Handle = new RuleHandle<TFamily>(generation, index),
        }).ToDictionary(static pair => pair.Id, static pair => pair.Handle, StringComparer.Ordinal);

    private static readonly IReadOnlyDictionary<string, ulong> EmptyWeights =
        new ReadOnlyDictionary<string, ulong>(new Dictionary<string, ulong>(StringComparer.Ordinal));

    private static ReadOnlyCollection<RuntimeScriptTrigger<TFamily>> ScriptTriggers<TFamily>(
        Dictionary<string, RuleHandle<TFamily>> handles, IReadOnlyDictionary<string, bool> source) =>
        Array.AsReadOnly(source.OrderBy(pair => pair.Key, StringComparer.Ordinal).Select(pair =>
            new RuntimeScriptTrigger<TFamily>(new(pair.Key, OptionalRuntime(handles, pair.Key)), pair.Value)).ToArray());

    private static ReadOnlyCollection<RuntimeScriptWeight<TFamily>> ScriptWeights<TFamily>(
        Dictionary<string, RuleHandle<TFamily>> handles, IReadOnlyDictionary<string, ulong> source) =>
        Array.AsReadOnly(source.Where(pair => pair.Value != 0).OrderBy(pair => pair.Key, StringComparer.Ordinal).Select(pair =>
            new RuntimeScriptWeight<TFamily>(new(pair.Key, OptionalRuntime(handles, pair.Key)), pair.Value)).ToArray());

    private static ReadOnlyCollection<RuntimeScriptTimeline<TFamily>> ScriptTimeline<TFamily>(
        Dictionary<string, RuleHandle<TFamily>> handles, IReadOnlyList<WeightedTimelineEntry> source) =>
        Array.AsReadOnly(source.Select(entry => new RuntimeScriptTimeline<TFamily>(entry.Month,
            ScriptWeights(handles, entry.Weights))).ToArray());

    private static ReadOnlyCollection<RuntimeWeightedTimeline> Timeline(
        IEnumerable<WeightedTimelineEntry> source) =>
        Array.AsReadOnly(source.Select(static entry => new RuntimeWeightedTimeline(entry.Month, entry.Weights)).ToArray());

    private static ReadOnlyCollection<RuntimeWeightedTimeline> Timeline(
        IEnumerable<KeyValuePair<ulong, IReadOnlyDictionary<string, ulong>>> source) =>
        Array.AsReadOnly(source.Select(static entry => new RuntimeWeightedTimeline(entry.Key, entry.Value)).ToArray());

    private static RuntimeRuleFamily<TFamily, TProjection> BuildFamily<TFamily, TRule, TProjection>(
        ContentGenerationId generation,
        TypedRuleSection<TRule> source,
        Func<TypedRule<TRule>, TProjection> project)
        where TRule : notnull
        where TProjection : notnull => new(generation, source.Rules.Select(rule =>
            new RuntimeRule<TProjection>(rule.Id, project(rule), rule.CreationSource, rule.LastUpdateSource)));

    private static RuntimeRuleFamily<TFamily, RuntimeIdentityRule> IdentityFamily<TFamily, TRule>(
        ContentGenerationId generation,
        TypedRuleSection<TRule> source)
        where TRule : notnull => BuildFamily<TFamily, TRule, RuntimeIdentityRule>(
            generation, source, static rule => new RuntimeIdentityRule(rule.Id));

    private static RuleHandle<TFamily>? OptionalRuntime<TFamily>(
        Dictionary<string, RuleHandle<TFamily>> handles,
        string id) => id.Length != 0 && handles.TryGetValue(id, out var handle) ? handle : null;

    private static RuntimeRuleReferenceList<TFamily> References<TFamily>(
        Dictionary<string, RuleHandle<TFamily>> handles,
        IEnumerable<string> ids) => new(ids.Select(id =>
            new RuntimeRuleReference<TFamily>(id, OptionalRuntime(handles, id))));

    private static RuleHandle<ItemRuleFamily>? OptionalItem(
        Dictionary<string, RuleHandle<ItemRuleFamily>> handles,
        string id) => id.Length != 0 && id != "STR_NONE" && handles.TryGetValue(id, out var handle) ? handle : null;

    private static Dictionary<string, string> ItemCategoryReplacements(Phase3ContentCatalog content)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        // C++ applies each std::map replacement once in UTF-8 byte order. Resolve
        // the same forward-only chains in reverse order, avoiding a scan of every
        // replacement for every item category. Backward links and cycles stop.
        var byteOrder = Comparer<byte[]>.Create(static (left, right) => left.AsSpan().SequenceCompareTo(right));
        foreach (var rule in content.EquipmentProduction.ItemCategories.Rules
            .Where(static rule => rule.Value.ReplaceBy.Length != 0)
            .Select(static rule => (rule.Id, rule.Value.ReplaceBy, Key: Encoding.UTF8.GetBytes(rule.Id)))
            .OrderByDescending(static rule => rule.Key, byteOrder))
            result[rule.Id] = result.GetValueOrDefault(rule.ReplaceBy, rule.ReplaceBy);
        return result;
    }

    private static RuntimeIndexedResourceReference Resource(
        RuleIndexReference reference,
        string defaultSet,
        ResourceKind kind,
        ResolvedResourceCatalog resources)
    {
        var set = defaultSet;
        if (resources.TryResolveIndex(kind, set, reference.ModId, reference.Index, out var resolved))
        {
            return new RuntimeIndexedResourceReference(
                set,
                resolved!.RuntimeIndex,
                kind,
                resolved.Handle);
        }
        return new RuntimeIndexedResourceReference(
            set,
            reference.Index,
            kind,
            null);
    }

    private static ScriptBuild BuildScripts(
        ContentGenerationId generation,
        IReadOnlyList<ContentScriptArtifact> scripts,
        CancellationToken cancellationToken)
    {
        var owners = new Dictionary<(string Section, string Id), List<RuleHandle<RuntimeScriptFamily>>>();
        var rules = new RuntimeRule<RuntimeScriptRule>[scripts.Count];
        for (var index = 0; index < scripts.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var script = scripts[index];
            var handle = new RuleHandle<RuntimeScriptFamily>(generation, index);
            var key = (script.OwnerSection, script.OwnerId);
            if (!owners.TryGetValue(key, out var list)) owners.Add(key, list = []);
            list.Add(handle);
            var position = new SourcePosition(Math.Max(1, script.SourceLine), 1, 0);
            var source = new RuleOperationSource(
                string.Empty,
                string.Empty,
                script.SourcePath,
                new SourceSpan(script.SourcePath, position, position));
            var id = $"{script.Scope}/{script.OwnerSection}/{script.OwnerId}/{script.ParserName}/{index}";
            rules[index] = new RuntimeRule<RuntimeScriptRule>(id, new RuntimeScriptRule(script), source, source);
        }

        return new ScriptBuild(new RuntimeRuleFamily<RuntimeScriptFamily, RuntimeScriptRule>(generation, rules), owners);
    }

    private static RuleHandleList<RuntimeScriptFamily> ScriptsFor(
        Dictionary<(string Section, string Id), List<RuleHandle<RuntimeScriptFamily>>> owners,
        string section,
        string id) => owners.TryGetValue((section, id), out var handles) ? new(handles) : new([]);

    private static IEnumerable<YamlNode> Sequence(YamlMappingNode node, string key) =>
        node.TryGet(key, out var value) && value is YamlSequenceNode sequence ? sequence.Items : [];

    private static IEnumerable<YamlMappingEntry> Mapping(YamlMappingNode node, string key) =>
        node.TryGet(key, out var value) && value is YamlMappingNode mapping ? mapping.Entries : [];

    private static string Type(YamlNode node, string defaultValue = "")
    {
        if (node is YamlMappingNode mapping && mapping.TryGet("type", out var type))
            return YamlValueReader.ReadString(type!);
        return node is YamlScalarNode ? YamlValueReader.ReadString(node) : defaultValue;
    }

    private static bool Flag(YamlNode node, string key) => node is YamlMappingNode mapping &&
        mapping.TryGet(key, out var value) && YamlValueReader.ReadBoolean(value!);

    private static int Integer(YamlNode node, string key) => node is YamlMappingNode mapping &&
        mapping.TryGet(key, out var value) ? YamlValueReader.ReadInt32(value!) : 0;

    private static int ScalarInteger(YamlNode node) => YamlValueReader.ReadInt32(node);

    private static IEnumerable<RuntimeRuleCompatibilityEntry> CompatibilityEntries(Phase3ContentCatalog content)
    {
        return Entries("countries", content.CampaignStart.Countries)
            .Concat(Entries("regions", content.CampaignStart.Regions))
            .Concat(Entries("facilities", content.CampaignStart.Facilities))
            .Concat(Entries("crafts", content.EquipmentProduction.Crafts))
            .Concat(Entries("items", content.Items.Items))
            .Concat(Entries("armors", content.PersonnelTactical.Armors))
            .Concat(Entries("soldiers", content.PersonnelTactical.Soldiers));

        static IEnumerable<RuntimeRuleCompatibilityEntry> Entries<TRule>(
            string family,
            TypedRuleSection<TRule> section)
            where TRule : notnull => section.Rules
            .Where(static rule => rule.DeferredProperties.Count != 0)
            .Select(rule => new RuntimeRuleCompatibilityEntry(family, rule.Id, rule.DeferredProperties));
    }

    private sealed record ScriptBuild(
        RuntimeRuleFamily<RuntimeScriptFamily, RuntimeScriptRule> Family,
        Dictionary<(string Section, string Id), List<RuleHandle<RuntimeScriptFamily>>> ByOwner);
}

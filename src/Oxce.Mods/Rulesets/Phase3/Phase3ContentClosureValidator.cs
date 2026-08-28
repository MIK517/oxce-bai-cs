using Oxce.Core.Diagnostics;
using Oxce.Mods.Rulesets.CampaignStart;
using Oxce.Mods.Rulesets.EquipmentProduction;
using Oxce.Mods.Rulesets.Items;
using Oxce.Mods.Rulesets.MissionEvents;
using Oxce.Mods.Rulesets.PersonnelTactical;
using Oxce.Mods.Rulesets.TerrainDeployment;

namespace Oxce.Mods.Rulesets.Phase3;

internal static class Phase3ContentClosureValidator
{
    public static Phase3ContentClosureValidation Validate(
        Phase3ContentCatalogView content,
        IDiagnosticSink diagnostics)
    {
        var issues = new List<Phase3ContentClosureIssue>();

        foreach (var country in content.Campaign.Countries.Rules)
        {
            Optional(country, "signedPactEvent", country.Value.SignedPactEvent, content.Missions.Events);
            Optional(country, "rejoinedXcomEvent", country.Value.RejoinedXcomEvent, content.Missions.Events);
        }
        foreach (var region in content.Campaign.Regions.Rules)
        {
            DeferEach(region, "missionWeights", region.Value.MissionWeights.Keys, content.Missions.AlienMissions);
            DeferOptional(region, "missionRegion", region.Value.MissionRegion, content.Campaign.Regions);
        }
        foreach (var facility in content.Campaign.Facilities.Rules)
        {
            DeferEach(facility, "requires", facility.Value.Requires, content.Equipment.Research);
            Optional(facility, "ammoItem", facility.Value.AmmoItem, content.Items.Items);
            DeferEach(facility, "buildCostItems", facility.Value.BuildCostItems.Keys, content.Items.Items);
        }

        foreach (var item in content.Items.Items.Rules)
        {
            Each(item, "requires", item.Value.Requirements, content.Equipment.Research);
            Each(item, "requiresBuy", item.Value.BuyRequirements, content.Equipment.Research);
            DeferEach(item, "categories", item.Value.Categories, content.Equipment.ItemCategories);
            Each(item, "supportedInventorySections", item.Value.SupportedInventorySections,
                content.Personnel.Inventories);
            Optional(item, "defaultInventorySlot", item.Value.Values.NullableName("defaultInventorySlot"),
                content.Personnel.Inventories);
            DeferOptional(item, "zombieUnit", item.Value.Values.NullableName("zombieUnit"), content.Personnel.Units);
            Optional(item, "spawnUnit", item.Value.Values.NullableName("spawnUnit"), content.Personnel.Units);
            DeferOptional(item, "requiresBuyCountry", item.Value.Values.GetString("requiresBuyCountry"),
                content.Campaign.Countries);
            DeferEach(item, "zombieUnitByType", item.Value.ZombieUnitsByType.Values, content.Personnel.Units);
            DeferEach(item, "zombieUnitByMaleArmor", item.Value.ZombieUnitsByMaleArmor.Keys,
                content.Personnel.Armors);
            DeferEach(item, "zombieUnitByMaleArmor", item.Value.ZombieUnitsByMaleArmor.Values,
                content.Personnel.Units);
            DeferEach(item, "zombieUnitByFemaleArmor", item.Value.ZombieUnitsByFemaleArmor.Keys,
                content.Personnel.Armors);
            DeferEach(item, "zombieUnitByFemaleArmor", item.Value.ZombieUnitsByFemaleArmor.Values,
                content.Personnel.Units);
        }

        foreach (var craft in content.Equipment.Crafts.Rules)
        {
            DeferEach(craft, "requires", craft.Value.Requirements, content.Equipment.Research);
            Each(craft, "requiredPilotBonuses", craft.Value.RequiredPilotBonuses, content.Personnel.Bonuses);
        }
        foreach (var ufo in content.Equipment.Ufos.Rules)
        {
            DeferOptional(ufo, "craftCustomDeployment", ufo.Value.Stats.CraftCustomDeployment,
                content.Terrain.AlienDeployments);
            DeferOptional(ufo, "missionCustomDeployment", ufo.Value.Stats.MissionCustomDeployment,
                content.Terrain.AlienDeployments);
        }
        foreach (var research in content.Equipment.Research.Rules)
        {
            DeferOptional(research, "spawnedItem", research.Value.SpawnedItem, content.Items.Items);
            DeferEach(research, "spawnedItemList", research.Value.SpawnedItemList, content.Items.Items);
            DeferOptional(research, "spawnedEvent", research.Value.SpawnedEvent, content.Missions.Events);
            DeferEach(research, "events", research.Value.Events.Keys, content.Missions.Events);
        }
        foreach (var manufacture in content.Equipment.Manufacture.Rules)
        {
            DeferOptional(manufacture, "spawnedPersonType", manufacture.Value.SpawnedPersonType,
                content.Personnel.Soldiers);
            DeferEach(manufacture, "events", manufacture.Value.Events.Keys, content.Missions.Events);
        }

        foreach (var soldier in content.Personnel.Soldiers.Rules)
            DeferEach(soldier, "requires", soldier.Value.Requirements, content.Equipment.Research);
        foreach (var transformation in content.Personnel.Transformations.Rules)
            DeferEach(transformation, "events", transformation.Value.Events.Keys, content.Missions.Events);

        foreach (var terrain in content.Terrain.Terrains.Rules)
            DeferEach(terrain, "civilianTypes", terrain.Value.CivilianTypes, content.Personnel.Units);
        foreach (var race in content.Terrain.AlienRaces.Rules)
        {
            DeferOptional(race, "baseCustomDeploy", race.Value.BaseCustomDeploy, content.Terrain.AlienDeployments);
            DeferOptional(race, "baseCustomMission", race.Value.BaseCustomMission, content.Missions.AlienMissions);
            DeferEach(race, "members", race.Value.Members, content.Personnel.Units);
            foreach (var members in race.Value.RandomMembers)
                DeferEach(race, "randomMembers", members, content.Personnel.Units);
        }
        foreach (var condition in content.Terrain.StartingConditions.Rules)
        {
            Collection(condition, "allowedArmors", content.Personnel.Armors);
            Collection(condition, "forbiddenArmors", content.Personnel.Armors);
            Collection(condition, "allowedVehicles", content.Items.Items);
            Collection(condition, "forbiddenVehicles", content.Items.Items);
            Collection(condition, "allowedItems", content.Items.Items);
            Collection(condition, "forbiddenItems", content.Items.Items);
            Collection(condition, "allowedItemCategories", content.Equipment.ItemCategories);
            Collection(condition, "forbiddenItemCategories", content.Equipment.ItemCategories);
            Collection(condition, "allowedCraft", content.Equipment.Crafts);
            Collection(condition, "forbiddenCraft", content.Equipment.Crafts);
            Collection(condition, "allowedSoldierTypes", content.Personnel.Soldiers);
            Collection(condition, "forbiddenSoldierTypes", content.Personnel.Soldiers);

            void Collection<TRule>(TypedRule<StartingConditionRule> owner,
                string key, TypedRuleSection<TRule> target) where TRule : notnull =>
                DeferEach(owner, key, owner.Value.NameCollections[key], target);
        }
        foreach (var deployment in content.Terrain.AlienDeployments.Rules)
        {
            DeferEach(deployment, "civiliansByType", deployment.Value.CiviliansByType.Keys, content.Personnel.Units);
            DeferOptional(deployment, "customUfo", deployment.Value.Strings["customUfo"], content.Equipment.Ufos);
            DeferOptional(deployment, "nextStage", deployment.Value.Strings["nextStage"],
                content.Terrain.AlienDeployments);
            DeferOptional(deployment, "upgradeRace", deployment.Value.Strings["upgradeRace"], content.Terrain.AlienRaces);
            DeferOptional(deployment, "unlockedResearch", deployment.Value.Strings["unlockedResearch"],
                content.Equipment.Research);
            DeferOptional(deployment, "unlockedResearchOnFailure",
                deployment.Value.Strings["unlockedResearchOnFailure"], content.Equipment.Research);
            DeferOptional(deployment, "unlockedResearchOnDespawn",
                deployment.Value.Strings["unlockedResearchOnDespawn"], content.Equipment.Research);
            foreach (var entry in deployment.Value.HuntMissionWeights)
                DeferEach(deployment, "huntMissionWeights", entry.Value.Keys, content.Missions.AlienMissions);
            foreach (var entry in deployment.Value.AlienBaseUpgrades)
                DeferEach(deployment, "alienBaseUpgrades", entry.Value.Keys, content.Terrain.AlienDeployments);
        }

        return new Phase3ContentClosureValidation(Array.AsReadOnly(issues.ToArray()));

        void Optional<TOwner, TTarget>(TypedRule<TOwner> owner, string property, string? id,
            TypedRuleSection<TTarget> target) where TOwner : notnull where TTarget : notnull
        {
            if (!string.IsNullOrEmpty(id) && id != "STR_NULL" && !target.TryGet(id, out _))
                Missing(owner, property, id, target.Definition.Name);
        }

        void Each<TOwner, TTarget>(TypedRule<TOwner> owner, string property, IEnumerable<string> ids,
            TypedRuleSection<TTarget> target) where TOwner : notnull where TTarget : notnull
        {
            foreach (var id in ids)
                if (id.Length != 0 && id != "STR_NULL" && !target.TryGet(id, out _))
                    Missing(owner, property, id, target.Definition.Name);
        }

        void DeferOptional<TOwner, TTarget>(TypedRule<TOwner> owner, string property, string? id,
            TypedRuleSection<TTarget> target) where TOwner : notnull where TTarget : notnull
        {
            if (!string.IsNullOrEmpty(id) && id != "STR_NULL" && !target.TryGet(id, out _))
                Deferred(owner, property, id, target.Definition.Name);
        }

        void DeferEach<TOwner, TTarget>(TypedRule<TOwner> owner, string property, IEnumerable<string> ids,
            TypedRuleSection<TTarget> target) where TOwner : notnull where TTarget : notnull
        {
            foreach (var id in ids)
                if (id.Length != 0 && id != "STR_NULL" && !target.TryGet(id, out _))
                    Deferred(owner, property, id, target.Definition.Name);
        }

        void Deferred<TOwner>(TypedRule<TOwner> owner, string property, string id, string target)
            where TOwner : notnull => RuleReferenceDiagnostics.ReportDeferred(
                diagnostics, owner.LastUpdateSource, target, owner.Id, property, id);

        void Missing<TOwner>(TypedRule<TOwner> owner, string property, string id, string target)
            where TOwner : notnull
        {
            issues.Add(new Phase3ContentClosureIssue(owner.Id, property, id, target));
            diagnostics.Report(new DiagnosticEvent(
                ModDiagnosticCodes.MissingRuleReference,
                DiagnosticSeverity.Error,
                $"Rule '{owner.Id}' property '{property}' references missing {target} rule '{id}'.",
                owner.LastUpdateSource.Span,
                new DiagnosticContext(owner.LastUpdateSource.LayerId, owner.LastUpdateSource.ModId,
                    owner.Value.GetType().Name, owner.Id, id)));
        }
    }
}

internal sealed record Phase3ContentCatalogView(
    CampaignStartRuleCatalog Campaign,
    ItemRuleCatalog Items,
    EquipmentProductionRuleCatalog Equipment,
    PersonnelTacticalRuleCatalog Personnel,
    TerrainDeploymentRuleCatalog Terrain,
    MissionEventRuleCatalog Missions);

public sealed record Phase3ContentClosureIssue(string RuleId, string Property, string RelatedId, string TargetSection);

public sealed record Phase3ContentClosureValidation(IReadOnlyList<Phase3ContentClosureIssue> Issues)
{
    public bool IsValid => Issues.Count == 0;
}

using System.Collections.ObjectModel;
using Oxce.Formats.Yaml;
using Oxce.Mods.Rulesets.Presentation;

namespace Oxce.Mods.Rulesets.CampaignStart;

internal sealed class CountryRuleLoader(string sectionName) : TypedRuleFamilyLoader<CountryBuilder, CountryRule>(GetSection(sectionName))
{
    protected override CountryBuilder Create(string id) => new(id);

    protected override void Apply(CountryBuilder builder, RulePropertyReader reader)
    {
        reader.ApplyRefNode(parent => Apply(builder, parent));
        builder.SignedPactEvent = reader.ReadString("signedPactEvent", builder.SignedPactEvent);
        builder.RejoinedXcomEvent = reader.ReadString("rejoinedXcomEvent", builder.RejoinedXcomEvent);
        builder.FundingBase = reader.ReadInt32("fundingBase", builder.FundingBase);
        builder.FundingCap = reader.ReadInt32("fundingCap", builder.FundingCap);
        if (reader.TryGet("labelLon", out var longitude))
            builder.LabelLongitude = CampaignStartYaml.DegreesToRadians(YamlValueReader.ReadDouble(longitude!));
        if (reader.TryGet("labelLat", out var latitude))
            builder.LabelLatitude = CampaignStartYaml.DegreesToRadians(YamlValueReader.ReadDouble(latitude!));
        builder.LabelColor = reader.ReadInt32("labelColor", builder.LabelColor);
        builder.ZoomLevel = reader.ReadInt32("zoomLevel", builder.ZoomLevel);
        if (reader.TryGet("areas", out var areas))
            builder.Areas.AddRange(YamlValueReader.ReadSequence(areas!, CampaignStartYaml.ReadArea));
        ApplyNames(reader, "provideBaseFunc", builder.ProvidedBaseFunctions, unique: true);
        ApplyNames(reader, "forbiddenBaseFunc", builder.ForbiddenBaseFunctions, unique: true);
        reader.Defer("newMonthCountry", "compatible country script compilation belongs to Phase 4");
        reader.DeferRemaining("dynamic country script values require Phase 4 registration");
    }

    protected override CountryRule Freeze(CountryBuilder builder) => new(
        builder.SignedPactEvent,
        builder.RejoinedXcomEvent,
        builder.FundingBase,
        builder.FundingCap,
        builder.LabelLongitude,
        builder.LabelLatitude,
        builder.LabelColor,
        builder.ZoomLevel,
        builder.Areas.AsReadOnly(),
        builder.ProvidedBaseFunctions.AsReadOnly(),
        builder.ForbiddenBaseFunctions.AsReadOnly());

    private static RuleSectionDefinition GetSection(string name) =>
        RuleSectionRegistry.TryGetNamed(name, out var section) ? section! : throw new InvalidOperationException();

    internal static void ApplyNames(RulePropertyReader reader, string key, List<string> destination, bool unique)
    {
        if (reader.TryGet(key, out var node)) CampaignStartYaml.ApplyEditableNames(destination, node!, unique);
    }
}

internal sealed class RegionRuleLoader : TypedRuleFamilyLoader<RegionBuilder, RegionRule>
{
    public RegionRuleLoader() : base(GetSection()) { }
    protected override RegionBuilder Create(string id) => new(id);

    protected override void Apply(RegionBuilder builder, RulePropertyReader reader)
    {
        reader.ApplyRefNode(parent => Apply(builder, parent));
        builder.BaseCost = reader.ReadInt32("cost", builder.BaseCost);
        if (reader.ReadBoolean("deleteOldAreas", false)) builder.Areas.Clear();
        if (reader.TryGet("areas", out var areas))
            builder.Areas.AddRange(YamlValueReader.ReadSequence(areas!, CampaignStartYaml.ReadArea));
        if (reader.TryGet("missionZones", out var zones))
        {
            builder.MissionZones = YamlValueReader.ReadSequence(zones!, zone => new MissionZone(
                Array.AsReadOnly(YamlValueReader.ReadSequence(zone, CampaignStartYaml.ReadMissionArea)))).ToList();
        }
        if (reader.TryGet("missionWeights", out var weights))
        {
            if (weights is not YamlMappingNode mapping)
                throw new YamlFormatException("Region missionWeights must be a mapping.", weights!.Span);
            foreach (var entry in mapping.Entries)
            {
                var id = YamlValueReader.ReadString(entry.Key);
                var weight = YamlValueReader.ReadUInt64(entry.Value);
                if (weight == 0) builder.MissionWeights.Remove(id); else builder.MissionWeights[id] = weight;
            }
        }
        if (reader.TryGet("regionWeight", out var regionWeight))
            builder.RegionWeight = YamlValueReader.ReadUInt64(regionWeight!);
        builder.MissionRegion = reader.ReadString("missionRegion", builder.MissionRegion);
        CountryRuleLoader.ApplyNames(reader, "provideBaseFunc", builder.ProvidedBaseFunctions, unique: true);
        CountryRuleLoader.ApplyNames(reader, "forbiddenBaseFunc", builder.ForbiddenBaseFunctions, unique: true);
    }

    protected override RegionRule Freeze(RegionBuilder builder) => new(
        builder.BaseCost,
        builder.Areas.AsReadOnly(),
        builder.MissionZones.AsReadOnly(),
        new ReadOnlyDictionary<string, ulong>(builder.MissionWeights),
        builder.RegionWeight,
        builder.MissionRegion,
        builder.ProvidedBaseFunctions.AsReadOnly(),
        builder.ForbiddenBaseFunctions.AsReadOnly());

    private static RuleSectionDefinition GetSection() => RuleSectionRegistry.TryGetNamed("regions", out var section)
        ? section! : throw new InvalidOperationException();
}

internal sealed class BaseFacilityRuleLoader : TypedRuleFamilyLoader<BaseFacilityBuilder, BaseFacilityRule>
{
    public BaseFacilityRuleLoader() : base(GetSection()) { }
    protected override BaseFacilityBuilder Create(string id) => throw new NotSupportedException();
    protected override BaseFacilityBuilder Create(UnresolvedRule rule) =>
        new(rule.Id, checked((rule.CreationOrdinal + 1) * 100));

    protected override void Apply(BaseFacilityBuilder builder, RulePropertyReader reader)
    {
        reader.ApplyRefNode(parent => Apply(builder, parent));
        builder.UfopediaType = reader.ReadString("ufopediaType", builder.UfopediaType);
        ApplyNames(reader, "requires", builder.Requires, unique: false);
        ApplyNames(reader, "requiresBaseFunc", builder.RequiredBaseFunctions, unique: true);
        ApplyNames(reader, "provideBaseFunc", builder.ProvidedBaseFunctions, unique: true);
        ApplyNames(reader, "forbiddenBaseFunc", builder.ForbiddenBaseFunctions, unique: true);
        ApplyIndex(reader, "spriteShape", value => builder.SpriteShape = value);
        ApplyIndex(reader, "spriteFacility", value => builder.SpriteFacility = value);
        builder.ConnectorsDisabled = reader.ReadBoolean("connectorsDisabled", builder.ConnectorsDisabled);
        builder.FakeUnderwater = reader.ReadInt32("fakeUnderwater", builder.FakeUnderwater);
        builder.MissileAttraction = reader.ReadInt32("missileAttraction", builder.MissileAttraction);
        builder.Lift = reader.ReadBoolean("lift", builder.Lift);
        builder.HyperWave = reader.ReadBoolean("hyper", builder.HyperWave);
        builder.MindShield = reader.ReadBoolean("mind", builder.MindShield);
        builder.GravShield = reader.ReadBoolean("grav", builder.GravShield);
        builder.MindPower = reader.ReadInt32("mindPower", builder.MindPower);
        if (reader.TryGet("size", out var legacySize))
            builder.SizeX = builder.SizeY = YamlValueReader.ReadInt32(legacySize!);
        builder.SizeX = reader.ReadInt32("sizeX", builder.SizeX);
        builder.SizeY = reader.ReadInt32("sizeY", builder.SizeY);
        builder.BuildCost = reader.ReadInt32("buildCost", builder.BuildCost);
        builder.RefundValue = reader.ReadInt32("refundValue", builder.RefundValue);
        builder.BuildTime = reader.ReadInt32("buildTime", builder.BuildTime);
        builder.MonthlyCost = reader.ReadInt32("monthlyCost", builder.MonthlyCost);
        builder.Storage = reader.ReadInt32("storage", builder.Storage);
        builder.Personnel = reader.ReadInt32("personnel", builder.Personnel);
        builder.Aliens = reader.ReadInt32("aliens", builder.Aliens);
        builder.Crafts = reader.ReadInt32("crafts", builder.Crafts);
        builder.Laboratories = reader.ReadInt32("labs", builder.Laboratories);
        builder.Workshops = reader.ReadInt32("workshops", builder.Workshops);
        builder.PsiLaboratories = reader.ReadInt32("psiLabs", builder.PsiLaboratories);
        builder.SpriteEnabled = reader.ReadBoolean("spriteEnabled", builder.SpriteEnabled);
        builder.SightRange = reader.ReadInt32("sightRange", builder.SightRange);
        builder.SightChance = reader.ReadInt32("sightChance", builder.SightChance);
        builder.RadarRange = reader.ReadInt32("radarRange", builder.RadarRange);
        builder.RadarChance = reader.ReadInt32("radarChance", builder.RadarChance);
        builder.Defense = reader.ReadInt32("defense", builder.Defense);
        builder.HitRatio = reader.ReadInt32("hitRatio", builder.HitRatio);
        ApplyIndex(reader, "fireSound", value => builder.FireSound = value);
        ApplyIndex(reader, "hitSound", value => builder.HitSound = value);
        ApplyIndex(reader, "placeSound", value => builder.PlaceSound = value);
        builder.AmmoMaximum = reader.ReadInt32("ammoMax", builder.AmmoMaximum);
        builder.RearmRate = reader.ReadInt32("rearmRate", builder.RearmRate);
        builder.AmmoNeeded = reader.ReadInt32("ammoNeeded", builder.AmmoNeeded);
        builder.UnifiedDamageFormula = reader.ReadBoolean("unifiedDamageFormula", builder.UnifiedDamageFormula);
        builder.ShieldDamageModifier = reader.ReadInt32("shieldDamageModifier", builder.ShieldDamageModifier);
        builder.AmmoItem = reader.ReadString("ammoItem", builder.AmmoItem);
        builder.MapName = reader.ReadString("mapName", builder.MapName);
        builder.ListOrder = reader.ReadInt32("listOrder", builder.ListOrder);
        builder.TrainingRooms = reader.ReadInt32("trainingRooms", builder.TrainingRooms);
        builder.MaximumAllowedPerBase = reader.ReadInt32("maxAllowedPerBase", builder.MaximumAllowedPerBase);
        builder.ManaRecoveryPerDay = reader.ReadInt32("manaRecoveryPerDay", builder.ManaRecoveryPerDay);
        builder.HealthRecoveryPerDay = reader.ReadInt32("healthRecoveryPerDay", builder.HealthRecoveryPerDay);
        builder.SickBayAbsoluteBonus = reader.ReadSingle("sickBayAbsoluteBonus", builder.SickBayAbsoluteBonus);
        builder.SickBayRelativeBonus = reader.ReadSingle("sickBayRelativeBonus", builder.SickBayRelativeBonus);
        builder.PrisonType = reader.ReadInt32("prisonType", builder.PrisonType);
        builder.HangarType = reader.ReadInt32("hangarType", builder.HangarType);
        builder.RightClickActionType = reader.ReadInt32("rightClickActionType", builder.RightClickActionType);
        ApplyBuildCosts(builder, reader);
        reader.Defer("verticalLevels", "facility vertical map levels belong to the terrain/deployment slice");
        if (reader.TryGet("leavesBehindOnSell", out var leaves))
            builder.LeavesBehindOnSell = YamlValueReader.ReadSequence(leaves!, YamlValueReader.ReadString).ToList();
        builder.RemovalTime = reader.ReadInt32("removalTime", builder.RemovalTime);
        builder.CanBeBuiltOver = reader.ReadBoolean("canBeBuiltOver", builder.CanBeBuiltOver);
        builder.UpgradeOnly = reader.ReadBoolean("upgradeOnly", builder.UpgradeOnly);
        ApplyNames(reader, "buildOverFacilities", builder.BuildOverFacilities, unique: false);
        if (reader.TryGet("storageTiles", out var storageTiles))
            builder.StorageTiles = YamlValueReader.ReadSequence(storageTiles!, CampaignStartYaml.ReadPosition).ToList();
        if (reader.TryGet("craftSlots", out var craftSlots))
            builder.CraftSlots = YamlValueReader.ReadSequence(craftSlots!, CampaignStartYaml.ReadPosition).ToList();
        builder.DestroyedFacility = reader.ReadString("destroyedFacility", builder.DestroyedFacility);
    }

    protected override BaseFacilityRule Freeze(BaseFacilityBuilder b) => new(
        b.UfopediaType, b.Requires.AsReadOnly(), b.RequiredBaseFunctions.AsReadOnly(),
        b.ProvidedBaseFunctions.AsReadOnly(), b.ForbiddenBaseFunctions.AsReadOnly(), b.SpriteShape,
        b.SpriteFacility, b.ConnectorsDisabled, b.FakeUnderwater, b.MissileAttraction, b.Lift, b.HyperWave,
        b.MindShield, b.GravShield, b.MindPower, b.SizeX, b.SizeY, b.BuildCost, b.RefundValue, b.BuildTime,
        b.MonthlyCost, b.Storage, b.Personnel, b.Aliens, b.Crafts, b.Laboratories, b.Workshops,
        b.PsiLaboratories, b.SpriteEnabled, b.SightRange, b.SightChance, b.RadarRange, b.RadarChance,
        b.Defense, b.HitRatio, b.FireSound, b.HitSound, b.PlaceSound, b.AmmoMaximum, b.RearmRate,
        b.AmmoNeeded, b.UnifiedDamageFormula, b.ShieldDamageModifier, b.AmmoItem, b.MapName, b.ListOrder,
        b.TrainingRooms, b.MaximumAllowedPerBase, b.ManaRecoveryPerDay, b.HealthRecoveryPerDay,
        b.SickBayAbsoluteBonus, b.SickBayRelativeBonus, b.PrisonType, b.HangarType, b.RightClickActionType,
        new ReadOnlyDictionary<string, FacilityItemCost>(b.BuildCostItems), b.LeavesBehindOnSell.AsReadOnly(),
        b.RemovalTime, b.CanBeBuiltOver, b.UpgradeOnly, b.BuildOverFacilities.AsReadOnly(),
        b.StorageTiles.AsReadOnly(), b.CraftSlots.AsReadOnly(), b.DestroyedFacility);

    private static void ApplyBuildCosts(BaseFacilityBuilder builder, RulePropertyReader reader)
    {
        if (!reader.TryGet("buildCostItems", out var node)) return;
        if (node is not YamlMappingNode mapping)
            throw new YamlFormatException("Facility buildCostItems must be a mapping.", node!.Span);
        foreach (var entry in mapping.Entries)
        {
            var id = YamlValueReader.ReadString(entry.Key);
            if (entry.Value is not YamlMappingNode costNode)
                throw new YamlFormatException("Facility item costs must be mappings.", entry.Value.Span);
            builder.BuildCostItems.TryGetValue(id, out var existing);
            var build = YamlValueReader.ReadInt32(costNode, "build", existing?.Build ?? 0);
            var refund = YamlValueReader.ReadInt32(costNode, "refund", existing?.Refund ?? 0);
            if (build <= 0 && refund <= 0) builder.BuildCostItems.Remove(id);
            else builder.BuildCostItems[id] = new FacilityItemCost(build, refund);
        }
    }

    private static void ApplyIndex(
        RulePropertyReader reader, string key, Action<RuleIndexReference> apply)
    {
        if (reader.TryGet(key, out var node))
            apply(PresentationYaml.ReadIndexReference(node!, reader.Source.ModId));
    }

    private static void ApplyNames(RulePropertyReader reader, string key, List<string> destination, bool unique) =>
        CountryRuleLoader.ApplyNames(reader, key, destination, unique);

    private static RuleSectionDefinition GetSection() => RuleSectionRegistry.TryGetNamed("facilities", out var section)
        ? section! : throw new InvalidOperationException();
}

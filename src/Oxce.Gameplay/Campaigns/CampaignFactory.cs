using Oxce.Core.Random;
using Oxce.Mods.Rulesets.CampaignStart;
using Oxce.Mods.Rulesets.Content;
using Oxce.Mods.Rulesets.Runtime;
using Oxce.Mods.Rulesets;
using Oxce.Scripting.Globals;

namespace Oxce.Gameplay.Campaigns;

public sealed record NewCampaignRequest(
    CampaignId Id,
    string Name,
    string MasterId,
    IReadOnlyList<string> ActiveMods,
    CampaignDifficulty Difficulty,
    bool Ironman = false)
{
    public CampaignOptions Options { get; init; } = new();
}

public static class CampaignFactory
{
    public static CampaignState Create(
        RuntimeContent content,
        NewCampaignRequest request,
        IStatefulRandomSource random,
        ICampaignClock clock)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Options);
        ArgumentNullException.ThrowIfNull(random);
        ArgumentNullException.ThrowIfNull(clock);
        if (!content.Capabilities.Has(ContentLoadStage.RuntimeLinked))
            throw new InvalidOperationException("Campaign creation requires runtime-linked content.");
        if (!Enum.IsDefined(request.Difficulty)) throw new ArgumentOutOfRangeException(nameof(request));

        var rules = content.RuntimeRules;
        var countries = rules.Countries.Rules
            .Select((rule, index) => (Rule: rule, Handle: rules.Countries.GetRequired(rule.Id), Index: index))
            .Where(static item => item.Rule.Value.Areas.Count != 0)
            .Select(item => new CampaignState.CountryState(
                item.Handle,
                [checked(random.NextInclusive(item.Rule.Value.FundingBase,
                    checked(item.Rule.Value.FundingBase * 2)) * 1000)],
                [0],
                [0],
                false,
                false,
                false,
                InitialScriptValues(content, "Country", "countries", item.Rule.Id)))
            .ToArray();
        if (countries.Length == 0) throw new InvalidDataException("A new campaign requires at least one geographic country.");

        var countryFunding = countries.Sum(static country => (long)country.Funding[^1]);
        var missing = checked(((rules.Campaign.InitialFunding - countryFunding / 1000) / countries.Length) * 1000);
        foreach (var country in countries)
        {
            var adjusted = checked(country.Funding[^1] + missing);
            if (adjusted >= 0) country.Funding[^1] = checked((int)adjusted);
        }
        countryFunding = countries.Sum(static country => (long)country.Funding[^1]);

        var regions = rules.Regions.Rules
            .Where(static rule => rule.Value.Areas.Count != 0)
            .Select(rule => new CampaignState.RegionState(rules.Regions.GetRequired(rule.Id), [0], [0]))
            .ToArray();

        var variant = (StartingBaseVariant)((int)request.Difficulty + 1);
        var template = rules.Campaign.GetStartingBase(variant) ??
            throw new InvalidDataException($"No starting-base template is available for {request.Difficulty}.");
        var ids = new SortedDictionary<string, int>(StringComparer.Ordinal);
        var facilities = template.Facilities.Select(facility => new CampaignState.FacilityState(
            facility.Rule, facility.X, facility.Y, facility.BuildTime, facility.Ammo, facility.AmmoMissingReported,
            facility.Disabled, facility.HadPreviousFacility)).ToArray();
        var crafts = template.Crafts.Select(craft =>
        {
            var type = rules.Crafts.GetExternalId(craft.Rule);
            var allocated = NextId(ids, type);
            return new CampaignState.CraftState(craft.Rule, craft.Id > 0 ? craft.Id : allocated)
            { Logistics = CraftLogistics.LoadStarting(rules.Crafts[craft.Rule].Value, craft.Template) };
        }).ToArray();
        var soldiers = new List<CampaignState.SoldierState>();
        foreach (var soldier in template.Soldiers)
        {
            var allocated = NextId(ids, "STR_SOLDIER");
            var personal = SoldierGeneration.LoadStarting(rules.Soldiers[soldier.Rule].Value, soldier.Template, rules, random,
                request.Options.AutoCombatDefaultSoldier);
            if (crafts.Any(c => c.Id == soldier.CraftId && rules.Crafts.GetExternalId(c.Rule) == soldier.CraftType))
                personal = personal with { CraftType = soldier.CraftType, CraftId = soldier.CraftId };
            soldiers.Add(new CampaignState.SoldierState(soldier.Rule, soldier.Id > 0 ? soldier.Id : allocated) { Personal = personal });
        }
        var randomTypes = new List<RuleHandle<SoldierRuleFamily>>();
        foreach (var batch in template.RandomSoldiers)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(batch.Quantity);
            for (var index = 0; index < batch.Quantity; index++)
                randomTypes.Add(batch.Rule);
        }
        if (template.RandomSoldierCount > 0)
        {
            var eligible = rules.Soldiers.Rules
                .Select(rule => rules.Soldiers.GetRequired(rule.Id))
                .Where(handle => rules.Soldiers[handle].Value.Requirements.Count == 0)
                .ToArray();
            if (eligible.Length == 0)
                throw new InvalidDataException("Random starting soldiers were requested but no soldier type is eligible.");
            for (var index = 0; index < template.RandomSoldierCount; index++)
                randomTypes.Add(eligible[random.NextExclusive(eligible.Length)]);
        }
        var names = soldiers.Select(s => s.Personal!.Name).ToHashSet(StringComparer.Ordinal);
        foreach (var type in randomTypes)
        {
            var rule = rules.Soldiers[type].Value;
            var personal = SoldierGeneration.Generate(rule, rules.Armors.GetExternalId(rule.Armor), -1, names, random)
                with
            { AllowAutoCombat = request.Options.AutoCombatDefaultSoldier };
            if (rules.Commendations.TryGet("STR_MEDAL_ORIGINAL8_NAME", out _))
                personal = personal with { Commendations = Array.AsReadOnly(new[] { new SoldierCommendation("STR_MEDAL_ORIGINAL8_NAME", "NoNoun", 0) }) };
            soldiers.Add(new CampaignState.SoldierState(type, NextId(ids, "STR_SOLDIER")) { Personal = personal });
            names.Add(personal.Name);
        }

        for (var index = 0; index < crafts.Length; index++)
        {
            var preservationKey = FormattableString.Invariant(
                $"created:{request.Id}:craft:{rules.Crafts.GetExternalId(crafts[index].Rule)}:{crafts[index].Id}");
            var logistics = crafts[index].Logistics!;
            crafts[index] = crafts[index] with
            {
                PreservationKey = preservationKey,
                Logistics = logistics with
                {
                    Vehicles = Array.AsReadOnly(logistics.Vehicles.Select((vehicle, slot) => vehicle with
                    {
                        PreservationKey = $"{preservationKey}:vehicle:{slot}",
                    }).ToArray()),
                },
            };
        }
        for (var index = 0; index < soldiers.Count; index++)
            soldiers[index] = soldiers[index] with
            {
                PreservationKey = FormattableString.Invariant($"created:{request.Id}:soldier:{soldiers[index].Id}"),
            };

        var items = new Dictionary<RuleHandle<ItemRuleFamily>, int>();
        foreach (var item in template.Items)
        {
            if (item.Quantity <= 0) throw new InvalidDataException("Starting item quantities must be positive.");
            items.Add(item.Rule, item.Quantity);
        }
        // Mod::newSave unloads every weapon when the combined raw capacity is negative.
        for (var index = 0; index < crafts.Length; index++)
        {
            var craft = crafts[index];
            var state = craft.Logistics!;
            if (!CraftLogistics.HasNegativeCapacity(state, rules.Crafts[craft.Rule].Value, rules)) continue;
            foreach (var item in CraftLogistics.UnloadedWeaponItems(state, rules))
            {
                var handle = rules.Items.GetRequired(item.Key);
                items[handle] = checked(items.GetValueOrDefault(handle) + item.Value);
            }
            crafts[index] = craft with
            {
                Logistics = state with { Weapons = Array.AsReadOnly(new CraftWeaponSnapshot?[state.Weapons.Count]) },
            };
        }
        if (template.AssignRandomSoldiers) StartingCrewAssignment.Assign(crafts, soldiers, rules);
        var startingBase = new CampaignState.BaseState(
            0, string.Empty, 0, 0, facilities, crafts, soldiers, items, template.Scientists, template.Engineers);
        var start = rules.Campaign.StartingTime;
        var identity = new CampaignIdentity(
            request.Id,
            request.Name,
            request.MasterId,
            CampaignSnapshot.ReadOnly(request.ActiveMods),
            clock.UtcNow.ToUniversalTime(),
            request.Ironman);

        return new CampaignState(
            content,
            random,
            identity,
            request.Difficulty,
            new CampaignTime(start.Weekday, start.Day, start.Month, start.Year, start.Hour, start.Minute, start.Second),
            ending: 0,
            monthsPassed: -1,
            daysPassed: 0,
            [countryFunding],
            [0],
            [0],
            [0],
            [0],
            ids,
            countries,
            regions,
            [startingBase],
            EmptyScriptValues(content, "GeoscapeGame"))
        { Options = request.Options };
    }

    private static int NextId(SortedDictionary<string, int> ids, string name)
    {
        var next = ids.TryGetValue(name, out var value) ? value : 1;
        ids[name] = checked(next + 1);
        return next;
    }

    private static ScriptValueState? InitialScriptValues(
        RuntimeContent content,
        string ownerType,
        string ownerSection,
        string ownerId)
    {
        var type = content.Tags.Types.FirstOrDefault(candidate => candidate.Name == ownerType);
        var values = content.InitialValues.Where(value =>
            value.OwnerSection == ownerSection && value.OwnerId == ownerId).ToArray();
        if (type is null)
        {
            if (values.Length != 0)
                throw new InvalidDataException($"Initial values target unavailable script type '{ownerType}'.");
            return null;
        }
        var state = new ScriptValueState(content.Tags, type.Id);
        foreach (var value in values) state.Set(value.TagName, value.Value);
        return state;
    }

    private static ScriptValueState? EmptyScriptValues(RuntimeContent content, string ownerType)
    {
        var type = content.Tags.Types.FirstOrDefault(candidate => candidate.Name == ownerType);
        return type is null ? null : new ScriptValueState(content.Tags, type.Id);
    }
}

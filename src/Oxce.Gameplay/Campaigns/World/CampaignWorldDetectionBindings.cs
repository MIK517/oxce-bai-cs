using Oxce.Mods.Rulesets.Runtime;
using Oxce.Scripting.Api;
using Oxce.Scripting.Runtime;

namespace Oxce.Gameplay.Campaigns.World;

/// <summary>Read-only providers from Ufo::ScriptRegister, Craft::ScriptRegister and BindBase.</summary>
internal sealed partial class CampaignWorld
{
    private readonly DetectionTimeReference _detectionTime = new();
    private int _detectionShieldCapacity;
    private int _detectionDamageMaximum;

    private bool DetectionBindingSupported(ScriptBindingDeclaration binding) =>
        DetectionBindingNameSupported(binding.Name) || DetectionUfoStatIndex(binding.Name) >= 0 || DetectionTagSupported(binding);

    private static bool DetectionBindingNameSupported(string name) => name is
        "Ufo.getAltitude" or "Ufo.getStatus" or "Ufo.getVisibility" or "Ufo.getDamage" or
        "Ufo.getDamageMax" or "Ufo.getDamagePercentage" or "Ufo.getDetected" or "Ufo.getHyperDetected" or "Ufo.getShield" or "Ufo.getShieldMax" or
        "Ufo.getShieldPercentage" or "Ufo.isHunterKiller" or "Ufo.isHunting" or "Ufo.isEscorting" or
        "Ufo.getHuntMode" or "Ufo.getHuntBehavior" or "Ufo.getRules" or "Ufo.getRuleUfo" or
        "Craft.getDamage" or "Craft.getShield" or "Craft.getRuleCraft" or
        "Craft.Stats.getRadarRange" or "Craft.Stats.getRadarChance" or
        "RuleCraft.Stats.getRadarRange" or "RuleCraft.Stats.getRadarChance" or
        "RuleCraft.getPilotsMax" or "RuleCraft.getSoldiersMax" or "RuleCraft.getVehiclesMax" or "RuleCraft.getWeaponsMax" or
        "GeoscapeGame.difficultyLevel" or "GeoscapeGame.getDaysPassed" or "GeoscapeGame.getMonthsPassed" or
        "GeoscapeGame.getCountries.init" or "GeoscapeGame.getCountries.list" or
        "Country.getRuleCountry" or "RuleCountry.getFundingCap" or "Country.getPact" or "Country.getCurrentFunding" or "Country.getCurrentActivityXcom" or "Country.getCurrentActivityAlien" or
        "GeoscapeGame.getTime" or "GeoscapeGame.isResearched" or "RuleMod.getRuleResearch" or
        "RuleMod.getRuleItem" or "RuleMod.getRuleArmor" or "RuleMod.getRuleSoldier" or "RuleMod.getRuleSkill" or
        "RuleResearch.getNeededItem" or "RuleResearch.getCost" or "RuleResearch.getPoints" or "RuleResearch.getLookup" or "Time.getDay" or "Time.getMonth" or "Time.getYear" or
        "Time.getHour" or "Time.getMinute" or "Time.getSecond" or "Time.getSecondsPastMidnight";

    private ScriptBindingResult InvokeDetectionBinding(ScriptBindingDeclaration binding, Span<ScriptRuntimeValue> args)
    {
        var name = binding.Name;
        if (name.EndsWith(".getTag", StringComparison.Ordinal)) return InvokeDetectionTag(binding, args);
        var receiver = args[0].Reference;
        var ufo = receiver as UfoSnapshot;
        var craft = receiver as CampaignState.CraftState;
        var rules = campaign.Content.RuntimeRules;
        switch (name)
        {
            case "Ufo.getRules":
            case "Ufo.getRuleUfo":
                args[1] = ScriptRuntimeValue.FromReference(ufo is null ? null : rules.Ufos[rules.Ufos.GetRequired(ufo.RuleId)].Value);
                break;
            case "Craft.getRuleCraft":
                args[1] = ScriptRuntimeValue.FromReference(craft is null ? null : rules.Crafts[craft.Rule].Value);
                break;
            case "RuleMod.getRuleResearch":
                args[1] = ScriptRuntimeValue.FromReference(receiver is null ? null : DetectionResearchRule(args[2].Text));
                break;
            case "RuleMod.getRuleItem":
                args[1] = ScriptRuntimeValue.FromReference(receiver is null || args[2].Text == "STR_NONE"
                    ? null : DetectionRule(rules.Items, args[2].Text));
                break;
            case "RuleMod.getRuleArmor":
                args[1] = ScriptRuntimeValue.FromReference(receiver is null ? null : DetectionRule(rules.Armors, args[2].Text));
                break;
            case "RuleMod.getRuleSoldier":
                args[1] = ScriptRuntimeValue.FromReference(receiver is null ? null : DetectionRule(rules.Soldiers, args[2].Text));
                break;
            case "RuleMod.getRuleSkill":
                args[1] = ScriptRuntimeValue.FromReference(receiver is null ? null : DetectionRule(rules.Skills, args[2].Text));
                break;
            case "RuleResearch.getNeededItem":
                var researchRule = receiver is string neededTopic ? rules.Research[rules.Research.GetRequired(neededTopic)].Value : null;
                args[1] = ScriptRuntimeValue.FromReference(researchRule is { NeedItem: true } && researchRule.NeededItem != "STR_NONE"
                    ? DetectionRule(rules.Items, researchRule.NeededItem) : null);
                break;
            case "RuleResearch.getLookup":
                args[1] = ScriptRuntimeValue.FromReference(receiver is string researchId
                    ? DetectionResearchRule(rules.Research[rules.Research.GetRequired(researchId)].Value.Lookup) : null);
                break;
            case "GeoscapeGame.isResearched":
                args[1] = ScriptRuntimeValue.FromScalar(receiver is not null && args[2].Reference is string research &&
                    campaign.HasCompletedResearch(research) ? 1 : 0);
                break;
            case "RuleResearch.getCost":
            case "RuleResearch.getPoints":
                args[1] = ScriptRuntimeValue.FromScalar(receiver is string topic
                    ? name == "RuleResearch.getCost" ? rules.Research[rules.Research.GetRequired(topic)].Value.Cost
                        : rules.Research[rules.Research.GetRequired(topic)].Value.Points : 0);
                break;
            case "Country.getRuleCountry":
                args[1] = ScriptRuntimeValue.FromReference(receiver is CampaignState.CountryState owner ? rules.Countries[owner.Rule].Value : null);
                break;
            case "GeoscapeGame.getCountries.init":
                args[1] = ScriptRuntimeValue.FromScalar(0);
                args[2] = ScriptRuntimeValue.FromScalar(receiver is null ? 0 : campaign.CountryStates.Count);
                break;
            case "GeoscapeGame.getCountries.list":
                if (receiver is null)
                {
                    args[1] = ScriptRuntimeValue.FromScalar(0);
                    args[2] = ScriptRuntimeValue.FromScalar(0);
                    args[3] = ScriptRuntimeValue.FromReference(null);
                }
                else
                {
                    var index = args[1].Scalar;
                    args[3] = ScriptRuntimeValue.FromReference((uint)index < (uint)campaign.CountryStates.Count ? campaign.CountryStates[index] : null);
                    args[1] = ScriptRuntimeValue.FromScalar(unchecked(index + 1));
                }
                break;
            case "GeoscapeGame.getTime":
                _detectionTime.Time = campaign.Time;
                args[1] = ScriptRuntimeValue.FromReference(receiver is null ? null : _detectionTime);
                break;
            default:
                args[1] = ScriptRuntimeValue.FromScalar(receiver is null ? 0 :
                    ufo is not null ? DetectionUfoValue(name, ufo) :
                    craft is not null ? DetectionCraftValue(name, craft) :
                    receiver is CampaignState.CountryState country ? name switch
                    {
                        "Country.getPact" => country.Pact ? 1 : 0,
                        "Country.getCurrentFunding" => country.Funding[^1],
                        "Country.getCurrentActivityXcom" => country.ActivityXcom[^1],
                        "Country.getCurrentActivityAlien" => country.ActivityAlien[^1],
                        _ => throw new InvalidOperationException("Detection country provider was not preflighted."),
                    } :
                    receiver is RuntimeCraftRule rule ? DetectionCraftRuleValue(name, rule) :
                    receiver is RuntimeUfoRule ufoRule ? DetectionUfoStat(ufoRule.Stats, DetectionUfoStatIndex(name)) :
                    receiver is RuntimeCountryRule countryRule ? countryRule.FundingCap :
                    receiver is DetectionTimeReference time ? DetectionTimeValue(name, time.Time) :
                    name switch
                    {
                        "GeoscapeGame.difficultyLevel" => (int)campaign.Difficulty,
                        "GeoscapeGame.getDaysPassed" => campaign.DaysPassed,
                        "GeoscapeGame.getMonthsPassed" => campaign.MonthsPassed,
                        _ => throw new InvalidOperationException("Detection provider receiver was not preflighted."),
                    });
                break;
        }
        return ScriptBindingResult.Success;
    }

    private string? DetectionResearchRule(string? id)
    {
        var rules = campaign.Content.RuntimeRules.Research;
        return id is not null && id.Length != 0 && id != "\0" && rules.TryGet(id, out var handle)
            ? rules.GetExternalId(handle) : null;
    }

    private static TProjection? DetectionRule<TFamily, TProjection>(
        RuntimeRuleFamily<TFamily, TProjection> rules, string? id) where TProjection : class =>
        id is not null && id.Length != 0 && id != "\0" && rules.TryGet(id, out var handle) ? rules[handle].Value : null;

    private int DetectionUfoValue(string name, UfoSnapshot ufo)
    {
        var field = DetectionUfoStatIndex(name);
        if (field >= 0) return unchecked(DetectionUfoStat(_detectionUfoStats, field) + DetectionUfoStat(_detectionUfoBonus, field));
        switch (name)
        {
            case "Ufo.getAltitude": return WorldAltitudes.Index(ufo.Altitude);
            case "Ufo.getStatus": return (int)ufo.Status;
            case "Ufo.getDamage": return ufo.Damage;
            case "Ufo.getDamageMax": return _detectionDamageMaximum;
            case "Ufo.getDamagePercentage": return (int)Math.Floor((double)ufo.Damage / _detectionDamageMaximum * 100);
            case "Ufo.getDetected": return ufo.Detected ? 1 : 0;
            case "Ufo.getHyperDetected": return ufo.HyperDetected ? 1 : 0;
            case "Ufo.getShield": return ufo.Shield;
            case "Ufo.isHunterKiller": return ufo.HunterKiller ? 1 : 0;
            case "Ufo.isHunting": return ufo.Hunting ? 1 : 0;
            case "Ufo.isEscorting": return ufo.Escorting ? 1 : 0;
            case "Ufo.getHuntMode": return ufo.HuntMode;
            case "Ufo.getHuntBehavior": return ufo.HuntBehavior;
        }
        var rules = campaign.Content.RuntimeRules;
        var rule = rules.Ufos[rules.Ufos.GetRequired(ufo.RuleId)].Value;
        if (name == "Ufo.getVisibility") return WorldAltitudes.Visibility(rule.DefaultVisibility, ufo.Altitude);
        var capacity = _detectionShieldCapacity;
        return name switch
        {
            "Ufo.getShieldMax" => capacity,
            "Ufo.getShieldPercentage" => capacity == 0 ? 0 : unchecked(ufo.Shield * 100) / capacity,
            _ => throw new InvalidOperationException("Detection UFO provider was not preflighted."),
        };
    }

    private int DetectionCraftValue(string name, CampaignState.CraftState craft)
    {
        if (name == "Craft.getDamage") return craft.Logistics?.Damage ?? 0;
        if (name == "Craft.getShield") return craft.Logistics?.Shield ?? 0;
        var rules = campaign.Content.RuntimeRules;
        CraftLogistics.EffectiveRadar(rules.Crafts[craft.Rule].Value, craft.Logistics?.Weapons ?? [],
            rules, out var range, out var chance);
        return name == "Craft.Stats.getRadarRange" ? range : chance;
    }

    private static int DetectionCraftRuleValue(string name, RuntimeCraftRule rule) => name switch
    {
        "RuleCraft.Stats.getRadarRange" => rule.RadarRange,
        "RuleCraft.Stats.getRadarChance" => rule.RadarChance,
        "RuleCraft.getPilotsMax" => rule.Pilots,
        "RuleCraft.getSoldiersMax" => rule.SoldierCapacity,
        "RuleCraft.getVehiclesMax" => rule.VehicleCapacity,
        "RuleCraft.getWeaponsMax" => rule.WeaponSlots,
        _ => throw new InvalidOperationException("Detection craft rule provider was not preflighted."),
    };

    private static int DetectionTimeValue(string name, CampaignTime time) => name switch
    {
        "Time.getDay" => time.Day,
        "Time.getMonth" => time.Month,
        "Time.getYear" => time.Year,
        "Time.getHour" => time.Hour,
        "Time.getMinute" => time.Minute,
        "Time.getSecond" => time.Second,
        "Time.getSecondsPastMidnight" => time.Hour * 3600 + time.Minute * 60 + time.Second,
        _ => throw new InvalidOperationException("Detection time provider was not preflighted."),
    };

    private sealed class DetectionTimeReference
    {
        public CampaignTime Time { get; set; }
    }
}

using Oxce.Mods.Rulesets.Runtime;
using Oxce.Scripting.Api;
using Oxce.Scripting.Runtime;

namespace Oxce.Gameplay.Campaigns;

public sealed partial class CampaignState
{
    private Dictionary<(ushort Owner, int Index), (string Name, string Owner)>? _priceTagNames;
    private Dictionary<(string Section, string Id, string Tag), int>? _priceRuleTags;

    private static bool PriceValueCopy(ScriptBindingDeclaration binding) =>
        binding.Name == "set" && binding.Parameters.Count == 2 &&
        binding.Parameters[0].Writable && binding.Parameters[0].Type.IsRegister &&
        binding.Parameters[0].Type.IsReference == binding.Parameters[1].Type.IsReference &&
        !binding.Parameters[1].Type.IsRegister && binding.Parameters[0].Type.Id == binding.Parameters[1].Type.Id;

    private bool PriceTagSupported(ScriptBindingDeclaration binding) =>
        binding.Name is "RuleItem.getTag" or "RuleResearch.getTag" or "RuleMod.getTag" or "GeoscapeGame.getTag" or
            "RuleArmor.getTag" or "RuleSkill.getTag" or "RuleSoldier.getTag" or "Country.getTag" or "RuleCountry.getTag" &&
        _content.Tags.Types.Any(type => type.Id.Value == binding.Parameters[2].Type.Id.Value &&
            binding.Name == type.Name + ".getTag");

    private ScriptBindingResult InvokePriceTag(ScriptBindingDeclaration binding, Span<ScriptRuntimeValue> args, PriceForecastContext forecast)
    {
        if (_priceTagNames is null)
        {
            forecast.Work += _content.Tags.Types.Count + (long)_content.Tags.Tags.Count + _content.InitialValues.Count;
            if (forecast.Work > 1_000_000)
                return ScriptBindingResult.Failure("Production price tag initialization exceeds the supported work limit.");
            var types = _content.Tags.Types.ToDictionary(type => type.Id.Value, type => type.Name);
            _priceTagNames = [];
            foreach (var entry in _content.Tags.Tags)
                _priceTagNames.Add((entry.OwnerType.Value, entry.Index), (entry.Name, types[entry.OwnerType.Value]));
            _priceRuleTags = [];
            foreach (var value in _content.InitialValues)
                _priceRuleTags[(value.OwnerSection, value.OwnerId, value.TagName)] = value.Value;
        }
        var result = 0;
        if (_priceTagNames.TryGetValue((binding.Parameters[2].Type.Id.Value, args[2].Scalar), out var tag))
        {
            var receiver = args[0].Reference;
            result = receiver switch
            {
                PriceItemReference item when tag.Owner == "RuleItem" => _priceRuleTags!.GetValueOrDefault(("items", item.Id, tag.Name)),
                string research when tag.Owner == "RuleResearch" => _priceRuleTags!.GetValueOrDefault(("research", research, tag.Name)),
                RuntimeRuleCatalog when tag.Owner == "RuleMod" => _priceRuleTags!.GetValueOrDefault(("extended", "globals", tag.Name)),
                RuntimeArmorRule armor when tag.Owner == "RuleArmor" => _priceRuleTags!.GetValueOrDefault(("armors", armor.Type, tag.Name)),
                RuntimeIdentityRule skill when tag.Owner == "RuleSkill" => _priceRuleTags!.GetValueOrDefault(("skills", skill.Id, tag.Name)),
                RuntimeSoldierRule soldier when tag.Owner == "RuleSoldier" && _priceSoldierIds.TryGetValue(soldier, out var id) =>
                    _priceRuleTags!.GetValueOrDefault(("soldiers", id, tag.Name)),
                CountryState country when tag.Owner == "Country" => country.ScriptValues?.Get(tag.Name) ?? 0,
                RuntimeCountryRule countryRule when tag.Owner == "RuleCountry" && _priceCountryIds.TryGetValue(countryRule, out var countryId) =>
                    _priceRuleTags!.GetValueOrDefault(("countries", countryId, tag.Name)),
                CampaignState state when tag.Owner == "GeoscapeGame" => state.ScriptValue(tag.Name),
                _ => 0,
            };
        }
        args[1] = ScriptRuntimeValue.FromScalar(result);
        return ScriptBindingResult.Success;
    }
}

using Oxce.Mods.Rulesets.Runtime;
using Oxce.Scripting.Api;
using Oxce.Scripting.Globals;
using Oxce.Scripting.Runtime;

namespace Oxce.Gameplay.Campaigns.World;

internal sealed partial class CampaignWorld
{
    private Dictionary<(ushort Owner, int Index), DetectionTagName>? _detectionTagNames;
    private Dictionary<object, Dictionary<string, int>>? _detectionRuleTags;
    private readonly Dictionary<string, int> _detectionModTags = new(StringComparer.Ordinal);

    private bool DetectionTagSupported(ScriptBindingDeclaration binding)
    {
        if (binding.Name is not ("Ufo.getTag" or "Craft.getTag" or "RuleUfo.getTag" or
            "RuleCraft.getTag" or "RuleMod.getTag" or "GeoscapeGame.getTag")) return false;
        var owner = binding.Parameters[2].Type.Id.Value;
        for (var index = 0; index < campaign.Content.Tags.Types.Count; index++)
        {
            var type = campaign.Content.Tags.Types[index];
            if (type.Id.Value != owner) continue;
            return binding.Name switch
            {
                "Ufo.getTag" => type.Name is "Ufo" or "RuleUfo",
                "Craft.getTag" => type.Name is "Craft" or "RuleCraft",
                "RuleUfo.getTag" => type.Name == "RuleUfo",
                "RuleCraft.getTag" => type.Name == "RuleCraft",
                "RuleMod.getTag" => type.Name == "RuleMod",
                "GeoscapeGame.getTag" => type.Name == "GeoscapeGame",
                _ => false,
            };
        }
        return false;
    }

    private ScriptBindingResult InvokeDetectionTag(ScriptBindingDeclaration binding, Span<ScriptRuntimeValue> args)
    {
        var value = 0;
        if (GetDetectionTagName(binding.Parameters[2].Type.Id.Value, args[2].Scalar) is { } tag)
        {
            var receiver = args[0].Reference;
            if (receiver is UfoSnapshot ufo)
            {
                if (tag.Owner == "RuleUfo")
                    value = DetectionRuleTag(campaign.Content.RuntimeRules.Ufos[
                        campaign.Content.RuntimeRules.Ufos.GetRequired(ufo.RuleId)].Value, tag.Qualified);
                else
                    for (var index = 0; index < ufo.ScriptValues.Count; index++)
                    {
                        var entry = ufo.ScriptValues[index];
                        if (entry.TagName == tag.Qualified || entry.TagName == tag.Bare) { value = entry.Value; break; }
                    }
            }
            else if (receiver is CampaignState.CraftState craft)
                value = tag.Owner == "RuleCraft" ? DetectionRuleTag(campaign.Content.RuntimeRules.Crafts[craft.Rule].Value, tag.Qualified)
                    : craft.ScriptValues?.Get(tag.Qualified) ?? 0;
            else if (receiver is CampaignState state)
                value = state.ScriptValue(tag.Qualified);
            else if (receiver is RuntimeRuleCatalog)
                value = _detectionModTags.GetValueOrDefault(tag.Qualified);
            else if (receiver is RuntimeUfoRule or RuntimeCraftRule)
                value = DetectionRuleTag(receiver, tag.Qualified);
        }
        args[1] = ScriptRuntimeValue.FromScalar(value);
        return ScriptBindingResult.Success;
    }

    private DetectionTagName? GetDetectionTagName(ushort owner, int index)
    {
        if (_detectionTagNames is null)
        {
            _detectionTagNames = [];
            foreach (var tag in campaign.Content.Tags.Tags)
                _detectionTagNames.Add((tag.OwnerType.Value, tag.Index),
                    new DetectionTagName(tag.Name, tag.Name.StartsWith("Tag.", StringComparison.Ordinal) ? tag.Name[4..] : tag.Name,
                        campaign.Content.Tags.Types.Single(type => type.Id == tag.OwnerType).Name));
            PrepareDetectionRuleTags();
        }
        return _detectionTagNames.TryGetValue((owner, index), out var name) ? name : null;
    }

    private void PrepareDetectionRuleTags()
    {
        if (_detectionRuleTags is not null) return;
        _detectionRuleTags = new(ReferenceEqualityComparer.Instance);
        var rules = campaign.Content.RuntimeRules;
        foreach (var initial in campaign.Content.InitialValues)
        {
            object? owner = null;
            if (initial.OwnerSection == "extended" && initial.OwnerId == "globals")
            {
                _detectionModTags[initial.TagName] = initial.Value;
                continue;
            }
            if (initial.OwnerSection == "ufos" && rules.Ufos.TryGet(initial.OwnerId, out var ufo))
                owner = rules.Ufos[ufo].Value;
            if (initial.OwnerSection == "crafts" && rules.Crafts.TryGet(initial.OwnerId, out var craft))
                owner = rules.Crafts[craft].Value;
            if (owner is null) continue;
            if (!_detectionRuleTags.TryGetValue(owner, out var values))
                _detectionRuleTags.Add(owner, values = new(StringComparer.Ordinal));
            values[initial.TagName] = initial.Value;
        }
    }

    private int DetectionRuleTag(object owner, string name) =>
        _detectionRuleTags!.TryGetValue(owner, out var values) ? values.GetValueOrDefault(name) : 0;

    private sealed record DetectionTagName(string Qualified, string Bare, string Owner);
}

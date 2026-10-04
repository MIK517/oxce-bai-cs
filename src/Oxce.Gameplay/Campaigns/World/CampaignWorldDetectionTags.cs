using Oxce.Scripting.Api;
using Oxce.Scripting.Globals;
using Oxce.Scripting.Runtime;

namespace Oxce.Gameplay.Campaigns.World;

internal sealed partial class CampaignWorld
{
    private Dictionary<(ushort Owner, int Index), DetectionTagName>? _detectionTagNames;

    private bool DetectionEntityTagSupported(ScriptBindingDeclaration binding)
    {
        if (binding.Name is not ("Ufo.getTag" or "Craft.getTag")) return false;
        var owner = binding.Parameters[2].Type.Id.Value;
        for (var index = 0; index < campaign.Content.Tags.Types.Count; index++)
        {
            var type = campaign.Content.Tags.Types[index];
            if (type.Id.Value == owner && (binding.Name == "Ufo.getTag" ? type.Name == "Ufo" : type.Name == "Craft")) return true;
        }
        return false;
    }

    private ScriptBindingResult InvokeDetectionEntityTag(ScriptBindingDeclaration binding, Span<ScriptRuntimeValue> args)
    {
        var value = 0;
        if (GetDetectionTagName(binding.Parameters[2].Type.Id.Value, args[2].Scalar) is { } tag)
        {
            if (args[0].Reference is UfoSnapshot ufo)
            {
                for (var index = 0; index < ufo.ScriptValues.Count; index++)
                {
                    var entry = ufo.ScriptValues[index];
                    if (entry.TagName == tag.Qualified || entry.TagName == tag.Bare) { value = entry.Value; break; }
                }
            }
            else if (args[0].Reference is CampaignState.CraftState craft)
                value = craft.ScriptValues?.Get(tag.Qualified) ?? 0;
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
                    new DetectionTagName(tag.Name, tag.Name.StartsWith("Tag.", StringComparison.Ordinal) ? tag.Name[4..] : tag.Name));
        }
        return _detectionTagNames.TryGetValue((owner, index), out var name) ? name : null;
    }

    private sealed record DetectionTagName(string Qualified, string Bare);
}

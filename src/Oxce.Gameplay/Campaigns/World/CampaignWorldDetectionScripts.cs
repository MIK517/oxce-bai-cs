using Oxce.Mods.Rulesets.Runtime;
using Oxce.Scripting;
using Oxce.Scripting.Compilation;
using Oxce.Scripting.Events;
using Oxce.Scripting.Runtime;

namespace Oxce.Gameplay.Campaigns.World;

internal sealed partial class CampaignWorld
{
    private readonly Dictionary<(string Rule, bool Craft), DetectionScript> _detectionScripts = [];
    private readonly Dictionary<bool, ScriptProgram> _detectionDefaults = [];
    private readonly ScriptExecutionFrame _detectionFrame = new();
    private readonly ScriptRuntimeValue[] _detectionInputs = new ScriptRuntimeValue[9];
    private readonly ScriptRuntimeValue[] _detectionOutputs = new ScriptRuntimeValue[2];

    private string? DetectionScriptsReason(string ruleId) =>
        GetDetectionScript(ruleId, false).Unavailable ?? GetDetectionScript(ruleId, true).Unavailable;

    private DetectionScript GetDetectionScript(string ruleId, bool fromCraft)
    {
        var key = (ruleId, fromCraft);
        if (_detectionScripts.TryGetValue(key, out var cached)) return cached;
        var rules = campaign.Content.RuntimeRules;
        var rule = rules.Ufos[rules.Ufos.GetRequired(ruleId)].Value;
        var parser = fromCraft ? "detectUfoFromCraft" : "detectUfoFromBase";
        var artifact = rule.Scripts.Select(handle => rules.Scripts[handle].Value.Artifact)
            .FirstOrDefault(candidate => candidate.ParserName == parser);
        var plan = campaign.Content.EventPlans.FirstOrDefault(candidate => candidate.ParserName == parser)?.Plan;
        if (artifact is null && (plan is null || plan.Count == 0))
        {
            var identity = new DetectionScript(null, null, null);
            _detectionScripts.Add(key, identity);
            return identity;
        }
        if (!_detectionDefaults.TryGetValue(fromCraft, out var fallback))
        {
            fallback = ScriptCompiler.Compile("return detection_type detection_chance;",
                ScriptParserDefinition.FromCatalog(parser)).Program
                ?? throw new InvalidOperationException("Could not compile identity detection script.");
            _detectionDefaults.Add(fromCraft, fallback);
        }
        var program = artifact?.Program ?? fallback;
        var reason = plan is not null && plan.Count + 1 > ScriptLimits.DefaultMaximumEventExecutions
            ? "Detection script exceeds the supported event execution limit." : null;
        foreach (var candidate in (plan?.Before.Select(item => item.Program) ?? [])
            .Append(program).Concat(plan?.After.Select(item => item.Program) ?? []))
        {
            reason ??= WorldDetectionScriptSafety.UnavailableReason(candidate, static _ => false);
            _detectionFrame.Prepare(candidate);
        }
        var result = new DetectionScript(program, plan, reason);
        _detectionScripts.Add(key, result);
        return result;
    }

    private (UfoDetectionResult Type, int Chance) RunDetectionScript(
        UfoSnapshot ufo, WorldUfoDetectionInputs caller, CampaignState.CraftState? craft = null)
    {
        var script = GetDetectionScript(ufo.RuleId, craft is not null);
        if (script.Program is not { } program) return (caller.Type, caller.Chance);
        if (script.Unavailable is not null) throw new InvalidOperationException("Detection script was not preflighted.");
        var inputIndex = 0;
        foreach (var register in program.Registers)
        {
            if (!register.IsInput) continue;
            _detectionInputs[inputIndex++] = register.Name switch
            {
                "already_tracked" => ScriptRuntimeValue.FromScalar(caller.AlreadyTracked ? 1 : 0),
                "distance" => ScriptRuntimeValue.FromScalar(caller.Distance),
                "radar_total_strength" => ScriptRuntimeValue.FromScalar(caller.RadarTotalStrength),
                "radar_max_distance" => ScriptRuntimeValue.FromScalar(caller.RadarMaxDistance),
                "hyperwave_total_strength" => ScriptRuntimeValue.FromScalar(caller.HyperwaveTotalStrength),
                "hyperwave_max_distance" => ScriptRuntimeValue.FromScalar(caller.HyperwaveMaxDistance),
                "ufo" => ScriptRuntimeValue.FromReference(ufo),
                "craft" => ScriptRuntimeValue.FromReference(craft),
                "geoscape_game" => ScriptRuntimeValue.FromReference(campaign),
                "rules" => ScriptRuntimeValue.FromReference(campaign.Content.RuntimeRules),
                _ => throw new InvalidOperationException("Unknown detection caller input."),
            };
        }
        _detectionOutputs[0] = ScriptRuntimeValue.FromScalar((int)caller.Type);
        _detectionOutputs[1] = ScriptRuntimeValue.FromScalar(caller.Chance);
        try
        {
            var inputs = _detectionInputs.AsSpan(0, inputIndex);
            if (script.Plan is { } plan)
            {
                var outcome = ScriptEventRunner.Execute(plan, program, _detectionOutputs, _detectionOutputs,
                    _detectionFrame, inputs: inputs);
                if (!outcome.Succeeded) throw new InvalidOperationException($"Preflighted detection event failed: {outcome.FailureMessage}");
            }
            else
            {
                var outcome = ScriptVm.ExecuteWithInputs(program, _detectionOutputs, inputs, _detectionOutputs, _detectionFrame);
                if (!outcome.Succeeded) throw new InvalidOperationException($"Preflighted detection script failed: {outcome.FailureMessage}");
            }
            return ((UfoDetectionResult)_detectionOutputs[0].Scalar, _detectionOutputs[1].Scalar);
        }
        finally { Array.Clear(_detectionInputs); Array.Clear(_detectionOutputs); }
    }

    private sealed record DetectionScript(ScriptProgram? Program, ScriptEventPlan? Plan, string? Unavailable);
}

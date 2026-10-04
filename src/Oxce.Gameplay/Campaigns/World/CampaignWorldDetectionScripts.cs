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
        var built = BuildDetectionScript(ruleId, fromCraft);
        _detectionScripts.Add(key, built);
        return built;
    }

    private DetectionScript BuildDetectionScript(string ruleId, bool fromCraft)
    {
        var rules = campaign.Content.RuntimeRules;
        var rule = rules.Ufos[rules.Ufos.GetRequired(ruleId)].Value;
        var parser = fromCraft ? "detectUfoFromCraft" : "detectUfoFromBase";
        var artifact = rule.Scripts.Select(handle => rules.Scripts[handle].Value.Artifact)
            .FirstOrDefault(candidate => candidate.ParserName == parser);
        var plan = campaign.Content.EventPlans.FirstOrDefault(candidate => candidate.ParserName == parser)?.Plan;
        if (artifact is null && (plan is null || plan.Count == 0))
        {
            var identity = new DetectionScript(null, null, null, ScriptHostBindings.Empty, [], 0, 0, 0, false, false, false);
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
        var builder = new ScriptHostBindingsBuilder();
        HashSet<int> providers = [];
        long stepBound = 0;
        var radarQueries = 0;
        var ufoTagQueries = 0;
        var shieldStats = false;
        var damageStats = false;
        var damagePercentage = false;
        foreach (var candidate in (plan?.Before.Select(item => item.Program) ?? [])
            .Append(program).Concat(plan?.After.Select(item => item.Program) ?? []))
        {
            var candidateReason = WorldDetectionScriptSafety.UnavailableReason(candidate, DetectionBindingSupported, out var visits);
            reason ??= candidateReason;
            if (candidateReason is not null) continue;
            stepBound += visits.Sum(static count => (long)count);
            for (var instructionIndex = 0; instructionIndex < candidate.Instructions.Count; instructionIndex++)
            {
                var instruction = candidate.Instructions[instructionIndex];
                if ((CoreScriptOperation)instruction.Operation.Value == CoreScriptOperation.HostCall)
                {
                    var bindingId = instruction.Operands[0].Scalar;
                    var declaration = candidate.Bindings.First(binding => binding.Id.Value == bindingId);
                    var bindingName = declaration.Name;
                    if (bindingName == "Ufo.getTag" && campaign.Content.Tags.Types.Any(type =>
                        type.Id.Value == declaration.Parameters[2].Type.Id.Value && type.Name == "Ufo")) ufoTagQueries += visits[instructionIndex];
                    if (bindingName is "Craft.Stats.getRadarRange" or "Craft.Stats.getRadarChance") radarQueries += visits[instructionIndex];
                    if (bindingName is "Ufo.getDamageMax" or "Ufo.getDamagePercentage") damageStats = true;
                    if (bindingName == "Ufo.getDamagePercentage") damagePercentage = true;
                    if (bindingName is "Ufo.getShieldMax" or "Ufo.getShieldPercentage") shieldStats = true;
                }
            }
            foreach (var binding in candidate.Bindings)
                if (DetectionBindingSupported(binding) && providers.Add(binding.Id.Value))
                {
                    if (DetectionTagSupported(binding)) _ = GetDetectionTagName(binding.Parameters[2].Type.Id.Value, 0);
                    builder.Add(binding.Id, (_, arguments) => InvokeDetectionBinding(binding, arguments));
                }
            _detectionFrame.Prepare(candidate);
        }
        var result = new DetectionScript(program, plan, reason, builder.Build(), program.Registers.Where(static register => register.IsInput).Select(static register => register.Name).ToArray(), stepBound, radarQueries, ufoTagQueries, shieldStats, damageStats, damagePercentage);
        return result;
    }

    private (UfoDetectionResult Type, int Chance) RunDetectionScript(
        UfoSnapshot ufo, WorldUfoDetectionInputs caller, CampaignState.CraftState? craft = null)
    {
        var script = GetDetectionScript(ufo.RuleId, craft is not null);
        if (script.Program is not { } program) return (caller.Type, caller.Chance);
        if (script.Unavailable is not null) throw new InvalidOperationException("Detection script was not preflighted.");
        if (script.DamageStats)
        {
            var rules = campaign.Content.RuntimeRules;
            var rule = rules.Ufos[rules.Ufos.GetRequired(ufo.RuleId)].Value;
            _detectionDamageMaximum = checked(rule.Stats.DamageMaximum + rule.RaceBonus(FindMission(ufo.MissionId)!.Race).DamageMaximum);
        }
        if (script.ShieldStats)
        {
            var rules = campaign.Content.RuntimeRules;
            var rule = rules.Ufos[rules.Ufos.GetRequired(ufo.RuleId)].Value;
            _detectionShieldCapacity = checked(rule.Stats.ShieldCapacity + rule.RaceBonus(FindMission(ufo.MissionId)!.Race).ShieldCapacity);
        }
        var inputIndex = 0;
        foreach (var inputName in script.InputNames)
        {
            _detectionInputs[inputIndex++] = inputName switch
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
                    _detectionFrame, hostBindings: script.Providers, inputs: inputs);
                if (!outcome.Succeeded) throw new InvalidOperationException($"Preflighted detection event failed: {outcome.FailureMessage}");
            }
            else
            {
                var outcome = ScriptVm.ExecuteWithInputs(program, _detectionOutputs, inputs, _detectionOutputs, _detectionFrame, hostBindings: script.Providers);
                if (!outcome.Succeeded) throw new InvalidOperationException($"Preflighted detection script failed: {outcome.FailureMessage}");
            }
            return ((UfoDetectionResult)_detectionOutputs[0].Scalar, _detectionOutputs[1].Scalar);
        }
        finally { Array.Clear(_detectionInputs); Array.Clear(_detectionOutputs); }
    }

    private string? ReserveDetectionScriptWork(string ruleId, CampaignTimeTrigger highest,
        ref long work, ref int craftSources, ref int weaponSlots, int ufoTagCount = 0, string? race = null, int damage = 0)
    {
        var fromBase = GetDetectionScript(ruleId, false);
        var fromCraft = GetDetectionScript(ruleId, true);
        if (fromBase.DamageStats || fromCraft.DamageStats)
        {
            var rules = campaign.Content.RuntimeRules;
            var rule = rules.Ufos[rules.Ufos.GetRequired(ruleId)].Value;
            var maximum = (long)rule.Stats.DamageMaximum + rule.RaceBonus(race ?? "").DamageMaximum;
            if (maximum is < int.MinValue or > int.MaxValue)
                return "Detection UFO damage maximum exceeds the supported range.";
            if (fromBase.DamagePercentage || fromCraft.DamagePercentage)
            {
                var percentage = Math.Floor((double)damage / maximum * 100);
                if (!double.IsFinite(percentage) || percentage is < int.MinValue or > int.MaxValue)
                    return "Detection UFO damage percentage exceeds the supported range.";
            }
        }
        work += (fromBase.StepBound + (long)fromBase.UfoTagQueries * ufoTagCount) * campaign.BaseStates.Count;
        if (fromCraft.StepBound != 0)
        {
            if (craftSources < 0)
            {
                craftSources = 0;
                foreach (var owner in campaign.BaseStates)
                {
                    foreach (var craft in owner.Crafts)
                    {
                        craftSources++;
                        weaponSlots = Math.Max(weaponSlots, craft.Logistics?.Weapons.Count ?? 0);
                    }
                    if (highest < CampaignTimeTrigger.OneHour) continue;
                    foreach (var transfer in owner.Transfers)
                        if (!transfer.Delivered && transfer.Hours <= 1 && transfer.Craft is { } incoming)
                        {
                            craftSources++;
                            weaponSlots = Math.Max(weaponSlots, incoming.Logistics?.Weapons.Count ?? 0);
                        }
                }
                // Fresh research/production craft have no auto-patrol order and cannot
                // join this detection pass. Existing/arriving orders can relaunch.
            }
            work += (fromCraft.StepBound + (long)fromCraft.RadarQueries * weaponSlots + (long)fromCraft.UfoTagQueries * ufoTagCount) * craftSources;
        }
        return work > MaximumAlienBaseChecks ? "Detection script work exceeds the supported half-hour limit." : null;
    }

    private sealed record DetectionScript(ScriptProgram? Program, ScriptEventPlan? Plan, string? Unavailable,
        ScriptHostBindings Providers, string[] InputNames, long StepBound, int RadarQueries, int UfoTagQueries, bool ShieldStats, bool DamageStats, bool DamagePercentage);
}

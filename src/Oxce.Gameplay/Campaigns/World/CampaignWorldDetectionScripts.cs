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

    private static readonly DetectionScript IdentityDetectionScript =
        new(null, null, null, ScriptHostBindings.Empty, [], 0, 0, 0, false, false, false, [], [], false);

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
            return IdentityDetectionScript;
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
        var craftStatQueries = 0;
        var ufoTagQueries = 0;
        var shieldStats = false;
        var damageStats = false;
        var damagePercentage = false;
        var craftDamagePercentage = false;
        HashSet<int> ufoStats = [];
        HashSet<string> craftStats = [];
        foreach (var candidate in (plan?.Before.Select(item => item.Program) ?? [])
            .Append(program).Concat(plan?.After.Select(item => item.Program) ?? []))
        {
            var candidateReason = WorldDetectionScriptSafety.UnavailableReason(candidate, DetectionBindingSupported, out var visits, campaign.CountryStates.Count);
            reason ??= candidateReason;
            if (candidateReason is not null) continue;
            reason ??= WorldDetectionDamageQueries.UnavailableReason(candidate, rules, visits, MaximumAlienBaseChecks);
            reason ??= WorldDetectionInventoryCosts.UnavailableReason(candidate, rules, visits, MaximumAlienBaseChecks);
            stepBound += visits.Sum(static count => (long)count);
            var nullReferences = candidate.Bindings.Any(binding => DetectionInventoryRole(binding.Name) is { } role &&
                !rules.Inventories.TryGet(role, out _)) ? WorldDetectionScriptSafety.GuaranteedNullReferences(candidate) : null;
            for (var instructionIndex = 0; instructionIndex < candidate.Instructions.Count; instructionIndex++)
            {
                if (visits[instructionIndex] == 0) continue;
                var instruction = candidate.Instructions[instructionIndex];
                if ((CoreScriptOperation)instruction.Operation.Value == CoreScriptOperation.HostCall)
                {
                    var bindingId = instruction.Operands[0].Scalar;
                    var declaration = candidate.Bindings.First(binding => binding.Id.Value == bindingId);
                    var bindingName = declaration.Name;
                    if (DetectionInventoryRole(bindingName) is { } inventoryRole &&
                        !rules.Inventories.TryGet(inventoryRole, out _) &&
                        instruction.Operands[1] != ScriptOperand.IntegerValue(0) &&
                        !(instruction.Operands[1].Kind == ScriptOperandKind.Register &&
                          nullReferences!.Contains(instruction.Operands[1].Scalar)))
                        reason ??= $"Detection script requires missing inventory {inventoryRole}.";
                    if (bindingName.StartsWith("Ufo.Stats.", StringComparison.Ordinal) &&
                        DetectionUfoStatIndex(bindingName) is >= 0 and var field) ufoStats.Add(field);
                    if (bindingName == "Ufo.getTag" && campaign.Content.Tags.Types.Any(type =>
                        type.Id.Value == declaration.Parameters[2].Type.Id.Value && type.Name == "Ufo")) ufoTagQueries += visits[instructionIndex];
                    if (bindingName.StartsWith("Craft.Stats.", StringComparison.Ordinal) &&
                        DetectionCraftStatKey(bindingName) is { } craftKey)
                    {
                        craftStats.Add(craftKey);
                        craftStatQueries += visits[instructionIndex];
                    }
                    if (bindingName is "Craft.getDamageMax" or "Craft.getDamagePercentage" or "Craft.getShieldMax" or "Craft.getShieldPercentage")
                    {
                        craftStats.Add(bindingName is "Craft.getDamageMax" or "Craft.getDamagePercentage" ? "damageMax" : "shieldCapacity");
                        craftStatQueries += visits[instructionIndex];
                    }
                    if (bindingName == "Craft.getDamagePercentage") craftDamagePercentage = true;
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
        var result = new DetectionScript(program, plan, reason, builder.Build(), program.Registers.Where(static register => register.IsInput).Select(static register => register.Name).ToArray(), stepBound, craftStatQueries, ufoTagQueries, shieldStats, damageStats, damagePercentage, ufoStats.Order().ToArray(), craftStats.Order(StringComparer.Ordinal).ToArray(), craftDamagePercentage);
        return result;
    }

    private (UfoDetectionResult Type, int Chance) RunDetectionScript(
        UfoSnapshot ufo, WorldUfoDetectionInputs caller, CampaignState.CraftState? craft = null)
    {
        var script = GetDetectionScript(ufo.RuleId, craft is not null);
        if (script.Program is not { } program) return (caller.Type, caller.Chance);
        if (script.Unavailable is not null) throw new InvalidOperationException("Detection script was not preflighted.");
        if (script.UfoStats.Length != 0)
        {
            var rules = campaign.Content.RuntimeRules;
            var rule = rules.Ufos[rules.Ufos.GetRequired(ufo.RuleId)].Value;
            _detectionUfoStats = rule.Stats;
            _detectionUfoBonus = rule.RaceBonus(FindMission(ufo.MissionId)!.Race);
        }
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
        if (craftSources < 0) ForecastDetectionCraftSources(highest, out craftSources, out weaponSlots);
        var fromBase = campaign.BaseStates.Count == 0 ? IdentityDetectionScript : GetDetectionScript(ruleId, false);
        var fromCraft = craftSources == 0 ? IdentityDetectionScript : GetDetectionScript(ruleId, true);
        if ((fromBase.Unavailable ?? fromCraft.Unavailable) is { } unavailable) return unavailable;
        // Reserve contact and host-scan work before evaluating consumed fields.
        work += (fromBase.StepBound + (long)fromBase.UfoTagQueries * ufoTagCount) * campaign.BaseStates.Count;
        work += (fromCraft.StepBound + (long)fromCraft.CraftStatQueries * weaponSlots + (long)fromCraft.UfoTagQueries * ufoTagCount) * craftSources;
        if (work > MaximumAlienBaseChecks) return "Detection script work exceeds the supported half-hour limit.";
        if (fromBase.UfoStats.Length != 0 || fromCraft.UfoStats.Length != 0)
        {
            var rules = campaign.Content.RuntimeRules;
            var rule = rules.Ufos[rules.Ufos.GetRequired(ruleId)].Value;
            var bonus = rule.RaceBonus(race ?? "");
            foreach (var field in fromBase.UfoStats)
                if ((long)DetectionUfoStat(rule.Stats, field) + DetectionUfoStat(bonus, field) is < int.MinValue or > int.MaxValue)
                    return "Detection UFO statistics exceed the supported range.";
            foreach (var field in fromCraft.UfoStats)
                if ((long)DetectionUfoStat(rule.Stats, field) + DetectionUfoStat(bonus, field) is < int.MinValue or > int.MaxValue)
                    return "Detection UFO statistics exceed the supported range.";
        }
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
        if (fromCraft.StepBound != 0)
        {
            if (fromCraft.CraftStats.Length != 0)
                foreach (var owner in campaign.BaseStates)
                {
                    foreach (var craft in owner.Crafts)
                        if (DetectionCraftStatsReason(fromCraft, campaign.Content.RuntimeRules.Crafts[craft.Rule].Value,
                            craft.Logistics) is { } reason) return reason;
                    if (highest < CampaignTimeTrigger.OneHour) continue;
                    foreach (var transfer in owner.Transfers)
                        if (!transfer.Delivered && transfer.Hours <= 1 && transfer.Craft is { } incoming &&
                            DetectionCraftStatsReason(fromCraft, campaign.Content.RuntimeRules.Crafts[
                                campaign.Content.RuntimeRules.Crafts.GetRequired(incoming.RuleId)].Value,
                                incoming.Logistics) is { } reason) return reason;
                }
        }
        return null;
    }

    private void ForecastDetectionCraftSources(CampaignTimeTrigger highest, out int craftSources, out int weaponSlots)
    {
        craftSources = 0;
        weaponSlots = 0;
        var rules = campaign.Content.RuntimeRules;
        foreach (var owner in campaign.BaseStates)
        {
            foreach (var craft in owner.Crafts)
                if (PossibleDetectionCraft(rules.Crafts[craft.Rule].Value, craft.Logistics))
                {
                    craftSources++;
                    weaponSlots = Math.Max(weaponSlots, craft.Logistics?.Weapons.Count ?? 0);
                }
            if (highest < CampaignTimeTrigger.OneHour) continue;
            foreach (var transfer in owner.Transfers)
                if (!transfer.Delivered && transfer.Hours <= 1 && transfer.Craft is { } incoming &&
                    PossibleDetectionCraft(rules.Crafts[rules.Crafts.GetRequired(incoming.RuleId)].Value, incoming.Logistics))
                {
                    craftSources++;
                    weaponSlots = Math.Max(weaponSlots, incoming.Logistics?.Weapons.Count ?? 0);
                }
        }
        // Fresh research/production craft have no auto-patrol order. Existing
        // orders can relaunch after servicing or delivery of item-based fuel.
    }

    private string? DetectionCraftStatsReason(DetectionScript script, RuntimeCraftRule rule, CraftLogisticsState? state)
    {
        // Grounded craft without an auto-patrol order cannot become callers in
        // this tick. Servicing and due transfers can relaunch an existing order.
        if (!PossibleDetectionCraft(rule, state)) return null;
        foreach (var key in script.CraftStats)
            if (DetectionCraftStat(rule, state, key) is < int.MinValue or > int.MaxValue)
                return "Detection craft statistics exceed the supported range.";
        if (script.CraftDamagePercentage)
        {
            var maximum = DetectionCraftStat(rule, state, "damageMax");
            var percentage = Math.Floor((double)(state?.Damage ?? 0) / maximum * 100);
            // Repair can reduce damage before this pass; zero is the other endpoint.
            if (maximum == 0 || !double.IsFinite(percentage) || percentage is < int.MinValue or > int.MaxValue)
                return "Detection craft damage percentage exceeds the supported range.";
        }
        return null;
    }

    private static bool PossibleDetectionCraft(RuntimeCraftRule rule, CraftLogisticsState? state) =>
        state is { Status: "STR_OUT" } || state is { IsAutoPatrolling: true } && rule.AutoPatrol;

    private sealed record DetectionScript(ScriptProgram? Program, ScriptEventPlan? Plan, string? Unavailable,
        ScriptHostBindings Providers, string[] InputNames, long StepBound, int CraftStatQueries, int UfoTagQueries, bool ShieldStats, bool DamageStats, bool DamagePercentage, int[] UfoStats, string[] CraftStats, bool CraftDamagePercentage);
}

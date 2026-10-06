using Oxce.Mods.Rulesets.Content;
using Oxce.Mods.Rulesets.Runtime;
using Oxce.Gameplay.Campaigns.World;
using Oxce.Scripting.Compilation;
using Oxce.Scripting.Api;
using Oxce.Scripting.Runtime;
using Oxce.Scripting;

namespace Oxce.Gameplay.Campaigns;

public sealed partial class CampaignState
{
    private readonly ScriptExecutionFrame _priceFrame = new();
    private readonly Dictionary<string, ScriptProgram> _priceDefaults = new(StringComparer.Ordinal);
    private readonly Dictionary<string, PriceItemReference> _priceItemReferences = new(StringComparer.Ordinal);
    private readonly ScriptRuntimeValue[] _priceInputs = new ScriptRuntimeValue[5];
    private readonly ScriptRuntimeValue[] _priceOutput = new ScriptRuntimeValue[1];
    private readonly Dictionary<(string Id, bool Buying), PriceInvocation?> _priceInvocations = [];
    // Programs are parser-specific, so the program instance identifies a shared invocation.
    private readonly Dictionary<ScriptProgram, PriceInvocation> _priceSharedInvocations = new(ReferenceEqualityComparer.Instance);
    /// <summary>The forecast and boxed time of the price invocation currently executing.</summary>
    private (PriceForecastContext Forecast, object Time)? _priceCall;

    private int ItemPrice(string id, RuntimeItemRule rule, bool buying, out string? unavailable, PriceForecastContext? forecast = null)
    {
        unavailable = null;
        var baseCost = buying ? rule.CostBuy : rule.CostSell;
        var coefficient = (buying ? _content.RuntimeRules.Campaign.BuyPriceCoefficients : _content.RuntimeRules.Campaign.SellPriceCoefficients)[(int)Difficulty];
        var adjusted = StrategicLogisticsMath.AdjustedItemPrice(baseCost, coefficient);
        if (PriceInvocationFor(id, rule, buying) is not { } invocation) return adjusted;
        // Current-price callers have the same bounded, read-only execution as
        // future production callers, with today's clock and research answers.
        forecast ??= new(Time, DaysPassed, false, 0);
        // The cached setup is replayed with its original work charges and refusal order.
        for (var index = 0; index < invocation.Programs.Length; index++)
        {
            var check = invocation.Checks[index];
            if ((forecast.Work += check.Charge) > 1_000_000)
            {
                unavailable = "Production price forecasts exceed the supported work limit.";
                return adjusted;
            }
            _priceFrame.Prepare(invocation.Programs[index]);
            foreach (var binding in check.Bindings)
            {
                if (binding.Tag && (forecast.Work += _content.Tags.Types.Count) > 1_000_000)
                {
                    unavailable = "Production price tag metadata exceeds the supported work limit.";
                    return adjusted;
                }
                if (!binding.Supported)
                {
                    unavailable = $"Price script requires unsupported binding {binding.Name}.";
                    return adjusted;
                }
            }
        }
        _priceInputs[0] = ScriptRuntimeValue.FromScalar(baseCost);
        _priceInputs[1] = ScriptRuntimeValue.FromScalar(coefficient);
        _priceInputs[2] = ScriptRuntimeValue.FromReference(this);
        _priceInputs[3] = ScriptRuntimeValue.FromReference(PriceItemReferenceFor(id, rule));
        _priceInputs[4] = ScriptRuntimeValue.FromReference(_content.RuntimeRules);
        _priceOutput[0] = ScriptRuntimeValue.FromScalar(adjusted);
        // Native getTime returns one pointer throughout this price invocation,
        // including its before/current/after programs. Box the value only once.
        var outer = _priceCall;
        _priceCall = new(forecast, forecast.Time);
        try
        {
            if (invocation.PlanCount + 1 > ScriptLimits.DefaultMaximumEventExecutions)
            {
                unavailable = "Production price forecasts exceed the supported event limit.";
                return adjusted;
            }
            foreach (var candidate in invocation.Programs)
            {
                var remaining = 1_000_000 - forecast.Work;
                if (remaining <= 0)
                {
                    unavailable = "Production price forecasts exceed the supported work limit.";
                    break;
                }
                var options = new ScriptExecutionOptions { MaximumSteps = (int)Math.Min(remaining, ScriptExecutionOptions.Default.MaximumSteps) };
                var result = ScriptVm.ExecuteWithInputs(candidate, _priceOutput, _priceInputs, _priceOutput, _priceFrame, options, invocation.Providers);
                forecast.Work += result.Steps;
                if (forecast.Work > 1_000_000)
                {
                    unavailable = "Production price forecasts exceed the supported work limit.";
                    break;
                }
                if (!result.Succeeded)
                {
                    unavailable = $"Price {(invocation.HasPlan ? "event" : "script")} failed: {result.FailureMessage ?? result.DiagnosticCode}.";
                    break;
                }
            }
            return _priceOutput[0].Scalar;
        }
        finally
        {
            _priceCall = outer;
            Array.Clear(_priceInputs);
            Array.Clear(_priceOutput);
        }
    }

    /// <summary>
    /// The invariant part of an item price invocation: its ordered programs, binding checks and host
    /// providers. Content is immutable for the campaign, so it is built once per item and parser,
    /// and shared by items that run the same program. Null when the item has no price script.
    /// </summary>
    private PriceInvocation? PriceInvocationFor(string id, RuntimeItemRule rule, bool buying)
    {
        if (_priceInvocations.TryGetValue((id, buying), out var cached)) return cached;
        var parser = buying ? "buyCostItem" : "sellCostItem";
        var artifact = rule.Scripts.Select(h => _content.RuntimeRules.Scripts[h].Value.Artifact).FirstOrDefault(a => a.ParserName == parser);
        var plan = _content.EventPlans.FirstOrDefault(p => p.ParserName == parser)?.Plan;
        PriceInvocation? invocation = null;
        if (artifact is not null || plan is not null)
        {
            var program = artifact?.Program ?? PriceDefault(parser);
            if (!_priceSharedInvocations.TryGetValue(program, out invocation))
            {
                invocation = BuildPriceInvocation([.. (plan?.Before.Select(e => e.Program) ?? []), program,
                    .. (plan?.After.Select(e => e.Program) ?? [])], plan is not null ? plan.Count : 0, plan is not null);
                _priceSharedInvocations.Add(program, invocation);
            }
        }
        _priceInvocations.Add((id, buying), invocation);
        return invocation;
    }

    private PriceInvocation BuildPriceInvocation(ScriptProgram[] programs, int planCount, bool hasPlan)
    {
        var builder = new ScriptHostBindingsBuilder();
        var seen = new HashSet<int>();
        var checks = new PriceProgramCheck[programs.Length];
        for (var index = 0; index < programs.Length; index++)
        {
            var bindings = programs[index].Bindings;
            var bindingChecks = new PriceBindingCheck[bindings.Count];
            for (var bindingIndex = 0; bindingIndex < bindings.Count; bindingIndex++)
            {
                var binding = bindings[bindingIndex];
                var supported = PriceBindingSupported(binding);
                bindingChecks[bindingIndex] = new(binding.Name.EndsWith(".getTag", StringComparison.Ordinal), supported, binding.Name);
                // The first declaration of a binding ID serves every program, as before caching.
                if (supported && seen.Add(binding.Id.Value))
                    builder.Add(binding.Id, (_, args) => InvokePriceBinding(binding, args, _priceCall!.Value.Forecast, _priceCall.Value.Time));
            }
            checks[index] = new(1L + bindings.Count, bindingChecks);
        }
        return new PriceInvocation(programs, checks, planCount, hasPlan, builder.Build());
    }

    private ScriptProgram PriceDefault(string parser)
    {
        if (_priceDefaults.TryGetValue(parser, out var cached)) return cached;
        var program = _content.Scripts.FirstOrDefault(a => a.Scope == ContentScriptScope.Default && a.ParserName == parser)?.Program;
        program ??= ScriptCompiler.Compile("return cost_current;", ScriptParserDefinition.FromCatalog(parser)).Program
            ?? throw new InvalidOperationException("Could not compile identity price script.");
        _priceDefaults.Add(parser, program);
        return program;
    }

    private bool PriceBindingSupported(ScriptBindingDeclaration binding) =>
        PriceValueCopy(binding) || PriceTagSupported(binding) || PriceRuleQuerySupported(binding.Name) || PriceCountryQuerySupported(binding.Name) ||
            PriceInventoryQuerySupported(binding.Name) || PriceDamageQuerySupported(binding.Name) || PriceBindingNameSupported(binding.Name);

    private static bool PriceBindingNameSupported(string name) => WorldDetectionItemQueries.Supports(name) || name is
        "GeoscapeGame.difficultyLevel" or "GeoscapeGame.getDaysPassed" or "GeoscapeGame.getMonthsPassed" or
        "GeoscapeGame.getTime" or "GeoscapeGame.isResearched" or "RuleMod.getRuleItem" or "RuleMod.getRuleResearch" or
        "RuleResearch.getCost" or "RuleResearch.getPoints" or "RuleResearch.getLookup" or "RuleResearch.getNeededItem" or
        "RuleMod.getMaxViewDistance" or "RuleMod.getMaxDarknessToSeeUnits" or "RuleMod.getSmokeReduction" or
        "RuleItem.getType" or "RuleItem.hasCategory" or "Time.getDay" or "Time.getMonth" or
        "Time.getYear" or "Time.getHour" or "Time.getMinute" or "Time.getSecond" or "Time.getSecondsPastMidnight";

    private ScriptBindingResult InvokePriceBinding(ScriptBindingDeclaration binding, Span<ScriptRuntimeValue> args, PriceForecastContext forecast, object timeReference)
    {
        if (PriceValueCopy(binding))
        {
            args[0] = args[1];
            return ScriptBindingResult.Success;
        }
        if (binding.Name.EndsWith(".getTag", StringComparison.Ordinal)) return InvokePriceTag(binding, args, forecast);
        var name = binding.Name;
        if (PriceCountryQuerySupported(name)) return InvokePriceCountryQuery(name, args, forecast);
        if (PriceDamageQuerySupported(name)) return InvokePriceDamageQuery(name, args);
        if (PriceInventoryQuerySupported(name)) return InvokePriceInventoryQuery(name, args);
        if (PriceRuleQuerySupported(name)) return InvokePriceRuleQuery(name, args);
        if (name == "RuleMod.getSmokeReduction")
        {
            if (args[0].Reference is not RuntimeRuleCatalog)
                return ScriptBindingResult.Failure("Price smoke reduction requires a nonnull Mod receiver.");
            args[1] = ScriptRuntimeValue.FromScalar(unchecked(args[1].Scalar * _content.RuntimeRules.Campaign.MaximumViewDistance) / 60);
            return ScriptBindingResult.Success;
        }
        if (name == "GeoscapeGame.isResearched" && forecast is { Daily: true } &&
            args[0].Reference is not null && args[2].Reference is string queriedResearch)
        {
            if (!forecast.ResearchProjectionAttempted)
            {
                forecast.ResearchProjectionAttempted = true;
                var work = forecast.Work;
                if (ResearchPreflight.TryForecastDailyStocks(ref work, out var research, includeDiscoveries: true))
                    forecast.CompletedResearch = research.CompletedResearch;
                forecast.Work = work;
            }
            if (forecast.Work > 1_000_000)
                return ScriptBindingResult.Failure("Production price research forecasts exceed the supported work limit.");
            if (forecast.CompletedResearch is { } completed)
            {
                args[1] = ScriptRuntimeValue.FromScalar(completed.Contains(queriedResearch) ? 1 : 0);
                return ScriptBindingResult.Success;
            }
            if (forecast.GuaranteedResearch is null)
            {
                // Bound both existing research projections before they scan rule edges.
                forecast.Work += 8L * (_bases.Count + _completedResearch.Count + _researchRuleStatus.Count + _content.RuntimeRules.Research.Count);
                foreach (var owner in _bases) forecast.Work += 8L * owner.Research.Count;
                if (forecast.Work > 1_000_000) return ScriptBindingResult.Failure("Production price research forecasts exceed the supported work limit.");
                foreach (var entry in _content.RuntimeRules.Research.Rules)
                {
                    var rule = entry.Value;
                    forecast.Work += 8L * (rule.GetOneFree.Count + rule.Disables.Count + rule.GetOneFreeProtected.Count);
                    foreach (var group in rule.GetOneFreeProtected) forecast.Work += 8L * group.Topics.Count;
                    if (forecast.Work > 1_000_000) return ScriptBindingResult.Failure("Production price research forecasts exceed the supported work limit.");
                }
                forecast.GuaranteedResearch = ResearchPreflight.GuaranteedResearchAfterDailyProgress();
                forecast.PossibleResearch = ResearchPreflight.PossibleResearchAfterDailyProgress();
            }
            var guaranteed = forecast.GuaranteedResearch.Contains(queriedResearch);
            if (guaranteed != forecast.PossibleResearch!.Contains(queriedResearch))
                return ScriptBindingResult.Failure("Production prices require uncertain daily research forecasts.");
            args[1] = ScriptRuntimeValue.FromScalar(guaranteed ? 1 : 0);
            return ScriptBindingResult.Success;
        }
        var item = args[0].Reference as PriceItemReference;
        if (WorldDetectionItemQueries.Supports(name))
        {
            args[1] = ScriptRuntimeValue.FromScalar(item is null ? 0 : WorldDetectionItemQueries.Value(name, item.Rule));
            return ScriptBindingResult.Success;
        }
        if (name == "RuleItem.hasCategory")
        {
            if (forecast is not null && (forecast.Work += item?.Rule.Categories.Count ?? 0) > 1_000_000)
                return ScriptBindingResult.Failure("Production price category queries exceed the supported work limit.");
            args[1] = ScriptRuntimeValue.FromScalar(item?.Rule.Categories.Contains(args[2].Text ?? "", StringComparer.Ordinal) == true ? 1 : 0);
            return ScriptBindingResult.Success;
        }
        var researchRule = args[0].Reference is string topic && _content.RuntimeRules.Research.TryGet(topic, out var researchHandle)
            ? _content.RuntimeRules.Research[researchHandle].Value : null;
        var time = args[0].Reference is CampaignTime value ? value : (CampaignTime)timeReference;
        switch (name)
        {
            case "GeoscapeGame.getTime": args[1] = ScriptRuntimeValue.FromReference(args[0].Reference is null ? null : timeReference); break;
            case "RuleItem.getType": args[1] = ScriptRuntimeValue.FromText(item?.Id ?? ""); break;
            case "RuleMod.getRuleItem":
                var ruleId = args[2].Text ?? "";
                args[1] = ScriptRuntimeValue.FromReference(args[0].Reference is not null && ruleId != "STR_NONE" && _content.RuntimeRules.Items.TryGet(ruleId, out var handle)
                    ? PriceItemReferenceFor(ruleId, _content.RuntimeRules.Items[handle].Value) : null);
                break;
            case "RuleMod.getRuleResearch":
                var researchId = args[2].Text ?? "";
                args[1] = ScriptRuntimeValue.FromReference(args[0].Reference is not null && _content.RuntimeRules.Research.TryGet(researchId, out var topicHandle)
                    ? _content.RuntimeRules.Research.GetExternalId(topicHandle) : null);
                break;
            case "RuleResearch.getLookup":
                args[1] = ScriptRuntimeValue.FromReference(researchRule is not null && _content.RuntimeRules.Research.TryGet(researchRule.Lookup, out var lookup)
                    ? _content.RuntimeRules.Research.GetExternalId(lookup) : null);
                break;
            case "RuleResearch.getNeededItem":
                args[1] = ScriptRuntimeValue.FromReference(researchRule is { NeedItem: true, NeededItem: { } needed } && needed != "STR_NONE" &&
                    _content.RuntimeRules.Items.TryGet(needed, out var neededHandle)
                    ? PriceItemReferenceFor(needed, _content.RuntimeRules.Items[neededHandle].Value) : null);
                break;
            default:
                args[1] = ScriptRuntimeValue.FromScalar(args[0].Reference is null ? 0 : name switch
                {
                    "GeoscapeGame.difficultyLevel" => (int)Difficulty,
                    "RuleMod.getMaxViewDistance" => _content.RuntimeRules.Campaign.MaximumViewDistance,
                    "RuleMod.getMaxDarknessToSeeUnits" => _content.RuntimeRules.Campaign.MaximumDarknessToSeeUnits,
                    "GeoscapeGame.getDaysPassed" => forecast?.DaysPassed ?? DaysPassed,
                    "GeoscapeGame.getMonthsPassed" => MonthsPassed,
                    "GeoscapeGame.isResearched" => args[2].Reference is string research && _completedResearch.Contains(research) ? 1 : 0,
                    "RuleItem.getWeight" => item?.Rule.Weight ?? 0,
                    "RuleItem.getBattleType" => item?.Rule.BattleType ?? 0,
                    "RuleResearch.getCost" => researchRule?.Cost ?? 0,
                    "RuleResearch.getPoints" => researchRule?.Points ?? 0,
                    "Time.getDay" => time.Day,
                    "Time.getMonth" => time.Month,
                    "Time.getYear" => time.Year,
                    "Time.getHour" => time.Hour,
                    "Time.getMinute" => time.Minute,
                    "Time.getSecond" => time.Second,
                    "Time.getSecondsPastMidnight" => time.Hour * 3600 + time.Minute * 60 + time.Second,
                    _ => throw new InvalidOperationException("Price provider was not preflighted."),
                });
                break;
        }
        return ScriptBindingResult.Success;
    }

    private PriceItemReference PriceItemReferenceFor(string id, RuntimeItemRule rule)
    {
        if (!_priceItemReferences.TryGetValue(id, out var reference))
        {
            reference = new(id, rule);
            _priceItemReferences.Add(id, reference);
        }
        return reference;
    }

    private sealed record PriceItemReference(string Id, RuntimeItemRule Rule);

    private sealed record PriceInvocation(ScriptProgram[] Programs, PriceProgramCheck[] Checks, int PlanCount, bool HasPlan,
        ScriptHostBindings Providers);

    /// <summary>Setup work charged before a program runs, and its binding checks in declaration order.</summary>
    private sealed record PriceProgramCheck(long Charge, PriceBindingCheck[] Bindings);

    private readonly record struct PriceBindingCheck(bool Tag, bool Supported, string Name);

    private sealed class PriceForecastContext(CampaignTime time, int daysPassed, bool daily, long work)
    {
        public CampaignTime Time { get; } = time;
        public int DaysPassed { get; } = daysPassed;
        public bool Daily { get; } = daily;
        public long Work { get; set; } = work;
        public bool ResearchProjectionAttempted { get; set; }
        public IReadOnlySet<string>? CompletedResearch { get; set; }
        public IReadOnlySet<string>? GuaranteedResearch { get; set; }
        public IReadOnlySet<string>? PossibleResearch { get; set; }
        public Dictionary<CountryState, int>? CountryAlienActivity { get; set; }
    }
}

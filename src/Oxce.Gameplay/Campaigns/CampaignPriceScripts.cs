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

    private int ItemPrice(string id, RuntimeItemRule rule, bool buying, out string? unavailable, PriceForecastContext? forecast = null)
    {
        unavailable = null;
        var parser = buying ? "buyCostItem" : "sellCostItem";
        var baseCost = buying ? rule.CostBuy : rule.CostSell;
        var coefficient = (buying ? _content.RuntimeRules.Campaign.BuyPriceCoefficients : _content.RuntimeRules.Campaign.SellPriceCoefficients)[(int)Difficulty];
        var adjusted = StrategicLogisticsMath.AdjustedItemPrice(baseCost, coefficient);
        var artifact = rule.Scripts.Select(h => _content.RuntimeRules.Scripts[h].Value.Artifact).FirstOrDefault(a => a.ParserName == parser);
        var plan = _content.EventPlans.FirstOrDefault(p => p.ParserName == parser)?.Plan;
        if (artifact is null && plan is null) return adjusted;
        // Current-price callers have the same bounded, read-only execution as
        // future production callers, with today's clock and research answers.
        forecast ??= new(Time, DaysPassed, false, 0);
        var program = artifact?.Program ?? PriceDefault(parser);
        var programs = (plan?.Before.Select(e => e.Program) ?? []).Append(program).Concat(plan?.After.Select(e => e.Program) ?? []);
        // Native getTime returns one pointer throughout this price invocation,
        // including its before/current/after programs. Box the value only once.
        object timeReference = forecast.Time;
        var builder = new ScriptHostBindingsBuilder();
        var seen = new HashSet<int>();
        foreach (var candidate in programs)
        {
            if ((forecast.Work += 1L + candidate.Bindings.Count) > 1_000_000)
            {
                unavailable = "Production price forecasts exceed the supported work limit.";
                return adjusted;
            }
            _priceFrame.Prepare(candidate);
            foreach (var binding in candidate.Bindings)
            {
                if (binding.Name.EndsWith(".getTag", StringComparison.Ordinal) &&
                    (forecast.Work += _content.Tags.Types.Count) > 1_000_000)
                {
                    unavailable = "Production price tag metadata exceeds the supported work limit.";
                    return adjusted;
                }
                if (!PriceBindingSupported(binding))
                {
                    unavailable = $"Price script requires unsupported binding {binding.Name}.";
                    return adjusted;
                }
                if (seen.Add(binding.Id.Value)) builder.Add(binding.Id, (_, args) => InvokePriceBinding(binding, args, forecast, timeReference));
            }
        }
        var providers = builder.Build();
        _priceInputs[0] = ScriptRuntimeValue.FromScalar(baseCost);
        _priceInputs[1] = ScriptRuntimeValue.FromScalar(coefficient);
        _priceInputs[2] = ScriptRuntimeValue.FromReference(this);
        _priceInputs[3] = ScriptRuntimeValue.FromReference(PriceItemReferenceFor(id, rule));
        _priceInputs[4] = ScriptRuntimeValue.FromReference(_content.RuntimeRules);
        _priceOutput[0] = ScriptRuntimeValue.FromScalar(adjusted);
        try
        {
            if ((plan?.Count ?? 0) + 1 > ScriptLimits.DefaultMaximumEventExecutions)
            {
                unavailable = "Production price forecasts exceed the supported event limit.";
                return adjusted;
            }
            foreach (var candidate in programs)
            {
                var remaining = 1_000_000 - forecast.Work;
                if (remaining <= 0)
                {
                    unavailable = "Production price forecasts exceed the supported work limit.";
                    break;
                }
                var options = new ScriptExecutionOptions { MaximumSteps = (int)Math.Min(remaining, ScriptExecutionOptions.Default.MaximumSteps) };
                var result = ScriptVm.ExecuteWithInputs(candidate, _priceOutput, _priceInputs, _priceOutput, _priceFrame, options, providers);
                forecast.Work += result.Steps;
                if (forecast.Work > 1_000_000)
                {
                    unavailable = "Production price forecasts exceed the supported work limit.";
                    break;
                }
                if (!result.Succeeded)
                {
                    unavailable = $"Price {(plan is null ? "script" : "event")} failed: {result.FailureMessage ?? result.DiagnosticCode}.";
                    break;
                }
            }
            return _priceOutput[0].Scalar;
        }
        finally { Array.Clear(_priceInputs); Array.Clear(_priceOutput); }
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
        PriceValueCopy(binding) || PriceTagSupported(binding) || PriceRuleQuerySupported(binding.Name) ||
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
    }
}

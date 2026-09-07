using Oxce.Mods.Rulesets.Content;
using Oxce.Mods.Rulesets.Runtime;
using Oxce.Scripting.Compilation;
using Oxce.Scripting.Events;
using Oxce.Scripting.Runtime;

namespace Oxce.Gameplay.Campaigns;

public sealed partial class CampaignState
{
    private readonly ScriptExecutionFrame _priceFrame = new();
    private readonly Dictionary<string, ScriptProgram> _priceDefaults = new(StringComparer.Ordinal);
    private readonly ScriptRuntimeValue[] _priceInputs = new ScriptRuntimeValue[5];
    private readonly ScriptRuntimeValue[] _priceOutput = new ScriptRuntimeValue[1];

    private int ItemPrice(string id, RuntimeItemRule rule, bool buying, out string? unavailable)
    {
        unavailable = null;
        var parser = buying ? "buyCostItem" : "sellCostItem";
        var baseCost = buying ? rule.CostBuy : rule.CostSell;
        var coefficient = (buying ? _content.RuntimeRules.Campaign.BuyPriceCoefficients : _content.RuntimeRules.Campaign.SellPriceCoefficients)[(int)Difficulty];
        var adjusted = StrategicLogisticsMath.AdjustedItemPrice(baseCost, coefficient);
        var artifact = rule.Scripts.Select(h => _content.RuntimeRules.Scripts[h].Value.Artifact).FirstOrDefault(a => a.ParserName == parser);
        var plan = _content.EventPlans.FirstOrDefault(p => p.ParserName == parser)?.Plan;
        if (artifact is null && plan is null) return adjusted;
        var program = artifact?.Program ?? PriceDefault(parser);
        var programs = (plan?.Before.Select(e => e.Program) ?? []).Append(program).Concat(plan?.After.Select(e => e.Program) ?? []);
        var builder = new ScriptHostBindingsBuilder();
        var seen = new HashSet<int>();
        foreach (var candidate in programs)
        {
            _priceFrame.Prepare(candidate);
            foreach (var binding in candidate.Bindings)
            {
                if (!PriceBindingSupported(binding.Name))
                {
                    unavailable = $"Price script requires unsupported binding {binding.Name}.";
                    return adjusted;
                }
                if (seen.Add(binding.Id.Value)) builder.Add(binding.Id, (_, args) => InvokePriceBinding(binding.Name, args));
            }
        }
        var providers = builder.Build();
        _priceInputs[0] = ScriptRuntimeValue.FromScalar(baseCost);
        _priceInputs[1] = ScriptRuntimeValue.FromScalar(coefficient);
        _priceInputs[2] = ScriptRuntimeValue.FromReference(this);
        _priceInputs[3] = ScriptRuntimeValue.FromReference(new PriceItemReference(id, rule));
        _priceInputs[4] = ScriptRuntimeValue.FromReference(_content.RuntimeRules);
        _priceOutput[0] = ScriptRuntimeValue.FromScalar(adjusted);
        try
        {
            if (plan is null)
            {
                var result = ScriptVm.ExecuteWithInputs(program, _priceOutput, _priceInputs, _priceOutput, _priceFrame, hostBindings: providers);
                if (!result.Succeeded) unavailable = $"Price script failed: {result.FailureMessage ?? result.DiagnosticCode}.";
            }
            else
            {
                var result = ScriptEventRunner.Execute(plan, program, _priceOutput, _priceOutput, _priceFrame, hostBindings: providers, inputs: _priceInputs);
                if (!result.Succeeded) unavailable = $"Price event failed: {result.FailureMessage ?? result.DiagnosticCode}.";
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

    private static bool PriceBindingSupported(string name) => name is
        "GeoscapeGame.difficultyLevel" or "GeoscapeGame.getDaysPassed" or "GeoscapeGame.getMonthsPassed" or
        "GeoscapeGame.getTime" or "GeoscapeGame.isResearched" or "RuleMod.getRuleItem" or "RuleMod.getRuleResearch" or
        "RuleItem.getType" or "RuleItem.getWeight" or "RuleItem.getBattleType" or "Time.getDay" or "Time.getMonth" or
        "Time.getYear" or "Time.getHour" or "Time.getMinute" or "Time.getSecond" or "Time.getSecondsPastMidnight";

    private ScriptBindingResult InvokePriceBinding(string name, Span<ScriptRuntimeValue> args)
    {
        var item = args[0].Reference as PriceItemReference;
        var time = args[0].Reference is CampaignTime value ? value : Time;
        switch (name)
        {
            case "GeoscapeGame.getTime": args[1] = ScriptRuntimeValue.FromReference(args[0].Reference is null ? null : Time); break;
            case "RuleItem.getType": args[1] = ScriptRuntimeValue.FromText(item?.Id ?? ""); break;
            case "RuleMod.getRuleItem":
                var ruleId = args[2].Text ?? "";
                args[1] = ScriptRuntimeValue.FromReference(args[0].Reference is not null && _content.RuntimeRules.Items.TryGet(ruleId, out var handle)
                    ? new PriceItemReference(ruleId, _content.RuntimeRules.Items[handle].Value) : null);
                break;
            case "RuleMod.getRuleResearch":
                var researchId = args[2].Text ?? "";
                args[1] = ScriptRuntimeValue.FromReference(args[0].Reference is not null && _content.RuntimeRules.Research.TryGet(researchId, out _) ? researchId : null);
                break;
            default:
                args[1] = ScriptRuntimeValue.FromScalar(args[0].Reference is null ? 0 : name switch
                {
                    "GeoscapeGame.difficultyLevel" => (int)Difficulty,
                    "GeoscapeGame.getDaysPassed" => DaysPassed,
                    "GeoscapeGame.getMonthsPassed" => MonthsPassed,
                    "GeoscapeGame.isResearched" => args[2].Reference is string research && _completedResearch.Contains(research) ? 1 : 0,
                    "RuleItem.getWeight" => item?.Rule.Weight ?? 0,
                    "RuleItem.getBattleType" => item?.Rule.BattleType ?? 0,
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

    private sealed record PriceItemReference(string Id, RuntimeItemRule Rule);
}

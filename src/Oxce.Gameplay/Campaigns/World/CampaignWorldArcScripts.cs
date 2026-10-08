using Oxce.Mods.Rulesets.Runtime;

namespace Oxce.Gameplay.Campaigns.World;

/// <summary>Isolated monthly arc phase, before mission and event scripts.</summary>
internal interface ICampaignWorldArcScripts
{
    CampaignCommandResult Schedule(int score, long funds);
}

/// <summary>Internal fixture seam; campaign month rollover remains guarded.</summary>
internal sealed record ScheduleMonthlyWorldArcs(int Score, long Funds) : ICampaignCommand;

/// <summary>The article that GeoscapeState opens for one granted arc.</summary>
public sealed record CampaignArcUnlocked(string RuleId, string ArticleId) : ICampaignEvent;

internal sealed partial class CampaignWorld : ICampaignWorldArcScripts
{
    private void RegisterArcScripts(CampaignCapabilityRegistry registry)
    {
        registry.Operation<ICampaignWorldArcScripts>(this);
        registry.Command<ScheduleMonthlyWorldArcs>(command =>
            ((ICampaignWorldArcScripts)this).Schedule(command.Score, command.Funds));
    }

    CampaignCommandResult ICampaignWorldArcScripts.Schedule(int score, long funds)
    {
        var context = campaign.WorldScriptContext(score, funds, _strategy.MissionRuns);
        var eligible = campaign.Content.RuntimeRules.ArcScripts.Rules.Where(entry =>
            WorldScriptEligibility.Matches(entry.Value.Criteria, context)).ToArray();
        if (eligible.Length > 4096) return Blocked("Arc scripts exceed the supported work limit.");
        var research = campaign.ArcResearch;
        if (ArcScriptReason(eligible, research) is { } reason) return Blocked(reason);

        return RunArcScriptCommands(eligible, research);
    }

    private CampaignCommandResult RunArcScriptCommands(
        RuntimeRule<RuntimeArcScriptRule>[] eligible, ICampaignArcResearch research)
    {
        // GeoscapeState collects all successful odds before granting any research.
        var relevant = eligible.Where(entry => Percent(entry.Value.Criteria.ExecutionOdds)).ToArray();
        var events = new List<ICampaignEvent>();
        foreach (var entry in relevant)
        {
            var rule = entry.Value;
            var enabled = 0;
            RuntimeRuleReference<ResearchRuleFamily>? first = null;
            foreach (var choice in rule.Sequential)
            {
                if (research.IsResearched(choice.Id)) enabled++;
                else first ??= choice;
            }
            var random = rule.Random.Where(choice => !research.IsResearched(choice.Rule.Id)).ToArray();
            enabled += rule.Random.Count - random.Length;

            if (rule.MaximumArcs == -1 || rule.MaximumArcs > enabled)
                if (first is { } sequential)
                {
                    events.Add(new CampaignArcUnlocked(sequential.Id, research.Grant(sequential.Id)));
                    enabled++;
                }
            if ((rule.MaximumArcs == -1 || rule.MaximumArcs > enabled) &&
                WorldScriptSelection.Choose(random, campaign.Random) is { } weighted)
                events.Add(new CampaignArcUnlocked(weighted.Id, research.Grant(weighted.Id)));
        }
        return new CampaignCommandResult(events);
    }

    private static string? ArcScriptReason(
        IReadOnlyList<RuntimeRule<RuntimeArcScriptRule>> eligible, ICampaignArcResearch research,
        HashSet<string>? changedResearch = null)
    {
        var priorGrants = new HashSet<string>(StringComparer.Ordinal);
        var priorDisables = new HashSet<string>(StringComparer.Ordinal);
        var grantSlots = new List<IReadOnlySet<string>>();
        IReadOnlySet<string>? automaticResearch = null;
        long references = 0;
        foreach (var entry in eligible)
        {
            var rule = entry.Value;
            if (rule.Criteria.ExecutionOdds <= 0) continue;
            references += rule.Sequential.Count + rule.Random.Count;
            if (references > 100_000) return "Arc scripts exceed the supported reference limit.";
            var enabled = rule.Sequential.Count(choice => research.IsResearched(choice.Id) &&
                !priorDisables.Contains(choice.Id)) + rule.Random.Count(choice => research.IsResearched(choice.Rule.Id) &&
                !priorDisables.Contains(choice.Rule.Id));
            if (rule.MaximumArcs != -1 && rule.MaximumArcs <= enabled) continue;

            // Earlier scripts can expose a later sequential entry; this script selects its
            // own sequential and random candidates before either grant changes eligibility.
            var sequential = new HashSet<string>(StringComparer.Ordinal);
            var guaranteedSequential = false;
            foreach (var choice in rule.Sequential)
            {
                var researched = research.IsResearched(choice.Id);
                if (researched && !priorDisables.Contains(choice.Id)) continue;
                if (research.GrantReason(choice.Id) is { } reason) return reason;
                sequential.Add(choice.Id);
                guaranteedSequential |= !researched && !priorGrants.Contains(choice.Id);
                if (!priorGrants.Contains(choice.Id)) break;
            }
            var random = new HashSet<string>(StringComparer.Ordinal);
            if (rule.MaximumArcs == -1 || rule.MaximumArcs > enabled + (guaranteedSequential ? 1 : 0))
            {
                ulong weight = 0;
                foreach (var choice in rule.Random)
                {
                    if (research.IsResearched(choice.Rule.Id) && !priorDisables.Contains(choice.Rule.Id)) continue;
                    if (choice.Weight > (ulong)int.MaxValue - weight)
                        throw new InvalidDataException("Strategic script weights exceed the supported range.");
                    weight += choice.Weight;
                    if (choice.Weight == 0) continue;
                    if (research.GrantReason(choice.Rule.Id) is { } reason) return reason;
                    random.Add(choice.Rule.Id);
                }
            }
            if (sequential.Count != 0) grantSlots.Add(sequential);
            if (random.Count != 0) grantSlots.Add(random);
            var possible = new HashSet<string>(sequential, StringComparer.Ordinal);
            possible.UnionWith(random);
            foreach (var id in possible)
            {
                priorGrants.Add(id);
                priorDisables.UnionWith(research.PossibleDisables(id));
                if (research.Lookup(id) is { } lookup)
                {
                    priorGrants.Add(lookup);
                    priorDisables.UnionWith(research.PossibleDisables(lookup));
                }
            }
            if (possible.Count != 0)
            {
                automaticResearch ??= research.PossibleAutomaticResearch();
                if (automaticResearch.Count > 100_000)
                    return "Arc research exceeds the supported automatic-topic limit.";
                foreach (var automatic in automaticResearch)
                    if (priorGrants.Add(automatic))
                        priorDisables.UnionWith(research.PossibleDisables(automatic));
            }
        }
        changedResearch?.UnionWith(priorGrants);
        changedResearch?.UnionWith(priorDisables);
        if (grantSlots.Count != 0 && !research.ScoreCapacityFits(grantSlots)) return "Arc research score exceeds the supported range.";
        return grantSlots.Count != 0 && !research.DiaryCapacityFits(grantSlots)
            ? "Arc research diary exceeds its supported population or identity range." : null;
    }
}

using Oxce.Mods.Rulesets.Runtime;

namespace Oxce.Gameplay.Campaigns.World;

/// <summary>Tagged mission scheduling used by the future campaign-event payload caller.</summary>
internal interface ICampaignWorldAdhocScripts
{
    CampaignCommandResult Schedule(string eventRuleId, int score, long funds);
}

/// <summary>Internal fixture seam; due event payloads remain guarded.</summary>
internal sealed record ScheduleAdhocWorldMissions(string EventRuleId, int Score, long Funds) : ICampaignCommand;

internal sealed partial class CampaignWorld : ICampaignWorldAdhocScripts
{
    private void RegisterAdhocScripts(CampaignCapabilityRegistry registry)
    {
        registry.Operation<ICampaignWorldAdhocScripts>(this);
        registry.Command<ScheduleAdhocWorldMissions>(command =>
            ((ICampaignWorldAdhocScripts)this).Schedule(command.EventRuleId, command.Score, command.Funds));
    }

    CampaignCommandResult ICampaignWorldAdhocScripts.Schedule(string eventRuleId, int score, long funds)
    {
        var events = campaign.Content.RuntimeRules.Events;
        if (!events.TryGet(eventRuleId, out var eventHandle))
            return Blocked($"Event rule '{eventRuleId}' is unavailable.");
        var eventTags = events[eventHandle].Value.AdhocMissionScriptTags;
        if (eventTags.Count == 0) return new CampaignCommandResult([]);

        var state = campaign.WorldScriptContext(score, funds, _strategy.MissionRuns);
        var available = campaign.Content.RuntimeRules.AdhocScripts.Rules.Where(entry =>
            // GeoscapeState breaks after its first script tag, even if later tags also match.
            entry.Value.AdhocTags.Count != 0 &&
            eventTags.Contains(entry.Value.AdhocTags[0], StringComparer.Ordinal) &&
            WorldScriptEligibility.Matches(entry.Value.Criteria, state) &&
            (entry.Value.MaximumRuns == -1 ||
             entry.Value.MaximumRuns > _strategy.MissionsRun(entry.Value.VariableName))).ToArray();
        return ScheduleMissionScriptCommands(available, state.Month);
    }
}

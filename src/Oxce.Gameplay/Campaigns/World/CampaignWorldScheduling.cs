using Oxce.Mods.Rulesets.Runtime;

namespace Oxce.Gameplay.Campaigns.World;

/// <summary>Ordered determineAlienMissions caller. Integrated campaign month progression remains guarded.</summary>
internal interface ICampaignWorldScheduling
{
    CampaignCommandResult Schedule(bool isNewMonth);
}

/// <summary>Subsystem acceptance seam after month advancement; does not advance the calendar.</summary>
internal sealed record ScheduleWorldScripts(bool IsNewMonth = true) : ICampaignCommand;

/// <summary>Enters the strategic world after first-base placement, like GeoscapeState::init.</summary>
public sealed record EnterCampaignWorld : ICampaignCommand;

internal sealed partial class CampaignWorld : ICampaignWorldScheduling
{
    private void RegisterWorldScheduling(CampaignCapabilityRegistry registry)
    {
        registry.Operation<ICampaignWorldScheduling>(this);
        registry.Command<EnterCampaignWorld>(_ => EnterWorld());
        registry.Command<ScheduleWorldScripts>(command => ((ICampaignWorldScheduling)this).Schedule(command.IsNewMonth));
    }

    private CampaignCommandResult EnterWorld()
    {
        var calendar = campaign.InitialWorldCalendar;
        if (!calendar.IsPending) return new([]);
        InitialWorldEntry entry;
        WorldSchedulingInputs inputs;
        try
        {
            entry = calendar.Prepare();
            inputs = campaign.WorldSchedulingInputs(isNewMonth: true, schedulingMonth: 0);
        }
        catch (Exception error) when (error is OverflowException or InvalidDataException) { return Blocked(error.Message); }
        // Legacy month -1 UFOs bypass load validation. Entering month zero must not
        // turn an accepted legacy snapshot into a corrupt mission/trajectory graph.
        var rules = campaign.Content.RuntimeRules;
        foreach (var ufo in _ufos)
            if (!_missions.Any(mission => mission.Id == ufo.MissionId) ||
                !rules.UfoTrajectories.TryGet(ufo.TrajectoryId, out var trajectory) ||
                (uint)ufo.TrajectoryPoint >= (uint)rules.UfoTrajectories[trajectory].Value.Waypoints.Count)
                return Blocked("Initial world entry requires a valid UFO mission and trajectory.");
        var context = campaign.WorldScriptContext(inputs.Score, inputs.Funds, _strategy.MissionRuns) with { Month = 0 };
        if (WorldSchedulingReason(context, true, out var arcs) is { } reason) return Blocked(reason);
        calendar.Begin(entry);
        var result = RunWorldScheduling(context, true, arcs);
        // Reference charges full maintenance except HQ personnel AFTER scheduling.
        calendar.Finish(entry);
        return result;
    }

    CampaignCommandResult ICampaignWorldScheduling.Schedule(bool isNewMonth)
    {
        WorldSchedulingInputs inputs;
        try { inputs = campaign.WorldSchedulingInputs(isNewMonth); }
        catch (Exception error) when (error is OverflowException or InvalidDataException) { return Blocked(error.Message); }
        var context = campaign.WorldScriptContext(inputs.Score, inputs.Funds, _strategy.MissionRuns);
        if (WorldSchedulingReason(context, isNewMonth, out var arcs) is { } reason) return Blocked(reason);
        return RunWorldScheduling(context, isNewMonth, arcs);
    }

    private string? WorldSchedulingReason(WorldScriptContext context, bool isNewMonth,
        out RuntimeRule<RuntimeArcScriptRule>[] arcs)
    {
        var rules = campaign.Content.RuntimeRules;
        arcs = rules.ArcScripts.Rules.Where(entry => WorldScriptEligibility.Matches(entry.Value.Criteria, context)).ToArray();
        if (arcs.Length > 4096) return "Arc scripts exceed the supported work limit.";
        var changed = new HashSet<string>(StringComparer.Ordinal);
        if (ArcScriptReason(arcs, campaign.ArcResearch, changed) is { } arcReason) return arcReason;
        var runs = new Dictionary<string, long>(StringComparer.Ordinal);
        var markers = new Dictionary<string, long>(StringComparer.Ordinal);
        var missions = rules.MissionScripts.Rules.Where(entry =>
            WorldScriptEligibility.MayMatch(entry.Value.Criteria, context, changed, runs, markers) &&
            (entry.Value.MaximumRuns == -1 || entry.Value.MaximumRuns > _strategy.MissionsRun(entry.Value.VariableName))).ToArray();
        if (missions.Length > 4096) return "Mission scripts exceed the supported work limit.";
        var births = new HashSet<string>(StringComparer.Ordinal);
        if (MissionScriptReason(missions, context.Month, runs, markers, births) is { } missionReason) return missionReason;
        var events = rules.EventScripts.Rules.Where(entry =>
            WorldScriptEligibility.MayMatch(entry.Value.Criteria, context, changed, runs, markers)).ToArray();
        if (events.Length > 4096) return "Event scripts exceed the supported work limit.";
        if (EventScriptReason(events, context.Month) is { } eventReason) return eventReason;
        if (isNewMonth && context.Month > 0)
        {
            foreach (var id in births)
            {
                var deployment = rules.AlienDeployments[rules.AlienDeployments.GetRequired(id)].Value;
                if (deployment.AlienBaseUpgrades.Count != 0 || deployment.AlienRaceEvolution.Count != 0)
                    return "Composed monthly scheduling requires preflight of a newly created alien base upgrade or evolution.";
            }
            if (AlienBaseMonthlyReason(context.Month) is { } baseReason) return baseReason;
        }
        return null;
    }

    private CampaignCommandResult RunWorldScheduling(WorldScriptContext context, bool isNewMonth,
        RuntimeRule<RuntimeArcScriptRule>[] arcs)
    {
        var notifications = RunArcScriptCommands(arcs, campaign.ArcResearch).Events;
        // Score/funds stay fixed for the entire caller; research and counters are live at each phase.
        var afterArcs = campaign.WorldScriptContext(context.Score, context.Funds, _strategy.MissionRuns) with { Month = context.Month };
        var missions = campaign.Content.RuntimeRules.MissionScripts.Rules.Where(entry =>
            WorldScriptEligibility.Matches(entry.Value.Criteria, afterArcs) &&
            (entry.Value.MaximumRuns == -1 || entry.Value.MaximumRuns > _strategy.MissionsRun(entry.Value.VariableName))).ToArray();
        RunMissionScriptCommands(missions, context.Month);
        var afterMissions = campaign.WorldScriptContext(context.Score, context.Funds, _strategy.MissionRuns) with { Month = context.Month };
        var events = campaign.Content.RuntimeRules.EventScripts.Rules.Where(entry =>
            WorldScriptEligibility.Matches(entry.Value.Criteria, afterMissions)).ToArray();
        RunEventScriptCommands(events, context.Month);
        if (isNewMonth && context.Month > 0) RunAlienBaseMonthly(context.Month);
        return new(notifications);
    }
}

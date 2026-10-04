using Oxce.Mods.Rulesets.Runtime;

namespace Oxce.Gameplay.Campaigns.World;

/// <summary>Ordered determineAlienMissions caller. Integrated campaign month progression remains guarded.</summary>
internal interface ICampaignWorldScheduling
{
    CampaignCommandResult Schedule();
}

/// <summary>Subsystem acceptance seam after month advancement; does not advance the calendar.</summary>
internal sealed record ScheduleWorldScripts : ICampaignCommand;

/// <summary>Enters the strategic world after first-base placement, like GeoscapeState::init.</summary>
public sealed record EnterCampaignWorld : ICampaignCommand;

internal sealed partial class CampaignWorld : ICampaignWorldScheduling
{
    private void RegisterWorldScheduling(CampaignCapabilityRegistry registry)
    {
        registry.Operation<ICampaignWorldScheduling>(this);
        registry.Command<EnterCampaignWorld>(_ => EnterWorld());
        registry.Timed(CampaignTimeTrigger.OneMonth, CampaignTimeOrder.MonthWorldScheduling,
            "monthly world scheduling", effects =>
            {
                var result = ((ICampaignWorldScheduling)this).Schedule();
                if (result.Events.OfType<CampaignActionBlocked>().FirstOrDefault() is { } blocked)
                    throw new InvalidOperationException("Monthly world preflight failed after calendar advancement: " + blocked.Reason);
                foreach (var notification in result.Events) effects.Notify(notification);
            });
        registry.Command<ScheduleWorldScripts>(_ => ((ICampaignWorldScheduling)this).Schedule());
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
        var missionIds = _missions.Select(mission => mission.Id).ToHashSet();
        foreach (var ufo in _ufos)
            if (!missionIds.Contains(ufo.MissionId) ||
                !rules.UfoTrajectories.TryGet(ufo.TrajectoryId, out var trajectory) ||
                (uint)ufo.TrajectoryPoint >= (uint)rules.UfoTrajectories[trajectory].Value.Waypoints.Count)
                return Blocked("Initial world entry requires a valid UFO mission and trajectory.");
        var context = campaign.WorldScriptContext(inputs.Score, inputs.Funds, _strategy.MissionRuns) with { Month = 0 };
        if (WorldSchedulingReason(context, out var arcs) is { } reason) return Blocked(reason);
        calendar.Begin(entry);
        var result = RunWorldScheduling(context, arcs);
        // Reference charges full maintenance except HQ personnel AFTER scheduling.
        calendar.Finish(entry);
        return result;
    }

    CampaignCommandResult ICampaignWorldScheduling.Schedule()
    {
        WorldSchedulingInputs inputs;
        try { inputs = campaign.WorldSchedulingInputs(isNewMonth: true); }
        catch (Exception error) when (error is OverflowException or InvalidDataException) { return Blocked(error.Message); }
        var context = campaign.WorldScriptContext(inputs.Score, inputs.Funds, _strategy.MissionRuns);
        if (WorldSchedulingReason(context, out var arcs) is { } reason) return Blocked(reason);
        return RunWorldScheduling(context, arcs);
    }

    private string? MonthlyWorldSchedulingReason(CampaignTimeTrigger highest)
    {
        if (highest < CampaignTimeTrigger.OneMonth) return null;
        try
        {
            var month = checked(campaign.MonthsPassed + 1);
            var inputs = campaign.WorldSchedulingInputs(isNewMonth: true, schedulingMonth: month);
            var context = campaign.WorldScriptContext(inputs.Score, inputs.Funds, _strategy.MissionRuns) with { Month = month };
            return WorldSchedulingReason(context, out _);
        }
        catch (Exception error) when (error is OverflowException or InvalidDataException) { return error.Message; }
    }

    private string? WorldSchedulingReason(WorldScriptContext context,
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
        var births = new HashSet<MonthlyBaseBirthChoice>();
        if (MissionScriptReason(missions, context.Month, runs, markers, births) is { } missionReason) return missionReason;
        var events = rules.EventScripts.Rules.Where(entry =>
            WorldScriptEligibility.MayMatch(entry.Value.Criteria, context, changed, runs, markers)).ToArray();
        if (events.Length > 4096) return "Event scripts exceed the supported work limit.";
        if (EventScriptReason(events, context.Month) is { } eventReason) return eventReason;
        if (context.Month > 0)
        {
            if (AlienBaseMonthlyReason(context.Month, markers,
                    missions.Count(entry => entry.Value.Criteria.ExecutionOdds > 0), births) is { } baseReason) return baseReason;
        }
        return null;
    }

    private CampaignCommandResult RunWorldScheduling(WorldScriptContext context,
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
        if (context.Month > 0) RunAlienBaseMonthly(context.Month);
        return new(notifications);
    }
}

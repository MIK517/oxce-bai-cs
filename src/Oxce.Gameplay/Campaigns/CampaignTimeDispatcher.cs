namespace Oxce.Gameplay.Campaigns;

/// <summary>Single-writer simulation effects. Preflight must not mutate state.</summary>
public interface ICampaignTimeEffects
{
    string? Preflight(CampaignTime nextTime, CampaignTimeTrigger highestTrigger);

    /// <returns>True to pause before the next tick, after all current handlers finish.</returns>
    bool Apply(CampaignTime current, CampaignTimeTrigger trigger);

    /// <summary>
    /// Stage points of the next tick (ADR 0035), read after its preflight passes: bit <c>1 &lt;&lt; level</c> means
    /// the rest of the tick after that trigger level is preflighted again on the state the level leaves.
    /// </summary>
    int StagePoints(CampaignTime nextTime, CampaignTimeTrigger highestTrigger) => 0;

    /// <summary>Checks every later stage on a shadow copy without changing this state; null when all pass.</summary>
    string? PreflightStages(CampaignTime nextTime, CampaignTimeTrigger highestTrigger, int stagePoints) => null;

    /// <summary>Preflights the rest of the current tick on real state after a stage point; it must pass.</summary>
    void Restage(CampaignTime current, CampaignTimeTrigger remainingHighestTrigger) { }
}

public readonly record struct CampaignTimeDispatchResult(
    CampaignTime Current,
    CampaignTimeTriggerSummary Summary,
    bool Paused,
    string? BlockedReason);

/// <summary>
/// Executes GeoscapeState::timeAdvance fallthrough. Summary counts retain the previous
/// exclusive highest-trigger contract; they are notifications, not simulation work.
/// </summary>
public static class CampaignTimeDispatcher
{
    public static CampaignTimeDispatchResult Advance(
        CampaignTime current, int ticks, ICampaignTimeEffects effects)
    {
        ArgumentNullException.ThrowIfNull(effects);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(ticks);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(ticks, CampaignState.MaximumCommandTicks);
        current.Validate();
        Span<int> counts = stackalloc int[6];
        counts.Clear();
        var completed = 0;
        var paused = false;
        string? blocked = null;
        while (completed < ticks && !paused)
        {
            var next = current.Advance(out var highest);
            blocked = effects.Preflight(next, highest);
            if (blocked is not null) break;
            // A stage point after the five-second level would have nothing left to check.
            var stages = effects.StagePoints(next, highest) & ((2 << (int)highest) - 2);
            if (stages != 0 && (blocked = effects.PreflightStages(next, highest, stages)) is not null) break;
            current = next;
            // A popup pause is observed by the outer loop, never between handlers.
            for (var trigger = (int)highest; trigger >= 0; trigger--)
            {
                paused |= effects.Apply(current, (CampaignTimeTrigger)trigger);
                if ((stages & (1 << trigger)) != 0) effects.Restage(current, (CampaignTimeTrigger)(trigger - 1));
            }
            counts[(int)highest]++;
            completed++;
        }
        return new CampaignTimeDispatchResult(current,
            new CampaignTimeTriggerSummary(completed, counts[0], counts[1], counts[2],
                counts[3], counts[4], counts[5]), paused, blocked);
    }
}

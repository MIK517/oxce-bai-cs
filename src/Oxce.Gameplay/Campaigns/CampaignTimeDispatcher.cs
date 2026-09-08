namespace Oxce.Gameplay.Campaigns;

/// <summary>Single-writer simulation effects. Preflight must not mutate state.</summary>
public interface ICampaignTimeEffects
{
    string? Preflight(CampaignTime nextTime, CampaignTimeTrigger highestTrigger);

    /// <returns>True to pause before the next tick, after all current handlers finish.</returns>
    bool Apply(CampaignTime current, CampaignTimeTrigger trigger);
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
            current = next;
            // A popup pause is observed by the outer loop, never between handlers.
            for (var trigger = (int)highest; trigger >= 0; trigger--)
                paused |= effects.Apply(current, (CampaignTimeTrigger)trigger);
            counts[(int)highest]++;
            completed++;
        }
        return new CampaignTimeDispatchResult(current,
            new CampaignTimeTriggerSummary(completed, counts[0], counts[1], counts[2],
                counts[3], counts[4], counts[5]), paused, blocked);
    }
}

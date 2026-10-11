namespace Oxce.Gameplay.Campaigns.World;

/// <summary>GeoscapeState's crash expiry and Ufo::think's non-moving crashed branch.</summary>
internal static class WorldCrashedUfo
{
    internal static UfoSnapshot HalfHour(UfoSnapshot ufo) => ufo.Status != UfoStatus.Crashed ? ufo :
        ufo.SecondsRemaining >= 1800 ? ufo with { SecondsRemaining = ufo.SecondsRemaining - 1800 } :
        ufo with { Status = UfoStatus.Destroyed };

    internal static UfoSnapshot FiveSeconds(UfoSnapshot ufo) => ufo.Status != UfoStatus.Crashed ? ufo :
        ufo.SecondsRemaining == 0 ? ufo with { Status = UfoStatus.Destroyed, Detected = false } :
        ufo.Detected ? ufo : ufo with { Detected = true };
}

internal sealed partial class CampaignWorld
{
    private bool HasDestroyedUfoAfterTransitions(CampaignTimeTrigger highest)
    {
        foreach (var ufo in _ufos)
            if (UfoDestroyedAfterTimers(ufo, highest)) return true;
        return false;
    }

    private static bool UfoDestroyedAfterTimers(UfoSnapshot ufo, CampaignTimeTrigger highest) =>
        ufo.Status == UfoStatus.Destroyed || ufo.Status == UfoStatus.Crashed &&
        (ufo.SecondsRemaining == 0 || highest >= CampaignTimeTrigger.ThirtyMinutes && ufo.SecondsRemaining <= 1800);

    private void ExpireCrashedUfos(CampaignState.TimeEffects _)
    {
        for (var index = 0; index < _ufos.Count; index++) _ufos[index] = WorldCrashedUfo.HalfHour(_ufos[index]);
    }
}

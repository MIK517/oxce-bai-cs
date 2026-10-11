namespace Oxce.Gameplay.Campaigns.World;

internal sealed partial class CampaignWorld
{
    private string? ProductionCounterWorkReason(CampaignTimeTrigger highest, ref long work, string boundary)
    {
        var hourly = highest >= CampaignTimeTrigger.OneHour;
        var rules = campaign.Content.RuntimeRules;
        foreach (var owner in campaign.BaseStates)
        {
            // Always account for identity collision scans, even between hourly ticks.
            // Hourly projections additionally scan arrivals and allocator floors.
            work += 1L + owner.Crafts.Count * (hourly ? 2L : 1L) + owner.Transfers.Count * (hourly ? 4L : 1L) + (hourly ? owner.Soldiers.Count : 0);
            if (work > MaximumAlienBaseChecks) return boundary;
            if (!hourly) continue;
            foreach (var production in owner.Productions)
            {
                var manufacture = rules.Manufacture[production.Rule].Value;
                work += 1L + manufacture.ProducedMaterials.Count + manufacture.RequiredMaterials.Count +
                    (production.IsFallback ? owner.Facilities.Count : 0);
                if (work > MaximumAlienBaseChecks) return boundary;
            }
        }
        return work > MaximumAlienBaseChecks ? boundary : null;
    }

    private bool CraftIdCollision(string marker, long first, long end)
    {
        var rules = campaign.Content.RuntimeRules;
        foreach (var owner in campaign.BaseStates)
        {
            foreach (var craft in owner.Crafts)
                if (rules.Crafts.GetExternalId(craft.Rule) == marker && craft.Id >= first && craft.Id < end) return true;
            foreach (var transfer in owner.Transfers)
                if (!transfer.Delivered && transfer.Craft is { } craft && craft.RuleId == marker && craft.Id >= first && craft.Id < end) return true;
        }
        return false;
    }
}

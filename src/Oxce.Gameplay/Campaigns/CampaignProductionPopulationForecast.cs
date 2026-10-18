namespace Oxce.Gameplay.Campaigns;

public sealed partial class CampaignState
{
    // Production::step can finish only this hour's assigned work, even for an
    // infinite queue. Materials and continuation failures can lower this bound.
    string? ICampaignProductionPreflight.CraftPopulationBound(CampaignTimeTrigger highest,
        ref long work, out long population)
    {
        population = 0;
        const string reason = "New UFO retargeting exceeds the supported work limit.";
        foreach (var owner in _bases)
        {
            population += owner.Crafts.Count;
            work += 1L + owner.Crafts.Count + owner.Transfers.Count;
            if (work > ForecastLimits.Work) return reason;
            if (highest < CampaignTimeTrigger.OneHour) continue;
            foreach (var transfer in owner.Transfers)
                if (!transfer.Delivered && transfer.Hours <= 1 && transfer.Craft is not null) population++;
            if (ForecastProductionAssignments(owner, highest, out var assignments, ref work) is { } staffing) return staffing;
            for (var index = 0; index < owner.Productions.Count; index++)
            {
                var project = owner.Productions[index];
                var rule = _content.RuntimeRules.Manufacture[project.Rule].Value;
                work += 1L + rule.ProducedMaterials.Count;
                if (work > ForecastLimits.Work) return reason;
                // Born craft with a positive transfer delay cannot enter the
                // already-completed hourly arrival pass or this tick's retarget pass.
                if (rule.TransferTimes.Count > 2 && rule.TransferTimes[2] > 0) continue;
                var units = ProductionProgress.Requested(rule.Time, project.Spent, assignments[index], project.Amount, project.Infinite);
                foreach (var material in rule.ProducedMaterials)
                    if (material.Craft is not null) population += units * (long)material.Quantity;
                if (population > 1_000_000) return reason;
            }
        }
        return population > 1_000_000 ? reason : null;
    }
}

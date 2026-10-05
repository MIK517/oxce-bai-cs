namespace Oxce.Gameplay.Campaigns;

public sealed partial class CampaignState
{
    /// <summary>Native hourly staffing before the base's deferred project-removal pass.</summary>
    private string? ForecastProductionAssignments(BaseState owner, CampaignTimeTrigger highest,
        out int[] assignments, ref long work)
    {
        assignments = [];
        if (owner.Productions.Count == 0) return null;
        var rules = _content.RuntimeRules;
        work += owner.Facilities.Count + (long)owner.Transfers.Count + owner.Productions.Count;
        if (work > 1_000_000) return "Production staffing exceeds the supported work limit.";
        long workshops = 0;
        foreach (var facility in owner.Facilities)
            if (facility.BuildTime == 0 || highest >= CampaignTimeTrigger.OneDay && facility.BuildTime == 1)
                workshops += rules.Facilities[facility.Rule].Value.Workshops;
        var used = UsedWorkshopSpace(owner, rules);
        long engineers = owner.Engineers;
        foreach (var transfer in owner.Transfers)
            if (!transfer.Delivered && transfer.Hours <= 1 && transfer.Kind == CampaignTransferKind.Engineer)
                engineers += transfer.Quantity;
        if (workshops > int.MaxValue || engineers > int.MaxValue)
            return "Fallback production capacity exceeds the supported range.";
        assignments = new int[owner.Productions.Count];
        for (var index = 0; index < owner.Productions.Count; index++)
        {
            var project = owner.Productions[index];
            var rule = rules.Manufacture[project.Rule].Value;
            if (rule.Space < 0) return "Production rule contains an unsupported negative value.";
            var assigned = project.Assigned;
            if (project.IsFallback)
            {
                var additional = ProductionProgress.FallbackAdditional(project.Spent, assigned,
                    rule.Space, engineers, workshops - used);
                if ((long)assigned + additional > int.MaxValue)
                    return "Fallback production staffing exceeds the supported range.";
                if (additional > 0)
                {
                    if (project.Spent == 0 && assigned == 0) used += rule.Space;
                    used += additional;
                    engineers -= additional;
                    assigned += (int)additional;
                }
            }
            if ((long)project.Spent + assigned > int.MaxValue)
                return "Production progress exceeds the supported range.";
            assignments[index] = assigned;
        }
        return null;
    }
}

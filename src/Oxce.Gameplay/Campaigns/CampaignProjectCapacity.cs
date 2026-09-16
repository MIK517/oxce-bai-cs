using Oxce.Mods.Rulesets.Runtime;

namespace Oxce.Gameplay.Campaigns;

public sealed partial class CampaignState
{
    private static long AvailableProjectCapacity(
        BaseState owner, RuntimeRuleCatalog rules, Func<RuntimeFacilityRule, int> capacity) =>
        owner.Facilities.Where(facility => facility.BuildTime == 0)
            .Sum(facility => (long)capacity(rules.Facilities[facility.Rule].Value));

    private static long AvailableLaboratories(BaseState owner, RuntimeRuleCatalog rules) =>
        AvailableProjectCapacity(owner, rules, static rule => rule.Laboratories);

    private static long AvailableWorkshops(BaseState owner, RuntimeRuleCatalog rules) =>
        AvailableProjectCapacity(owner, rules, static rule => rule.Workshops);

    private static long UsedLaboratories(BaseState owner) =>
        owner.Research.Sum(project => (long)project.Assigned);

    private static long UsedWorkshopSpace(
        BaseState owner, RuntimeRuleCatalog rules, ProductionState? exclude = null) =>
        owner.Productions.Where(project => project != exclude && (project.Assigned != 0 || project.Spent != 0))
            .Sum(project => (long)project.Assigned + rules.Manufacture[project.Rule].Value.Space);

    private int FreeLaboratories(BaseState owner) => checked((int)Math.Max(0,
        AvailableLaboratories(owner, _content.RuntimeRules) - UsedLaboratories(owner)));

    private int AvailableWorkshops(BaseState owner) =>
        checked((int)AvailableWorkshops(owner, _content.RuntimeRules));

    private int UsedWorkshopSpace(BaseState owner, ProductionState? exclude = null) =>
        checked((int)UsedWorkshopSpace(owner, _content.RuntimeRules, exclude));
}

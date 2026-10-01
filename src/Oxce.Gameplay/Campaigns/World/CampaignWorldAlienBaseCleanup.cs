using Oxce.Mods.Rulesets.Runtime;

namespace Oxce.Gameplay.Campaigns.World;

internal sealed partial class CampaignWorld
{
    private string? AlienBaseDestructionReason(CampaignTimeTrigger highest,
        ref IReadOnlySet<string>? guaranteedResearch)
    {
        if (highest < CampaignTimeTrigger.OneDay) return null;
        IReadOnlySet<string>? possible = null;
        long work = 0;
        foreach (var alienBase in _alienBases)
        {
            var rule = AlienBaseRule(alienBase);
            if (rule.BaseSelfDestructCodeId.Length == 0) continue;
            if (rule.BaseSelfDestructCode is null)
                return "Alien base self-destruction references undefined research.";
            guaranteedResearch ??= campaign.ResearchPreflight.GuaranteedResearchAfterDailyProgress();
            possible ??= campaign.ResearchPreflight.PossibleResearchAfterDailyProgress();
            if (!possible.Contains(rule.BaseSelfDestructCodeId)) continue;
            if (!guaranteedResearch.Contains(rule.BaseSelfDestructCodeId))
                return "Alien base self-destruction depends on uncertain daily research outcomes.";
            work += _missions.Count + (long)campaign.CountryStates.Count;
            if (work > MaximumAlienBaseChecks) return AlienBaseWorkReason;
            foreach (var mission in _missions)
                if (FollowsAlienBase(mission.AlienBase, alienBase) &&
                    MissionRule(mission).OperationType != RuntimeMissionOperationType.Space && mission.LiveUfos != 0)
                    return "Alien base self-destruction with live Earth-operation UFOs requires combined forecasts.";
        }
        if (guaranteedResearch is not null && (long)_alienBases.Count *
            _missions.Count(mission => mission.AlienBase is not null) > MaximumAlienBaseChecks) return AlienBaseWorkReason;
        return null;
    }

    private RuntimeAlienMissionRule MissionRule(AlienMissionSnapshot mission) =>
        campaign.Content.RuntimeRules.AlienMissions[
            campaign.Content.RuntimeRules.AlienMissions.GetRequired(mission.RuleId)].Value;

    private bool BaseDestroyedAfterDailyResearch(AlienBaseSnapshot alienBase, IReadOnlySet<string>? guaranteedResearch) =>
        guaranteedResearch is not null && AlienBaseRule(alienBase).BaseSelfDestructCodeId is { Length: > 0 } id &&
        guaranteedResearch.Contains(id);

    private bool FollowsAlienBase(WorldTargetReference? target, AlienBaseSnapshot alienBase) =>
        target is { Kind: WorldTargetKind.AlienBase } && target.Id == alienBase.Id &&
        target.TypeId == MarkerName(alienBase.DeploymentId);

    private void DestroyAlienBasesDaily()
    {
        for (var index = 0; index < _alienBases.Count;)
        {
            var alienBase = _alienBases[index];
            var id = AlienBaseRule(alienBase).BaseSelfDestructCodeId;
            if (id.Length == 0 || !campaign.HasCompletedResearch(id)) { index++; continue; }
            ClearAlienBaseLinks(alienBase);
            _alienBases.RemoveAt(index);
        }
    }

    // SavedGame::clearLinksForAlienBase: space missions survive; Earth operations interrupt.
    private void ClearAlienBaseLinks(AlienBaseSnapshot alienBase)
    {
        for (var index = 0; index < _missions.Count; index++)
        {
            var mission = _missions[index];
            if (FollowsAlienBase(mission.AlienBase, alienBase))
                _missions[index] = mission with
                {
                    AlienBase = null,
                    Interrupted = mission.Interrupted ||
                    MissionRule(mission).OperationType != RuntimeMissionOperationType.Space
                };
        }
        if (!campaign.Content.RuntimeRules.Campaign.AllowCountriesToCancelAlienPact || alienBase.PactCountryId.Length == 0) return;
        foreach (var country in campaign.CountryStates)
            if (campaign.Content.RuntimeRules.Countries.GetExternalId(country.Rule) == alienBase.PactCountryId)
            {
                country.CancelPact = country.Pact;
                country.NewPact = false;
                break;
            }
    }
}

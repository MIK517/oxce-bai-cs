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
            guaranteedResearch ??= GuaranteedDailyResearch();
            possible ??= campaign.ResearchPreflight.PossibleResearchAfterDailyProgress();
            if (!possible.Contains(rule.BaseSelfDestructCodeId)) continue;
            work += _missions.Count + (long)campaign.CountryStates.Count + _alienBases.Count;
            foreach (var owner in campaign.BaseStates) work += owner.Crafts.Count;
            if (work > MaximumAlienBaseChecks) return AlienBaseWorkReason;
            var marker = MarkerName(alienBase.DeploymentId);
            foreach (var other in _alienBases)
                if (!ReferenceEquals(other, alienBase) && other.Id == alienBase.Id && MarkerName(other.DeploymentId) == marker)
                    return "Alien base self-destruction cannot resolve an ambiguous marker identity.";
            if (!guaranteedResearch.Contains(rule.BaseSelfDestructCodeId))
            {
                (_pass.PossibleDailyBaseDeletions ??= []).Add((alienBase.DeploymentId, alienBase.Id));
                if (UncertainBaseDeletionReason(alienBase, rule, ref work) is { } uncertainReason) return uncertainReason;
            }
        }
        if (guaranteedResearch is not null && (long)_alienBases.Count *
            _missions.Count(mission => mission.AlienBase is not null) > MaximumAlienBaseChecks) return AlienBaseWorkReason;
        return null;
    }

    private string? UncertainBaseDeletionReason(AlienBaseSnapshot alienBase, RuntimeAlienDeploymentRule rule, ref long work)
    {
        // The surviving branch owns activity, timers and discovery proofs. A quiet
        // unlinked deletion only removes that actor; generation, link repair and
        // pact eligibility need their own earlier/later caller alternatives.
        work += _missions.Count + (long)rule.GenMission.Count + campaign.BaseStates.Count + campaign.CountryStates.Count;
        foreach (var owner in campaign.BaseStates) work += owner.Crafts.Count + (long)owner.Transfers.Count;
        if (work > MaximumAlienBaseChecks) return AlienBaseWorkReason;
        foreach (var mission in _missions)
            if (FollowsAlienBase(mission.AlienBase, alienBase))
                return "Alien base self-destruction requires linked-target alternatives for uncertain research.";
        // Ordinary UFO destinations restore as anonymous waypoints. Their source
        // links are owned by missions; they do not follow an alien-base pointer.
        foreach (var owner in campaign.BaseStates)
        {
            foreach (var craft in owner.Crafts)
                if (FollowsAlienBase(craft.Logistics?.Destination, alienBase))
                    return "Alien base self-destruction requires linked-target alternatives for uncertain research.";
            foreach (var transfer in owner.Transfers)
                if (FollowsAlienBase(transfer.Craft?.Logistics?.Destination, alienBase))
                    return "Alien base self-destruction requires linked-target alternatives for uncertain research.";
        }
        if (rule.GenMissionFrequency > 0 && alienBase.GenMissionCount < rule.GenMissionLimit &&
            rule.GenMission.Any(static choice => choice.Key.Length != 0 && choice.Value != 0))
            return "Alien base self-destruction requires optional mission-generation forecasts.";
        if (rule.BaseDetectionRange > 0 && rule.BaseDetectionChance > 0 &&
            rule.HuntMissions.Count != 0 && (long)alienBase.MinutesSinceLastHuntMissionGeneration + 10 >= rule.HuntMissionMaxFrequency)
            return "Alien base self-destruction requires optional hunting-generation forecasts.";
        if (campaign.Content.RuntimeRules.Campaign.AllowCountriesToCancelAlienPact && alienBase.PactCountryId.Length != 0)
            foreach (var country in campaign.CountryStates)
                if (campaign.Content.RuntimeRules.Countries.GetExternalId(country.Rule) == alienBase.PactCountryId &&
                    (country.NewPact || country.CancelPact != country.Pact))
                    return "Alien base self-destruction requires uncertain pact-cancellation forecasts.";
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
            ReturnAlienBaseCraftFollowers(alienBase);
            _alienBases.RemoveAt(index);
        }
    }

    // Target::~Target runs during deletion, before any later handler can skip craft movement.
    private void ReturnAlienBaseCraftFollowers(AlienBaseSnapshot alienBase)
    {
        var rules = campaign.Content.RuntimeRules;
        foreach (var owner in campaign.BaseStates)
            for (var index = 0; index < owner.Crafts.Count; index++)
            {
                var craft = owner.Crafts[index];
                if (craft.Logistics is not { } state || !FollowsAlienBase(state.Destination, alienBase)) continue;
                if (!CraftLogistics.TryEffectiveSpeedMaximum(rules.Crafts[craft.Rule].Value,
                    state.Weapons, rules, out var speed))
                    throw new InvalidOperationException("Deleted base follower speed changed after preflight.");
                owner.Crafts[index] = craft with { Logistics = SetDestination(state, speed, BaseReference(owner)) with { Status = state.Status } };
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

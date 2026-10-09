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
        // The surviving branch owns activity, timers, discovery and generated
        // mission proofs. Deletion omits those optional actors; source links and
        // pact eligibility still need their own caller alternatives.
        work += _missions.Count + (long)rule.GenMission.Count + campaign.BaseStates.Count + campaign.CountryStates.Count;
        foreach (var owner in campaign.BaseStates) work += owner.Crafts.Count + (long)owner.Transfers.Count;
        if (work > MaximumAlienBaseChecks) return AlienBaseWorkReason;
        foreach (var mission in _missions)
            if (FollowsAlienBase(mission.AlienBase, alienBase))
            {
                var missionRule = MissionRule(mission);
                if (missionRule.OperationType == RuntimeMissionOperationType.Space)
                {
                    if (missionRule.Objective == RuntimeMissionObjective.Supply)
                    {
                        if (PossibleSupplySourceLossReason(mission, missionRule, ref work) is { } supplyReason) return supplyReason;
                    }
                    // Ordinary Space flight ignores the source; objective supply
                    // births above reserve suppression separately from interruption.
                    continue;
                }
                // Earth operations interrupt when the source disappears. Their
                // surviving branch keeps all ordinary spawn/arrival proofs, while
                // the interrupted branch can expire earlier or omit wave awards.
                work += _ufos.Count;
                if (work > MaximumAlienBaseChecks) return AlienBaseWorkReason;
                // Native interruption does not reset hunting or escort flags.
                // Their shared flight/retarget proofs retain the continuing branch;
                // ordinary arrivals may terminate earlier in the interrupted branch.
                if (mission.LiveUfos == 0 && MissionExpiryReason(mission, missionRule, ref work) is { } cleanupReason)
                    return cleanupReason;
                (_pass.PossibleDailyMissionInterruptions ??= []).Add(mission.Id);
            }
        // Ordinary UFO destinations restore as anonymous waypoints. Their source
        // links are owned by missions; they do not follow an alien-base pointer.
        foreach (var owner in campaign.BaseStates)
        {
            foreach (var craft in owner.Crafts)
                if (craft.Logistics is { } state && FollowsAlienBase(state.Destination, alienBase) &&
                    UncertainCraftBaseReturnReason(owner, craft, state, ref work) is { } returnReason) return returnReason;
            foreach (var transfer in owner.Transfers)
                if (FollowsAlienBase(transfer.Craft?.Logistics?.Destination, alienBase))
                    return "Alien base self-destruction requires linked-target alternatives for uncertain research.";
        }
        // Both generation callers retain their full surviving-source choice,
        // capacity and activity envelopes. Immediate research births are proved
        // separately against the ordered daily research phase before mutation.
        return null;
    }

    private string? UncertainCraftBaseReturnReason(CampaignState.BaseState owner,
        CampaignState.CraftState craft, CraftLogisticsState state, ref long work)
    {
        if (!CraftLogistics.TryEffectiveSpeedMaximum(campaign.Content.RuntimeRules.Crafts[craft.Rule].Value,
                state.Weapons, campaign.Content.RuntimeRules, out var maximum)) return SpeedRangeReason;
        if (state.Status == "STR_OUT")
        {
            // Fuel quantity is not a native hunter/script input. Unequal burns
            // can compose when both capped outcomes retain the same low-fuel
            // eligibility; changed speed still needs craft-escort consumer proofs.
            var rule = campaign.Content.RuntimeRules.Crafts[craft.Rule].Value;
            if (state.Speed < 0)
                return "Alien base self-destruction requires craft speed alternatives for uncertain research.";
            var from = new WorldPosition(state.Longitude, state.Latitude);
            if (!from.IsNormalized) return "Airborne craft coordinates are invalid.";
            if (!owner.IsPlaced) return "An airborne craft belongs to a base that is not placed.";
            if (CraftFuelAlternativeReason(owner, state, rule, maximum,
                    WorldFlight.FuelConsumption(rule.RefuelItem is not null, maximum, state.Speed, 0),
                    WorldFlight.FuelConsumption(rule.RefuelItem is not null, maximum, maximum, 0),
                    "Alien base self-destruction requires craft speed alternatives for uncertain research.", ref work) is { } fuelReason)
                return fuelReason;
            if (state.Speed != maximum && UncertainCraftReturnEscortReason(craft, state.Speed, maximum, from, ref work) is { } escortReason)
                return escortReason;
            var home = new WorldPosition(owner.Longitude, owner.Latitude);
            if (WorldGeometry.ReachedDestination(from, home) || state.Takeoff <= 0 &&
                !(WorldGeometry.Distance(from, home) > WorldGeometry.RadianSpeed(maximum)))
                return "Alien base self-destruction requires craft arrival alternatives for uncertain research.";
            return null;
        }
        // Native returnToBase preserves status. Servicing either stays grounded,
        // without consuming flight fields, or relaunches auto-patrol with its own
        // waypoint/full speed/takeoff60 in both source alternatives.
        return null;
    }

    private string? CraftFuelAlternativeReason(CampaignState.BaseState owner, CraftLogisticsState state,
        RuntimeCraftRule rule, int maximum, int firstBurn, int secondBurn, string mismatchReason, ref long work)
    {
        if (firstBurn == secondBurn || state.LowFuel) return null;
        if (maximum <= 0) return mismatchReason;
        work += state.Weapons.Count;
        if (work > MaximumAlienBaseChecks) return AlienBaseWorkReason;
        if (!CraftLogistics.TryEffectiveFuelMaximum(rule, state.Weapons, campaign.Content.RuntimeRules, out var fuelMaximum) ||
            fuelMaximum < 0) return FlightFuelRangeReason;
        // Unequal burns arise only for ordinary fuel. These primitive bounds
        // match setFuel's clamp without allocating alternate craft records.
        var first = Math.Clamp((long)state.Fuel - firstBurn, 0, fuelMaximum);
        var second = Math.Clamp((long)state.Fuel - secondBurn, 0, fuelMaximum);
        var limit = WorldFlight.FuelLimit(false, maximum, WorldGeometry.Distance(
            new(state.Longitude, state.Latitude), new(owner.Longitude, owner.Latitude)));
        return (first <= limit) == (second <= limit) ? null : mismatchReason;
    }

    private string? UncertainCraftReturnEscortReason(CampaignState.CraftState source, int oldSpeed,
        int returnSpeed, WorldPosition sourcePosition, ref long work)
    {
        var rules = campaign.Content.RuntimeRules;
        var type = rules.Crafts.GetExternalId(source.Rule);
        foreach (var owner in campaign.BaseStates)
        {
            foreach (var craft in owner.Crafts)
            {
                if (++work > MaximumAlienBaseChecks) return AlienBaseWorkReason;
                if (craft.Logistics is { } state && FollowsCraft(state.Destination, type, source.Id) &&
                    UncertainEscortFuelReason(owner, rules.Crafts[craft.Rule].Value, state, oldSpeed, returnSpeed, true, sourcePosition, ref work) is { } reason)
                    return reason;
            }
            foreach (var transfer in owner.Transfers)
            {
                if (++work > MaximumAlienBaseChecks) return AlienBaseWorkReason;
                if (transfer.Craft is { Logistics: { } state } incoming && FollowsCraft(state.Destination, type, source.Id) &&
                    UncertainEscortFuelReason(owner, rules.Crafts[rules.Crafts.GetRequired(incoming.RuleId)].Value,
                        state, oldSpeed, returnSpeed, false, sourcePosition, ref work) is { } reason) return reason;
            }
        }
        return null;
    }

    private string? UncertainEscortFuelReason(CampaignState.BaseState owner, RuntimeCraftRule rule, CraftLogisticsState state,
        int oldSpeed, int returnSpeed, bool owned, WorldPosition sourcePosition, ref long work)
    {
        // Grounded owned craft either stay outside the fuel caller or relaunch
        // to an independent waypoint before fuel, replacing the craft destination.
        if (rule.RefuelItem is not null || owned && state.Status != "STR_OUT") return null;
        work += state.Weapons.Count;
        if (work > MaximumAlienBaseChecks) return AlienBaseWorkReason;
        if (!CraftLogistics.TryEffectiveSpeedMaximum(rule, state.Weapons,
                campaign.Content.RuntimeRules, out var maximum)) return SpeedRangeReason;
        // Fuel precedes movement. An owned OUT craft and this source retain
        // their coordinates until fuel; outside the strict native range the
        // leader's speed is unused. Transfer/checkup positions need other proofs.
        var position = new WorldPosition(state.Longitude, state.Latitude);
        if (owned && state.Status == "STR_OUT" && position.IsNormalized &&
            WorldGeometry.Distance(position, sourcePosition) >= WorldGeometry.Nautical(campaign.Content.RuntimeRules.Campaign.EscortRange))
            return null;
        // Positive speeds use the cap. At zero, only an already-owned OUT craft
        // retains its own saved speed until fuel; transfer/checkup or grounded
        // relaunch can replace that input and still need separate proofs.
        if (oldSpeed < 0 || returnSpeed <= 0 || oldSpeed == 0 && (!owned || state.Status != "STR_OUT"))
            return "Alien base self-destruction requires craft escort speed alternatives for uncertain research.";
        var firstBurn = WorldFlight.FuelConsumption(false, maximum, state.Speed, oldSpeed);
        var secondBurn = WorldFlight.FuelConsumption(false, maximum, state.Speed, returnSpeed);
        if (firstBurn == secondBurn) return null;
        if (!owned || state.Speed < 0 || !position.IsNormalized || !owner.IsPlaced)
            return "Alien base self-destruction requires craft escort speed alternatives for uncertain research.";
        return CraftFuelAlternativeReason(owner, state, rule, maximum, firstBurn, secondBurn,
            "Alien base self-destruction requires craft escort speed alternatives for uncertain research.", ref work);
    }

    private static bool FollowsCraft(WorldTargetReference? target, string type, int id) =>
        target is { Kind: WorldTargetKind.Craft } && target.TypeId == type && target.Id == id;

    private string? PossibleSupplySourceLossReason(AlienMissionSnapshot mission, RuntimeAlienMissionRule rule, ref long work)
    {
        if (mission.Interrupted || mission.NextWave >= rule.Waves.Count || mission.SpawnCountdown > 30 ||
            mission.MultiUfoRetaliationInProgress && !rule.MultiUfoRetaliationExtra) return null;
        var objectiveBirth = false;
        try
        {
            foreach (var step in DueWaveSteps(mission, rule))
            {
                if (++work > MaximumAlienBaseChecks) return AlienBaseWorkReason;
                var wave = SpawnWave(step, rule);
                if (wave.Ufo is not null && wave.Objective) objectiveBirth = true;
            }
        }
        catch (InvalidDataException error) { return error.Message; }
        if (!objectiveBirth) return null;
        // Saved flight and newborn escort initialization consume the optional
        // leader domain through their shared bounded owners after this phase.
        // Source loss suppresses objective UFOs without interrupting the mission.
        // Counter completion can therefore expire a zero-live mission earlier.
        if (mission.LiveUfos == 0 && MissionExpiryReason(mission, rule, ref work) is { } expiryReason) return expiryReason;
        (_pass.PossibleDailySupplySourceLoss ??= []).Add(mission.Id);
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

namespace Oxce.Gameplay.Campaigns.World;

/// <summary>
/// The craft-loop consequences of interceptions in <c>GeoscapeState::time5Seconds</c>: a craft destroyed
/// in a dogfight is lost at the start of the next craft pass (activity, crew evacuation, hunters and
/// followers released, <c>craftLostDogfight</c>), and a craft whose UFO landed or whose destination is no
/// longer a UFO leaves its interception. The daily dogfight experience cache clears in <c>time1Day</c>.
/// </summary>
internal sealed partial class CampaignWorld
{
    private const string CrewLossReason = "Crew lost with a destroyed craft require soldier deaths and the memorial.";

    private void RegisterCraftLosses(CampaignCapabilityRegistry registry)
    {
        registry.Preflight(CampaignPreflightOrder.DestroyedCrafts, "destroyed crafts", (_, _, _) => DestroyedCraftReason());
        registry.Timed(CampaignTimeTrigger.OneDay, CampaignTimeOrder.DayDogfightExperience,
            "dogfight experience cache", ClearDailyDogfightExperience);
    }

    // Every input of a destroyed craft's loss is checked before the tick. Evacuation draws a percent roll
    // for each crew member; a roll that can fail would kill a soldier, which the port does not own yet.
    private string? DestroyedCraftReason()
    {
        var rules = campaign.Content.RuntimeRules;
        var settings = rules.Campaign.Interception;
        var transfers = 0;
        var losses = 0;
        foreach (var owner in campaign.BaseStates)
        {
            foreach (var craft in owner.Crafts)
            {
                if (craft.Logistics is not { } state || !IsLostCraft(state, rules.Crafts[craft.Rule].Value)) continue;
                var type = rules.Crafts.GetExternalId(craft.Rule);
                foreach (var soldier in owner.Soldiers)
                {
                    if (soldier.Personal is not { } personal || personal.CraftType != type || personal.CraftId != craft.Id) continue;
                    var chance = state.Pilots.Contains(soldier.Id)
                        ? settings.PilotsEmergencyEvacuationSurvivalChance : settings.CrewEmergencyEvacuationSurvivalChance;
                    if (chance < 100) return CrewLossReason;
                    transfers++;
                }
                if (CraftLossActivityReason(state, rules.Crafts[craft.Rule].Value.Score) is { } activity) return activity;
                if (((ICampaignCraftRemoval)this).PlanReason([(type, craft.Id)]) is { } removal) return removal;
                foreach (var ufo in _ufos)
                    if (ufo.Destination is { Kind: WorldTargetKind.Craft } target && target.TypeId == type && target.Id == craft.Id &&
                        ufo.OriginalWaypoint is not { IsNormalized: true }) return HuntStateReason;
                losses++;
            }
        }
        if (losses == 0) return null;
        if (losses > int.MaxValue - _craftLostDogfight) return "Lost craft count exceeds the supported range.";
        return campaign.CanQueueTransfers(transfers) ? null : "Transfer identity range is exhausted.";
    }

    // Kept out of the craft loop so its closure is only allocated for a destroyed craft.
    private string? CraftLossActivityReason(CraftLogisticsState state, int score)
    {
        string? reason = null;
        AddXcomActivity(new WorldPosition(state.Longitude, state.Latitude), -score, (_, _, history, change) =>
        {
            if ((long)history[^1] + change is < int.MinValue or > int.MaxValue)
                reason = "X-COM activity exceeds the supported range.";
        });
        return reason;
    }

    /// <summary>The start of the craft loop for a craft destroyed in an interception.</summary>
    private void LoseDestroyedCraft(CampaignState.BaseState owner, int index)
    {
        var rules = campaign.Content.RuntimeRules;
        var craft = owner.Crafts[index];
        var state = craft.Logistics!;
        var rule = rules.Crafts[craft.Rule].Value;
        var type = rules.Crafts.GetExternalId(craft.Rule);
        AddXcomActivity(new WorldPosition(state.Longitude, state.Latitude), -rule.Score,
            static (_, _, history, change) => history[^1] = checked(history[^1] + change));
        // Craft::evacuateCrew, in base soldier order.
        var settings = rules.Campaign.Interception;
        foreach (var soldier in owner.Soldiers.ToArray())
        {
            if (soldier.Personal is not { } personal || personal.CraftType != type || personal.CraftId != craft.Id) continue;
            var chance = state.Pilots.Contains(soldier.Id)
                ? settings.PilotsEmergencyEvacuationSurvivalChance : settings.CrewEmergencyEvacuationSurvivalChance;
            if (!RandomChance.Percent(campaign.Random, chance))
                throw new InvalidOperationException("A crew member was lost after world preflight.");
            campaign.QueueEvacuatedSoldier(owner, soldier);
        }
        // SavedGame::stopHuntingXcomCraft, then the craft's destructor returns its followers.
        for (var ufoIndex = 0; ufoIndex < _ufos.Count; ufoIndex++)
        {
            var ufo = _ufos[ufoIndex];
            if (ufo.Destination is not { Kind: WorldTargetKind.Craft } target || target.TypeId != type || target.Id != craft.Id) continue;
            var restored = WorldHunting.Restore(ufo);
            var vector = WorldGeometry.SpeedVector(restored.Position, restored.Destination!.Position, restored.SpeedRadian);
            _ufos[ufoIndex] = restored with { SpeedLongitude = vector.Longitude, SpeedLatitude = vector.Latitude };
        }
        owner.Crafts.RemoveAt(index);
        // Its window has ended by now; never keep one that points at a removed craft.
        _interceptions.RemoveAll(dogfight => dogfight.Craft.Key is InterceptionCraftKey key &&
            key.BaseId == owner.Id && key.TypeId == type && key.Id == craft.Id);
        ((ICampaignCraftRemoval)this).Removed(type, craft.Id, null);
        _craftLostDogfight++;
    }

    /// <summary>
    /// The craft loop's destination checks for a craft in an interception: a landed UFO or a destination
    /// that is not a UFO takes the craft out of the fight (Craft::setInDogfight(false)).
    /// </summary>
    private void ReleaseInterceptionCraft(CampaignState.BaseState owner, CampaignState.CraftState craft, WorldTargetReference destination)
    {
        if (_interceptions.Count == 0 && _pendingInterceptions.Count == 0) return;
        if (destination.Kind == WorldTargetKind.Ufo && TargetUfo(destination)?.Status != UfoStatus.Landed) return;
        var type = campaign.Content.RuntimeRules.Crafts.GetExternalId(craft.Rule);
        foreach (var dogfight in OpenAndPendingInterceptions)
            if (dogfight.Craft.Key is InterceptionCraftKey key && key.BaseId == owner.Id && key.TypeId == type && key.Id == craft.Id)
                dogfight.Craft.InDogfight = false;
    }

    // GeoscapeState::time1Day clears every soldier's daily dogfight experience cache.
    private void ClearDailyDogfightExperience(CampaignState.TimeEffects _)
    {
        foreach (var owner in campaign.BaseStates)
            for (var index = 0; index < owner.Soldiers.Count; index++)
                if (owner.Soldiers[index].Personal is { DailyDogfightExperience.IsEmpty: false } personal)
                    owner.Soldiers[index] = owner.Soldiers[index] with
                    {
                        Personal = personal with { DailyDogfightExperience = SoldierDogfightExperience.None },
                    };
    }
}

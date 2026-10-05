namespace Oxce.Gameplay.Campaigns.World;

public sealed record UfoStartedHunting(int UniqueId, string CraftTypeId, int CraftId) : ICampaignEvent;

internal sealed partial class CampaignWorld
{
    private Dictionary<int, UfoSnapshot>? _retargetForecast;
    private int _possibleHuntMarkerIds;
    private long _huntingForecastWork;
    private Dictionary<(string TypeId, int Id), CraftLogisticsState>? _huntingCraftForecast;
    private Dictionary<int, List<CampaignState.CraftState>>? _huntingCraftSources;
    private List<WorldPosition>? _escortFlightPositions;
    private int _escortFlightEndIndex;
    private const string HuntStateReason = "UFO hunting or escort state requires a valid original waypoint.";
    private const string HuntCombatReason = "UFO hunting interception requires strategic combat (Phase 6 branch 5).";

    private void RegisterHunting(CampaignCapabilityRegistry registry)
    {
        registry.Timed(CampaignTimeTrigger.TenMinutes, CampaignTimeOrder.TenMinutesWorldUfoRetarget,
            "UFO hunting and escort retargeting", effects => RetargetUfos(effects, due: true));
        registry.Timed(CampaignTimeTrigger.FiveSeconds, CampaignTimeOrder.FiveSecondsWorldUfoRetarget,
            "fast UFO retargeting and target repair", effects => RetargetUfos(effects,
                WorldHunting.RetargetDue(CampaignTimeTrigger.FiveSeconds, effects.SelectedSpeed,
                    campaign.Content.RuntimeRules.Campaign.HunterKillerFastRetarget)));
    }

    private string? HuntingReason(CampaignTimeTrigger highest, CampaignTimeSpeed speed)
    {
        _retargetForecast = null;
        _possibleHuntMarkerIds = 0;
        _huntingForecastWork = 0;
        _huntingCraftForecast = null;
        _huntingCraftSources = null;
        ResetEscortFlightPositions();
        var due = WorldHunting.RetargetDue(highest, speed, campaign.Content.RuntimeRules.Campaign.HunterKillerFastRetarget);
        long work = 0;
        foreach (var saved in _ufos)
        {
            var ufo = UfoAfterTimedTransitions(saved, highest);
            if (ufo.Status != UfoStatus.Flying || !ufo.HunterKiller && !ufo.Hunting && !ufo.Escorting) continue;
            if (due && ufo.HunterKiller || highest >= CampaignTimeTrigger.OneHour && ufo.Hunting)
            {
                foreach (var owner in campaign.BaseStates)
                {
                    work += owner.Crafts.Count + (long)owner.Transfers.Count;
                    if (work > MaximumAlienBaseChecks) return "UFO retargeting exceeds the supported work limit.";
                }
                if (_huntingCraftForecast is null && ForecastHuntingCrafts(highest, ref work) is { } craftReason)
                    return craftReason;
            }
            if (ProjectHunting(ufo, highest, due, forecastFuel: true, ref work, out var projected, out var alert) is { } reason)
                return reason;
            if (projected != ufo) (_retargetForecast ??= []).Add(saved.UniqueId, projected);
            if (alert && ufo.Id == 0) _possibleHuntMarkerIds++;
        }
        // Each prefix actor and target lookup is visited at most once per tick.
        // Refuse before building the ordered position cache, never during mutation.
        foreach (var saved in _ufos)
            if (UfoForFlight(saved, highest).Escorting)
            {
                work += 4L * _ufos.Count * _ufos.Count + (long)_ufos.Count * _missions.Count;
                break;
            }
        if (work > MaximumAlienBaseChecks)
            return "UFO escort movement exceeds the supported work limit.";
        _huntingForecastWork = work;
        return CanAllocateUfoMarkerIds(_possibleHuntMarkerIds)
            ? null : "UFO contact marker IDs are exhausted or collide with a saved UFO.";
    }

    private string? ProjectHunting(UfoSnapshot ufo, CampaignTimeTrigger highest, bool due, bool forecastFuel,
        ref long work, out UfoSnapshot projected, out bool alert)
    {
        projected = ufo;
        alert = false;
        if (ufo.Hunting || ufo.Escorting)
        {
            if (ufo.OriginalWaypoint is not { IsNormalized: true }) return HuntStateReason;
            var valid = ufo.Hunting
                ? ufo.Destination is { Kind: WorldTargetKind.Craft } craft &&
                    (forecastFuel ? ForecastHuntingTarget(craft) : TargetCraft(craft)) is not null
                : ufo.Destination is { Kind: WorldTargetKind.Ufo } target && TargetUfo(target) is not null;
            if (!valid) projected = WorldHunting.Restore(projected);
        }
        if (!due || !ufo.HunterKiller) return null;
        var rules = campaign.Content.RuntimeRules;
        var rule = rules.Ufos[rules.Ufos.GetRequired(ufo.RuleId)].Value;
        var mission = FindMission(ufo.MissionId);
        if (mission is null) return "UFO mission link requires world simulation.";
        var maximum = (long)rule.Stats.SpeedMaximum + rule.RaceBonus(mission.Race).SpeedMaximum;
        var radar = (long)rule.Stats.RadarRange + rule.RaceBonus(mission.Race).RadarRange;
        if (maximum is < 0 or > int.MaxValue || radar is < int.MinValue or > int.MaxValue)
            return "UFO hunting stats exceed the supported range.";
        var crafts = new List<WorldHuntCraft>();
        foreach (var owner in campaign.BaseStates)
        {
            foreach (var craft in forecastFuel ? _huntingCraftSources![owner.Id] : owner.Crafts)
            {
                if (++work > MaximumAlienBaseChecks) return "UFO retargeting exceeds the supported work limit.";
                if (craft.Logistics is not { } state) continue;
                var craftRule = rules.Crafts[craft.Rule].Value;
                if (forecastFuel) state = _huntingCraftForecast!.GetValueOrDefault(
                    (rules.Crafts.GetExternalId(craft.Rule), craft.Id), state);
                var original = projected.Hunting && projected.Destination is { } destination &&
                    destination.TypeId == rules.Crafts.GetExternalId(craft.Rule) && destination.Id == craft.Id;
                var damageMaximum = CraftLogistics.EffectiveDamageMaximum(state, craftRule, rules);
                var active = state.Status == "STR_OUT" && state.Damage < damageMaximum;
                if (!active && !original) continue;
                if (!CraftLogistics.TryEffectiveSpeedMaximum(craftRule, state.Weapons, rules, out var craftSpeed))
                    return SpeedRangeReason;
                var ignored = WorldHunting.IsIgnored(state.LowFuel, state.MissionComplete);
                var attraction = 0;
                if (!ignored && (original || !craftRule.Undetectable))
                {
                    if (!CraftLogistics.TryEffectiveUnitCapacities(craftRule, state.Weapons, rules, out var capacity) ||
                        damageMaximum is < int.MinValue or > int.MaxValue ||
                        !WorldHunting.TryAttraction(ufo.HuntMode, craftRule.AllowLanding, capacity.SoldierCapacity,
                            craftSpeed, state.Damage, (int)damageMaximum, out attraction))
                        return "Craft hunter attraction exceeds the supported range.";
                }
                var reference = new WorldTargetReference(WorldTargetKind.Craft, rules.Crafts.GetExternalId(craft.Rule),
                    craft.Id, state.Longitude, state.Latitude);
                crafts.Add(new(reference, reference.Position, attraction, ignored, craftRule.Undetectable, active));
            }
        }
        var selected = WorldHunting.Select(projected, (int)radar, crafts);
        if (selected is not null)
        {
            var original = projected.Hunting ? projected.Destination : null;
            if (original?.TypeId != selected.TypeId || original.Id != selected.Id)
            {
                if (!WorldHunting.TryHunt(projected, selected, (int)maximum, rule.HuntSpeed, out projected))
                    return "UFO hunt speed exceeds the supported range.";
                projected = projected with { Detected = true };
                alert = true;
            }
        }
        else if (projected.Hunting) projected = WorldHunting.Restore(projected);
        if (ufo.Escort && !projected.Hunting && !projected.Escorting)
        {
            work += _ufos.Count;
            if (work > MaximumAlienBaseChecks) return "UFO retargeting exceeds the supported work limit.";
            foreach (var target in _ufos)
            {
                if (target.MissionId != ufo.MissionId || target.HunterKiller) continue;
                projected = WorldHunting.Escort(projected,
                    new(WorldTargetKind.Ufo, WorldTargetReference.UfoType, target.Id, target.Longitude, target.Latitude)
                    { UniqueId = target.UniqueId }, (int)maximum);
                break;
            }
        }
        return null;
    }

    private void RetargetUfos(CampaignState.TimeEffects effects, bool due)
    {
        long work = 0;
        for (var index = 0; index < _ufos.Count; index++)
        {
            var ufo = _ufos[index];
            if (ufo.Status != UfoStatus.Flying || !ufo.HunterKiller && !ufo.Hunting && !ufo.Escorting) continue;
            if (ProjectHunting(ufo, CampaignTimeTrigger.FiveSeconds, due, forecastFuel: false,
                    ref work, out var projected, out var alert) is { } reason)
                throw new InvalidOperationException(reason);
            if (alert)
            {
                if (projected.Id == 0) projected = projected with { Id = campaign.NextId("STR_UFO") };
                var craft = TargetCraft(projected.Destination!)!;
                if (!campaign.Options.SuppressRedundantHunterAlert || !FollowsUfo(craft.Logistics?.Destination, projected))
                    effects.Notify(new UfoStartedHunting(projected.UniqueId, projected.Destination!.TypeId,
                        projected.Destination.Id));
            }
            _ufos[index] = projected;
        }
    }

    private UfoSnapshot UfoForFlight(UfoSnapshot ufo, CampaignTimeTrigger highest) =>
        _retargetForecast?.GetValueOrDefault(ufo.UniqueId) ?? UfoAfterTimedTransitions(ufo, highest);

    private bool PlannedHuntMarker(UfoSnapshot ufo) => ufo.Id == 0 &&
        _retargetForecast?.TryGetValue(ufo.UniqueId, out var projected) == true && projected.Detected;

    private UfoSnapshot ProjectFlightTarget(UfoSnapshot ufo, CampaignTimeTrigger highest, bool buildingPrefix = false)
    {
        if (ufo.Hunting && ufo.Destination is { Kind: WorldTargetKind.Craft } craft)
        {
            if (_huntingCraftForecast?.GetValueOrDefault((craft.TypeId, craft.Id)) is { } state)
                return ufo with { Destination = craft with { Longitude = state.Longitude, Latitude = state.Latitude } };
            return Resolve(craft) is { } resolved ? ufo with { Destination = resolved } : ufo;
        }
        if (!ufo.Escorting || ufo.Destination is not { Kind: WorldTargetKind.Ufo } reference || TargetUfo(reference) is not { } target)
            return ufo;
        var position = UfoForFlight(target, highest).Position;
        // Reference target pointers see earlier actors after movement and later
        // actors before movement. Even cycles only depend on earlier list entries.
        var targetIndex = UfoIndex(target.UniqueId);
        if (targetIndex < UfoIndex(ufo.UniqueId))
            position = buildingPrefix ? _escortFlightPositions![targetIndex] : EscortFlightPosition(targetIndex, highest);
        return ufo with { Destination = reference with { Longitude = position.Longitude, Latitude = position.Latitude } };
    }

    private WorldPosition EscortFlightPosition(int targetIndex, CampaignTimeTrigger highest)
    {
        _escortFlightPositions ??= [];
        for (var index = _escortFlightPositions.Count; index <= targetIndex; index++)
        {
            var actor = ProjectFlightTarget(UfoForFlight(_ufos[index], highest), highest, buildingPrefix: true);
            if (index > _escortFlightEndIndex)
            {
                _escortFlightPositions.Add(actor.Position);
                continue;
            }
            var flight = ForecastUfoStep(actor, highest);
            _escortFlightPositions.Add(flight.Position);
            if (actor.Status == UfoStatus.Flying && flight.Status == UfoStatus.Destroyed)
                _escortFlightEndIndex = index;
        }
        return _escortFlightPositions[targetIndex];
    }

    private void ResetEscortFlightPositions()
    {
        _escortFlightPositions = null;
        _escortFlightEndIndex = int.MaxValue;
    }

    private int UfoIndex(int uniqueId)
    {
        for (var index = 0; index < _ufos.Count; index++)
            if (_ufos[index].UniqueId == uniqueId) return index;
        return -1;
    }
}

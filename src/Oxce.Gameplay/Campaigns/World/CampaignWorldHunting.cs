namespace Oxce.Gameplay.Campaigns.World;

public sealed record UfoStartedHunting(int UniqueId, string CraftTypeId, int CraftId) : ICampaignEvent;

internal sealed partial class CampaignWorld
{
    private const string HuntStateReason = "UFO hunting or escort state requires a valid original waypoint.";
    // Execution has its own buffer: daily/hourly handlers may change the craft population
    // and state after preflight. Each hunter replaces the contents before selection.
    private readonly List<WorldHuntCraft> _huntingCandidates = [];

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
        _pass.BeginHunting();
        var due = WorldHunting.RetargetDue(highest, speed, campaign.Content.RuntimeRules.Campaign.HunterKillerFastRetarget);
        long work = 0;
        foreach (var saved in _ufos)
        {
            var ufo = UfoAfterTimedTransitions(saved, highest);
            if (ufo.Status != UfoStatus.Flying || !ufo.HunterKiller && !ufo.Hunting && !ufo.Escorting) continue;
            if (_pass.HuntingCraftForecast is null && (due && ufo.HunterKiller || highest >= CampaignTimeTrigger.OneHour && ufo.Hunting))
            {
                if (ForecastHuntingCrafts(highest, ref work) is { } craftReason)
                    return craftReason;
            }
            if (ProjectHunting(ufo, highest, due, forecastFuel: true, ref work, out var projected, out var alert) is { } reason)
                return reason;
            if (projected != ufo) (_pass.RetargetForecast ??= []).Add(saved.UniqueId, projected);
            if (alert && ufo.Id == 0) _pass.PossibleHuntMarkerIds++;
        }
        foreach (var saved in _ufos)
            if (ForecastNewLeaderFlight(UfoForFlight(saved, highest), highest, ref work) is { } leaderReason)
                return leaderReason;
        // Each prefix actor and target lookup is visited at most once per tick.
        // Refuse before building the ordered position cache, never during mutation.
        foreach (var saved in _ufos)
            if (UfoForFlight(saved, highest).Escorting)
            {
                work += 4L * _ufos.Count * _ufos.Count + (long)_ufos.Count * _missions.Count;
                break;
            }
        if (work > MaximumWorldWork)
            return "UFO escort movement exceeds the supported work limit.";
        _pass.HuntingForecastWork = work;
        return CanAllocateUfoMarkerIds(_pass.PossibleHuntMarkerIds)
            ? null : "UFO contact marker IDs are exhausted or collide with a saved UFO.";
    }

    private string? ProjectHunting(UfoSnapshot ufo, CampaignTimeTrigger highest, bool due, bool forecastFuel,
        ref long work, out UfoSnapshot projected, out bool alert, AlienMissionSnapshot? missionForecast = null)
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
        var rule = rules.Ufos.GetRequiredValue(ufo.RuleId);
        work += _missions.Count;
        if (work > MaximumWorldWork) return ForecastLimits.UfoRetargetingWork;
        var mission = missionForecast ?? FindMission(ufo.MissionId);
        if (mission is null) return "UFO mission link requires world simulation.";
        var maximum = (long)rule.Stats.SpeedMaximum + rule.RaceBonus(mission.Race).SpeedMaximum;
        var radar = (long)rule.Stats.RadarRange + rule.RaceBonus(mission.Race).RadarRange;
        if (maximum is < 0 or > int.MaxValue || radar is < int.MinValue or > int.MaxValue)
            return ForecastLimits.UfoHuntingStatsRange;
        var crafts = forecastFuel ? _pass.HuntingCandidates : _huntingCandidates;
        var factsCache = forecastFuel ? _pass.HuntingFacts : _huntingFacts;
        crafts.Clear();
        foreach (var owner in campaign.BaseStates)
        {
            if (++work > MaximumWorldWork) return ForecastLimits.UfoRetargetingWork;
            foreach (var craft in forecastFuel ? _pass.HuntingCraftSources![owner.Id] : owner.Crafts)
            {
                if (++work > MaximumWorldWork) return ForecastLimits.UfoRetargetingWork;
                if (craft.Logistics is not { } state) continue;
                var craftRule = rules.Crafts[craft.Rule].Value;
                if (forecastFuel) state = _pass.HuntingCraftForecast!.GetValueOrDefault(
                    (rules.Crafts.GetExternalId(craft.Rule), craft.Id), state);
                var original = projected.Hunting && projected.Destination is { } destination &&
                    destination.TypeId == rules.Crafts.GetExternalId(craft.Rule) && destination.Id == craft.Id;
                var facts = HuntingFactsFor(craft, state, craftRule, factsCache);
                var damageMaximum = facts.DamageMaximum;
                var active = state.Status == "STR_OUT" && state.Damage < damageMaximum;
                if (!active && !original) continue;
                if (!ReadHuntingSpeed(ref facts, craftRule))
                    return SpeedRangeReason;
                var ignored = WorldHunting.IsIgnored(state.LowFuel, state.MissionComplete);
                var attraction = 0;
                if (!ignored && (original || !craftRule.Undetectable))
                {
                    if (!ReadHuntingCapacity(ref facts, craftRule) ||
                        damageMaximum is < int.MinValue or > int.MaxValue ||
                        !WorldHunting.TryAttraction(ufo.HuntMode, craftRule.AllowLanding, facts.Units,
                            facts.Speed, state.Damage, (int)damageMaximum, out attraction))
                        return "Craft hunter attraction exceeds the supported range.";
                }
                var reference = facts.Reference ??= new WorldTargetReference(WorldTargetKind.Craft, rules.Crafts.GetExternalId(craft.Rule),
                    craft.Id, state.Longitude, state.Latitude);
                factsCache[craft] = facts;
                crafts.Add(new(reference, reference.Position, attraction, ignored, craftRule.Undetectable, active));
            }
        }
        var selected = WorldHunting.Select(projected, (int)radar, crafts);
        crafts.Clear();
        if (selected is not null)
        {
            var original = projected.Hunting ? projected.Destination : null;
            if (original?.TypeId != selected.TypeId || original.Id != selected.Id)
            {
                if (!WorldHunting.TryHunt(projected, selected, (int)maximum, rule.HuntSpeed, out projected))
                    return ForecastLimits.UfoHuntSpeedRange;
                projected = projected with { Detected = true };
                alert = true;
            }
        }
        else if (projected.Hunting) projected = WorldHunting.Restore(projected);
        if (ufo.Escort && !projected.Hunting && !projected.Escorting)
        {
            work += _ufos.Count;
            if (work > MaximumWorldWork) return ForecastLimits.UfoRetargetingWork;
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
        try { RetargetUfosCore(effects, due); }
        finally
        {
            _huntingFacts.Clear();
            _huntingCandidates.Clear();
        }
    }

    private void RetargetUfosCore(CampaignState.TimeEffects effects, bool due)
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
        _pass.NewLeaderFlightForecast?.GetValueOrDefault(ufo.UniqueId) ??
        _pass.RetargetForecast?.GetValueOrDefault(ufo.UniqueId) ?? UfoAfterTimedTransitions(ufo, highest);

    /// <summary>Test-only: recompute every shared flight read and fail on a difference (ADR 0033).
    /// The test assemblies enable it for every test; it never changes a result.</summary>
    internal static bool VerifySharedFlights { get; set; }

    [ThreadStatic] private static long _verifiedSharedFlightReads;

    /// <summary>Test-only: stored shared flight reads verified on the current thread, which shows that
    /// the shared projection is actually used.</summary>
    internal static long VerifiedSharedFlightReads => _verifiedSharedFlightReads;

    /// <summary>
    /// <c>ProjectFlightTarget(UfoForFlight(_ufos[index]))</c> and the distance to its destination,
    /// shared by the UFO movement, site conversion and half-hour steps (ADR 0033). Outside that
    /// window, and for hunting or escorting UFOs, it computes directly.
    /// </summary>
    private UfoSnapshot SharedFlightAt(int index, CampaignTimeTrigger highest, out double distance)
    {
        var saved = _ufos[index];
        if (!_pass.SharedFlightsReady) return ComputeFlight(saved, highest, memoize: true, out distance);
        if (_pass.SharedFlights is null || _pass.SharedFlights.Length < _ufos.Count)
            Array.Resize(ref _pass.SharedFlights, _ufos.Count);
        ref var entry = ref _pass.SharedFlights[index];
        if (!ReferenceEquals(entry.Source, saved))
        {
            var projected = ProjectFlightTarget(UfoForFlight(saved, highest), highest);
            var shared = !projected.Hunting && !projected.Escorting;
            // A stored entry holds its own distance, so it bypasses the distance memo.
            distance = FlightDistance(projected, memoize: !shared);
            if (shared) entry = new SharedFlight { Source = saved, Projected = projected, Distance = distance };
            return projected;
        }
        if (VerifySharedFlights)
        {
            var expected = ComputeFlight(saved, highest, memoize: false, out var expectedDistance);
            if (!expected.Equals(entry.Projected) ||
                BitConverter.DoubleToInt64Bits(expectedDistance) != BitConverter.DoubleToInt64Bits(entry.Distance))
                throw new InvalidOperationException($"Shared flight of UFO {saved.UniqueId} differs from its recomputation.");
            _verifiedSharedFlightReads++;
        }
        distance = entry.Distance;
        return entry.Projected;
    }

    private UfoSnapshot ComputeFlight(UfoSnapshot saved, CampaignTimeTrigger highest, bool memoize, out double distance)
    {
        var projected = ProjectFlightTarget(UfoForFlight(saved, highest), highest);
        distance = FlightDistance(projected, memoize);
        return projected;
    }

    private double FlightDistance(UfoSnapshot projected, bool memoize) =>
        projected.Destination is not { } destination ? double.NaN :
        memoize ? UfoDestinationDistance(projected) : WorldGeometry.Distance(projected.Position, destination.Position);

    private bool PlannedHuntMarker(UfoSnapshot ufo) => ufo.Id == 0 &&
        _pass.RetargetForecast?.TryGetValue(ufo.UniqueId, out var projected) == true && projected.Detected;

    private UfoSnapshot ProjectFlightTarget(UfoSnapshot ufo, CampaignTimeTrigger highest, bool buildingPrefix = false)
    {
        // A regional leader/follower proof guarantees flying escort status but
        // no exact movement target. Consumers use its envelope or status only.
        if (_pass.InexactNewLeaderFlights?.Contains(ufo.UniqueId) == true) return ufo;
        if (ufo.Hunting && ufo.Destination is { Kind: WorldTargetKind.Craft } craft)
        {
            if (_pass.HuntingCraftForecast?.GetValueOrDefault((craft.TypeId, craft.Id)) is { } state)
                return SameTargetCoordinates(craft, state.Longitude, state.Latitude) ? ufo :
                    ufo with { Destination = craft with { Longitude = state.Longitude, Latitude = state.Latitude } };
            return Resolve(craft) is { } resolved && !SameTargetCoordinates(craft, resolved.Longitude, resolved.Latitude)
                ? ufo with { Destination = resolved } : ufo;
        }
        if (!ufo.Escorting || ufo.Destination is not { Kind: WorldTargetKind.Ufo } reference || TargetUfo(reference) is not { } target)
            return ufo;
        var index = UfoIndex(ufo.UniqueId);
        if (index >= 0 && _pass.EscortFlightTargets is { } cached && index < cached.Length &&
            ReferenceEquals(cached[index].Source, ufo)) return cached[index].Projected!;
        var position = UfoForFlight(target, highest).Position;
        // Reference target pointers see earlier actors after movement and later
        // actors before movement. Even cycles only depend on earlier list entries.
        var targetIndex = UfoIndex(target.UniqueId);
        if (targetIndex < index)
            position = buildingPrefix ? _pass.EscortFlightPositions![targetIndex] : EscortFlightPosition(targetIndex, highest);
        var projected = reference.Position == position ? ufo : ufo with
        { Destination = reference with { Longitude = position.Longitude, Latitude = position.Latitude } };
        if (index >= 0)
        {
            if (_pass.EscortFlightTargets is null || _pass.EscortFlightTargets.Length < _ufos.Count)
                Array.Resize(ref _pass.EscortFlightTargets, _ufos.Count);
            _pass.EscortFlightTargets[index] = (ufo, projected);
        }
        return projected;
    }

    private WorldPosition EscortFlightPosition(int targetIndex, CampaignTimeTrigger highest)
    {
        _pass.EscortFlightPositions ??= [];
        for (var index = _pass.EscortFlightPositions.Count; index <= targetIndex; index++)
        {
            var actor = ProjectFlightTarget(UfoForFlight(_ufos[index], highest), highest, buildingPrefix: true);
            if (index > _pass.EscortFlightEndIndex)
            {
                _pass.EscortFlightPositions.Add(actor.Position);
                continue;
            }
            var flight = ForecastUfoStep(actor, highest);
            _pass.EscortFlightPositions.Add(flight.Position);
            if (actor.Status == UfoStatus.Flying && flight.Status == UfoStatus.Destroyed)
                _pass.EscortFlightEndIndex = index;
        }
        return _pass.EscortFlightPositions[targetIndex];
    }

    private int UfoIndex(int uniqueId)
    {
        for (var index = 0; index < _ufos.Count; index++)
            if (_ufos[index].UniqueId == uniqueId) return index;
        return -1;
    }

    private static bool SameTargetCoordinates(WorldTargetReference target, double longitude, double latitude) =>
        BitConverter.DoubleToInt64Bits(target.Longitude) == BitConverter.DoubleToInt64Bits(longitude) &&
        BitConverter.DoubleToInt64Bits(target.Latitude) == BitConverter.DoubleToInt64Bits(latitude);
}

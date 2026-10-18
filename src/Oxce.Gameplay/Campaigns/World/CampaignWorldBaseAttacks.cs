using Oxce.Gameplay.Campaigns.World.Interception;
using Oxce.Mods.Rulesets.Runtime;

namespace Oxce.Gameplay.Campaigns.World;

/// <summary>A retaliation UFO reached an X-COM base with defenses: the base defense window opens and pauses time.</summary>
public sealed record BaseDefenseStarted(int BaseId, int UfoUniqueId, bool InstaHyper) : ICampaignEvent;

/// <summary>Fires the base defenses (BaseDefenseState's Start) or skips them (Skip firing), then applies the outcome.</summary>
public sealed record ResolveBaseDefense(int BaseId, bool Fire) : ICampaignCommand;

/// <summary>The defense window closed: the UFO was shot down, or the attack went on to its battle.</summary>
public sealed record BaseDefenseResolved(int BaseId, int UfoUniqueId, bool UfoDestroyed, bool RetaliationStopped) : ICampaignEvent;

/// <summary>
/// A base was destroyed by an attack (BaseDestroyedState); its crafts, crew and stores are lost.
/// <paramref name="Missiles"/> marks a nuclear missile strike rather than an undefended base.
/// </summary>
public sealed record BaseDestroyed(int BaseId, string Name, bool Missiles = false) : ICampaignEvent;

/// <summary>
/// Missiles damaged a base that survived (BaseDestroyedState with partial destruction): the facilities lost
/// by type, and whether the stores no longer fit (the reference then opens the sell screen).
/// </summary>
public sealed record BaseDamagedByMissiles(int BaseId, string Name, IReadOnlyList<DestroyedFacilityCount> Destroyed,
    bool StoresOverfull) : ICampaignEvent;

/// <summary>The campaign ended (SavedGame::setEnding): 1 is END_WIN, 2 is END_LOSE.</summary>
public sealed record CampaignEnded(int Ending) : ICampaignEvent
{
    /// <summary>
    /// For an ending inside a tick, the video that ends the game (CutsceneState then opens the statistics and drops the
    /// queued popups): the defeat video for a loss, otherwise the first video the tick queued. A window's own ending
    /// names its video in its acknowledgement.
    /// </summary>
    public string? Cutscene { get; init; }
}

/// <summary>The open base defense window.</summary>
public sealed record CampaignBaseDefense(int BaseId, int UfoUniqueId, bool InstaHyper, int Defenses, int GravShields);

public interface ICampaignBaseDefenseQuery
{
    CampaignBaseDefense? QueryBaseDefense();
}

/// <summary>
/// The base attack of <c>GeoscapeState::time5Seconds</c>: a retaliation assault UFO reaching the X-COM
/// base at its ground waypoint (<c>AlienMission::ufoReachedWaypoint</c>) draws the next wave's
/// countdown, loses its destination and meets <c>Base::setupDefenses</c>. With defenses (and without
/// <c>ignoreBaseDefenses</c>) the <c>BaseDefenseState</c> window opens; otherwise, or after the window,
/// <c>handleBaseDefense</c> ends the attack: the UFO is spent, an instant retaliation ends, and the
/// defenders' battle becomes the deployment request (D4), a base without defenders is destroyed, or
/// missiles damage or destroy the base. Either path ends the tick's five-second pass.
/// </summary>
internal sealed partial class CampaignWorld : ICampaignBaseDefenseQuery
{
    private const string BaseDefenseMissionType = "STR_BASE_DEFENSE";
    private (int BaseId, int UfoUniqueId, bool InstaHyper)? _baseDefense;

    private void RegisterBaseAttacks(CampaignCapabilityRegistry registry)
    {
        registry.Query<ICampaignBaseDefenseQuery>(this);
        registry.Timed(CampaignTimeTrigger.FiveSeconds, CampaignTimeOrder.FiveSecondsCampaignEnding, "campaign ending", CheckCampaignEnding);
        registry.Command<ResolveBaseDefense>(ResolveBaseDefense);
        // The window is modal: time stops (landing preflight) and other commands wait.
        // time1Month queues the monthly report, and time30Minutes the events, before a base defense of the same tick,
        // so those are answered first.
        registry.CommandGate(command => _baseDefense is not null &&
            command is not (World.ResolveBaseDefense or AdvanceCampaignTime or AcknowledgeMonthlyReport or AcknowledgeCampaignEvent)
            ? Blocked(WorldCommandReasons.BaseDefenseOpen) : null);
        // D2: the window is not saved and its opening already parked the UFO, so a save under it would lose the attack.
        registry.SaveGate(() => _baseDefense is not null ? WorldCommandReasons.BaseDefenseOpen : null);
    }

    public CampaignBaseDefense? QueryBaseDefense() => campaign.Read(() =>
    {
        if (_baseDefense is not { } open) return null;
        var (defenses, grav) = BaseDefenseSetup.For(campaign.BaseStates.Single(owner => owner.Id == open.BaseId), campaign.Content.RuntimeRules);
        return new CampaignBaseDefense(open.BaseId, open.UfoUniqueId, open.InstaHyper, defenses.Count, grav);
    });

    /// <summary>A retaliation assault run's UFO reaching its ground waypoint: the base assault.</summary>
    private static bool IsBaseAssaultArrival(UfoSnapshot ufo, RuntimeUfoTrajectoryRule trajectory) =>
        ufo.TrajectoryId == RuntimeUfoTrajectoryRule.RetaliationAssaultRun && ufo.TrajectoryPoint + 1 < trajectory.Waypoints.Count &&
        trajectory.Altitude(ufo.TrajectoryPoint + 1) == 0;

    // fmath AreSame: the base whose coordinates equal the UFO's within a relative double epsilon.
    private CampaignState.BaseState? BaseAt(WorldPosition position)
    {
        const double DoubleEpsilon = 2.220446049250313e-16;
        static bool Same(double left, double right) =>
            Math.Abs(left - right) <= DoubleEpsilon * Math.Max(1.0, Math.Max(Math.Abs(left), Math.Abs(right)));
        foreach (var owner in campaign.BaseStates)
            if (owner.IsPlaced && Same(owner.Longitude, position.Longitude) && Same(owner.Latitude, position.Latitude)) return owner;
        return null;
    }

    /// <summary>The preflight of a base assault arrival: only outcomes the port can apply may run.</summary>
    private string? BaseAssaultReason(string ufoRuleId, RuntimeAlienMissionRule rule, WorldPosition arrival)
    {
        if (BaseAt(arrival) is not { } owner) return null;
        if (BaseDefenseSetup.For(owner, campaign.Content.RuntimeRules).Defenses.Count != 0 && !rule.IgnoreBaseDefenses) return null;
        return BaseDefenseOutcomeReason(owner, ufoRuleId);
    }

    // The checks of handleBaseDefense's missile damage: any craft in the base's hangars may be destroyed, and
    // the base's hunters return to their waypoints (SavedGame::stopHuntingXcomCrafts).
    private string? BaseDefenseOutcomeReason(CampaignState.BaseState owner, string ufoRuleId)
    {
        var rules = campaign.Content.RuntimeRules;
        if (rules.Ufos.GetRequiredValue(ufoRuleId).MissilePower <= 0) return null;
        var crafts = owner.Crafts.Where(static craft => craft.Logistics?.Status != "STR_OUT")
            .Select(craft => (rules.Crafts.GetExternalId(craft.Rule), craft.Id)).ToArray();
        if (((ICampaignCraftRemoval)this).PlanReason(crafts) is { } removal) return removal;
        foreach (var craft in owner.Crafts)
        {
            var type = rules.Crafts.GetExternalId(craft.Rule);
            foreach (var ufo in _ufos)
                if (ufo.Destination is { Kind: WorldTargetKind.Craft } target && target.TypeId == type && target.Id == craft.Id &&
                    ufo.OriginalWaypoint is not { IsNormalized: true }) return HuntStateReason;
        }
        return null;
    }

    /// <summary>
    /// Base::setupDefenses's vehicles left on the base: each stored vehicle type without clip ammunition
    /// joins in full; one with clip ammunition joins as far as the stores still hold its loaded clips.
    /// The reference walks its stores in RuleItem address order, which follows the order rules were first
    /// loaded; the port uses that rule order, so vehicle types sharing too little ammunition are served
    /// first-loaded first (ADR 0034). A vehicle that loads zero clips, which the reference divides by, cannot
    /// be loaded and stays in the stores. Returns the vehicles and what they take out of the stores.
    /// </summary>
    private (IReadOnlyList<string> Vehicles, IReadOnlyDictionary<RuleHandle<ItemRuleFamily>, int> Taken) StoredDefenders(
        CampaignState.BaseState owner)
    {
        var rules = campaign.Content.RuntimeRules;
        var vehicles = new List<string>();
        var taken = new Dictionary<RuleHandle<ItemRuleFamily>, int>();
        foreach (var item in rules.Items.Rules)
        {
            if (item.Value.VehicleArmor is null) continue;
            var handle = rules.Items.GetRequired(item.Id);
            if (owner.Items.GetValueOrDefault(handle) is var quantity && quantity <= 0) continue;
            var (ammo, perVehicle) = CraftLogistics.VehicleAmmunition(new CraftVehicleSnapshot(item.Id, 0), rules);
            if (ammo.Length == 0)
            {
                vehicles.AddRange(Enumerable.Repeat(item.Id, quantity));
                taken[handle] = quantity;
                continue;
            }
            if (perVehicle <= 0) continue;
            var ammoHandle = rules.Items.GetRequired(ammo);
            var stock = owner.Items.GetValueOrDefault(ammoHandle) - taken.GetValueOrDefault(ammoHandle);
            var added = Math.Min(quantity, stock / perVehicle);
            if (added == 0) continue;
            vehicles.AddRange(Enumerable.Repeat(item.Id, added));
            taken[handle] = added;
            taken[ammoHandle] = taken.GetValueOrDefault(ammoHandle) + added * perVehicle;
        }
        return (vehicles, taken);
    }

    // GeoscapeState::time5Seconds: game over when no base is left (D5: the reference rule).
    private void CheckCampaignEnding(CampaignState.TimeEffects effects)
    {
        // Game over when no base is left; then this tick's ending, if any, is reported once (time5Seconds).
        if (campaign.Ending == 0 && campaign.BaseStates.Count == 0 && campaign.MonthsPassed >= 0) campaign.EndInTick(2);
        campaign.ReportTickEnding(effects);
    }

    /// <summary>
    /// The UFO pass reaching the base: AlienMission::ufoReachedWaypoint's waypoint draws and assault branch,
    /// then the attack. Returns the UFO as the pass leaves it; the caller ends the five-second pass.
    /// </summary>
    private void ArriveForBaseAssault(int index, UfoSnapshot ufo, AlienMissionSnapshot mission, RuntimeUfoTrajectoryRule trajectory,
        WorldPosition position, CampaignState.TimeEffects effects)
    {
        var rules = campaign.Content.RuntimeRules;
        var nextWaypoint = ufo.TrajectoryPoint + 1;
        var region = rules.Regions.GetRequiredValue(mission.RegionId);
        var ufoRule = rules.Ufos.GetRequiredValue(ufo.RuleId);
        var missionRule = MissionRule(mission);
        var wave = missionRule.Waves[MissionWaveIndex(ufo, mission, missionRule)];
        // getWaypoint's random choices happen before the assault branch ignores them.
        _ = OrdinaryWaypoint(mission, trajectory, nextWaypoint, region, ufoRule, wave);
        if (campaign.Options.AggressiveRetaliation && missionRule.Objective == RuntimeMissionObjective.Retaliation &&
            trajectory.Zone(nextWaypoint) != 5)
            foreach (var candidate in campaign.BaseStates)
                if (WorldGeometry.InsideRegion(region, new(candidate.Longitude, candidate.Latitude)))
                {
                    var radius = Math.PI / 12;
                    _ = WorldGeometry.RandomDouble(campaign.Random, candidate.Longitude - radius, candidate.Longitude + radius);
                    _ = WorldGeometry.RandomDouble(campaign.Random, candidate.Latitude - radius, candidate.Latitude + radius);
                    break;
                }
        var waypoint = OrdinaryWaypoint(mission, trajectory, nextWaypoint, region, ufoRule, wave);
        var owner = BaseAt(position);
        // setDestination recalculates the speed vector and direction: towards the new waypoint, which a UFO
        // whose base is gone keeps, or none at all for the base it is standing on.
        var next = WorldPosition.Create(waypoint.Longitude, waypoint.Latitude);
        var vector = owner is null ? WorldGeometry.SpeedVector(position, next, ufo.SpeedRadian) : (Longitude: 0.0, Latitude: 0.0);
        var arrived = ufo with
        {
            TrajectoryPoint = nextWaypoint,
            Longitude = position.Longitude,
            Latitude = position.Latitude,
            Altitude = WorldAltitudes.All[trajectory.Altitude(nextWaypoint)],
            Detected = false,
            Status = owner is null ? UfoStatus.Destroyed : ufo.Status,
            Destination = owner is null
                ? new WorldTargetReference(WorldTargetKind.Waypoint, WorldTargetReference.WaypointType, 0, next.Longitude, next.Latitude)
                : BaseReference(owner),
            SpeedLongitude = vector.Longitude,
            SpeedLatitude = vector.Latitude,
            Direction = WorldAltitudes.Direction(vector.Longitude, vector.Latitude),
        };
        // UfoLostState for a contact that disappeared from its followers' screens.
        if (ufo.Detected && HasArrivalContactFollowers(ufo))
            effects.Notify(new UfoContactLost(ufo.UniqueId) { MarkerId = ufo.Id });
        _ufos[index] = arrived;
        // Only spawn mission if the base is still there.
        if (owner is null) return;

        var missionIndex = _missions.FindIndex(candidate => candidate.Id == mission.Id);
        var countdown = WorldTrajectory.ShotDownDelay(campaign.Random);
        // AlienMission::setWaveCountdown leaves a mission that is over alone.
        if (!(mission.NextWave == missionRule.Waves.Count && mission.LiveUfos == 0))
            _missions[missionIndex] = _missions[missionIndex] with { SpawnCountdown = countdown };
        _ufos[index] = arrived with { Destination = null, SpeedLongitude = 0, SpeedLatitude = 0 };
        // Base::setupDefenses remembers the retaliation mission.
        if (missionRule.Objective == RuntimeMissionObjective.Retaliation) owner.RetaliationMissionId = mission.Id;
        if (BaseDefenseSetup.For(owner, rules).Defenses.Count != 0 && !missionRule.IgnoreBaseDefenses)
        {
            var instaHyper = ufoRule.InstaHyper || missionRule.InstaHyper;
            _baseDefense = (owner.Id, ufo.UniqueId, instaHyper);
            effects.Notify(new BaseDefenseStarted(owner.Id, ufo.UniqueId, instaHyper));
            return;
        }
        if (HandleBaseDefense(owner, index) is { } request) effects.Notify(new DeploymentRequested(request));
        else if (TakeBaseOutcome() is { } outcome) effects.Notify(outcome);
    }

    // The window an attack without a battle leaves: the base destroyed or damaged by missiles.
    private ICampaignEvent? TakeBaseOutcome()
    {
        if (_damagedBase is { } damaged)
        {
            _damagedBase = null;
            return damaged;
        }
        if (_destroyedBase is not { } destroyed) return null;
        _destroyedBase = null;
        return new BaseDestroyed(destroyed.Id, destroyed.Name, destroyed.Missiles);
    }

    private BaseDamagedByMissiles? _damagedBase;

    private CampaignCommandResult ResolveBaseDefense(ResolveBaseDefense command)
    {
        if (_baseDefense is not { } open || open.BaseId != command.BaseId) return Blocked(WorldCommandReasons.NoBaseDefense);
        var owner = campaign.BaseStates.Single(candidate => candidate.Id == open.BaseId);
        var index = UfoIndex(open.UfoUniqueId);
        var saved = _ufos[index];
        var mission = FindMission(saved.MissionId)!;
        var rules = campaign.Content.RuntimeRules;
        var missionRule = MissionRule(mission);
        var stats = rules.Ufos.GetRequiredValue(saved.RuleId).StatsForRace(mission.Race);
        var ufoRule = rules.Ufos.GetRequiredValue(saved.RuleId);
        var random = campaign.Random;
        var before = random.State;
        var (defenses, gravShields) = BaseDefenseSetup.For(owner, rules);
        var ufo = new BaseDefenseUfo
        {
            DamageMaximum = stats.DamageMaximum,
            ShieldCapacity = stats.ShieldCapacity,
            AvoidBonus2 = stats.AvoidBonus2,
            Armor = stats.Armor,
            ShieldBleedThrough = stats.ShieldBleedThrough,
            NeverCrashes = ufoRule.Unmanned || saved.HuntBehavior == 1,
            Damage = saved.Damage,
            Shield = saved.Shield,
        };
        // setupDefenses has already taken the stored vehicles and their clips out of the stores the defenses
        // draw on; the port leaves them stored (battle generation moves them), so they are set aside here.
        var stored = StoredDefenders(owner);
        var storage = owner.Items.ToDictionary(pair => rules.Items.GetExternalId(pair.Key),
            pair => pair.Value - stored.Taken.GetValueOrDefault(pair.Key), StringComparer.Ordinal);
        var interrupted = false;
        if (command.Fire)
            BaseDefense.Fire(defenses, gravShields, ufo, storage, random, () => WaveInterrupt(saved, missionRule),
                () => interrupted = true);
        var destroyed = ufo.Status == DogfightUfoStatus.Destroyed;
        if (!destroyed && BaseDefenseOutcomeReason(owner, saved.RuleId) is { } refusal)
        {
            random.Restore(before);
            return Blocked(refusal);
        }

        // The rounds' effects: ammunition, stores and the UFO.
        foreach (var defense in defenses)
            owner.Facilities[defense.FacilityIndex] = owner.Facilities[defense.FacilityIndex] with { Ammo = defense.Ammo };
        foreach (var handle in owner.Items.Keys.ToArray())
            if (!storage.ContainsKey(rules.Items.GetExternalId(handle))) owner.Items.Remove(handle);
        foreach (var (item, quantity) in storage)
        {
            var handle = rules.Items.GetRequired(item);
            owner.Items[handle] = quantity + stored.Taken.GetValueOrDefault(handle);
        }
        _ufos[index] = saved with
        {
            Damage = ufo.Damage,
            Shield = ufo.Shield,
            Status = ufo.Status switch
            {
                DogfightUfoStatus.Destroyed => UfoStatus.Destroyed,
                DogfightUfoStatus.Crashed => UfoStatus.Crashed,
                _ => saved.Status,
            },
        };
        var missionIndex = _missions.FindIndex(candidate => candidate.Id == mission.Id);
        if (interrupted) _missions[missionIndex] = _missions[missionIndex] with { Interrupted = true };
        _baseDefense = null;
        var events = new List<ICampaignEvent>();
        var stopped = false;
        if (destroyed)
        {
            // Base::cleanupDefenses; an instant retaliation spawns one UFO only.
            if (missionRule.Objective == RuntimeMissionObjective.InstantRetaliation)
                _missions[missionIndex] = _missions[missionIndex] with { Interrupted = true };
            if (missionRule.Objective == RuntimeMissionObjective.Retaliation &&
                RandomChance.Percent(random, rules.Campaign.Interception.ChanceToStopRetaliation))
            {
                stopped = true;
                StopRetaliation(owner);
            }
        }
        else if (HandleBaseDefense(owner, index) is { } request)
        {
            events.Add(new DeploymentRequested(request));
        }
        else if (TakeBaseOutcome() is { } outcome)
        {
            events.Add(outcome);
        }
        events.Insert(0, new BaseDefenseResolved(owner.Id, open.UfoUniqueId, destroyed, stopped));
        return new CampaignCommandResult(events);
    }

    private static int WaveInterrupt(UfoSnapshot ufo, RuntimeAlienMissionRule rule)
    {
        var wave = ufo.MissionWaveNumber;
        if (rule.SpawnUfoId.Length != 0 && rule.SpawnUfoId == ufo.RuleId && ufo.TrajectoryId == RuntimeUfoTrajectoryRule.RetaliationAssaultRun)
            wave = rule.Waves.Count - 1;
        return wave > -1 && wave < rule.Waves.Count ? rule.Waves[wave].InterruptPercentage : 0;
    }

    // BaseDefenseState::btnOkClick: the aliens give up on this base.
    private void StopRetaliation(CampaignState.BaseState owner)
    {
        owner.RetaliationTarget = false;
        var mission = RetaliationMissionOf(owner);
        if (mission is not null && MissionRule(mission).MultiUfoRetaliation)
        {
            var index = _missions.FindIndex(candidate => candidate.Id == mission.Id);
            _missions[index] = _missions[index] with { MultiUfoRetaliationInProgress = true };
            return;
        }
        DeleteRetaliationMission(mission, owner);
    }

    /// <summary>
    /// BaseDestroyedState and its OK: the retaliation mission and its UFOs go, hunters of the base's crafts
    /// return to their waypoints, crafts following them go home, and the base is removed with all it holds.
    /// </summary>
    private void DestroyBase(CampaignState.BaseState owner, bool missiles = false)
    {
        DeleteRetaliationMission(RetaliationMissionOf(owner), owner);
        var rules = campaign.Content.RuntimeRules;
        StopHuntingBaseCrafts(owner);
        _interceptions.RemoveAll(dogfight => dogfight.Craft.Key is InterceptionCraftKey key && key.BaseId == owner.Id);
        _pendingInterceptions.RemoveAll(dogfight => dogfight.Craft.Key is InterceptionCraftKey key && key.BaseId == owner.Id);
        campaign.RemoveBase(owner);
        foreach (var craft in owner.Crafts)
            ((ICampaignCraftRemoval)this).Removed(rules.Crafts.GetExternalId(craft.Rule), craft.Id, null);
        _destroyedBase = (owner.Id, owner.Name, missiles);
    }

    private (int Id, string Name, bool Missiles)? _destroyedBase;

    // SavedGame::stopHuntingXcomCrafts: UFOs hunting the base's crafts return to their waypoints.
    private void StopHuntingBaseCrafts(CampaignState.BaseState owner)
    {
        var rules = campaign.Content.RuntimeRules;
        foreach (var craft in owner.Crafts)
        {
            var type = rules.Crafts.GetExternalId(craft.Rule);
            for (var index = 0; index < _ufos.Count; index++)
                if (_ufos[index].Destination is { Kind: WorldTargetKind.Craft } target && target.TypeId == type && target.Id == craft.Id &&
                    _ufos[index].OriginalWaypoint is not null)
                    _ufos[index] = WorldHunting.RestoreAndAim(_ufos[index]);
        }
    }

    private AlienMissionSnapshot? RetaliationMissionOf(CampaignState.BaseState owner)
    {
        var rules = campaign.Content.RuntimeRules;
        AlienMissionSnapshot? mission = owner.RetaliationMissionId is { } id ? FindMission(id) : null;
        if (mission is null)
        {
            // Backwards compatibility: the first region containing the base (else the first region), by objective.
            var region = campaign.RegionStates.FirstOrDefault(candidate =>
                WorldGeometry.InsideRegion(rules.Regions[candidate.Rule].Value, new WorldPosition(owner.Longitude, owner.Latitude))) ??
                campaign.RegionStates[0];
            var regionId = rules.Regions.GetExternalId(region.Rule);
            mission = _missions.FirstOrDefault(candidate => candidate.RegionId == regionId &&
                MissionRule(candidate).Objective == RuntimeMissionObjective.Retaliation);
        }
        return mission;
    }

    // SavedGame::deleteRetaliationMission: the mission and its UFOs go.
    private void DeleteRetaliationMission(AlienMissionSnapshot? mission, CampaignState.BaseState owner)
    {
        if (mission is not null)
        {
            _ufos.RemoveAll(ufo => ufo.MissionId == mission.Id);
            _missions.RemoveAll(candidate => candidate.Id == mission.Id);
        }
        owner.RetaliationMissionId = null;
    }

    /// <summary>
    /// GeoscapeState::handleBaseDefense: the UFO is spent; missiles damage the base's facilities, a nuclear
    /// strike or the lack of defenders destroys the base, and otherwise the defenders' battle is requested.
    /// </summary>
    private CampaignDeploymentRequest? HandleBaseDefense(CampaignState.BaseState owner, int index)
    {
        var rules = campaign.Content.RuntimeRules;
        var ufo = _ufos[index];
        var mission = FindMission(ufo.MissionId)!;
        var missionRule = MissionRule(mission);
        var stats = rules.Ufos.GetRequiredValue(ufo.RuleId).StatsForRace(mission.Race);
        var texture = StrategicGeography.TextureAt(rules.Campaign.Globe, ufo.Longitude, ufo.Latitude) ?? -1;
        var shade = WorldShading.LandingShade(campaign.Time, ufo.Longitude, ufo.Latitude, campaign.Options.GlobeSeasons);
        var damagePercentage = rules.Campaign.Interception.LessAliensDuringBaseDefense && stats.DamageMaximum != 0
            ? (int)Math.Floor((double)ufo.Damage / stats.DamageMaximum * 100) : 0;
        _ufos[index] = ufo with { Status = UfoStatus.Destroyed };
        var missionIndex = _missions.FindIndex(candidate => candidate.Id == mission.Id);
        if (missionRule.Objective == RuntimeMissionObjective.InstantRetaliation)
            _missions[missionIndex] = _missions[missionIndex] with { Interrupted = true };
        var ufoRule = rules.Ufos.GetRequiredValue(ufo.RuleId);
        if (ufoRule.MissilePower > 0)
        {
            // Missiles damage facilities; the base survives and its retaliation mission may strike again.
            StopHuntingBaseCrafts(owner);
            var (destroyedFacilities, overfull) = campaign.DamageBaseFacilities(owner, ufoRule.MissilePower, craft =>
                ((ICampaignCraftRemoval)this).Removed(rules.Crafts.GetExternalId(craft.Rule), craft.Id, null));
            _damagedBase = new BaseDamagedByMissiles(owner.Id, owner.Name, destroyedFacilities, overfull);
            if (ufoRule.MissileStopChance > 0 && RandomChance.Percent(campaign.Random, ufoRule.MissileStopChance))
                _missions[missionIndex] = _missions[missionIndex] with { Interrupted = true };
            return null;
        }
        if (ufoRule.MissilePower < 0)
        {
            // A nuclear warhead: BaseDestroyedState(missiles) deletes the retaliation mission at once.
            DestroyBase(owner, missiles: true);
            // "Continue mayhem?" The reference reads the deleted UFO's rules here; the roll is kept and only a
            // mission that still exists is interrupted.
            if (ufoRule.MissileStopChance > 0 && RandomChance.Percent(campaign.Random, ufoRule.MissileStopChance))
            {
                var stopped = _missions.FindIndex(candidate => candidate.Id == mission.Id);
                if (stopped >= 0) _missions[stopped] = _missions[stopped] with { Interrupted = true };
            }
            return null;
        }
        var defenders = BaseDefenders(owner);
        if (defenders.Soldiers.Count == 0 && defenders.Vehicles.Count == 0)
        {
            // "Please garrison your bases in future": BaseDestroyedState.
            DestroyBase(owner);
            return null;
        }
        _deploymentRequest = new CampaignDeploymentRequest(BaseDefenseMissionType, "",
            stats.MissionCustomDeployment.Length != 0 && rules.AlienDeployments.TryGet(stats.MissionCustomDeployment, out _) ? stats.MissionCustomDeployment : "",
            "", mission.Race, texture, texture, shade, BaseReference(owner), owner.Id, "", 0)
        {
            Soldiers = defenders.Soldiers,
            Vehicles = defenders.Vehicles,
            UfoDamagePercentage = damagePercentage,
        };
        return _deploymentRequest;
    }

    /// <summary>
    /// Base::getAvailableSoldiers(true, true) and the craft vehicles Base::setupDefenses collects: soldiers on
    /// crafts that are not out, and unassigned soldiers who are unwounded or fit enough (Soldier::canDefendBase
    /// with <c>oxceWoundedDefendBaseIf</c>), then the vehicles of crafts at the base and those left on it.
    /// </summary>
    private (IReadOnlyList<int> Soldiers, IReadOnlyList<string> Vehicles) BaseDefenders(CampaignState.BaseState owner)
    {
        var rules = campaign.Content.RuntimeRules;
        var soldiers = new List<int>();
        foreach (var soldier in owner.Soldiers)
        {
            if (soldier.Personal is not { } personal) continue;
            if (personal.CraftType.Length != 0)
            {
                var craft = owner.Crafts.FirstOrDefault(candidate => candidate.Id == personal.CraftId &&
                    rules.Crafts.GetExternalId(candidate.Rule) == personal.CraftType);
                if (craft?.Logistics?.Status != "STR_OUT") soldiers.Add(soldier.Id);
                continue;
            }
            var health = (int)personal.CurrentStats.GetValueOrDefault("health");
            var wounded = SoldierReadiness.IsWounded(personal, rules.Campaign.ManaWoundThreshold, rules.Campaign.HealthWoundThreshold);
            var fit = health > 0 && Math.Max(0, health - (int)Math.Ceiling(personal.Recovery) - personal.HealthMissing) * 100 / health >=
                campaign.Options.WoundedDefendBaseIf;
            if (!wounded || fit) soldiers.Add(soldier.Id);
        }
        var vehicles = new List<string>();
        foreach (var craft in owner.Crafts)
            if (craft.Logistics is { } state && state.Status != "STR_OUT")
                vehicles.AddRange(state.Vehicles.Select(static vehicle => vehicle.RuleId));
        vehicles.AddRange(StoredDefenders(owner).Vehicles);
        return (soldiers, vehicles);
    }
}

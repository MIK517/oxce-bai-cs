using Oxce.Core.Random;
using Oxce.Gameplay.Campaigns.World.Interception;
using Oxce.Mods.Rulesets.CampaignStart;
using Oxce.Mods.Rulesets.Runtime;

namespace Oxce.Gameplay.Campaigns.World;

/// <summary>
/// The world consequences of an interception step: the <c>DogfightState</c> calls into the geoscape
/// (crash bookkeeping, <c>AlienMission::ufoShotDown</c>, retaliation, X-COM activity, wave interrupts,
/// pilot experience, break-off and hunter-killer endings, crash followers). Reference:
/// <c>DogfightState::update</c>, <c>awardExperienceToPilots</c>, <c>Ufo::setDamage</c> and
/// <c>SavedGame::findAlienMission</c> at 4df3a5e.
/// </summary>
internal sealed partial class CampaignWorld
{
    /// <summary>
    /// The world as one interception step sees it. A live step changes the campaign directly. A
    /// forecast keeps every change in overlays, so later callbacks of the same step read what they
    /// would see and draw the same random values, and records the first outcome the port cannot apply;
    /// the live step only runs after a forecast that found none.
    /// </summary>
    private sealed class InterceptionWorld(CampaignWorld world, bool forecast, IReadOnlyList<Dogfight> windows) : IDogfightWorld
    {
        private readonly Dictionary<InterceptionCraftKey, CraftLogisticsState> _crafts = [];
        private readonly Dictionary<int, UfoSnapshot> _ufos = [];
        private readonly Dictionary<int, AlienMissionSnapshot> _missions = [];
        private readonly List<AlienMissionSnapshot> _createdMissions = [];
        private readonly Dictionary<(int BaseId, int SoldierId), SoldierPersonalState> _soldiers = [];
        private readonly Dictionary<string, int> _ids = new(StringComparer.Ordinal);
        private readonly Dictionary<(bool Country, int Index), int> _activity = [];

        public string? Reason { get; private set; }

        public IRandomSource Random => world.Campaign.Random;
        public CampaignInterceptionSettings Settings => Rules.Campaign.Interception;
        public int Difficulty => (int)world.Campaign.Difficulty;
        public int MonthsPassed => world.Campaign.MonthsPassed;
        public bool DogfightAI => world.Campaign.Options.DogfightAI;

        private RuntimeRuleCatalog Rules => world.Campaign.Content.RuntimeRules;

        // ---- Reads and writes through the forecast overlays ----------------------------------------

        private (CampaignState.BaseState Owner, int Index, CraftLogisticsState State) Craft(InterceptionCraftKey key)
        {
            if (!world.TryFindCraft(key.BaseId, key.TypeId, key.Id, out var owner, out var index))
                throw new InvalidOperationException("An intercepting craft no longer exists.");
            return (owner, index, _crafts.TryGetValue(key, out var state) ? state : owner.Crafts[index].Logistics!);
        }

        private void SetCraft(InterceptionCraftKey key, CraftLogisticsState state)
        {
            if (forecast)
            {
                _crafts[key] = state;
                return;
            }
            var (owner, index, _) = Craft(key);
            owner.Crafts[index] = owner.Crafts[index] with { Logistics = state };
        }

        private UfoSnapshot Ufo(DogfightUfo ufo) => Ufo((int)ufo.Key) ?? throw new InvalidOperationException("An intercepted UFO no longer exists.");

        private UfoSnapshot? Ufo(int uniqueId)
        {
            if (_ufos.TryGetValue(uniqueId, out var overlay)) return overlay;
            var index = world.UfoIndex(uniqueId);
            return index < 0 ? null : world._ufos[index];
        }

        private void SetUfo(UfoSnapshot ufo)
        {
            if (forecast) _ufos[ufo.UniqueId] = ufo;
            else world._ufos[world.UfoIndex(ufo.UniqueId)] = ufo;
        }

        private AlienMissionSnapshot Mission(DogfightUfo ufo)
        {
            var id = Ufo(ufo).MissionId;
            return _missions.TryGetValue(id, out var overlay) ? overlay
                : world.FindMission(id) ?? throw new InvalidOperationException("An intercepted UFO has no mission.");
        }

        private void SetMission(AlienMissionSnapshot mission)
        {
            if (forecast)
            {
                _missions[mission.Id] = mission;
                return;
            }
            var index = world._missions.FindIndex(candidate => candidate.Id == mission.Id);
            world._missions[index] = mission;
        }

        private IEnumerable<AlienMissionSnapshot> Missions =>
            world._missions.Select(mission => _missions.GetValueOrDefault(mission.Id, mission)).Concat(_createdMissions);

        private SoldierPersonalState Personal(CampaignState.BaseState owner, CampaignState.SoldierState soldier) =>
            _soldiers.TryGetValue((owner.Id, soldier.Id), out var overlay) ? overlay : soldier.Personal!;

        private void SetPersonal(CampaignState.BaseState owner, int soldierId, SoldierPersonalState personal)
        {
            if (forecast)
            {
                _soldiers[(owner.Id, soldierId)] = personal;
                return;
            }
            var index = owner.Soldiers.FindIndex(soldier => soldier.Id == soldierId);
            owner.Soldiers[index] = owner.Soldiers[index] with { Personal = personal };
        }

        /// <summary>An outcome the port cannot apply: the forecast stops the step; a live step never gets here.</summary>
        private void Fail(string reason)
        {
            if (!forecast) throw new InvalidOperationException("An interception step reached an outcome its forecast did not stop: " + reason);
            Reason ??= reason;
        }

        // ---- IDogfightWorld ---------------------------------------------------------------------

        public bool CraftTargetsUfo(DogfightCraft craft, DogfightUfo ufo) =>
            Craft((InterceptionCraftKey)craft.Key).State.Destination is { Kind: WorldTargetKind.Ufo } target &&
            (target.UniqueId > 0 ? target.UniqueId == (int)ufo.Key : Ufo((int)ufo.Key) is { } snapshot && snapshot.Id == target.Id);

        public void DogfightExperience()
        {
            // GeoscapeState::handleDogfightExperience: every window whose UFO is down awards once.
            foreach (var window in windows)
            {
                if (!window.FiredAtLeastOnce || window.ExperienceAwarded || !(window.Ufo.IsCrashed || window.Ufo.IsDestroyed)) continue;
                AwardExperience(window);
                window.ExperienceAwarded = true;
            }
        }

        // DogfightState::awardExperienceToPilots. The stat string is presentation and is not kept.
        private void AwardExperience(Dogfight window)
        {
            var key = (InterceptionCraftKey)window.Craft.Key;
            var (owner, index, state) = Craft(key);
            var craft = owner.Crafts[index];
            var rule = Rules.Crafts[craft.Rule].Value;
            var (pilots, ids) = world.PilotList(owner, craft, rule, key.TypeId, state);
            if (rule.Pilots != 0) SetCraft(key, state with { Pilots = ids });
            foreach (var pilot in pilots)
            {
                var personal = Personal(owner, pilot);
                var soldierRule = Rules.Soldiers[pilot.Rule].Value;
                var stats = new Dictionary<string, short>(personal.CurrentStats, StringComparer.Ordinal);
                var daily = personal.DailyDogfightExperience;
                if (Gain("firing", 1)) daily = daily with { Firing = daily.Firing + 1 };
                if (Gain("reactions", 1)) daily = daily with { Reactions = daily.Reactions + 1 };
                // Bravery grows by ten to keep it a multiple of ten.
                if (Gain("bravery", 10)) daily = daily with { Bravery = daily.Bravery + 10 };
                // SoldierDiary::addUfoShotDown counts the UFO's maximum damage, as the reference does.
                var damage = (long)personal.UfosDamageTotal + window.Ufo.Stats.DamageMaximum;
                if (personal.UfosShotDownTotal == int.MaxValue || damage is < int.MinValue or > int.MaxValue)
                {
                    Fail("Pilot interception totals exceed the supported range.");
                    continue;
                }
                SetPersonal(owner, pilot.Id, personal with
                {
                    CurrentStats = new System.Collections.ObjectModel.ReadOnlyDictionary<string, short>(stats),
                    DailyDogfightExperience = daily,
                    UfosShotDownTotal = personal.UfosShotDownTotal + 1,
                    UfosDamageTotal = (int)damage,
                });

                bool Gain(string stat, short amount)
                {
                    var current = stats.GetValueOrDefault(stat);
                    if (current >= soldierRule.StatCaps.GetValueOrDefault(stat)) return false;
                    if (!RandomChance.Percent(Random, soldierRule.DogfightExperience.GetValueOrDefault(stat))) return false;
                    stats[stat] = unchecked((short)(current + amount));
                    return true;
                }
            }
        }

        public bool InsideLand(DogfightUfo ufo) => world.InsideLand(ufo);

        public bool InsideFakeUnderwater(DogfightUfo ufo) => WorldGeometry.InsideFakeUnderwaterTexture(
            Rules.Campaign.Globe, new WorldPosition(ufo.Longitude, ufo.Latitude));

        // MovingTarget::move once, toward the destination the UFO broke off to (Ufo::calculateSpeed sets
        // its heading).
        public void MoveUfo(DogfightUfo ufo)
        {
            var snapshot = Ufo(ufo) with { Speed = ufo.Speed };
            if ((snapshot.Hunting || snapshot.Escorting) && world.Resolve(snapshot.Destination) is { } target &&
                !SameTargetCoordinates(snapshot.Destination!, target.Longitude, target.Latitude))
                snapshot = snapshot with { Destination = target };
            if (snapshot.Destination is not { } destination)
            {
                SetUfo(snapshot);
                return;
            }
            var speedRadian = WorldGeometry.RadianSpeed(snapshot.Speed);
            var vector = WorldGeometry.SpeedVector(snapshot.Position, destination.Position, speedRadian);
            var moved = WorldGeometry.Move(snapshot.Position, destination.Position, speedRadian, vector);
            SetUfo(snapshot with
            {
                Longitude = moved.Longitude,
                Latitude = moved.Latitude,
                SpeedRadian = speedRadian,
                SpeedLongitude = vector.Longitude,
                SpeedLatitude = vector.Latitude,
                Direction = WorldAltitudes.Direction(vector.Longitude, vector.Latitude),
            });
            ufo.Longitude = moved.Longitude;
            ufo.Latitude = moved.Latitude;
        }

        // Craft::setDestination(ufo): the craft keeps chasing at full speed.
        public void CraftPursueUfo(DogfightCraft craft, DogfightUfo ufo)
        {
            var key = (InterceptionCraftKey)craft.Key;
            var (_, _, state) = Craft(key);
            var snapshot = Ufo(ufo);
            var target = state.Destination is { } current && FollowsUfo(current, snapshot) ? current
                : new WorldTargetReference(WorldTargetKind.Ufo, WorldTargetReference.UfoType, snapshot.Id, 0, 0) { UniqueId = snapshot.UniqueId };
            if (MaximumSpeed(key) is not { } speed) return;
            SetCraft(key, SetDestination(state, speed, target with { Longitude = ufo.Longitude, Latitude = ufo.Latitude }));
        }

        public void CraftReturnToBase(DogfightCraft craft) => ReturnToBase((InterceptionCraftKey)craft.Key);

        private void ReturnToBase(InterceptionCraftKey key)
        {
            var (owner, _, state) = Craft(key);
            if (MaximumSpeed(key) is not { } speed) return;
            SetCraft(key, SetDestination(state, speed, BaseReference(owner)));
        }

        private int? MaximumSpeed(InterceptionCraftKey key)
        {
            var (owner, index, state) = Craft(key);
            if (CraftLogistics.TryEffectiveSpeedMaximum(Rules.Crafts[owner.Crafts[index].Rule].Value, state.Weapons, Rules, out var speed))
                return speed;
            Fail(WorldCommandReasons.SpeedRange);
            return null;
        }

        public bool CraftThink(DogfightCraft craft)
        {
            var key = (InterceptionCraftKey)craft.Key;
            var (owner, index, state) = Craft(key);
            var updated = world.CraftThink(owner.Crafts[index], state, out _, out var returned);
            SetCraft(key, updated);
            return returned;
        }

        // SavedGame::stopHuntingXcomCraft: every UFO targeting the craft returns to its original waypoint.
        public void StopHuntingCraft(DogfightCraft craft)
        {
            var key = (InterceptionCraftKey)craft.Key;
            foreach (var saved in world._ufos.ToArray())
            {
                var ufo = Ufo(saved.UniqueId)!;
                if (TargetsCraft(ufo, key)) RestoreOriginalDestination(ufo);
            }
        }

        public void ReturnUnlandableFollowers(DogfightUfo ufo)
        {
            var snapshot = Ufo(ufo);
            foreach (var owner in world.Campaign.BaseStates)
            {
                for (var index = 0; index < owner.Crafts.Count; index++)
                {
                    var craft = owner.Crafts[index];
                    if (craft.Logistics is null) continue;
                    var key = new InterceptionCraftKey(owner.Id, Rules.Crafts.GetExternalId(craft.Rule), craft.Id);
                    var state = Craft(key).State;
                    if (!FollowsUfo(state.Destination, snapshot)) continue;
                    if (world.CraftHasUnits(owner, craft, state) && Rules.Crafts[craft.Rule].Value.AllowLanding) continue;
                    ReturnToBase(key);
                }
            }
        }

        // Ufo::resetOriginalDestination(craft): only while the UFO's destination is still that craft.
        public void ResetHunterDestination(DogfightUfo ufo, DogfightCraft craft)
        {
            var snapshot = Ufo(ufo);
            if (!TargetsCraft(snapshot, (InterceptionCraftKey)craft.Key)) return;
            RestoreOriginalDestination(snapshot);
            ufo.TargetCraft = null;
        }

        private static bool TargetsCraft(UfoSnapshot ufo, InterceptionCraftKey key) =>
            ufo.Destination is { Kind: WorldTargetKind.Craft } target && target.TypeId == key.TypeId && target.Id == key.Id;

        private void RestoreOriginalDestination(UfoSnapshot ufo)
        {
            if (ufo.OriginalWaypoint is null)
            {
                Fail(HuntStateReason);
                return;
            }
            SetUfo(WorldHunting.RestoreAndAim(ufo));
        }

        // Ufo::setDestination(0): followers keep following; MovingTarget::calculateSpeed stops the vector.
        public void ClearUfoDestination(DogfightUfo ufo) =>
            SetUfo(Ufo(ufo) with { Destination = null, SpeedLongitude = 0, SpeedLatitude = 0 });

        // AlienMission::ufoShotDown: a lost UFO delays the next wave.
        public void UfoShotDown(DogfightUfo ufo)
        {
            var mission = Mission(ufo);
            if (mission.NextWave == world.MissionRule(mission).Waves.Count) return;
            var countdown = (long)mission.SpawnCountdown + WorldTrajectory.ShotDownDelay(Random);
            if (countdown > int.MaxValue)
            {
                Fail("Alien mission spawn countdown exceeds the supported range.");
                return;
            }
            SetMission(mission with { SpawnCountdown = (int)countdown });
        }

        public int MissionRetaliationOdds(DogfightUfo ufo) => world.MissionRule(Mission(ufo)).RetaliationOdds;

        public int RetaliationAggression(DogfightUfo ufo) => Race(Mission(ufo).Race)?.RetaliationAggression ?? 0;

        public string UfoMissionRegion(DogfightUfo ufo) => Mission(ufo).RegionId;

        // SavedGame::locateRegion(craft base).
        public string? CraftBaseRegion(DogfightCraft craft)
        {
            var (owner, _, _) = Craft((InterceptionCraftKey)craft.Key);
            foreach (var region in world.Campaign.RegionStates)
                if (WorldGeometry.InsideRegion(Rules.Regions[region.Rule].Value, new WorldPosition(owner.Longitude, owner.Latitude)))
                    return Rules.Regions.GetExternalId(region.Rule);
            return null;
        }

        // SavedGame::findAlienMission(region, OBJECTIVE_RETALIATION, race): by the race's retaliation
        // mission names when it has some for this month, otherwise by objective.
        public bool RetaliationMissionExists(string region, DogfightUfo ufo)
        {
            var race = Race(Mission(ufo).Race);
            if (race is not null && race.RetaliationMissionWeights.Count != 0)
            {
                if (RetaliationWeights(race) is not { } weights) return true;
                var names = weights.Where(static pair => pair.Value != 0).Select(static pair => pair.Key)
                    .OrderBy(static name => name, StringComparer.Ordinal).ToArray();
                if (names.Length != 0)
                    return names.Any(name => Missions.Any(mission => mission.RuleId == name && mission.RegionId == region));
            }
            return Missions.Any(mission => mission.RegionId == region &&
                world.MissionRule(mission).Objective == RuntimeMissionObjective.Retaliation);
        }

        // DogfightState::update: the race's retaliation weights, else Mod::getRandomMission(OBJECTIVE_RETALIATION),
        // then AlienMission::setRegion, setRace and start with the first wave's spawn timer.
        public void StartRetaliation(string region, DogfightUfo ufo)
        {
            var source = Mission(ufo);
            var race = Race(source.Race);
            var chosen = "";
            if (race is not null && race.RetaliationMissionWeights.Count != 0)
            {
                if (RetaliationWeights(race) is not { } weights) return;
                if (WeightTotal(weights) is null) return;
                chosen = WorldAlienMissionGeneration.Choose(weights, Random);
            }
            var (ruleId, rule) = chosen.Length != 0 && Rules.AlienMissions.TryGet(chosen, out var handle)
                ? (chosen, Rules.AlienMissions[handle].Value) : RandomRetaliationMission();
            if (rule is null) return;
            if (rule.Waves.Count == 0)
            {
                Fail("A retaliation mission without waves cannot start.");
                return;
            }
            if (rule.OperationType != RuntimeMissionOperationType.Space && world.MissionOperationReason(rule, region) is { } operationReason)
            {
                Fail(operationReason);
                return;
            }
            if (!Rules.Regions.TryGet(region, out var regionHandle))
            {
                Fail("A retaliation region is not defined.");
                return;
            }
            var id = NextId("ALIEN_MISSIONS");
            var missionRegion = Rules.Regions[regionHandle].Value.MissionRegionId is { Length: > 0 } remapped ? remapped : region;
            var spawnTimer = rule.Waves[0].SpawnTimer;
            if (spawnTimer > int.MaxValue)
            {
                Fail("A retaliation spawn timer exceeds the supported range.");
                return;
            }
            // AlienMission::start draws the countdown only for a zero timer.
            var countdown = spawnTimer == 0 ? WorldTrajectory.SpawnCountdown(0, Random) : (int)spawnTimer;
            var created = new AlienMissionSnapshot(id, ruleId, missionRegion, source.Race, 0, 0, countdown, 0, -1);
            if (rule.OperationType != RuntimeMissionOperationType.Space)
            {
                // AlienMission::start then picks or spawns its operation base (Earth-based operations).
                if (!forecast) created = world.InitializeMissionOperation(created, rule);
                else if (OperationBaseReason(created, rule, region) is { } baseReason)
                {
                    Fail(baseReason);
                    return;
                }
            }
            if (forecast) _createdMissions.Add(created);
            else world._missions.Add(created);
        }

        private Dictionary<string, int>? _baseMarkers;
        private ActivityRange[]? _baseRegionScores;
        private ActivityRange[]? _baseCountryScores;
        private int _basePopulation = -1;
        private long _baseWork;

        // The forecast of AlienMission::start's operation base: the same draws as the live step, and a base about
        // to be spawned reserves its marker ID and activity as mission scripts do. Several retaliations in one
        // step share the reservation.
        private string? OperationBaseReason(AlienMissionSnapshot mission, RuntimeAlienMissionRule rule, string region)
        {
            var (selected, spawn) = world.PlanMissionOperation(mission, rule);
            if (selected is not null || spawn is null) return null;
            _baseMarkers ??= new(StringComparer.Ordinal);
            _baseRegionScores ??= world.Campaign.RegionStates.Select(static state => new ActivityRange(state.ActivityAlien[^1])).ToArray();
            _baseCountryScores ??= world.Campaign.CountryStates.Select(static state => new ActivityRange(state.ActivityAlien[^1])).ToArray();
            if (_basePopulation < 0) _basePopulation = world._alienBases.Count;
            return world.ReserveOperationBaseCreation([(Rules.AlienMissions.GetRequired(mission.RuleId), region)], _baseMarkers,
                _baseRegionScores, _baseCountryScores, ref _basePopulation, ref _baseWork);
        }

        private RuntimeAlienRaceRule? Race(string id)
        {
            if (Rules.AlienRaces.TryGet(id, out var handle)) return Rules.AlienRaces[handle].Value;
            Fail("An intercepted UFO's race is not defined.");
            return null;
        }

        // AlienRace::retaliationMissionWeights: the last entry, in rule order, that has started.
        private IReadOnlyDictionary<string, ulong>? RetaliationWeights(RuntimeAlienRaceRule race)
        {
            var month = unchecked((ulong)world.Campaign.MonthsPassed);
            for (var index = race.RetaliationMissionWeights.Count - 1; index >= 0; index--)
                if (month >= race.RetaliationMissionWeights[index].FirstMonth) return race.RetaliationMissionWeights[index].Weights;
            Fail("A race's retaliation mission weights have no entry for the current month.");
            return null;
        }

        private long? WeightTotal(IReadOnlyDictionary<string, ulong> weights)
        {
            ulong total = 0;
            foreach (var weight in weights.Values)
            {
                if (weight > int.MaxValue - total)
                {
                    Fail("Retaliation mission weights exceed the supported range.");
                    return null;
                }
                total += weight;
            }
            return (long)total;
        }

        // Mod::getRandomMission(OBJECTIVE_RETALIATION, monthsPassed) over the missions in id order.
        private (string Id, RuntimeAlienMissionRule? Rule) RandomRetaliationMission()
        {
            var month = unchecked((ulong)world.Campaign.MonthsPassed);
            var candidates = new List<(string Id, RuntimeAlienMissionRule Rule, long Total)>();
            long total = 0;
            foreach (var entry in Rules.AlienMissions.Rules.OrderBy(static rule => rule.Id, StringComparer.Ordinal))
            {
                if (entry.Value.Objective != RuntimeMissionObjective.Retaliation) continue;
                var weight = MissionWeight(entry.Value, month);
                if (weight <= 0) continue;
                total += weight;
                if (total > int.MaxValue)
                {
                    Fail("Retaliation mission weights exceed the supported range.");
                    return ("", null);
                }
                candidates.Add((entry.Id, entry.Value, total));
            }
            if (total == 0) return ("", null);
            var pick = Random.NextInclusive(1, (int)total);
            foreach (var candidate in candidates)
                if (pick <= candidate.Total) return (candidate.Id, candidate.Rule);
            throw new InvalidOperationException("Random source returned an invalid retaliation mission roll.");
        }

        // RuleAlienMission::getWeight: 1 without weights, else the last entry that has started (0 before the first).
        private static int MissionWeight(RuntimeAlienMissionRule rule, ulong month)
        {
            if (rule.MissionWeights.Count == 0) return 1;
            var weight = 0;
            foreach (var pair in rule.MissionWeights.OrderBy(static pair => pair.Key))
            {
                if (pair.Key > month) break;
                weight = pair.Value;
            }
            return weight;
        }

        // The first country and the first region containing the UFO.
        public void AddXcomActivity(DogfightUfo ufo, int score) =>
            world.AddXcomActivity(new WorldPosition(ufo.Longitude, ufo.Latitude), score, AddActivity);

        private void AddActivity(bool country, int index, List<int> history, int score)
        {
            var current = _activity.TryGetValue((country, index), out var overlay) ? overlay : history[^1];
            var next = (long)current + score;
            if (next is < int.MinValue or > int.MaxValue)
            {
                Fail("X-COM activity exceeds the supported range.");
                return;
            }
            if (forecast) _activity[(country, index)] = (int)next;
            else history[^1] = (int)next;
        }

        public int NextId(string key)
        {
            if (!forecast) return world.Campaign.NextId(key);
            var used = _ids.GetValueOrDefault(key);
            var next = (long)world.Campaign.PeekNextId(key) + used;
            if (next >= int.MaxValue)
            {
                Fail("Marker identity range is exhausted.");
                return 0;
            }
            _ids[key] = used + 1;
            return (int)next;
        }

        // Ufo::setDamage: the interrupt chance of the UFO's wave; the battleship of a retaliation assault
        // run counts as the last wave.
        public int WaveInterruptPercentage(DogfightUfo ufo)
        {
            var snapshot = Ufo(ufo);
            var rule = world.MissionRule(Mission(ufo));
            var wave = snapshot.MissionWaveNumber;
            if (rule.SpawnUfoId.Length != 0 && rule.SpawnUfoId == snapshot.RuleId &&
                snapshot.TrajectoryId == RuntimeUfoTrajectoryRule.RetaliationAssaultRun)
                wave = rule.Waves.Count - 1;
            if (wave <= -1) return 0;
            if (wave >= rule.Waves.Count)
            {
                Fail("An intercepted UFO's wave is not defined.");
                return 0;
            }
            return rule.Waves[wave].InterruptPercentage;
        }

        public void InterruptMission(DogfightUfo ufo) => SetMission(Mission(ufo) with { Interrupted = true });
    }

    /// <summary>Adds X-COM activity to the first country and the first region containing the position.</summary>
    private void AddXcomActivity(WorldPosition position, int score, Action<bool, int, List<int>, int> add)
    {
        var rules = campaign.Content.RuntimeRules;
        for (var index = 0; index < campaign.CountryStates.Count; index++)
            if (WorldGeometry.InsideCountry(rules.Countries[campaign.CountryStates[index].Rule].Value, position))
            {
                add(true, index, campaign.CountryStates[index].ActivityXcom, score);
                break;
            }
        for (var index = 0; index < campaign.RegionStates.Count; index++)
            if (WorldGeometry.InsideRegion(rules.Regions[campaign.RegionStates[index].Rule].Value, position))
            {
                add(false, index, campaign.RegionStates[index].ActivityXcom, score);
                break;
            }
    }
}

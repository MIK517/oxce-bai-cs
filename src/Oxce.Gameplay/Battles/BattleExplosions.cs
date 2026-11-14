using Oxce.Gameplay.Campaigns;
using Oxce.Gameplay.Battles.Generation;
using Oxce.Gameplay.Battles.Terrain;
using Oxce.Mods.Rulesets.Runtime;

namespace Oxce.Gameplay.Battles;

/// <summary>
/// Explosions (Phase 7 branch 5 S5): <c>ExplosionBState</c> (its <c>init</c>, <c>explode</c> and the chain of terrain explosions it starts),
/// <c>TileEngine::explode</c>, <c>detonate</c> and <c>checkForTerrainExplosions</c>, the units and items an explosion spawns, the self-destruct
/// of a unit, and the generator's <c>explodePowerSources</c> and <c>explodeOtherJunk</c>. The reference's list of states
/// (<c>statePushFront</c>, <c>statePushNext</c>, <c>popState</c>) is one ordered queue here: a state's <c>init</c> runs when it is pushed to the
/// front or becomes the front, its <c>think</c> (with the animation collapsed) when <see cref="RunStates"/> reaches it, so explosions, deaths
/// and the explosions a hit pushes happen in the reference's order. The queue is empty between commands and is not saved.
/// </summary>
internal sealed partial class BattleState
{
    private const int BigWallNesw = 2;
    private const int BigWallNwse = 3;
    private const int MeleeBattleTypeOfItem = 3;
    private const int PsiAmpBattleTypeOfItem = 9;
    private const int FirearmBattleTypeOfItem = 1;
    private const int ProximityBattleTypeOfItem = 5;
    private const int StunDamage = 6;

    /// <summary><c>Options::battleExplosionHeight</c> is 0 (a flat explosion) by default: the vertical decrement of a ray is 1000.</summary>
    private const int FlatExplosionDecrement = 1000;

    private readonly List<QueuedState> _states = [];
    private readonly List<BattleCasualty> _stateCasualties = [];
    private bool _runningStates;

    /// <summary>One <c>BattleState</c> of the battlescape's list: <c>init</c> when it reaches the front, <c>think</c> until it pops itself.</summary>
    private abstract class QueuedState
    {
        public abstract void Init();

        public abstract void Think();
    }

    /// <summary>
    /// The refusal a self-destruct item that is a melee weapon left behind (set by <c>DamageUnit</c>, deep inside a blast or a hit): the commands
    /// and the generator take it with <see cref="TakeExplosionRefusal"/> when their states have run, and refuse (the generator fails, a command's trial copy is dropped).
    /// </summary>
    internal CampaignReason? ExplosionRefusal { get; set; }

    /// <summary>Returns and clears <see cref="ExplosionRefusal"/>.</summary>
    internal CampaignReason? TakeExplosionRefusal()
    {
        var refusal = ExplosionRefusal;
        ExplosionRefusal = null;
        return refusal;
    }

    /// <summary>Whether a state waits to run.</summary>
    internal bool HasPendingStates => _states.Count != 0;

    /// <summary>BattlescapeGame::statePushFront: the state goes first and starts at once.</summary>
    private void PushFront(QueuedState state)
    {
        _states.Insert(0, state);
        state.Init();
    }

    /// <summary>BattlescapeGame::statePushNext: into an empty list the state starts at once, else it goes right behind the running state.</summary>
    private void PushNext(QueuedState state)
    {
        if (_states.Count == 0)
        {
            _states.Add(state);
            state.Init();
        }
        else
        {
            _states.Insert(1, state);
        }
    }

    /// <summary>BattlescapeGame::popState: the running state leaves and the next one starts.</summary>
    private void PopState()
    {
        if (_states.Count == 0) return;
        _states.RemoveAt(0);
        if (_states.Count != 0) _states[0].Init();
    }

    /// <summary>
    /// Runs the list of states to its end (the battlescape's loop with the animations collapsed): each state thinks until it pops itself. Returns
    /// the units taken out by the deaths that ran. Called by the commands that can push a state; a call from inside a running state does nothing.
    /// </summary>
    internal IReadOnlyList<BattleCasualty> RunStates()
    {
        if (_runningStates) return [];
        _runningStates = true;
        try
        {
            while (_states.Count != 0)
            {
                var front = _states[0];
                front.Think();
                if (_states.Count != 0 && ReferenceEquals(_states[0], front))
                    throw new InvalidOperationException("A battle state did not finish.");
            }
            var casualties = _stateCasualties.ToArray();
            return casualties;
        }
        finally
        {
            _runningStates = false;
            _stateCasualties.Clear();
        }
    }

    /// <summary><c>UnitDieBState</c> in the list: its constructor ran when <c>checkForCasualties</c> made it; <c>init</c> may skip it, <c>think</c> is the fall.</summary>
    private sealed class DeathState(BattleState battle, PendingDeath death) : QueuedState
    {
        public override void Init()
        {
            if (!death.Finished && SkippedByInit(death.Unit)) battle.PopState();
        }

        public override void Think()
        {
            if (battle.RunDeath(death) is { } casualty) battle._stateCasualties.Add(casualty);
            battle.PopState();
        }
    }

    // ---- ExplosionBState ----

    /// <summary>
    /// Pushes an <c>ExplosionBState</c> to the front of the list (<c>statePushFront</c>), as the shots, throws and hits do: <paramref name="center"/> is where
    /// it goes off in voxels, <paramref name="before"/> where the spawns appear (the projectile's last position before the impact; the center when omitted).
    /// The explosion happens in its <c>init</c>; the casualty check, the spawns and the terrain explosions it starts happen when the list runs
    /// (<see cref="RunStates"/>). A melee hit or a psi attack is not an explosion of this slice (S6 and branch 6).
    /// </summary>
    internal void PushExplosion(in BattleAttack attack, BattlePosition center, BattlePosition? before = null, bool lowerWeapon = false,
        int range = 0)
    {
        PushFront(new ExplosionState(this, RequiredRules, attack, center, before ?? center, null, lowerWeapon, range, 0));
    }

    /// <summary>
    /// <c>ExplosionBState</c>: the damage of a thrown or fired item that explodes, of a unit that blows up with its death, of an exploding terrain part,
    /// or of a hit that did not explode. The animation, sound and camera are presentation and draw nothing here.
    /// </summary>
    private sealed class ExplosionState : QueuedState
    {
        private readonly BattleState _battle;
        private readonly RuntimeRuleCatalog _rules;
        private readonly BattleAttack _attack;
        private readonly BattlePosition _center;
        private readonly BattlePosition _before;
        private readonly int? _tile;
        private readonly bool _lowerWeapon;
        private readonly int _range;
        private readonly int _explosionCounter;
        private int _power;
        private int _radius = 6;
        private RuntimeItemDamageRule _damageType = HighExplosiveType;
        private bool _areaOfEffect;

        public ExplosionState(BattleState battle, RuntimeRuleCatalog ruleCatalog, BattleAttack attack, BattlePosition center, BattlePosition before, int? tile,
            bool lowerWeapon, int range, int explosionCounter)
        {
            _battle = battle;
            _rules = ruleCatalog;
            _attack = attack;
            _center = center;
            _before = before;
            _tile = tile;
            _lowerWeapon = lowerWeapon;
            _range = range;
            _explosionCounter = explosionCounter;
        }

        /// <summary>ExplosionBState::init.</summary>
        public override void Init()
        {
            var rangeAttack = true;
            if (_attack.DamageItem is { } damageItem)
            {
                var itemRule = _battle.ItemRuleOf(damageItem.ItemType);
                var type = itemRule.BattleType;
                var hit = _attack.Type == BattleActionType.Hit;
                if (hit || type == MeleeBattleTypeOfItem || type == PsiAmpBattleTypeOfItem && _attack.Type != BattleActionType.SelfDestruct)
                    throw new NotSupportedException("A melee hit or a psi attack is not an explosion of Phase 7 branch 5 S5 (S6 and branch 6).");

                _power = 0;
                if (_attack.Weapon is { } weapon && _rules.Battle.Equipment.Items[weapon.ItemType].Combat.IgnoreAmmoPower)
                {
                    _power += _battle.PowerBonus(weapon, _attack);
                    _power = (int)(_power - _rules.Battle.Equipment.Items[weapon.ItemType].Combat.PowerRangeReductionAt(_range));
                }
                else
                {
                    _power += _battle.PowerBonus(damageItem, _attack);
                    _power = (int)(_power - _rules.Battle.Equipment.Items[damageItem.ItemType].Combat.PowerRangeReductionAt(_range));
                }
                _radius = _battle.ExplosionRadius(damageItem, _attack);
                _damageType = itemRule.Damage;

                var miss = false;
                if (_attack.Type == BattleActionType.SelfDestruct)
                {
                    if (!RandomChance.Percent(_battle.Random, _rules.Battle.Equipment.Items[damageItem.ItemType].Combat.SpecialChance)) _power = 0;
                }
                else if (type == FirearmBattleTypeOfItem && _power <= 0)
                {
                    miss = true;
                }
                // (A psi amplifier's self-destruct is a plain hit: its area test fails, and no psi attack runs for BA_SELF_DESTRUCT.)
                _areaOfEffect = type != PsiAmpBattleTypeOfItem && _radius != 0 && !miss;
                rangeAttack = !(_attack.Weapon is { } held && _battle.ItemRuleOf(held.ItemType).BattleType == PsiAmpBattleTypeOfItem);
            }
            else if (_tile is { } tile)
            {
                var damageType = _battle.Map.ExplosiveType(tile) switch
                {
                    0 => RuntimeItemDamageRule.DamageHighExplosive,
                    5 => RuntimeItemDamageRule.DamageIncendiary,
                    6 => StunDamage,
                    _ => RuntimeItemDamageRule.DamageSmoke,
                };
                _power = _battle.Map.Explosive(tile);
                _battle.Map.SetExplosive(tile, 0, 0, force: true);
                _damageType = RuntimeItemDamageRule.Predefined(damageType);
                _radius = _power / 10;
                _areaOfEffect = true;
            }
            else
            {
                _power = 120;
                _damageType = HighExplosiveType;
                _areaOfEffect = true;
            }

            if (_areaOfEffect)
            {
                if (_power > 0)
                {
                    _battle.Events.Add(new BattleExploded(_center, _power, _radius, _tile is not null));
                    _battle.Explode(_attack, _center, _power, _damageType, _radius, rangeAttack);
                }
                else
                {
                    _battle.PopState();
                }
            }
            else
            {
                // A bullet hit.
                _battle.Hit(_attack, _center, _power, _damageType, rangeAttack);
            }

            if (_attack.Type == BattleActionType.SelfDestruct) _attack.Attacker?.AlreadyExploded = false;
        }

        /// <summary>ExplosionBState::explode, which <c>think</c> runs when the animation is done.</summary>
        public override void Think()
        {
            var terrainExplosion = _tile is not null || _attack.DamageItem is null;

            // now check for new casualties
            _battle.QueueCasualties(_attack.DamageItem is not null ? _damageType : null, _attack, hiddenExplosion: false, terrainExplosion,
                statesQueued: true, pushNext: death => _battle.PushNext(new DeathState(_battle, death)));
            // revive units if damage could give hp or reduce stun
            _battle.ReviveUnconsciousUnits(noTU: true);
            // if any unit get infected turn it to zombie
            _battle.ConvertInfected();

            // if this explosion was caused by a unit shooting, now it's the time to put the gun down
            if (_attack.Attacker is { IsOut: false } attacker && _lowerWeapon) attacker.Status = BattleUnitStatus.Standing;

            if (_attack.DamageItem is { } item && _battle.ItemRuleOf(item.ItemType).BattleType is GrenadeBattleType or ProximityBattleTypeOfItem)
                _battle.RemoveItem(item);

            _battle.PopState();

            // check for terrain explosions
            if (_battle.CheckForTerrainExplosions() is { } next)
            {
                var position = _battle.Map.PositionOf(next);
                var voxel = new BattlePosition(position.X * 16 + 8, position.Y * 16 + 8, position.Z * 24);
                _battle.PushFront(new ExplosionState(_battle, _rules, new BattleAttack(BattleActionType.None, _attack.Attacker), voxel, voxel, next, false, 0,
                    _explosionCounter + 1));
            }

            // Spawn a unit and an item if the item does that
            if (_attack.DamageItem is not null)
            {
                var spawnAt = BattleGeometry.TileOfVoxel(_before);
                _battle.SpawnNewUnit(_attack, spawnAt);
                _battle.SpawnNewItem(_attack, spawnAt);
            }
        }
    }

    /// <summary>RuleItem::getPowerBonus(attack): the item's damage bonus over its power, for the unit that attacks (a bonus written as code is refused at the briefing).</summary>
    private int PowerBonus(BattleItem item, in BattleAttack attack)
    {
        var rule = ItemRuleOf(item.ItemType);
        var bonus = RequiredRules.Battle.Equipment.Items[item.ItemType].Combat.StatBonuses.Damage;
        var power = bonus.IsScript ? rule.Power : bonus.Evaluate(attack.Attacker, rule.Power);
        return Hooks.StatBonus(BattleStatBonusKind.Damage, attack.Attacker, item, power);
    }

    /// <summary>
    /// RuleItem::getExplosionRadius(attack): the damage type's fixed radius, or from the power bonus times the type's radius effectiveness (a fire
    /// blast adds one), kept between 1 and 11.
    /// </summary>
    private int ExplosionRadius(BattleItem item, in BattleAttack attack)
    {
        var type = ItemRuleOf(item.ItemType).Damage;
        if (type.FixedRadius != -1) return type.FixedRadius;
        var radius = (int)(PowerBonus(item, attack) * type.RadiusEffectiveness);
        if (type.FireBlastCalc) radius += 1;
        // cap the formula to 11
        if (radius > 11) radius = 11;
        if (radius <= 0) radius = 1;
        return radius;
    }

    /// <summary>
    /// TileEngine::checkForTerrainExplosions: the first tile (by index) that holds an explosive a destroyed part left, or null. (A preview has no
    /// terrain explosions; the port has no preview.)
    /// </summary>
    internal int? CheckForTerrainExplosions()
    {
        for (var tile = 0; tile < Map.TileCount; tile++)
            if (Map.Explosive(tile) != 0) return tile;
        return null;
    }

    /// <summary>
    /// The chain the battlescape starts when a turn ends or between actions (<c>BattlescapeGame::endTurn</c>, <c>statePushNext</c> of an
    /// <c>ExplosionBState</c> for the first tile): every pending terrain explosion goes off, each one's casualties, spawns and further explosions
    /// included. Branch 6's end of turn calls it after <see cref="SpreadFireAndSmoke"/>.
    /// </summary>
    internal IReadOnlyList<BattleCasualty> RunTerrainExplosions()
    {
        if (CheckForTerrainExplosions() is { } tile)
        {
            var position = Map.PositionOf(tile);
            PushNext(new ExplosionState(this, RequiredRules, new BattleAttack(BattleActionType.None), new(position.X * 16, position.Y * 16, position.Z * 24),
                new(position.X * 16, position.Y * 16, position.Z * 24), tile, false, 0, 0));
        }
        return RunStates();
    }

    /// <summary>
    /// The self-destruct of a unit that took the hit (<c>BattleUnit::damage</c>): <c>statePushNext</c> of an <c>ExplosionBState</c> of the unit's
    /// special weapon at the voxel of its position's corner, run by <see cref="RunStates"/>.
    /// </summary>
    private void PushSelfDestruct(BattleUnit unit, BattleItem item)
    {
        // (A unit carried as a body has the invalid position (-1, -1, -1).)
        var position = RestingPosition(unit) ?? new BattlePosition(-1, -1, -1);
        var voxel = new BattlePosition(position.X * 16, position.Y * 16, position.Z * 24);
        PushNext(new ExplosionState(this, RequiredRules, new BattleAttack(BattleActionType.SelfDestruct, unit, item, item), voxel, voxel, null, false, 0, 0));
    }

    // ---- spawns ----

    /// <summary>The unit or item an explosion's item spawns: the attacker, else the item's owner, else its previous owner.</summary>
    private BattleUnit? SpawnOwner(in BattleAttack attack) => attack.Attacker ?? OwnerOf(attack.DamageItem!);

    /// <summary>The chance of a spawn: the item's, or the ammunition override of the weapon's action configuration.</summary>
    private int SpawnChance(in BattleAttack attack, int chance, Func<RuntimeItemAction, int> overrideOf)
    {
        if (attack.Weapon is { } weapon && ActionIndex(attack.Type) is { } index)
        {
            var conf = RequiredRules.Battle.Equipment.Items[weapon.ItemType].Combat;
            var replaced = overrideOf(index switch { 0 => conf.Aimed, 1 => conf.Auto, 2 => conf.Snap, _ => conf.Melee });
            if (replaced != -1) return replaced;
        }
        return chance;
    }

    /// <summary>
    /// BattlescapeGame::spawnNewUnit(attack, position): the item's spawn unit, with its chance (the weapon's ammunition override first), of the
    /// owner's faction or the item's, placed on the tile or beside it, facing as the attacker does (or a random way), armed and equipped, with gravity
    /// and a full recalculation of sight around it.
    /// </summary>
    internal bool SpawnNewUnit(in BattleAttack attack, BattlePosition position)
    {
        if (attack.DamageItem is not { } damageItem) return false;
        var rule = RequiredRules.Battle.Equipment.Items[damageItem.ItemType];
        if (rule.SpawnUnit.Length == 0) return false;
        var chance = SpawnChance(attack, rule.SpawnUnitChance, static action => action.AmmoSpawnUnitChanceOverride);
        if (!RandomChance.Percent(Random, chance)) return false;

        var owner = SpawnOwner(attack);
        var faction = rule.SpawnUnitFaction == -1 && owner is not null ? owner.Faction : rule.SpawnUnitFaction switch
        {
            0 => BattleFaction.Player,
            1 => BattleFaction.Hostile,
            2 => BattleFaction.Neutral,
            _ => BattleFaction.Hostile,
        };
        var (unitRule, armor) = UnitRule(rule.SpawnUnit);
        // createTempUnit: the next ID after the last unit's; a player unit made this way is a summoned one.
        var unit = TempUnit((_units.Count == 0 ? -1 : _units[^1].Id) + 1, faction,
            faction == BattleFaction.Player ? BattleController.Human(0) : BattleController.Ai, rule.SpawnUnit, unitRule, armor,
            CampaignLink?.Difficulty ?? 0, summoned: faction == BattleFaction.Player);
        var checkDirection = attack.Attacker is { } shooter ? (shooter.Direction + 4) % 8 : 0;
        if (!Contains(position) || BattlePlacement.ValidForUnit(Map, unit, position, surrounding: true, startDirection: checkDirection) is not { } placed)
            return false;
        var direction = attack.Attacker?.Direction ?? Random.NextInclusive(0, 7);
        var equipment = new UnitEquipment(this, RequiredRules, Random);
        // A tank is armed with its weapon (the item named like the unit) and that weapon's clip.
        if (RequiredRules.Battle.Equipment.Items.TryGetValue(unit.UnitType, out var weaponRule) && weaponRule.Fixed)
        {
            equipment.CreateItemForUnit(unit.UnitType, unit, fixedWeapon: true);
            if (equipment.VehicleClipAmmo(unit.UnitType) is { } clip && equipment.CreateItemForUnit(clip, unit) is { } ammo)
                ammo.AmmoQuantity = equipment.VehicleClipSize(unit.UnitType);
            unit.TurretType = weaponRule.TurretType;
        }
        var itemLevel = equipment.DrawItemLevel();
        unit.Position = placed;
        unit.SetDirection(direction);
        unit.TimeUnits = 0;
        unit.PreviousOwnerId = owner?.Id;
        AddUnit(unit);
        SetSpotted(unit, BattleFaction.Player, faction == BattleFaction.Player);
        equipment.InitUnit(unit, unitRule, "", itemLevel);
        ApplyGravity(unit.Position);
        // This happens fairly rarely, so do a full recalculation of the units in range.
        Sight().CalculateFov(unit.Position!.Value);
        MarkChanged();
        return true;
    }

    /// <summary>
    /// BattlescapeGame::spawnNewItem(attack, position): the item's spawn item, with its chance (the weapon's ammunition override first), lies on the
    /// tile, remembers its previous owner, falls to a floor, and a glowing one lights its surroundings.
    /// </summary>
    internal bool SpawnNewItem(in BattleAttack attack, BattlePosition position)
    {
        if (attack.DamageItem is not { } damageItem) return false;
        var rule = RequiredRules.Battle.Equipment.Items[damageItem.ItemType];
        if (rule.SpawnItem.Length == 0) return false;
        var chance = SpawnChance(attack, rule.SpawnItemChance, static action => action.AmmoSpawnItemChanceOverride);
        if (!RandomChance.Percent(Random, chance)) return false;

        var owner = attack.Attacker ?? OwnerOf(damageItem);
        if (!Contains(position)) return false;
        var created = new UnitEquipment(this, RequiredRules, Random).CreateItemForTile(rule.SpawnItem, position);
        created.PreviousOwnerUnitId = owner?.Id;
        var landed = ApplyGravity(created.Tile);
        if (GlowRange(created) is { } range && (created.Tile ?? landed) is { } at)
        {
            CalculateLighting(BattleLightLayer.Items, at);
            Sight().CalculateFov(at, range, updateTiles: false);
        }
        MarkChanged();
        return true;
    }

    // ---- TileEngine::explode ----

    /// <summary>
    /// TileEngine::explode: rays leave the center over a sphere (every 5 degrees of elevation and 3 of azimuth), each losing the type's radius
    /// reduction per tile and what the terrain between tiles blocks, up to the radius; a tile a ray reaches takes a damage roll once (the units on
    /// it, their things, the items on the ground, then fire or smoke) and remembers the highest tile damage any ray brought; high explosive and
    /// the types that damage tiles then detonate the tiles reached, in tile order (the reference's map is ordered by tile pointer). A hit at the
    /// center is direct, one within a square of two tiles on the same level or from above is ground zero, the rest are hit from the side.
    /// </summary>
    internal void Explode(in BattleAttack attack, BattlePosition center, int power, RuntimeItemDamageRule type, int maxRadius,
        bool rangeAttack)
    {
        ArgumentNullException.ThrowIfNull(type);
        var centerTile = BattleGeometry.TileOfVoxel(center);
        if (!Contains(centerTile)) return;
        var hitSide = 0;
        var diagonalWall = 0;
        var tilesAffected = new Dictionary<int, int>();
        var toRemove = new List<BattleItem>();

        if (type.FireBlastCalc) power /= 2;

        var origin = Map.TileIndex(centerTile);
        if (BattleBlockage.IsBigWall(Map, origin))
        {
            // pre-calculations for bigwall deflection
            diagonalWall = Map.MapData(origin, BattleTilePart.ObjectPart)!.BigWall;
            if (diagonalWall == BigWallNwse) hitSide = center.X % 16 - center.Y % 16 > 0 ? 1 : -1;
            if (diagonalWall == BigWallNesw) hitSide = center.X % 16 + center.Y % 16 - 15 > 0 ? 1 : -1;
        }
        var blockType = type.ResistType switch
        {
            RuntimeItemDamageRule.DamageNone => BattleBlockType.None,
            RuntimeItemDamageRule.DamageSmoke => BattleBlockType.Smoke,
            _ => BattleBlockType.Damage,
        };

        for (var fi = -90; fi <= 90; fi += 5)
        {
            // raytrace every 3 degrees makes sure we cover all tiles in a circle.
            for (var te = 0; te <= 360; te += 3)
            {
                var cosTe = Math.Cos(te * Math.PI / 180.0);
                var sinTe = Math.Sin(te * Math.PI / 180.0);
                var sinFi = Math.Sin(fi * Math.PI / 180.0);
                var cosFi = Math.Cos(fi * Math.PI / 180.0);

                origin = Map.TileIndex(centerTile);
                var dest = origin;
                double l = 0;
                var rayPower = power;
                while (rayPower > 0 && l <= maxRadius)
                {
                    var isNew = tilesAffected.TryAdd(dest, 0);
                    var tileDamage = type.TileFinalDamage(rayPower, Random);
                    if (tileDamage > tilesAffected[dest]) tilesAffected[dest] = tileDamage;
                    if (isNew) ExplodeTile(attack, centerTile, dest, rayPower, type, rangeAttack, toRemove);

                    l += 1.0;
                    var tileX = (int)Math.Floor(centerTile.X + 0.5 + l * sinTe * cosFi);
                    var tileY = (int)Math.Floor(centerTile.Y + 0.5 + l * cosTe * cosFi);
                    var tileZ = (int)Math.Floor(centerTile.Z + 0.5 + l * sinFi);

                    origin = dest;
                    dest = Map.TileAt(tileX, tileY, tileZ);
                    if (dest < 0) break; // out of map!

                    // blockage by terrain is deducted from the explosion power
                    rayPower = (int)(rayPower - type.RadiusReduction); // explosive damage decreases by 10 per tile
                    var originPosition = Map.PositionOf(origin);
                    if (originPosition.Z != tileZ) rayPower -= FlatExplosionDecrement; // 3d explosion factor

                    if (type.FireBlastCalc)
                    {
                        var destPosition = Map.PositionOf(dest);
                        var direction = BattleGeometry.VectorToDirection(originPosition.X - destPosition.X, originPosition.Y - destPosition.Y);
                        // diagonal movement costs an extra 50% for fire.
                        if (direction != -1 && direction % 2 != 0) rayPower = (int)(rayPower - 0.5f * type.RadiusReduction);
                    }
                    if (l > 0.5)
                    {
                        if (l > 1.5)
                        {
                            rayPower -= BattleBlockage.VerticalBlockage(Map, origin, dest, blockType, false) * 2;
                            rayPower -= BattleBlockage.HorizontalBlockage(Map, origin, dest, blockType, false) * 2;
                        }
                        else
                        {
                            // tricky bigwall deflection
                            var skipObject = diagonalWall == 0;
                            if (diagonalWall == BigWallNesw)
                            {
                                if (hitSide < 0 && te >= 135 && te < 315) skipObject = true;
                                if (hitSide > 0 && (te < 135 || te > 315)) skipObject = true;
                            }
                            if (diagonalWall == BigWallNwse)
                            {
                                if (hitSide > 0 && te >= 45 && te < 225) skipObject = true;
                                if (hitSide < 0 && (te < 45 || te > 225)) skipObject = true;
                            }
                            rayPower -= BattleBlockage.VerticalBlockage(Map, origin, dest, blockType, skipObject) * 2;
                            rayPower -= BattleBlockage.HorizontalBlockage(Map, origin, dest, blockType, skipObject) * 2;
                        }
                    }
                }
            }
        }

        // now detonate the tiles affected by explosion
        if (type.Conversions.Tile > 0.0f)
        {
            foreach (var (tile, explosive) in tilesAffected.OrderBy(static pair => pair.Key))
            {
                if (Detonate(tile, explosive)) AddDestroyedObjective();
                var position = Map.PositionOf(tile);
                ApplyGravity(position);
                if (Map.TileAt(position.X, position.Y, position.Z + 1) is var above and >= 0) ApplyGravity(Map.PositionOf(above));
            }
        }
        CalculateLighting(BattleLightLayer.Ambient, centerTile, maxRadius + 1, true);
        var sight = Sight();
        sight.CalculateFov(centerTile, maxRadius + 1, true, true);
        if (attack.Attacker is { Position: { } attackerPosition } &&
            (int)Math.Ceiling(Math.Sqrt(BattleGeometry.HorizontalDistanceSquared(centerTile, attackerPosition))) > maxRadius + 1)
        {
            // unit is away from blast but its visibility can be affected by scripts.
            sight.CalculateFov(centerTile, 1, false);
        }
        MarkChanged();
    }

    /// <summary>The first time a ray reaches a tile: the damage roll hits its unit (and what it carries), the items on the ground, and the tile takes fire or smoke.</summary>
    private void ExplodeTile(in BattleAttack attack, BattlePosition centerTile, int dest, int rayPower, RuntimeItemDamageRule type,
        bool rangeAttack, List<BattleItem> toRemove)
    {
        var damage = type.RandomDamage(rayPower, RequiredRules.Campaign.Interception, Random);
        var position = Map.PositionOf(dest);
        var unit = OverlappingUnit(dest);

        toRemove.Clear();
        if (unit is not null)
        {
            if (position == centerTile)
            {
                // direct hit, similar to ground zero but AI will remember attacker, done for compatibility
                HitUnit(attack, unit, new BattlePosition(0, 0, 0), damage, type, rangeAttack);
            }
            else if (HorizontalDistance2dSquared(position, centerTile) < 4 && position.Z == centerTile.Z || position.Z > centerTile.Z)
            {
                // ground zero effect is in effect, or unit is above explosion
                HitUnit(attack, unit, new BattlePosition(0, 0, -1), damage, type, rangeAttack);
            }
            else
            {
                // directional damage relative to explosion position.
                // units above the explosion will be hit in the legs, units lateral to or below will be hit in the torso
                HitUnit(attack, unit, new BattlePosition(centerTile.X - position.X, centerTile.Y - position.Y, centerTile.Z + 5 - position.Z), damage,
                    type, rangeAttack);
            }

            // Affect all items and units in inventory
            var itemDamage = OverKillDamage(unit);
            if (itemDamage > 0)
            {
                foreach (var item in InventoryOf(unit).ToArray())
                {
                    var (hit, _) = HitUnit(attack, item.CorpseUnitId is { } body ? UnitById(body) : null, new BattlePosition(0, 0, 0), itemDamage, type,
                        rangeAttack);
                    if (!hit && type.ItemFinalDamage(itemDamage, Random) > ItemRuleOf(item.ItemType).ArmorValue) toRemove.Add(item);
                }
            }
        }
        // Affect all items and units on ground
        foreach (var item in ItemsOnTile(position).ToArray())
        {
            var (hit, _) = HitUnit(attack, item.CorpseUnitId is { } body ? UnitById(body) : null, new BattlePosition(0, 0, 0), damage, type, true);
            if (!hit && type.ItemFinalDamage(damage, Random) > ItemRuleOf(item.ItemType).ArmorValue) toRemove.Add(item);
        }
        foreach (var item in toRemove) RemoveItem(item);

        HitTile(dest, damage, type);
    }

    private static int HorizontalDistance2dSquared(BattlePosition one, BattlePosition other) => BattleGeometry.HorizontalDistanceSquared(one, other);

    // ---- TileEngine::detonate ----

    private static readonly BattleTilePart[] DetonateParts =
    [
        BattleTilePart.Floor, BattleTilePart.WestWall, BattleTilePart.NorthWall, BattleTilePart.Floor, BattleTilePart.WestWall, BattleTilePart.NorthWall,
        BattleTilePart.ObjectPart, BattleTilePart.ObjectPart, BattleTilePart.ObjectPart,
    ];

    /// <summary>
    /// TileEngine::detonate: the explosive power is applied to the nine parts around a tile (the ceiling, the walls the east and south neighbours
    /// own, the tile's floor, walls and object, and the big walls of the north and west neighbours), each losing its armor from the power until
    /// the power no longer reaches the next part; a part whose destroyed form is another part carries on through it. What the power has left sets
    /// a flammable tile alight, and a part that was destroyed makes smoke. A base module lost counts against its block (a block the saved module map
    /// lacks is skipped; ADR 0041). Returns whether the mission's objective was destroyed.
    /// </summary>
    internal bool Detonate(int tile, int explosive)
    {
        if (explosive == 0) return false; // no damage applied for this tile
        var objective = false;
        var pos = Map.PositionOf(tile);
        int[] tiles =
        [
            Map.TileAt(pos.X, pos.Y, pos.Z + 1), // ceiling
            Map.TileAt(pos.X + 1, pos.Y, pos.Z), // east wall
            Map.TileAt(pos.X, pos.Y + 1, pos.Z), // south wall
            tile, tile, tile, tile,
            Map.TileAt(pos.X, pos.Y - 1, pos.Z), // north bigwall
            Map.TileAt(pos.X - 1, pos.Y, pos.Z), // west bigwall
        ];

        bool bigwallDestroyed = true, skipNorthWest = false;
        for (var i = 8; i >= 0; --i)
        {
            if (tiles[i] < 0 || Map.MapData(tiles[i], DetonateParts[i]) is not { } first) continue; // skip out of map and emptiness
            var bigwall = first.BigWall;
            if (i > 6 && !(bigwall == 1 || bigwall == 8 || i == 8 && bigwall == 6 || i == 7 && bigwall == 7)) continue;
            if (bigwall != 0) skipNorthWest = true;
            if (!bigwallDestroyed && i < 6) continue; // when ground shouldn't be destroyed
            if (skipNorthWest && (i == 2 || i == 1)) continue;
            var remainingPower = explosive;
            var destroyed = false;
            var volume = 0;
            var currentPart = DetonateParts[i];
            var fireProof = first.Flammable;
            var fuel = first.Fuel + 1;
            // get the volume of the object by checking it's loftemps objects.
            for (var layer = 0; layer < BattleMapData.LoftLayers; layer++)
                if (first.LoftId(layer) != 0) ++volume;
            if (i == 6 && (bigwall == 2 || bigwall == 3) && first.Armor > remainingPower) bigwallDestroyed = false; // not enough to destroy

            // iterate through tile armor and destroy if can
            while (Map.MapData(tiles[i], currentPart) is { } data && data.Armor <= remainingPower && data.Armor != 255)
            {
                if (i == 6 && (bigwall == 2 || bigwall == 3)) bigwallDestroyed = true; // diagonals for the current tile
                if (i == 6 && bigwall is 6 or 7 or 8) skipNorthWest = false; // n/w/nw
                remainingPower -= data.Armor;
                destroyed = true;
                if (MissionType == BaseDefenseMissionType && data.IsBaseModule) CountModuleLost(pos);
                // this trick is to follow transformed object parts (object can become a ground)
                var next = currentPart;
                if (data.DieMcd != 0 && DestroyedForm(tiles[i], currentPart, data) is { } dead) next = dead.ObjectType;
                if (DestroyPart(tiles[i], currentPart, ObjectiveType)) objective = true;
                currentPart = next;
                if (Map.MapData(tiles[i], currentPart) is { } taken)
                {
                    // take new values
                    fireProof = taken.Flammable;
                    fuel = taken.Fuel + 1;
                    if (taken.Armor == 0) break;
                }
            }

            // set tile on fire
            if (fireProof < remainingPower &&
                (Map.MapData(tiles[i], BattleTilePart.Floor) is not null || Map.MapData(tiles[i], BattleTilePart.ObjectPart) is not null))
            {
                SetTileAlight(tiles[i], fuel);
                SetTileSmoke(tiles[i], Math.Clamp(15 - fireProof / 10, 1, 12));
            }
            // add some smoke if tile was destroyed and not set on fire
            if (destroyed)
            {
                if (Map.Fire(tiles[i]) != 0 && Map.MapData(tiles[i], BattleTilePart.Floor) is null && Map.MapData(tiles[i], BattleTilePart.ObjectPart) is null)
                    SetTileFire(tiles[i], 0); // if the object set the floor on fire, and the floor was subsequently destroyed, the fire needs to go out
                if (Map.Fire(tiles[i]) == 0)
                {
                    var smoke = Random.NextInclusive(1, volume / 2 + 3) + volume / 2;
                    if (smoke > Map.Smoke(tiles[i])) SetTileSmoke(tiles[i], Math.Clamp(smoke, 0, 15));
                }
            }
        }
        return objective;
    }

    /// <summary>
    /// The base defense's count of a block's module parts that are left, one less. The reference indexes the saved module map of the block with no
    /// check; a block the map lacks (a save kept as written) is where it reads past a vector, which the port skips (ADR 0041).
    /// </summary>
    internal void CountModuleLost(BattlePosition tile)
    {
        if (tile.X / 10 >= BaseModules.Length || tile.Y / 10 >= BaseModules[tile.X / 10].Length) return;
        var counted = BaseModules[tile.X / 10][tile.Y / 10];
        BaseModules[tile.X / 10][tile.Y / 10] = counted with { Remaining = counted.Remaining - 1 };
    }

    // ---- BattlescapeGenerator::explodePowerSources and explodeOtherJunk ----

    private const int UfoPowerSourceSpecialType = 2;

    /// <summary>
    /// BattlescapeGenerator::explodePowerSources: each power source of a crashed UFO explodes with a chance of 75 percent (a high explosive of
    /// 180 to 250 and radius 10), and then the terrain explosions that follow, each at the power its tile holds. The casualties are the battlescape's
    /// opening check (<see cref="OpenBattlescape"/>).
    /// </summary>
    internal void ExplodePowerSources()
    {
        Generating = true;
        try
        {
            for (var tile = 0; tile < Map.TileCount; tile++)
            {
                if (Map.MapData(tile, BattleTilePart.ObjectPart)?.SpecialType != UfoPowerSourceSpecialType || !RandomChance.Percent(Random, 75)) continue;
                var position = Map.PositionOf(tile);
                var voxel = new BattlePosition(position.X * 16, position.Y * 16, position.Z * 24 + 12);
                var power = 180 + Random.NextInclusive(0, 70);
                Events.Add(new BattleExploded(voxel, power, 10, false));
                Explode(default, voxel, power, HighExplosiveType, 10, true);
            }
            ExplodeLeftoverTerrain(tileDamageTypeFromExplosive: false);
        }
        finally
        {
            Generating = false;
        }
    }

    /// <summary>
    /// True while the generator's explosions run. The reference has no <c>BattlescapeState</c> then (<c>SavedBattleGame::getBattleState()</c> is null until
    /// the briefing opens the battlescape), and <c>BattleUnit::damage</c> skips its special effects (morale loss, fire, conversion, self-destruct, the
    /// attacker's spotting and the special damage scripts) without one.
    /// </summary>
    internal bool Generating { get; private set; }

    /// <summary>
    /// BattlescapeGenerator::explodeOtherJunk: the grenades on the map primed for the start (fuse timer -2) that spawn nothing and belong to no unit are
    /// removed, then each goes off on its tile with its power and damage type; then the terrain explosions that follow (each by the damage type
    /// its tile's explosive type names).
    /// </summary>
    internal void ExplodeOtherJunk()
    {
        Generating = true;
        try
        {
            ExplodeJunk();
        }
        finally
        {
            Generating = false;
        }
    }

    private void ExplodeJunk()
    {
        var goBoom = new List<(BattlePosition Tile, BattleItem Item)>();
        foreach (var item in _items)
        {
            if (item.Tile is not { } tile || item.XcomProperty || item.IsSpecialWeapon) continue;
            if (RequiredRules.Battle.Equipment.Items[item.ItemType].Spawns) continue;
            // fuseTimer == 0 cannot be used, because it is already used for "explode after the first player turn"; -1 is an unprimed grenade
            if (ItemRuleOf(item.ItemType).BattleType == GrenadeBattleType && item.FuseTimer == -2) goBoom.Add((tile, item));
        }
        foreach (var (_, item) in goBoom) RemoveItem(item);
        foreach (var (tile, item) in goBoom)
        {
            var rule = ItemRuleOf(item.ItemType);
            var voxel = new BattlePosition(tile.X * 16 + 8, tile.Y * 16 + 8, tile.Z * 24 - Map.TerrainLevel(Map.TileIndex(tile)));
            var radius = ExplosionRadius(item, default);
            Events.Add(new BattleExploded(voxel, rule.Power, radius, false));
            Explode(default, voxel, rule.Power, rule.Damage, radius, true);
        }
        ExplodeLeftoverTerrain(tileDamageTypeFromExplosive: true);
    }

    /// <summary>The loop at the end of both generator routines: while a tile holds an explosive, it goes off at that power with the radius a tenth of it.</summary>
    private void ExplodeLeftoverTerrain(bool tileDamageTypeFromExplosive)
    {
        while (CheckForTerrainExplosions() is { } tile)
        {
            var damageType = tileDamageTypeFromExplosive
                ? Map.ExplosiveType(tile) switch
                {
                    0 => RuntimeItemDamageRule.DamageHighExplosive,
                    5 => RuntimeItemDamageRule.DamageIncendiary,
                    6 => StunDamage,
                    _ => RuntimeItemDamageRule.DamageSmoke,
                }
                : RuntimeItemDamageRule.DamageHighExplosive;
            var power = Map.Explosive(tile);
            Map.SetExplosive(tile, 0, 0, force: true);
            var position = Map.PositionOf(tile);
            var voxel = new BattlePosition(position.X * 16 + 8, position.Y * 16 + 8, position.Z * 24);
            Events.Add(new BattleExploded(voxel, power, power / 10, true));
            Explode(default, voxel, power, RuntimeItemDamageRule.Predefined(damageType), power / 10, true);
        }
        // A unit that blew itself up in these explosions is the battlescape's to finish: the reference has no battlescape yet and would fail; the port runs it.
        RunStates();
    }
}

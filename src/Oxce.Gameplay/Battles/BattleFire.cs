using Oxce.Core.Random;
using Oxce.Gameplay.Battles.Terrain;
using Oxce.Gameplay.Campaigns;
using Oxce.Mods.Rulesets.Runtime;

namespace Oxce.Gameplay.Battles;

/// <summary>The reasons a hit refuses (Phase 7 branch 5 S5 retired the terrain explosion and module map ones).</summary>
internal static class BattleFireReasons
{
    internal static readonly CampaignReason SelfDestructItemUnsupported = new("battle.self-destruct-item-unsupported",
        "A unit would blow itself up as it dies with a melee weapon as its self-destruct item; the melee attack that explosion runs arrives with Phase 7 branch 5 S6.");
}

/// <summary>
/// The base damage types of <c>Mod::Mod</c> the battle uses on its own (a walk burns floors with <c>DT_IN</c>), and the tile damage of
/// <c>TileEngine::hit</c>: <c>getTileFinalDamage(getRandomDamageForTile(power, damage))</c>.
/// </summary>
internal static class BattleDamageTypes
{
    /// <summary>The base <c>DT_IN</c>: <c>FireThreshold</c> 0, <c>ToTile</c> 0, <c>TileDamageMethod</c> 2, damage rolled in the fire damage range.</summary>
    public static RuntimeItemDamageRule Fire { get; } = RuntimeItemDamageRule.Predefined(RuntimeItemDamageRule.DamageIncendiary);

    /// <summary>getTileFinalDamage(getRandomDamageForTile(power, damage)).</summary>
    public static int TileDamage(RuntimeItemDamageRule type, IRandomSource random, int power, int damage) =>
        type.TileFinalDamage(type.RandomDamageForTile(power, damage, random), random);
}

/// <summary>
/// Fire, smoke and terrain destruction (Phase 7 branch 4 S9): <c>Tile::ignite</c>, <c>addSmoke</c>, <c>setFire</c>, <c>setSmoke</c>, <c>damage</c> and
/// <c>destroy</c>, the tile part of <c>TileEngine::hit</c> and <c>hitTile</c> (the code branch 5's explosions call), the burning of floors by a unit,
/// and <c>SavedBattleGame::prepareNewTurn</c>'s spread of fire and smoke as a routine for branch 6's end of turn. The hit's
/// <c>hitUnit</c> is <c>BattleDamage.cs</c>'s (S3). The environment's damage to units is branch 6's. A destroyed part that explodes only leaves its explosive on the
/// tile (<c>Tile::setExplosive</c>); the explosion is <see cref="RunTerrainExplosions"/>'s (S5), which an <c>ExplosionBState</c> and the end of a turn run.
/// The animation offsets the reference draws for the fire and smoke sprites are presentation and draw nothing here.
/// </summary>
internal sealed partial class BattleState
{
    private const int NoFlammability = 255;

    /// <summary>Tile::getFlammability: the lowest flammability of the tile's parts (255 when it has none).</summary>
    internal int Flammability(int tile)
    {
        var flammability = NoFlammability;
        for (var part = BattleTilePart.Floor; part <= BattleTilePart.ObjectPart; part++)
            if (Map.MapData(tile, part) is { } data && data.Flammable < flammability) flammability = data.Flammable;
        return flammability;
    }

    /// <summary>Tile::getFuel: the highest fuel of the tile's parts.</summary>
    internal int Fuel(int tile)
    {
        var fuel = 0;
        for (var part = BattleTilePart.Floor; part <= BattleTilePart.ObjectPart; part++)
            if (Map.MapData(tile, part) is { } data && data.Fuel > fuel) fuel = data.Fuel;
        return fuel;
    }

    /// <summary>Tile::isVoid: no parts, no smoke and no items.</summary>
    internal bool TileIsVoid(int tile) => Map.HasNoPartsOrSmoke(tile) && !ItemsOnTile(Map.PositionOf(tile)).Any();

    /// <summary>Tile::setFire: the turns the tile burns, kept in 0 to 255.</summary>
    internal void SetTileFire(int tile, int fire) => Map.SetFire(tile, Math.Clamp(fire, 0, 255));

    /// <summary>
    /// Tile::setFire for a hit or a blast that sets the tile alight (<c>TileEngine::hitTile</c>, the fire part of <c>explode</c>): a tile that was not
    /// burning and now is raises <see cref="TileIgnited"/>, as <see cref="Ignite"/> does.
    /// </summary>
    internal void SetTileAlight(int tile, int fire)
    {
        var burning = Map.Fire(tile) != 0;
        SetTileFire(tile, fire);
        if (!burning && Map.Fire(tile) != 0) Events.Add(new TileIgnited(Map.PositionOf(tile), Map.Fire(tile)));
    }

    /// <summary>Tile::setSmoke: the turns the tile smokes, kept in 0 to 255.</summary>
    internal void SetTileSmoke(int tile, int smoke) => Map.SetSmoke(tile, Math.Clamp(smoke, 0, 255));

    /// <summary>
    /// Tile::ignite: a tile that can burn catches fire with the chance <c>power - flammability / 10 + 15</c> percent (the roll is drawn first) when it
    /// has fuel, burning fuel + 1 turns (a <c>Uint8</c>) with a smoke that tells the fire's strength.
    /// </summary>
    internal void Ignite(int tile, int power)
    {
        var flammability = Flammability(tile);
        if (flammability == NoFlammability) return;
        power = Math.Max(power - flammability / 10 + 15, 0);
        if (!(RandomChance.Percent(Random, power) && Fuel(tile) != 0)) return;
        if (Map.Fire(tile) != 0) return;
        Map.SetSmoke(tile, 15 - Math.Clamp(flammability / 10, 1, 12));
        Map.SetOverlaps(tile, 1);
        Map.SetFire(tile, unchecked((byte)(Fuel(tile) + 1)));
        Events.Add(new TileIgnited(Map.PositionOf(tile), Map.Fire(tile)));
    }

    /// <summary>
    /// Tile::addSmoke: a tile that does not burn takes smoke (kept in 1 to 15 while nothing else added smoke this turn, otherwise summed as a
    /// <c>Uint8</c> to be averaged by <see cref="AverageSmoke"/>) and counts the overlap.
    /// </summary>
    internal void AddSmoke(int tile, int smoke)
    {
        if (Map.Fire(tile) != 0) return;
        Map.SetSmoke(tile, Map.Overlaps(tile) == 0 ? Math.Clamp(Map.Smoke(tile) + smoke, 1, 15) : unchecked((byte)(Map.Smoke(tile) + smoke)));
        Map.AddOverlap(tile);
    }

    /// <summary>
    /// Tile::destroy: the part is removed and replaced by its destroyed form (the object's <c>dieMCD</c>, of the same data set and of the part
    /// the destroyed object belongs to), a high explosive part marks the tile with its explosive, and a floor on the ground level that is gone
    /// becomes scorched earth. Returns whether the part was the mission's objective. A grav lift is never destroyed. A <c>dieMCD</c> past the
    /// data set is not applied (the reference throws; ADR 0040), and so is a missing scorched earth object.
    /// </summary>
    internal bool DestroyPart(int tile, BattleTilePart part, int objectiveType)
    {
        var objective = false;
        var set = Map.Part(tile, part).Set;
        if (Map.MapData(tile, part) is { } original)
        {
            if (original.IsGravLift) return false;
            objective = original.SpecialType == objectiveType;
            var dead = DestroyedForm(tile, part, original);
            Map.SetPart(tile, part, -1, -1);
            if (dead is not null) Map.SetPart(tile, dead.ObjectType, original.DieMcd, set);
            if (original.Explosive != 0) Map.SetExplosive(tile, original.Explosive, original.ExplosiveType);
        }
        if (part == BattleTilePart.Floor && Map.PositionOf(tile).Z == 0 && Map.MapData(tile, BattleTilePart.Floor) is null &&
            Map.DataSets.Count != 0 && Map.DataSets[0].Objects.Count > 1)
            Map.SetPart(tile, BattleTilePart.Floor, 1, 0);
        return objective;
    }

    /// <summary>The part's destroyed form (<c>dieMCD</c>, of the part's data set), or null for none or one past the data set's end (ADR 0040).</summary>
    private BattleMapData? DestroyedForm(int tile, BattleTilePart part, BattleMapData data)
    {
        if (data.DieMcd == 0) return null;
        var objects = Map.DataSets[Map.Part(tile, part).Set].Objects;
        return (uint)data.DieMcd < (uint)objects.Count ? objects[data.DieMcd] : null;
    }

    /// <summary>Tile::damage: a part whose armor the power reaches is destroyed. Returns whether that destroyed the objective.</summary>
    internal bool DamagePart(int tile, BattleTilePart part, int power, int objectiveType) =>
        power >= (Map.MapData(tile, part)?.Armor ?? 0) && DestroyPart(tile, part, objectiveType);

    /// <summary>
    /// SavedBattleGame::addDestroyedObjective: counts one more objective destroyed. When that is the last one the reference tells the player (a
    /// popup) or, with the option <c>battleAutoEnd</c> (off by default), ends a destroy mission: the popup is presentation and the option is at its
    /// default, so only the count changes.
    /// </summary>
    internal void AddDestroyedObjective()
    {
        if (ObjectiveCount > 0 && ObjectivesDestroyed == ObjectiveCount) return;
        ObjectivesDestroyed++;
    }

    /// <summary>
    /// TileEngine::hitTile: whether the tile caught smoke (1) or fire (2) from a hit of this damage, or neither (0). Smoke from a damage at the
    /// type's smoke threshold replaces any fire; fire needs a tile that is not void with a floor or an object and no fire yet.
    /// </summary>
    internal int HitTile(RuntimeRuleCatalog rules, int tile, int damage, RuntimeItemDamageRule type)
    {
        if (damage >= type.SmokeThreshold)
        {
            // Smoke from explosions always stays 6 to 14 turns.
            if (Map.Smoke(tile) < rules.Campaign.TooMuchSmokeThreshold && Map.TerrainLevel(tile) > -24)
            {
                SetTileFire(tile, 0);
                SetTileSmoke(tile, damage >= type.SmokeThreshold * 2
                    ? Random.NextInclusive(7, 15)
                    : Random.NextInclusive(7, 15) * (damage - type.SmokeThreshold) / type.SmokeThreshold);
                return 1;
            }
        }
        else if (damage >= type.FireThreshold)
        {
            if (!TileIsVoid(tile) && Map.Fire(tile) == 0 &&
                (Map.MapData(tile, BattleTilePart.Floor) is not null || Map.MapData(tile, BattleTilePart.ObjectPart) is not null))
            {
                SetTileAlight(tile, damage >= type.FireThreshold * 2
                    ? Fuel(tile) + 1
                    : Fuel(tile) * (damage - type.FireThreshold) / type.FireThreshold + 1);
                SetTileSmoke(tile, Math.Max(1, Math.Min(15 - Flammability(tile) / 10, 12)));
                return 2;
            }
        }
        return 0;
    }

    /// <summary>
    /// <c>TileEngine::hit</c> at a voxel, for a hit of terrain with no weapon: the attacker only stays out of the voxel check
    /// (<see cref="Hit"/> with an attack of no action, weapon or ammunition).
    /// </summary>
    internal CampaignReason? HitTerrain(RuntimeRuleCatalog rules, BattleUnit? attacker, BattlePosition voxel, int power, RuntimeItemDamageRule type) =>
        Hit(rules, new BattleAttack(BattleActionType.None, attacker), voxel, power, type, false);

    /// <summary>
    /// <c>TileEngine::hit</c> at a voxel. What the voxel holds decides: a floor or an object first passes the hit to a body lying on the tile (an
    /// unconscious unit's item) that is still alive, else, like a wall, takes <see cref="HitTile"/>'s fire or smoke and the damage to the part (the
    /// base defense's module count and the objective count follow); a unit in the voxel takes the hit (<see cref="HitUnit"/>). Then, when the terrain
    /// changed or fire or smoke appeared, gravity on the tile, the light of the layer it changes and the field of view around it are updated. A destroyed
    /// part that explodes leaves its explosive on the tile for <see cref="RunTerrainExplosions"/>; a unit that blows itself up as it dies pushes its
    /// explosion on the list of states (<see cref="RunStates"/>). The result is the refusal of a self-destruct item that is a melee weapon or a psi amplifier
    /// (S6 and branch 6), after the unit has taken the hit (commands run on a trial copy first). Terrain melee (the melee part of the voxel given) is S6's.
    /// </summary>
    internal CampaignReason? Hit(RuntimeRuleCatalog rules, in BattleAttack attack, BattlePosition voxel, int power, RuntimeItemDamageRule type,
        bool rangeAttack)
    {
        ArgumentNullException.ThrowIfNull(rules);
        ArgumentNullException.ThrowIfNull(type);
        var tilePosition = BattleGeometry.TileOfVoxel(voxel);
        if (!Contains(tilePosition) || power <= 0) return null;
        var tile = Map.TileIndex(tilePosition);
        var sight = Sight();
        var part = sight.VoxelCheck(voxel, attack.Attacker);
        var damage = type.RandomDamage(power, rules.Campaign.Interception, Random);
        var finalDamage = BattleDamageTypes.TileDamage(type, Random, power, damage);
        var terrainChanged = false;
        var effect = 0;
        CampaignReason? unitRefusal = null;
        if (part is >= BattleVoxelType.Floor and <= BattleVoxelType.Object)
        {
            var tilePart = (BattleTilePart)(int)part;
            var bodyHit = false;
            if (part is BattleVoxelType.Floor or BattleVoxelType.Object)
            {
                // A body that is still alive takes the hit, and the terrain is spared.
                (bodyHit, effect, unitRefusal) = HitUnitsInVoxel(rules, attack, voxel, part, tile, tilePosition, damage, type, rangeAttack);
            }
            if (!bodyHit)
            {
                var data = Map.MapData(tile, tilePart)!;
                terrainChanged = finalDamage >= data.Armor;
                var module = part == BattleVoxelType.Object && MissionType == BaseDefenseMissionType && finalDamage >= data.Armor && data.IsBaseModule;
                effect = HitTile(rules, tile, damage, type);
                if (module) CountModuleLost(tilePosition);
                if (DamagePart(tile, tilePart, finalDamage, ObjectiveType)) AddDestroyedObjective();
            }
        }
        else if (part == BattleVoxelType.Unit)
        {
            (_, effect, unitRefusal) = HitUnitsInVoxel(rules, attack, voxel, part, tile, tilePosition, damage, type, rangeAttack);
        }

        if (terrainChanged || effect != 0)
        {
            ApplyGravity(tilePosition);
            // A destroyed floor changes the sunlight of the column below it; fire and smoke that can block light change the fire layer.
            var layer = part == BattleVoxelType.Floor && tilePosition.Z > 0 ? BattleLightLayer.Ambient : BattleLightLayer.Fire;
            CalculateLighting(rules, layer, tilePosition, 1, true);
            sight.CalculateFov(tilePosition, 1, true, terrainChanged);
        }
        else
        {
            sight.CalculateFov(tilePosition, 1, false);
        }
        return unitRefusal;
    }

    private const string BaseDefenseMissionType = "STR_BASE_DEFENSE";

    /// <summary>BattleUnit's floor-burning abilities: <c>SPECAB_BURNFLOOR</c> and <c>SPECAB_BURN_AND_EXPLODE</c>.</summary>
    private static bool BurnsFloor(RuntimeRuleCatalog rules, BattleUnit unit) => SpecialAbilityOf(rules, unit) >= 2;

    /// <summary>
    /// What <c>UnitWalkBState</c> and <c>UnitFallBState</c> do for a unit that burns floors once it stands still: ignite its tile (power 1) and hit
    /// the floor under it with <c>DT_IN</c> at the strength of the unit. The hole that may burn through the floor makes the unit fall later (gravity
    /// queues it). A part that explodes leaves its explosive for the next explosion or the end of the turn, as the reference does.
    /// </summary>
    private CampaignReason? BurnFloor(RuntimeRuleCatalog rules, BattleUnit unit)
    {
        var position = unit.Position!.Value;
        var tile = Map.TileIndex(position);
        var voxel = new BattlePosition(position.X * 16 + 8, position.Y * 16 + 8, position.Z * 24 - Map.TerrainLevel(tile));
        Ignite(tile, 1);
        return HitTerrain(rules, unit, voxel, unit.Stats.Strength, BattleDamageTypes.Fire);
    }

    /// <summary>Tile::prepareNewTurn without the environment's damage: smoke added this turn is averaged over its overlaps.</summary>
    private void AverageSmoke(int tile)
    {
        var overlaps = Map.Overlaps(tile);
        if (overlaps != 0 && Map.Smoke(tile) != 0 && Map.Fire(tile) == 0)
            Map.SetSmoke(tile, Math.Clamp(Map.Smoke(tile) / overlaps - 1, 0, 15));
    }

    /// <summary>The parts <c>prepareNewTurn</c> destroys when a tile's fire burns out: the object and the floor of a burnable object tile, or a burnable floor.</summary>
    private IEnumerable<BattleTilePart> BurnsOutParts(int tile)
    {
        if (Map.MapData(tile, BattleTilePart.ObjectPart) is { } item)
        {
            if (item.Flammable != 255 && item.Armor != 255)
            {
                yield return BattleTilePart.ObjectPart;
                yield return BattleTilePart.Floor;
            }
        }
        else if (Map.MapData(tile, BattleTilePart.Floor) is { } floor && floor.Flammable != 255 && floor.Armor != 255)
        {
            yield return BattleTilePart.Floor;
        }
    }

    /// <summary>
    /// <c>SavedBattleGame::prepareNewTurn</c>'s fire and smoke (branch 6's end of turn calls it): every burning tile that was not set alight this
    /// turn burns one turn less and spreads to its four neighbours that no wall shields (<see cref="Ignite"/> at the strength of its smoke), or burns
    /// out, clearing its smoke, destroying its flammable object and floor and applying gravity; then every smoking tile that does not burn smokes one
    /// turn less and passes smoke to its neighbours, and a burning tile's smoke rises one level through a floorless tile and spreads sideways at half
    /// strength; and the smoke added this turn is averaged. <paramref name="environment"/> is called for every tile that still smokes after that
    /// (<c>Tile::applyEnvi</c>, the smoke's and fire's damage to the units, which branch 6 supplies); the units' own <c>calculateEnviDamage</c>, the
    /// AI's danger flags and the light and sight updates (<c>BattlescapeGame::endTurn</c>) are the caller's. A burn that destroys an exploding part
    /// leaves its explosive on the tile: the caller runs <see cref="RunTerrainExplosions"/> after the fire has spread, as <c>endTurn</c> does.
    /// Units that lose their floor are queued (<see cref="FallingUnits"/>); the caller resolves the falls.
    /// </summary>
    internal void SpreadFireAndSmoke(Action<int>? environment = null)
    {
        var onFire = new List<int>();
        for (var tile = 0; tile < Map.TileCount; tile++)
            if (Map.Fire(tile) > 0) onFire.Add(tile);
        if (onFire.Count == 0 && !Enumerable.Range(0, Map.TileCount).Any(tile => Map.Smoke(tile) > 0)) return;

        // first: fires spread
        foreach (var tile in onFire)
        {
            // if we haven't added fire here this turn
            if (Map.Overlaps(tile) != 0) continue;
            // reduce the fire timer
            SetTileFire(tile, Map.Fire(tile) - 1);
            var position = Map.PositionOf(tile);
            if (Map.Fire(tile) != 0)
            {
                // propagate in four cardinal directions (0, 2, 4, 6)
                for (var direction = 0; direction <= 6; direction += 2)
                {
                    var next = Map.Neighbour(position, direction);
                    // if there's no wall blocking the path of the flames, attempt to set this tile on fire
                    if (next >= 0 && BattleBlockage.HorizontalBlockage(Map, tile, next, BattleBlockType.Damage) == 0) Ignite(next, Map.Smoke(tile));
                }
                continue;
            }
            // fire has burnt out: burn this tile, and any object in it, if it's not fireproof/indestructible.
            SetTileSmoke(tile, 0);
            foreach (var part in BurnsOutParts(tile).ToArray())
                if (DestroyPart(tile, part, ObjectiveType)) AddDestroyedObjective();
            ApplyGravity(position);
        }

        // prepare a list of tiles with smoke in them (smoke acts as fire intensity)
        var smoking = new List<int>();
        for (var tile = 0; tile < Map.TileCount; tile++)
            if (Map.Smoke(tile) > 0) smoking.Add(tile);

        // now make the smoke spread.
        foreach (var tile in smoking)
        {
            var position = Map.PositionOf(tile);
            if (Map.Fire(tile) == 0)
            {
                // reduce the smoke counter
                SetTileSmoke(tile, Map.Smoke(tile) - 1);
                if (Map.Smoke(tile) == 0) continue;
                // spread in four cardinal directions as long as there are no walls blocking us
                for (var direction = 0; direction <= 6; direction += 2)
                {
                    var next = Map.Neighbour(position, direction);
                    if (next < 0 || BattleBlockage.HorizontalBlockage(Map, tile, next, BattleBlockType.Smoke) != 0) continue;
                    // only add smoke to empty tiles, or tiles with no fire, and smoke that was added this turn
                    if (Map.Smoke(next) == 0 || Map.Fire(next) == 0 && Map.Overlaps(next) != 0) AddSmoke(next, Map.Smoke(tile));
                }
            }
            else
            {
                // smoke from fire spreads upwards one level if there's no floor blocking it, at half the intensity of the fire
                var above = Map.TileAt(position.X, position.Y, position.Z + 1);
                if (above >= 0 && Map.HasNoFloor(above)) AddSmoke(above, Map.Smoke(tile) / 2);
                // then it spreads in the four cardinal directions.
                for (var direction = 0; direction <= 6; direction += 2)
                {
                    var next = Map.Neighbour(position, direction);
                    if (next >= 0 && BattleBlockage.HorizontalBlockage(Map, tile, next, BattleBlockType.Smoke) == 0) AddSmoke(next, Map.Smoke(tile) / 2);
                }
            }
        }

        if (onFire.Count != 0 || smoking.Count != 0)
        {
            // average out the smoke added this turn (and let the caller apply its damage to the units)
            for (var tile = 0; tile < Map.TileCount; tile++)
            {
                if (Map.Smoke(tile) == 0) continue;
                AverageSmoke(tile);
                if (Map.Smoke(tile) != 0) environment?.Invoke(tile);
                Map.SetOverlaps(tile, 0);
            }
        }
        MarkChanged();
    }
}

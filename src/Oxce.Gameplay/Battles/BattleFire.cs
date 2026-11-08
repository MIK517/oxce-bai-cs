using Oxce.Core.Random;
using Oxce.Gameplay.Battles.Terrain;
using Oxce.Gameplay.Campaigns;
using Oxce.Mods.Rulesets.Runtime;

namespace Oxce.Gameplay.Battles;

/// <summary>The reasons the tile part of a hit refuses before any change (Phase 7 branch 4 S9).</summary>
internal static class BattleFireReasons
{
    internal static readonly CampaignReason HitUnitUnsupported = new("battle.hit-unit-unsupported",
        "The hit would damage a unit (a unit in the voxel, or the body of a unit lying on the tile); damage to units arrives with the attack work (Phase 7 branch 5).");

    internal static readonly CampaignReason ModuleMapUnsupported = new("battle.module-map-unsupported",
        "The hit would destroy a base module in a block the saved module map does not hold (a save keeps its map as written; ADR 0039); destroying modules of such a base arrives with the attack work (Phase 7 branch 5).");

    internal static readonly CampaignReason TerrainExplosionUnsupported = new("battle.terrain-explosion-unsupported",
        "The hit or the fire would destroy a terrain part that explodes (a high explosive strength); explosions arrive with the attack work (Phase 7 branch 5).");
}

/// <summary>
/// The parts of a <c>RuleDamageType</c> the tile part of <c>TileEngine::hit</c> reads: the fire and smoke thresholds and the damage a tile takes.
/// Only the random type <c>DRT_FIRE</c> (the base damage type <c>DT_IN</c>, which a walk burns floors with) is ported: branch 5 builds the others from
/// the item rules and extends <see cref="RandomDamage"/>.
/// </summary>
internal sealed record BattleTileDamageType(int FireThreshold, int SmokeThreshold, double ToTile, bool RandomTile, int TileDamageMethod, int TileDamageLimit,
    bool FireRandom = true)
{
    /// <summary>The base <c>DT_IN</c> of <c>Mod::Mod</c>: <c>FireThreshold</c> 0, <c>ToTile</c> 0, <c>TileDamageMethod</c> 2, damage rolled in the fire damage range.</summary>
    public static BattleTileDamageType Fire { get; } = new(0, 1000, 0.0, false, 2, -1);

    /// <summary>RuleDamageType::getRandomDamage for <c>DRT_FIRE</c>: a roll in <c>Mod::FIRE_DAMAGE_RANGE</c> (the power is not read).</summary>
    public int RandomDamage(RuntimeRuleCatalog rules, IRandomSource random)
    {
        if (!FireRandom) throw new NotSupportedException("Only the fire random type is ported (Phase 7 branch 5).");
        var range = rules.Campaign.Interception.FireDamageRange;
        return random.NextInclusive(range[0], range[1]);
    }

    /// <summary>getTileFinalDamage(getRandomDamageForTile(power, damage)).</summary>
    public int TileFinalDamage(IRandomSource random, int power, int damage)
    {
        var forTile = TileDamageMethod == 1 ? random.NextInclusive(power / 2, 3 * power / 2) : damage;
        var result = 0;
        if (forTile > 0)
            result = (int)Math.Round((RandomTile ? random.NextInclusive(0, forTile) : forTile) * ToTile, MidpointRounding.AwayFromZero);
        return TileDamageLimit >= 0 && result > TileDamageLimit ? TileDamageLimit : result;
    }
}

/// <summary>
/// Fire, smoke and terrain destruction (Phase 7 branch 4 S9): <c>Tile::ignite</c>, <c>addSmoke</c>, <c>setFire</c>, <c>setSmoke</c>, <c>damage</c> and
/// <c>destroy</c>, the tile part of <c>TileEngine::hit</c> and <c>hitTile</c> (the code branch 5's explosions call), the burning of floors by a unit,
/// and <c>SavedBattleGame::prepareNewTurn</c>'s spread of fire and smoke as a routine for branch 6's end of turn. What reaches a unit (the hit's
/// <c>hitUnit</c>, the environment's damage) and what starts an explosion (<c>setExplosive</c>'s chain) is branch 5's and 6's, refused by name.
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
    internal int HitTile(RuntimeRuleCatalog rules, int tile, int damage, BattleTileDamageType type)
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
                SetTileFire(tile, damage >= type.FireThreshold * 2
                    ? Fuel(tile) + 1
                    : Fuel(tile) * (damage - type.FireThreshold) / type.FireThreshold + 1);
                SetTileSmoke(tile, Math.Max(1, Math.Min(15 - Flammability(tile) / 10, 12)));
                return 2;
            }
        }
        return 0;
    }

    /// <summary>
    /// The tile part of <c>TileEngine::hit</c> at a voxel (a hit of terrain with no weapon: the attacker only stays out of the voxel check). What
    /// the voxel holds decides: a floor, wall or object takes <see cref="HitTile"/>'s fire or smoke and the damage to the part (the base defense's
    /// module count and the objective count follow); then, when the terrain changed or fire or smoke appeared, gravity on the tile, the light of the
    /// layer it changes and the field of view around it are updated. Refused before any change when the hit would reach a unit or start an
    /// explosion; <paramref name="dryRun"/> only asks that.
    /// </summary>
    internal CampaignReason? HitTerrain(RuntimeRuleCatalog rules, BattleUnit? attacker, BattlePosition voxel, int power, BattleTileDamageType type,
        bool dryRun = false)
    {
        ArgumentNullException.ThrowIfNull(rules);
        ArgumentNullException.ThrowIfNull(type);
        var tilePosition = BattleGeometry.TileOfVoxel(voxel);
        if (!Contains(tilePosition) || power <= 0) return null;
        var tile = Map.TileIndex(tilePosition);
        var sight = Sight();
        var part = sight.VoxelCheck(voxel, attacker);
        // A dry run rolls nothing: the damage of the fire type does not decide a refusal.
        var damage = dryRun ? 0 : type.RandomDamage(rules, Random);
        var finalDamage = dryRun ? 0 : type.TileFinalDamage(Random, power, damage);
        var terrainChanged = false;
        var effect = 0;
        if (part is >= BattleVoxelType.Floor and <= BattleVoxelType.Object)
        {
            var tilePart = (BattleTilePart)(int)part;
            if (part is BattleVoxelType.Floor or BattleVoxelType.Object)
                foreach (var item in ItemsOnTile(tilePosition))
                    if (item.CorpseUnitId is { } corpse && UnitById(corpse) is { Health: > 0 }) return BattleFireReasons.HitUnitUnsupported;
            var data = Map.MapData(tile, tilePart)!;
            terrainChanged = finalDamage >= data.Armor;
            if (terrainChanged && !data.IsGravLift && data.Explosive != 0) return BattleFireReasons.TerrainExplosionUnsupported;
            var module = part == BattleVoxelType.Object && MissionType == BaseDefenseMissionType && finalDamage >= data.Armor && data.IsBaseModule;
            if (module && !(tilePosition.X / 10 < BaseModules.Length && tilePosition.Y / 10 < BaseModules[tilePosition.X / 10].Length))
                return BattleFireReasons.ModuleMapUnsupported;
            if (dryRun) return null;
            effect = HitTile(rules, tile, damage, type);
            if (module)
            {
                var counted = BaseModules[tilePosition.X / 10][tilePosition.Y / 10];
                BaseModules[tilePosition.X / 10][tilePosition.Y / 10] = counted with { Remaining = counted.Remaining - 1 };
            }
            if (DamagePart(tile, tilePart, finalDamage, ObjectiveType)) AddDestroyedObjective();
        }
        else if (part == BattleVoxelType.Unit)
        {
            if (OverlappingUnit(rules, tile) is { Health: > 0 }) return BattleFireReasons.HitUnitUnsupported;
            if (dryRun) return null;
        }
        else if (dryRun)
        {
            return null;
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
        return null;
    }

    private const string BaseDefenseMissionType = "STR_BASE_DEFENSE";

    /// <summary>BattleUnit's floor-burning abilities: <c>SPECAB_BURNFLOOR</c> and <c>SPECAB_BURN_AND_EXPLODE</c>.</summary>
    private static bool BurnsFloor(RuntimeRuleCatalog rules, BattleUnit unit) => SpecialAbilityOf(rules, unit) >= 2;

    /// <summary>
    /// What <c>UnitWalkBState</c> and <c>UnitFallBState</c> do for a unit that burns floors once it stands still: ignite its tile (power 1) and hit
    /// the floor under it with <c>DT_IN</c> at the strength of the unit. The hole that may burn through the floor makes the unit fall later (gravity
    /// queues it). A dry run only asks the hit's refusals.
    /// </summary>
    private CampaignReason? BurnFloor(RuntimeRuleCatalog rules, BattleUnit unit, bool dryRun)
    {
        var position = unit.Position!.Value;
        var tile = Map.TileIndex(position);
        var voxel = new BattlePosition(position.X * 16 + 8, position.Y * 16 + 8, position.Z * 24 - Map.TerrainLevel(tile));
        // The hit's refusals are asked first, so a refusal leaves the tile as it was (the ignite is the first change).
        var refusal = HitTerrain(rules, unit, voxel, unit.Stats.Strength, BattleTileDamageType.Fire, dryRun: true);
        if (refusal is not null || dryRun) return refusal;
        Ignite(tile, 1);
        return HitTerrain(rules, unit, voxel, unit.Stats.Strength, BattleTileDamageType.Fire);
    }

    /// <summary>Tile::prepareNewTurn without the environment's damage: smoke added this turn is averaged over its overlaps.</summary>
    private void AverageSmoke(int tile)
    {
        var overlaps = Map.Overlaps(tile);
        if (overlaps != 0 && Map.Smoke(tile) != 0 && Map.Fire(tile) == 0)
            Map.SetSmoke(tile, Math.Clamp(Map.Smoke(tile) / overlaps - 1, 0, 15));
    }

    /// <summary>
    /// What <see cref="SpreadFireAndSmoke"/> would refuse, before it changes anything: a tile whose fire burns out this turn destroys an object
    /// that explodes (the objects it burns are the tile's object and, with it, its floor).
    /// </summary>
    internal CampaignReason? FireSpreadRefusal()
    {
        for (var tile = 0; tile < Map.TileCount; tile++)
        {
            if (Map.Fire(tile) != 1 || Map.Overlaps(tile) != 0) continue;
            // The parts as they are when each destroy runs: destroying the object can put its dead form into the floor part.
            var floor = Map.MapData(tile, BattleTilePart.Floor);
            foreach (var part in BurnsOutParts(tile))
            {
                var target = part == BattleTilePart.Floor ? floor : Map.MapData(tile, part);
                if (target is { IsGravLift: false, Explosive: not 0 }) return BattleFireReasons.TerrainExplosionUnsupported;
                if (part == BattleTilePart.ObjectPart && target is not null && DestroyedForm(tile, part, target) is { ObjectType: BattleTilePart.Floor } dead)
                    floor = dead;
            }
        }
        return null;
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
    /// AI's danger flags and the light and sight updates (<c>BattlescapeGame::endTurn</c>) are the caller's. Refused before any change when a burn
    /// would destroy an exploding object. Units that lose their floor are queued (<see cref="FallingUnits"/>); the caller resolves the falls.
    /// </summary>
    internal CampaignReason? SpreadFireAndSmoke(Action<int>? environment = null)
    {
        if (FireSpreadRefusal() is { } refusal) return refusal;
        var onFire = new List<int>();
        for (var tile = 0; tile < Map.TileCount; tile++)
            if (Map.Fire(tile) > 0) onFire.Add(tile);
        if (onFire.Count == 0 && !Enumerable.Range(0, Map.TileCount).Any(tile => Map.Smoke(tile) > 0)) return null;

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
        Revision++;
        return null;
    }
}

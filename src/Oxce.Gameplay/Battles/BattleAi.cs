using Oxce.Mods.Rulesets.Runtime;

namespace Oxce.Gameplay.Battles;

/// <summary>
/// Phase 7 branch 5 S9: the parts of <c>AIModule</c> that reaction fire reads (<c>tryReaction</c> asks them whether a reaction is worth firing):
/// the reaction form of <c>brutalScoreFiringMode</c>, <c>explosiveEfficacy</c> and <c>brutalExplosiveEfficacy</c> with the target weights, <c>validateArcingShot</c>,
/// <c>getSideFacingToPosition</c> and <c>shouldAvoidMeleeRange</c>. The AI's own turn (branch 6) calls the same functions with the rest of their parameters.
/// The danger zone of a grenade with a live fuse (<c>Tile::getDangerous</c>, a flag of the tile that <c>TileEngine::setDangerZone</c> sets when such a grenade is
/// dropped where a unit stands; the AI's own throw sets it too, branch 6) is kept on the map; the reference clears it at the end of every side's turn, which
/// branch 6's turn processing ports.
/// </summary>
internal sealed partial class BattleState
{
    /// <summary>AIAttackWeight's <c>AIW_SCALE</c>: the base scale of attack weights.</summary>
    private const int AiWeightScale = 100;

    /// <summary><c>Options::battleExplosionHeight</c> (0): the levels above or below an explosion that it still reaches in the AI's estimate.</summary>
    private const int AiExplosionHeight = 0;

    /// <summary>
    /// <c>AIModule::_targetFaction</c> of the unit: the side its AI module aims at (a unit that began on X-COM's or the civilians' side aims at the aliens, an alien
    /// at X-COM; mind control changes it). A unit with no module has the one a new module gets (<c>tryReaction</c> makes it).
    /// </summary>
    private static BattleFaction AiTargetFactionOf(BattleUnit unit) =>
        unit.Ai is { } ai ? (BattleFaction)ai.TargetFaction : BattleAiState.DefaultTargetFaction(unit.OriginalFaction);

    /// <summary>BattleUnit::getIntelligence: the turns the AI remembers a target (a soldier's is 2).</summary>
    private int IntelligenceOf(BattleUnit unit) =>
        unit.SoldierId is null && RequiredRules.Battle.Equipment.Units.TryGetValue(unit.UnitType, out var rule) ? rule.Intelligence : 2;

    /// <summary>BattleUnit::isSniper: the unit rule's <c>sniper</c> is above 0 (a soldier has none).</summary>
    private bool IsSniper(BattleUnit unit) =>
        unit.SoldierId is null && RequiredRules.Battle.Equipment.Units.TryGetValue(unit.UnitType, out var rule) && rule.SniperPercentage > 0;

    /// <summary>
    /// <c>AIModule::getTargetAttackWeight</c>: how much the unit wants the target: a friend's negative weight (for blasts), nothing for a unit the unit does not
    /// "know" about (not seen for more turns than its intelligence, unless it is a sniper and its spotters know the target), the weight of a target of the side it
    /// aims at or of another side, in the armor's or the mod's values; then the armor's <c>aiCalculateTargetWeight</c> script.
    /// </summary>
    private int TargetAttackWeight(BattleUnit unit, BattleUnit target)
    {
        var combat = RequiredRules.Campaign.Combat;
        var armor = ArmorOf(target);
        int weight;
        if (target.Faction == unit.Faction) weight = armor.AiTargetWeightAsFriendly ?? combat.AiTargetWeightAsFriendly;
        else if (IntelligenceOf(unit) < target.TurnsSinceSpotted[(int)unit.Faction] &&
            (!IsSniper(unit) || target.TurnsLeftSpottedForSnipers[(int)unit.Faction] == 0))
            weight = 0;
        else if (target.Faction == BattleFaction.Hostile || unit.Faction == BattleFaction.Hostile)
            weight = target.Faction == AiTargetFactionOf(unit)
                ? armor.AiTargetWeightAsHostile ?? combat.AiTargetWeightAsHostile
                : armor.AiTargetWeightAsHostileCivilians ?? combat.AiTargetWeightAsHostileCivilians;
        else if (target.Faction == BattleFaction.Neutral || unit.Faction == BattleFaction.Neutral) weight = armor.AiTargetWeightAsNeutral ?? combat.AiTargetWeightAsNeutral;
        else weight = 0;
        return Hooks.AiCalculateTargetWeight(unit, target, weight);
    }

    /// <summary>AIModule::isEnemy (without the mind control argument): another unit the AI does not ignore, of a side the unit fights.</summary>
    private bool IsAiEnemy(BattleUnit unit, BattleUnit other)
    {
        if (unit == other || IgnoredByAi(other)) return false;
        // A mind-controlled unit counts as the side it began on.
        var faction = other.Faction != other.OriginalFaction ? other.OriginalFaction : other.Faction;
        return unit.Faction switch
        {
            BattleFaction.Hostile => faction is BattleFaction.Player or BattleFaction.Neutral,
            BattleFaction.Neutral or BattleFaction.Player => faction == BattleFaction.Hostile,
            _ => false,
        };
    }

    /// <summary>AIModule::isAlly: every unit that is not an enemy by side (the unit itself included).</summary>
    private static bool IsAiAlly(BattleUnit unit, BattleUnit other) => unit.Faction switch
    {
        BattleFaction.Hostile => other.Faction is not (BattleFaction.Player or BattleFaction.Neutral),
        BattleFaction.Neutral or BattleFaction.Player => other.Faction != BattleFaction.Hostile,
        _ => true,
    };

    /// <summary>
    /// AIModule::getSideFacingToPosition: which of the unit's eight sides faces the position, as the direction from its facing (0 front, 1 left front, 2 left,
    /// 3 left rear, 4 rear, 5 right rear, 6 right, 7 right front); -1 for a unit that is out (its underside).
    /// </summary>
    private static int SideFacingToPosition(BattleUnit unit, BattlePosition position)
    {
        if (unit.IsOut) return -1;
        return (BattleGeometry.DirectionTo(unit.RequiredPosition, position) - unit.Direction + 8) % 8;
    }

    /// <summary>
    /// AIModule::shouldAvoidMeleeRange: a unit that can only reach one tile with its main weapon never does; else, with close quarters combat on, one avoids an
    /// enemy whose armor makes a melee threat unless its own armor ignores that. (<c>battleUFOExtenderAccuracy</c> is off: the extended range is the weapon's
    /// maximum range, a melee weapon's is 1.)
    /// </summary>
    internal bool ShouldAvoidMeleeRange(BattleUnit unit, BattleUnit enemy)
    {
        var weapon = MainHandWeapon(unit);
        var maxRange = weapon is null ? 0 : ItemRuleOf(weapon.ItemType).BattleType == RuntimeBattleItem.MeleeBattleType ? 1 : RequiredRules.Battle.Equipment.Items[weapon.ItemType].Combat.MaxRange;
        if (maxRange == 1) return false;
        return RequiredRules.Campaign.Combat.EnableCloseQuartersCombat != 0 && !(ArmorOf(unit).IgnoresMeleeThreat ?? false) && (ArmorOf(enemy).CreatesMeleeThreat ?? true);
    }

    /// <summary>
    /// AIModule::validateArcingShot: whether an arcing shot (or a throw) of the action reaches its target tile from the unit's tile: the first of the target's
    /// voxels (the unit's middle, bottom and top when it is visible to X-COM, else the object's, wall's or floor's) that a throw arc can reach.
    /// </summary>
    private bool ValidateArcingShot(BattleAimAction action)
    {
        var projectile = new BattleProjectile(this, RequiredRules, action, action.Actor.RequiredPosition, default);
        var arc = projectile.FindArc(null, out _, out _, out _, out var forced, visibleUnitsOnly: true);
        return forced || arc != BattleVoxelType.OutOfBounds;
    }

    /// <summary>
    /// <c>AIModule::explosiveEfficacy</c>: whether an explosion of that radius is worth setting off at the position for a hostile unit, in whole targets: its
    /// desperation (morale, and a third of its health gone) and the difficulty weigh against the weights of the units it would hit (and a blast on itself
    /// when it stands in it), nothing for a lone grenade target of an unhurried unit. A difficulty of -1 is the campaign's.
    /// </summary>
    internal int ExplosiveEfficacy(BattlePosition targetPosition, BattleUnit attacker, int radius, int difficulty = -1, bool grenade = false)
    {
        var tile = Map.TileIndex(targetPosition);
        // don't throw grenades at flying enemies.
        if (grenade && targetPosition.Z > 0 && Map.HasNoFloor(tile)) return 0;
        if (difficulty == -1) difficulty = RequiredRules.Campaign.Interception.DifficultyCoefficient(CampaignLink?.Difficulty ?? 0);
        var distance = Distance2d(attacker.RequiredPosition, targetPosition);
        var injury = attacker.Stats.Health - attacker.Health;
        var desperation = (100 - attacker.Morale) / 10;
        var enemiesAffected = 0;
        // if we're below 1/3 health, let's assume things are dire, and increase desperation.
        if (injury > attacker.Stats.Health / 3 * 2) desperation += 3;
        var efficacy = AiWeightScale * desperation;
        // don't go kamikaze unless we're already doomed.
        if (Math.Abs(attacker.RequiredPosition.Z - targetPosition.Z) <= AiExplosionHeight && distance <= radius) efficacy -= AiWeightScale * 4;
        // allow difficulty to have its influence
        efficacy += AiWeightScale * difficulty / 2;

        // account for the unit we're targetting
        var target = UnitOnTile(tile);
        if (target is not null && !Map.Dangerous(tile))
        {
            ++enemiesAffected;
            efficacy += TargetAttackWeight(attacker, target);
        }
        var sight = Sight();
        var targetFaction = AiTargetFactionOf(attacker);
        foreach (var other in _units)
        {
            // don't grenade dead guys, count ourself or the target twice, or count units that probably won't be affected cause they're out of range
            if (other.IsOut || other == attacker || other == target || other.Position is not { } position ||
                Math.Abs(position.Z - targetPosition.Z) > AiExplosionHeight || Distance2d(position, targetPosition) > radius)
                continue;
            // don't count people who were already grenaded this turn
            if (Map.Dangerous(Map.TileIndex(position))) continue;
            var weight = TargetAttackWeight(attacker, other);
            // AI do not know anything about this unit
            if (weight == 0) continue;
            // trace a line from the grenade origin to the unit we're checking against
            var trajectory = new List<BattlePosition>();
            var hit = sight.CalculateLineVoxel(BattleGeometry.TileCentreVoxel(targetPosition), BattleGeometry.TileCentreVoxel(position), false, trajectory, target, other);
            if (hit == BattleVoxelType.Unit && trajectory.Count > 0 && BattleGeometry.TileOfVoxel(trajectory[0]) == position)
            {
                if (other.Faction == targetFaction) ++enemiesAffected;
                efficacy += weight;
            }
        }
        // don't throw grenades at single targets, unless morale is in the danger zone or we're halfway towards panicking while bleeding to death.
        if (grenade && desperation < 6 && enemiesAffected < 2) return 0;
        // Ignore loses if we can kill lot of enemies.
        if (enemiesAffected >= 10) return enemiesAffected;
        // We kill more enemies than allies. Scale back to number of targets, can round down to zero
        return efficacy > 0 ? efficacy / AiWeightScale : 0;
    }

    /// <summary>
    /// <c>AIModule::brutalExplosiveEfficacy</c> (with <c>validOnly</c> off: every enemy counts, seen or not): the enemies an explosion of that radius would hit
    /// at the position less the allies, each by how near the centre it stands; the unit itself counts as an ally (or, for one that changed sides, an enemy)
    /// by its share of the blast; nothing for a grenade at a flying target.
    /// </summary>
    internal float BrutalExplosiveEfficacy(BattlePosition targetPosition, BattleUnit attacker, int radius, bool grenade)
    {
        var tile = Map.TileIndex(targetPosition);
        if (grenade && Map.Dangerous(tile)) return 0;
        // don't throw grenades at flying enemies.
        if (grenade && targetPosition.Z > 0 && Map.HasNoFloor(tile)) return 0;
        var distance = Distance2d(attacker.RequiredPosition, targetPosition);
        float enemiesAffected = 0;
        // don't go kamikaze unless we're already doomed.
        if (Math.Abs(attacker.RequiredPosition.Z - targetPosition.Z) <= AiExplosionHeight && distance <= radius)
        {
            var share = (float)(radius - distance / 2.0) / radius;
            if (attacker.Faction == attacker.OriginalFaction) enemiesAffected -= share;
            else enemiesAffected += share;
        }
        // account for the unit we're targetting
        var target = UnitOnTile(tile);
        if (target is not null)
        {
            if (IsAiEnemy(attacker, target)) enemiesAffected++;
            else if (IsAiAlly(attacker, target)) enemiesAffected--;
        }
        var sight = Sight();
        foreach (var other in _units)
        {
            if (other.IsOut || other == attacker || other == target || other.Position is not { } position ||
                Math.Abs(position.Z - targetPosition.Z) > AiExplosionHeight || Distance2d(position, targetPosition) > radius)
                continue;
            // don't count people who were already grenaded this turn
            if (Map.Dangerous(Map.TileIndex(position))) continue;
            var trajectory = new List<BattlePosition>();
            var hit = sight.CalculateLineVoxel(BattleGeometry.TileCentreVoxel(targetPosition), BattleGeometry.TileCentreVoxel(position), false, trajectory, target, other);
            var distanceToTarget = Distance2d(targetPosition, position);
            var distanceModifier = (float)(radius - distanceToTarget / 2.0) / radius;
            if (hit == BattleVoxelType.Unit && trajectory.Count > 0 && BattleGeometry.TileOfVoxel(trajectory[0]) == position)
            {
                if (IsAiEnemy(attacker, other)) enemiesAffected += distanceModifier;
                else if (IsAiAlly(attacker, other)) enemiesAffected--;
            }
        }
        return enemiesAffected;
    }

    /// <summary>
    /// TileEngine::setDangerZone: marks the tile and the tiles within the radius that a blast from its middle would reach on a level (a line that nothing but the
    /// unit's own tile stops, and that ends on the tile) as dangerous. The loops stop short of the radius on the positive side, as the reference's do.
    /// </summary>
    internal void SetDangerZone(BattlePosition position, int radius, BattleUnit unit)
    {
        if (!Contains(position)) return;
        var tile = Map.TileIndex(position);
        // set the epicenter as dangerous
        Map.SetDangerous(tile, true);
        var originVoxel = DangerVoxel(position, tile);
        var sight = Sight();
        for (var x = -radius; x != radius; ++x)
        {
            for (var y = -radius; y != radius; ++y)
            {
                // we can skip the epicenter, and make sure we're within the radius
                if ((x == 0 && y == 0) || x * x + y * y > radius * radius) continue;
                var other = position with { X = position.X + x, Y = position.Y + y };
                if (!Contains(other)) continue;
                var otherTile = Map.TileIndex(other);
                var trajectory = new List<BattlePosition>();
                // we'll trace a line here, ignoring all units, to check if the explosion will reach this point
                if (sight.CalculateLineVoxel(originVoxel, DangerVoxel(other, otherTile), true, trajectory, unit, unit) == BattleVoxelType.Empty &&
                    trajectory.Count > 0 && BattleGeometry.TileOfVoxel(trajectory[^1]) == other)
                    Map.SetDangerous(otherTile, true);
            }
        }
    }

    /// <summary>setDangerZone's voxel of a tile: the middle of the tile, 12 voxels above its floor.</summary>
    private BattlePosition DangerVoxel(BattlePosition position, int tile) => BattleGeometry.TileCentreVoxel(position, 12 - Map.TerrainLevel(tile));

    /// <summary>Position::distance2d: the distance on a level, rounded up to whole tiles.</summary>
    private static int Distance2d(BattlePosition one, BattlePosition other)
    {
        int x = one.X - other.X, y = one.Y - other.Y;
        return (int)Math.Ceiling(Math.Sqrt(x * x + y * y));
    }
}

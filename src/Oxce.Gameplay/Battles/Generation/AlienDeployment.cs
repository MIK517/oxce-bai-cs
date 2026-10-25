using Oxce.Core.Random;
using Oxce.Gameplay.Battles.Terrain;
using Oxce.Gameplay.Campaigns;
using Oxce.Gameplay.Campaigns.World;
using Oxce.Mods.Rulesets.Runtime;

namespace Oxce.Gameplay.Battles.Generation;

/// <summary>
/// BattlescapeGenerator::deployAliens and addAlien: each deployment group's quantity of aliens of its rank (or custom unit
/// type) from the battle's race, spawned on nodes by rank (or outside the UFO), facing a window or the craft, with
/// difficulty-adjusted statistics, their built-in items and the group's item set for their item level.
/// </summary>
internal sealed class AlienDeployment
{
    /// <summary>Node::nodeRank: the node ranks each alien rank tries, in order.</summary>
    private static readonly int[][] NodeRanks =
    [
        [4, 3, 5, 8, 7, 2, 0], // commander
        [4, 3, 5, 8, 7, 2, 0], // leader
        [5, 4, 3, 2, 7, 8, 0], // engineer
        [7, 6, 2, 8, 3, 4, 0], // medic
        [3, 4, 5, 2, 7, 8, 0], // navigator
        [2, 5, 3, 4, 6, 8, 0], // soldier
        [2, 5, 3, 4, 6, 8, 0], // terrorist
        [2, 5, 3, 4, 6, 8, 0], // also terrorist
    ];

    private readonly BattleState _battle;
    private readonly RuntimeRuleCatalog _rules;
    private readonly IRandomSource _random;
    private readonly UnitEquipment _equipment;
    private readonly BattlePlacement _placement;
    private readonly int _difficulty;
    private int _unitSequence;

    private AlienDeployment(BattleState battle, RuntimeRuleCatalog rules, IRandomSource random, int difficulty, int unitSequence)
    {
        _battle = battle;
        _rules = rules;
        _random = random;
        _equipment = new UnitEquipment(battle, rules, random);
        _placement = new BattlePlacement(battle, random);
        _difficulty = difficulty;
        _unitSequence = unitSequence;
    }

    /// <summary>Deploys the aliens and returns the next unit ID.</summary>
    public static int Deploy(CampaignState campaign, CampaignDeploymentRequest request, BattleState battle, string deploymentId,
        RuntimeAlienDeploymentRule deployment, int unitSequence)
    {
        var rules = campaign.Content.RuntimeRules;
        var deployer = new AlienDeployment(battle, rules, campaign.Random, (int)campaign.Difficulty, unitSequence);
        var count = battle.Units.Count;
        deployer.Run(campaign, request, deploymentId, deployment);
        if (battle.Units.Count == count) throw new MapGenerationException("Map generator encountered an error: no alien units could be placed on the map.");
        return deployer._unitSequence;
    }

    private void Run(CampaignState campaign, CampaignDeploymentRequest request, string deploymentId, RuntimeAlienDeploymentRule deployment)
    {
        if (deployment.Battle.DataInvalid is { } invalid) throw new MapGenerationException(invalid);
        // The deployment's race wins in a campaign (months passed is never -1 here).
        var (raceId, race) = AlienRaces.Resolve(_rules, deploymentId, deployment.Race, request.Race, _battle.Depth);
        _battle.ReinforcementsDeployment = deploymentId;
        _battle.ReinforcementsRace = raceId;
        _battle.ReinforcementsItemLevel = _battle.AlienItemLevel;
        var ufo = request.Target.Kind == WorldTargetKind.Ufo;
        foreach (var data in deployment.Battle.Data)
        {
            var quantity = AlienQuantities.Quantity(data, (CampaignDifficulty)_difficulty, _rules.Campaign.DifficultyDemigod, _random);
            for (var index = 0; index < quantity; index++)
            {
                // A damaged UFO loses each alien after the first with that chance (base defense).
                if (request.UfoDamagePercentage > 0 && index > 0 && RandomChance.Percent(_random, request.UfoDamagePercentage)) continue;
                var name = data.CustomUnitType.Length == 0 ? AlienRaces.Member(raceId, race, data.AlienRank, _random) : data.CustomUnitType;
                var outside = RandomChance.Percent(_random, data.PercentageOutsideUfo);
                if (!ufo && !deployment.Battle.ForcePercentageOutsideUfo) outside = false;
                var (unitRule, armor) = _battle.UnitRule(_rules, name);
                var unit = AddAlien(name, unitRule, armor, data.AlienRank, outside);
                var itemLevel = _equipment.DrawItemLevel();
                if (unit is null) continue;
                _equipment.InitUnit(unit, unitRule, "", itemLevel);
                if (unitRule.LivingWeapon) continue;
                if (data.ItemSets.Count == 0) throw new MapGenerationException("Unit generator encountered an error: item set not defined");
                foreach (var item in data.ItemSets[Math.Min(itemLevel, data.ItemSets.Count - 1)])
                    if (_rules.Items.TryGet(item, out _)) _equipment.CreateItemForUnit(item, unit);
                foreach (var set in data.ExtraRandomItems)
                {
                    if (set.Count == 0) continue;
                    var pick = set[_random.NextInclusive(0, set.Count - 1)];
                    if (_rules.Items.TryGet(pick, out _)) _equipment.CreateItemForUnit(pick, unit);
                }
            }
        }
    }

    /// <summary>BattlescapeGenerator::addAlien.</summary>
    private BattleUnit? AddAlien(string name, RuntimeBattleUnitRule unitRule, RuntimeArmorRule armor, int alienRank, bool outside)
    {
        var unit = _battle.TempUnit(_rules, _unitSequence++, BattleFaction.Hostile, BattleController.Ai, name, unitRule, armor, _difficulty);
        // Safety against index out of bounds.
        if (alienRank > 7) alienRank = 7;
        if (alienRank < 0) throw new MapGenerationException($"Deployment data has a negative alien rank {alienRank}.");
        BattleNode? node = null;
        for (var attempt = 0; attempt < 7 && node is null; attempt++)
            node = _placement.SpawnNode(outside ? 0 : NodeRanks[alienRank][attempt], unit);
        var odds = 20 * _rules.Campaign.Interception.DifficultyCoefficient(_difficulty);
        if (_rules.Campaign.Interception.AliensFacingCraftOdds.Count > _difficulty) odds = _rules.Campaign.Interception.AliensFacingCraftOdds[_difficulty];
        BattlePosition spawned;
        if (node is not null && _placement.TryPosition(unit, node.Position) is { } position)
        {
            unit.Position = position;
            unit.StartNodeId = node.Id;
            spawned = node.Position;
        }
        else if ((_rules.Campaign.DifficultyDemigod || _rules.Campaign.ExtendedForceSpawn) && _placement.PlaceNearFriend(unit))
        {
            // Demigod difficulty (or extendedForceSpawn): spawn as many aliens as possible.
            spawned = unit.Position!.Value;
        }
        else
        {
            return null;
        }
        unit.Rank = alienRank;
        var direction = FaceWindow(spawned);
        // The first unit's position; a base defense's dummy that could not stand on the first tile is off the map here,
        // where the reference's unit keeps its default position (0, 0, 0) without a tile.
        var craft = _battle.Units[0].Position ?? default;
        if (Distance2d(spawned, craft) <= 20 && RandomChance.Percent(_random, odds)) direction = DirectionTo(unit.Position!.Value, craft);
        unit.Direction = direction != -1 ? direction : _random.NextInclusive(0, 7);
        _battle.AddUnit(unit);
        return unit;
    }

    /// <summary>TileEngine::faceWindow: the direction of a see-through wall at the position, or -1.</summary>
    private int FaceWindow(BattlePosition position)
    {
        bool Window(BattlePosition at, BattleTilePart part) => _battle.Map.Contains(at) &&
            _battle.Map.MapData(_battle.Map.TileIndex(at), part) is { } data && data.Block(1) == 0;
        if (Window(position, BattleTilePart.NorthWall)) return 0;
        if (Window(position with { X = position.X + 1 }, BattleTilePart.WestWall)) return 2;
        if (Window(position with { Y = position.Y + 1 }, BattleTilePart.NorthWall)) return 4;
        if (Window(position, BattleTilePart.WestWall)) return 6;
        return -1;
    }

    /// <summary>Position::distance2d: the rounded-up horizontal distance.</summary>
    private static int Distance2d(BattlePosition from, BattlePosition to)
    {
        var dx = from.X - to.X;
        var dy = from.Y - to.Y;
        return (int)Math.Ceiling(Math.Sqrt(dx * dx + dy * dy));
    }

    /// <summary>BattleUnit::directionTo: the eighth of the circle the point lies in, 0 north and clockwise.</summary>
    private static int DirectionTo(BattlePosition from, BattlePosition point)
    {
        var angle = Math.Atan2(point.X - from.X, -(double)(point.Y - from.Y));
        ReadOnlySpan<double> pie = [Math.PI - Math.PI / 8, Math.PI * 3 / 4 - Math.PI / 8, Math.PI / 2 - Math.PI / 8, Math.PI / 4 - Math.PI / 8];
        if (angle > pie[0] || angle < -pie[0]) return 4;
        if (angle > pie[1]) return 3;
        if (angle > pie[2]) return 2;
        if (angle > pie[3]) return 1;
        if (angle < -pie[1]) return 5;
        if (angle < -pie[2]) return 6;
        if (angle < -pie[3]) return 7;
        return 0;
    }
}

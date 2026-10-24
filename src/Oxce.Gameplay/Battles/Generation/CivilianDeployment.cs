using System.Text;
using Oxce.Core.Random;
using Oxce.Mods.Rulesets.Runtime;

namespace Oxce.Gameplay.Battles.Generation;

/// <summary>
/// BattlescapeGenerator::deployCivilians and addCivilian: the deployment's civilians of the terrain's random civilian
/// types, then each civiliansByType entry (std::map order), on spawn nodes of the civilian node rank or near a friend.
/// </summary>
internal sealed class CivilianDeployment
{
    private readonly BattleState _battle;
    private readonly RuntimeRuleCatalog _rules;
    private readonly IRandomSource _random;
    private readonly UnitEquipment _equipment;
    private readonly BattlePlacement _placement;
    private int _unitSequence;

    private CivilianDeployment(BattleState battle, RuntimeRuleCatalog rules, IRandomSource random, int unitSequence)
    {
        _battle = battle;
        _rules = rules;
        _random = random;
        _equipment = new UnitEquipment(battle, rules, random);
        _placement = new BattlePlacement(battle, random);
        _unitSequence = unitSequence;
    }

    /// <summary>Deploys the civilians and returns the next unit ID.</summary>
    public static int Deploy(BattleState battle, RuntimeRuleCatalog rules, IRandomSource random, RuntimeDeploymentBattle deployment,
        RuntimeTerrain terrain, int unitSequence)
    {
        var deployer = new CivilianDeployment(battle, rules, random, unitSequence);
        deployer.Deploy(deployment.MarkCiviliansAsVip, deployment.CivilianSpawnNodeRank, deployment.Civilians, false, "", terrain);
        // std::map<std::string, int> walks its keys in UTF-8 byte order.
        foreach (var (type, count) in deployment.CiviliansByType.OrderBy(static pair => Encoding.UTF8.GetBytes(pair.Key), Utf8Order))
            deployer.Deploy(deployment.MarkCiviliansAsVip, deployment.CivilianSpawnNodeRank, count, true, type, terrain);
        return deployer._unitSequence;
    }

    private static readonly Comparer<byte[]> Utf8Order = Comparer<byte[]>.Create(static (left, right) => left.AsSpan().SequenceCompareTo(right));

    private void Deploy(bool markAsVip, int nodeRank, int maximum, bool roundUp, string civilianType, RuntimeTerrain terrain)
    {
        if (maximum == 0) return;
        // Between half the maximum (rounded as asked) and the maximum.
        var number = _random.NextInclusive(roundUp ? (maximum + 1) / 2 : maximum / 2, maximum);
        for (var index = 0; index < number; index++)
        {
            string type;
            if (civilianType.Length != 0) type = civilianType;
            else
            {
                // RNG::generate(0, size - 1) on no civilian types reads past the terrain's list in the reference.
                if (terrain.CivilianTypes.Count == 0)
                    throw new MapGenerationException($"Terrain {terrain.Id} has no civilian types for the deployment's civilians.");
                type = terrain.CivilianTypes[_random.NextInclusive(0, terrain.CivilianTypes.Count - 1)];
            }
            var (unitRule, armor) = _battle.UnitRule(_rules, type);
            if (AddCivilian(type, unitRule, armor, nodeRank) is not { } civilian) continue;
            civilian.IsVip = markAsVip;
            // Civilians' built-in weapon sets follow the alien item levels.
            _equipment.InitUnit(civilian, unitRule, "", _equipment.DrawItemLevel());
        }
    }

    /// <summary>BattlescapeGenerator::addCivilian: a neutral unit on a spawn node of the rank, else near a friend.</summary>
    private BattleUnit? AddCivilian(string type, RuntimeBattleUnitRule unitRule, RuntimeArmorRule armor, int nodeRank)
    {
        var unit = _battle.TempUnit(_unitSequence++, BattleFaction.Neutral, BattleController.Ai, type, unitRule, armor);
        if (_placement.SpawnNode(nodeRank, unit) is { } node)
        {
            // getSpawnNode only returns a node the unit can stand on.
            unit.Position = _placement.TryPosition(unit, node.Position);
            unit.StartNodeId = node.Id;
        }
        else if (!_placement.PlaceNearFriend(unit))
        {
            return null;
        }
        unit.Direction = _random.NextInclusive(0, 7);
        _battle.AddUnit(unit);
        return unit;
    }
}

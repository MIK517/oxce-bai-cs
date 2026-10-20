using Oxce.Core.Random;
using Oxce.Gameplay.Campaigns;
using Oxce.Mods.Rulesets.Runtime;

namespace Oxce.Gameplay.Battles.Generation;

/// <summary>The race a battle's aliens come from and the unit for each rank (BattlescapeGenerator::deployAliens, AlienRace).</summary>
internal static class AlienRaces
{
    /// <summary>
    /// deployAliens: the deployment's own race when it names one, else the battle's; an underwater battle (depth above 0)
    /// uses the race's <c>_UNDERWATER</c> variant. An unknown race is the reference's exception, a refusal here.
    /// </summary>
    public static (string Id, RuntimeAlienRaceRule Rule) Resolve(RuntimeRuleCatalog rules, string deploymentId, string deploymentRace,
        string battleRace, int depth)
    {
        var race = deploymentRace.Length != 0 ? deploymentRace : battleRace;
        if (depth > 0 && !race.Contains("_UNDERWATER", StringComparison.Ordinal)) race += "_UNDERWATER";
        return race.Length != 0 && rules.AlienRaces.TryGet(race, out var handle)
            ? (race, rules.AlienRaces[handle].Value)
            : throw new MapGenerationException($"Map generator encountered an error: Unknown race: {race} defined in deployment: {deploymentId}");
    }

    /// <summary>AlienRace::getMember: a random unit of the rank's random set when the race has random sets, else the rank's member.</summary>
    public static string Member(string raceId, RuntimeAlienRaceRule race, int rank, IRandomSource random)
    {
        if (race.RandomMembers.Count != 0)
        {
            if ((uint)rank >= (uint)race.RandomMembers.Count)
                throw new MapGenerationException($"Race {raceId} does not have a random member at position/rank {rank}");
            var set = race.RandomMembers[rank];
            // RNG::generate(0, -1) on an empty set would index past it in the reference.
            if (set.Count == 0) throw new MapGenerationException($"Race {raceId} has an empty random member set at position/rank {rank}");
            return set[random.NextInclusive(0, set.Count - 1)];
        }
        if ((uint)rank >= (uint)race.Members.Count)
            throw new MapGenerationException($"Race {raceId} does not have a member at position/rank {rank}");
        return race.Members[rank];
    }
}

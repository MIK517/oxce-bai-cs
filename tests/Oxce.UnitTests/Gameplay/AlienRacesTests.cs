using Oxce.Core.Random;
using Oxce.Gameplay.Battles.Generation;
using Oxce.Mods.Rulesets.Runtime;
using Xunit;

namespace Oxce.UnitTests.Gameplay;

/// <summary>AlienRace::getMember: the random sets win over the fixed members; a missing rank is an error.</summary>
public sealed class AlienRacesTests
{
    [Fact]
    public void MembersComeFromTheRandomSetsWhenTheRaceHasThem()
    {
        var fixedRace = new RuntimeAlienRaceRule("RACE", 0, []) { Members = ["SOLDIER", "LEADER"] };
        Assert.Equal("LEADER", AlienRaces.Member("RACE", fixedRace, 1, new SplitMix64RandomSource(1)));
        Assert.Throws<MapGenerationException>(() => AlienRaces.Member("RACE", fixedRace, 2, new SplitMix64RandomSource(1)));

        var randomRace = fixedRace with { RandomMembers = [["A", "B"], []] };
        var picks = Enumerable.Range(0, 50).Select(seed => AlienRaces.Member("RACE", randomRace, 0, new SplitMix64RandomSource((ulong)seed))).ToHashSet();
        Assert.Equal(["A", "B"], picks.Order());
        Assert.Throws<MapGenerationException>(() => AlienRaces.Member("RACE", randomRace, 1, new SplitMix64RandomSource(1)));
        Assert.Throws<MapGenerationException>(() => AlienRaces.Member("RACE", randomRace, 2, new SplitMix64RandomSource(1)));
    }
}

using Oxce.Core.Random;
using Oxce.Gameplay.Campaigns;
using Oxce.Savegames.Oxce;
using Oxce.TestSupport;
using Xunit;
using static Oxce.CompatibilityTests.StrategicWorldTestSupport;

namespace Oxce.CompatibilityTests;

/// <summary>
/// Branch 5 S2: the user options read by interception (<c>Options::dogfightAI</c>, default true, and
/// <c>Options::oxceRememberDisabledCraftWeapons</c>, default false) are campaign options that persist,
/// seed from a reference save's option snapshot and honour a mod's fixed user options.
/// </summary>
public sealed class StrategicInterceptionOptionsTests
{
    [Fact]
    public void DogfightOptionsDefaultPersistAndCanBeFixed()
    {
        var defaults = new CampaignOptions();
        Assert.True(defaults.DogfightAI);
        Assert.False(defaults.RememberDisabledCraftWeapons);

        var content = StrategicReadinessTestContent.Load("strategic-world.rul");
        var snapshot = CreateSnapshot(content, "Dogfight options") with
        {
            Options = defaults with { DogfightAI = false, RememberDisabledCraftWeapons = true },
        };
        var reloaded = Reload(content, CampaignState.Restore(snapshot, content, new SplitMix64RandomSource(3)), 3, "dogfight-options.sav");
        Assert.False(reloaded.Options.DogfightAI);
        Assert.True(reloaded.Options.RememberDisabledCraftWeapons);

        var forced = defaults.WithFixedUserOptions(new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["dogfightAI"] = "false",
            ["oxceRememberDisabledCraftWeapons"] = "true",
            // OptionInfo::load reads an int with `ss >> std::dec`: leading blanks, digits up to the first other character.
            ["oxceWoundedDefendBaseIf"] = " 75%",
        });
        Assert.False(forced.DogfightAI);
        Assert.True(forced.RememberDisabledCraftWeapons);
        Assert.Equal(75, forced.WoundedDefendBaseIf);
        Assert.Equal(0, defaults.WithFixedUserOptions(new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["oxceWoundedDefendBaseIf"] = "half",
        }).WoundedDefendBaseIf);
    }

    [Fact]
    public void ReferenceSaveOptionsSnapshotSeedsDogfightOptions()
    {
        var content = StrategicReadinessTestContent.Load("strategic-world.rul");
        var lines = OxceSaveAdapter.EmitNewCampaign(CreateSnapshot(content, "Reference dogfight options")).Split('\n').ToList();
        var start = lines.FindIndex(line => line == "oxcePortOptions:");
        Assert.True(start >= 0);
        var end = lines.FindIndex(start + 1, line => line.Length == 0 || !line.StartsWith(' '));
        if (end < 0) end = lines.Count;
        // SavedGame::save writes the player's options as this snapshot.
        lines.RemoveRange(start, end - start);
        lines.InsertRange(start, ["options:", "  dogfightAI: false", "  oxceRememberDisabledCraftWeapons: true", "  oxceWoundedDefendBaseIf: 50"]);

        var options = TestFixtures.LoadLogisticsSave(string.Join('\n', lines), content, name: "reference-dogfight-options.sav").Campaign.Options;

        Assert.False(options.DogfightAI);
        Assert.True(options.RememberDisabledCraftWeapons);
        Assert.Equal(50, options.WoundedDefendBaseIf);
    }
}

using System.Text.Json;
using Oxce.Core.Random;
using Oxce.Gameplay.Campaigns;
using Oxce.Mods.Discovery;
using Oxce.Mods.Loading;
using Oxce.Mods.Rulesets.Content;
using Xunit;

namespace Oxce.CompatibilityTests;

public sealed class CampaignFoundationFixtureTests
{
    [Fact]
    public void NewCampaignAndStartingBaseMatchPinnedReferenceScenario()
    {
        var root = FindRepositoryRoot();
        using var expected = JsonDocument.Parse(File.ReadAllText(Path.Combine(
            root, "fixtures", "expected", "savegames", "campaign-foundation.expected.json")));
        var fixture = Path.Combine(root, "fixtures", "public", "mods", "runtime-rule-linking");
        var plan = ModLoadPlanner.Create(
            ModCatalog.Create(ModDiscovery.ScanDirectory(fixture).Mods),
            [new ModActivation("runtime-master", true), new ModActivation("runtime-addon", true)],
            "runtime-master",
            new ModEngineIdentity("Extended", "8.6.1.0"));
        var content = ContentSnapshotBuilder.Build(plan).Content;
        var campaign = CampaignFactory.Create(
            content,
            new NewCampaignRequest(
                new CampaignId(Guid.Parse("11111111-2222-3333-4444-555555555555")),
                "Campaign", "runtime-master", ["runtime-master", "runtime-addon"], CampaignDifficulty.Beginner),
            new SplitMix64RandomSource(42),
            new FixedClock());
        var placed = campaign.Execute(new PlaceStartingBase(0, "Alpha", 1.25, -0.5));
        var advanced = campaign.Execute(new AdvanceCampaignTime(12));
        var actual = campaign.Capture();
        var expectedRoot = expected.RootElement;

        Assert.Equal(expectedRoot.GetProperty("time").EnumerateArray().Select(static item => item.GetInt32()),
            new[] { actual.Time.Weekday, actual.Time.Day, actual.Time.Month, actual.Time.Year, actual.Time.Hour,
                actual.Time.Minute, actual.Time.Second });
        Assert.Equal(expectedRoot.GetProperty("funds").GetInt64(), Assert.Single(actual.Funds));
        var country = Assert.Single(actual.Countries);
        var expectedCountry = expectedRoot.GetProperty("country");
        Assert.Equal(expectedCountry[0].GetString(), country.RuleId);
        Assert.Equal(expectedCountry[1].GetInt32(), Assert.Single(country.Funding));
        Assert.Equal(expectedRoot.GetProperty("region").GetString(), Assert.Single(actual.Regions).RuleId);
        var baseState = Assert.Single(actual.Bases);
        var expectedBase = expectedRoot.GetProperty("base");
        Assert.Equal(expectedBase[0].GetString(), baseState.Name);
        Assert.Equal(expectedBase[1].GetDouble(), baseState.Longitude);
        Assert.Equal(expectedBase[2].GetDouble(), baseState.Latitude);
        Assert.Equal(expectedBase[3].GetString(), Assert.Single(baseState.Facilities).RuleId);
        Assert.Equal(expectedBase[4].GetString(), Assert.Single(baseState.Crafts).RuleId);
        Assert.Equal(expectedBase[5].GetInt32(), Assert.Single(baseState.Crafts).Id);
        Assert.Equal(expectedBase[6].GetInt32(), baseState.Soldiers.Count);
        Assert.Equal(expectedBase[7].GetInt32(), baseState.Items["ITEM"]);
        Assert.Equal(expectedRoot.GetProperty("events")[0].GetString(), Assert.Single(placed.Events).GetType().Name);
        Assert.Equal(expectedRoot.GetProperty("events")[1].GetString(), Assert.Single(advanced.Events).GetType().Name);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Oxce.slnx")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("Could not locate the repository root.");
    }

    private sealed class FixedClock : ICampaignClock
    {
        public DateTimeOffset UtcNow => DateTimeOffset.UnixEpoch;
    }
}

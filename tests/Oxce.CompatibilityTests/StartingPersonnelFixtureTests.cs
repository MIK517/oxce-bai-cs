using Oxce.Core.Random;
using Oxce.Gameplay.Campaigns;
using Oxce.Mods.Bootstrap;
using Oxce.Mods.Rulesets.Content;
using Oxce.Savegames.Oxce;
using Oxce.TestSupport;
using Xunit;

namespace Oxce.CompatibilityTests;

// Reference: Mod.cpp loadStartingBase/getStartingBase/newSave and Base.cpp constructor/load
// at 4df3a5e571a1a4b5e8a46d3161fb2e21a2adba15. Expected values follow selected-template
// overlay and int defaults, not the port's emitted save.
public sealed class StartingPersonnelFixtureTests
{
    [Theory]
    [InlineData(CampaignDifficulty.Beginner, 30, 0)]
    [InlineData(CampaignDifficulty.Experienced, 0, 40)]
    [InlineData(CampaignDifficulty.Veteran, 50, 60)]
    [InlineData(CampaignDifficulty.Genius, 0, 0)]
    [InlineData(CampaignDifficulty.Superhuman, 10, 20)]
    public void SelectedTemplatePersonnelSurviveCacheCreationAndSave(CampaignDifficulty difficulty, int scientists, int engineers)
    {
        using var fixture = new Installation();
        var first = fixture.Load();
        var cached = fixture.Load();
        Assert.True(first.IsSuccess, first.DescribeFailure());
        Assert.True(cached.IsSuccess, cached.DescribeFailure());
        Assert.Equal(CompiledContentCacheStatus.Hit, cached.CacheStatus);
        foreach (var content in new[] { first.Content!, cached.Content! })
        {
            var campaign = Create(content, difficulty);
            AssertCounts(campaign, scientists, engineers);
            var path = Path.Combine(fixture.Root, "campaign.sav");
            OxceSaveAdapter.WriteNewCampaignAtomic(path, campaign.Capture(),
                cancellationToken: TestContext.Current.CancellationToken);
            var loaded = OxceSaveAdapter.LoadFile(path, content, new SplitMix64RandomSource(0),
                new OxceSaveLoadOptions("runtime-master", new HashSet<string> { "runtime-master", "runtime-addon" }));
            AssertCounts(loaded.Campaign, scientists, engineers);
            Assert.Equivalent(campaign.Capture(), loaded.Campaign.Capture(), strict: true);
        }
    }

    [Fact]
    public void LaterModOverlayRetainsOmittedCountAndHonorsExplicitZero()
    {
        using var fixture = new Installation();
        Assert.True(fixture.Load().IsSuccess);
        fixture.Overlay("startingBaseVeteran:\n  scientists: 0\n");
        var changed = fixture.Load();
        Assert.True(changed.IsSuccess, changed.DescribeFailure());
        Assert.Equal(CompiledContentCacheStatus.Rejected, changed.CacheStatus);
        AssertCounts(Create(changed.Content!, CampaignDifficulty.Veteran), 0, 60);
        AssertCounts(Create(fixture.Load().Content!, CampaignDifficulty.Veteran), 0, 60);
    }

    [Theory]
    [InlineData("scientists", "invalid")]
    [InlineData("engineers", "1.5")]
    [InlineData("scientists", "[]")]
    [InlineData("engineers", "{}")]
    public void MalformedCountCannotReuseSuccessfulCache(string field, string value)
    {
        using var fixture = new Installation();
        Assert.True(fixture.Load().IsSuccess);
        fixture.Overlay($"startingBaseVeteran:\n  {field}: {value}\n");
        var result = fixture.Load();
        Assert.False(result.IsSuccess);
        Assert.Null(result.Content);
        Assert.NotNull(result.Failure);
    }

    [Theory]
    [InlineData("scientists", "-1")]
    [InlineData("engineers", "-1")]
    [InlineData("engineers", "2147483648")]
    public void NegativeCountCannotPublishCampaign(string field, string value)
    {
        using var fixture = new Installation();
        fixture.Overlay($"startingBaseVeteran:\n  {field}: {value}\n");
        var result = fixture.Load();
        Assert.True(result.IsSuccess, result.DescribeFailure());
        Assert.Throws<InvalidDataException>(() => Create(result.Content!, CampaignDifficulty.Veteran));
    }

    private static CampaignState Create(RuntimeContent content, CampaignDifficulty difficulty) =>
        CampaignFactory.Create(content,
            new NewCampaignRequest(new CampaignId(Guid.Parse("11111111-2222-3333-4444-555555555555")),
                "Personnel", "runtime-master", ["runtime-master", "runtime-addon"], difficulty),
            new SplitMix64RandomSource(42), new FixedClock());

    private static void AssertCounts(CampaignState campaign, int scientists, int engineers)
    {
        var snapshot = Assert.Single(campaign.Capture().Bases);
        Assert.Equal(scientists, snapshot.Scientists);
        Assert.Equal(engineers, snapshot.Engineers);
        var query = Assert.Single(campaign.QueryOverview().Bases);
        Assert.Equal(scientists, query.Scientists);
        Assert.Equal(engineers, query.Engineers);
    }

    private sealed class FixedClock : ICampaignClock
    {
        public DateTimeOffset UtcNow => DateTimeOffset.UnixEpoch;
    }

    private sealed class Installation : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), $"oxce-starting-personnel-{Guid.NewGuid():N}");

        public Installation()
        {
            TestFixtures.CopyDirectory(TestFixtures.PublicModsPath("runtime-rule-linking"), Path.Combine(Root, "standard"));
            Directory.CreateDirectory(Path.Combine(Root, "user", "mods"));
            File.Copy(TestFixtures.RepositoryPath("fixtures", "public", "savegames", "starting-personnel.rul"),
                Path.Combine(Root, "standard", "runtime-master", "Ruleset", "zz-personnel.rul"));
        }

        public void Overlay(string yaml) => File.WriteAllText(
            Path.Combine(Root, "standard", "runtime-addon", "Ruleset", "zzz-personnel.rul"), yaml);

        public InstallationContentLoadResult Load() => InstallationContentLoader.Load(
            InstallationLoadRequest.ForMasterAndAddOn(Root, "runtime-master", "runtime-addon", TestFixtures.Engine),
            cancellationToken: TestContext.Current.CancellationToken);

        public void Dispose() => Directory.Delete(Root, recursive: true);
    }
}

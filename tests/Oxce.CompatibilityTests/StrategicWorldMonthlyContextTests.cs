using System.Text.Json;
using Oxce.Core.Random;
using Oxce.FixtureSupport;
using Oxce.Gameplay.Campaigns;
using Oxce.TestSupport;
using Xunit;
using static Oxce.CompatibilityTests.StrategicWorldTestSupport;

namespace Oxce.CompatibilityTests;

public sealed class StrategicWorldMonthlyContextTests
{
    [Fact]
    public void ScoreAndEligibleFundsMatchExtractedReference()
    {
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-world-monthly-context");
        foreach (var row in TestFixtures.Rows(oracle.RootElement, "rows"))
        {
            var actual = WorldMonthlySchedulingMath.Calculate(row[0].GetInt32(), row[1].GetInt32(),
                row[2].EnumerateArray().Select(static value => value.GetInt32()).ToArray(),
                row[3].EnumerateArray().Select(static value => value.GetInt32()).ToArray(),
                row[4].GetInt64(), row[5].GetInt32(), row[6].GetInt32(), row[7].GetDouble(),
                row[8].GetBoolean());
            var encoded = JsonSerializer.SerializeToUtf8Bytes(new object[] { actual.Score, actual.Funds });
            var expected = JsonSerializer.SerializeToUtf8Bytes(new object[] { row[9].GetInt32(), row[10].GetInt64() });
            Assert.Equal(CanonicalJson.Normalize(expected), CanonicalJson.Normalize(encoded));
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CampaignContextReadsLiveHistoriesAndFullBaseMaintenance(bool fromCache)
    {
        TestFixtures.LoadVerifiedManifest("strategic-world-monthly-context");
        var content = StrategicReadinessTestContent.Load(
            ["strategic-world.rul", "strategic-world-monthly-context.rul"], fromCache);
        Assert.Equal(0.5, content.RuntimeRules.Campaign.PerformanceBonusFactor);
        var snapshot = CreateSnapshot(content, "Monthly world context") with
        {
            MonthsPassed = 2,
            Funds = [1000],
            ResearchScores = [30],
        };
        snapshot = snapshot with
        {
            Regions = [.. snapshot.Regions.Select((region, index) => region with
            {
                ActivityXcom = [index == 0 ? 100 : 0],
                ActivityAlien = [index == 0 ? 20 : 0],
            })],
            Countries = [.. snapshot.Countries.Select((country, index) => country with
            {
                Funding = [index == 0 ? 500 : 0],
            })],
        };
        var campaign = CampaignState.Restore(snapshot, content, new SplitMix64RandomSource(17));
        var maintenance = campaign.QueryBaseManagement(snapshot.Bases[0].Id).Maintenance;
        var expectedMaintenance = maintenance.Total;

        Assert.Equal(new WorldSchedulingInputs(510, 1000 + 500 + 255 - expectedMaintenance),
            campaign.WorldSchedulingInputs(isNewMonth: true));
        Assert.Equal(new WorldSchedulingInputs(510, 1000), campaign.WorldSchedulingInputs(isNewMonth: false));
        Assert.Equivalent(snapshot, campaign.Capture(), strict: true);

        campaign = Reload(content, campaign, 17, "monthly-world-context.sav");
        Assert.Equal(new WorldSchedulingInputs(510, 1000 + 500 + 255 - expectedMaintenance),
            campaign.WorldSchedulingInputs(isNewMonth: true));
    }

    [Fact]
    public void MalformedContextFailsIntentionally()
    {
        Assert.Throws<InvalidDataException>(() => WorldMonthlySchedulingMath.Calculate(
            0, 0, [1], [], 0, 0, 0, 0, true));
        Assert.Throws<InvalidDataException>(() => WorldMonthlySchedulingMath.Calculate(
            0, 0, [], [], 0, 0, 0, double.NaN, true));
        Assert.Throws<InvalidDataException>(() => WorldMonthlySchedulingMath.Calculate(
            0, int.MaxValue, [], [], 0, 0, 0, 2, true));
        Assert.Throws<OverflowException>(() => WorldMonthlySchedulingMath.Calculate(
            0, 0, [], [], long.MaxValue, 1, 0, 0, true));
    }
}

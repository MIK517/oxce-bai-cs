using System.Text;
using System.Text.Json;
using Oxce.FixtureSupport;
using Oxce.Gameplay.Campaigns.World;
using Oxce.Mods.Rulesets.CampaignStart;
using Oxce.TestSupport;
using Xunit;

namespace Oxce.CompatibilityTests;

public sealed class StrategicMissionSiteScriptTests
{
    [Fact]
    public void OrderedSiteCandidatesMatchPinnedPointRepeatAndRectangleRules()
    {
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-mission-site-candidates");
        foreach (var row in TestFixtures.Rows(oracle.RootElement, "rows"))
        {
            var direct = row[0].GetBoolean();
            var site = row[1].GetBoolean();
            var occupied = row[2].GetInt32();
            var repeat = row[3].GetInt32();
            var zone = row[4].GetInt32();
            var strategy = new AlienStrategyState();
            if ((repeat & 1) != 0) strategy.AddMissionLocation("RECENT", "REGION", 0, 10);
            if ((repeat & 2) != 0) strategy.AddMissionLocation("RECENT", "REGION_SECOND", 0, 10);
            MissionZone[] first = [new([Area(true, 0), Area(false, 0), Area(false, -1), Area(true, 0)]), new([])];
            MissionZone[] second = [new([Area(true, 0), Area(false, 0)])];
            WorldSiteScriptRegion[] regions =
                [new("REGION", first, (occupied & 1) != 0), new("REGION_SECOND", second, (occupied & 2) != 0)];
            IReadOnlyList<WorldSiteScriptCandidate> candidates = [];
            if (row[5].GetBoolean())
                Assert.Throws<InvalidDataException>(() =>
                    WorldMissionSiteSelection.Candidates(regions, zone, direct, site, strategy, "RECENT"));
            else candidates = WorldMissionSiteSelection.Candidates(regions, zone, direct, site, strategy, "RECENT");
            var actual = JsonSerializer.SerializeToUtf8Bytes(new object[]
            {
                direct, site, occupied, repeat, zone, row[5].GetBoolean(),
                candidates.Select(candidate => new object[] { candidate.Region, candidate.Area }).ToArray(),
            });
            Assert.Equal(CanonicalJson.Normalize(Encoding.UTF8.GetBytes(row.GetRawText())), CanonicalJson.Normalize(actual));
        }

        static MissionArea Area(bool point, int texture) => new(0.1, point ? 0.1 : 0.2, 0.1, 0.1, texture, "");
    }
}

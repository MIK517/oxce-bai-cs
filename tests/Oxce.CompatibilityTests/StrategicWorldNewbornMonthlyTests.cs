using System.Text;
using System.Text.Json;
using Oxce.Core.Random;
using Oxce.FixtureSupport;
using Oxce.Gameplay.Campaigns;
using Oxce.Gameplay.Campaigns.World;
using Oxce.TestSupport;
using Xunit;
using static Oxce.CompatibilityTests.StrategicWorldTestSupport;

namespace Oxce.CompatibilityTests;

public sealed class StrategicWorldNewbornMonthlyTests
{
    [Fact]
    public void OperationBirthsUpgradeAndEvolveInTheSameMonthlyCall()
    {
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-world-newborn-monthly");
        foreach (var row in TestFixtures.Rows(oracle.RootElement, "rows"))
        {
            var name = row[0].GetString()!;
            var extra = "alienMissions: [{type: NEWBORN_MISSION, operationBaseType: NEWBORN_" + name + "}]";
            if (name == "evolution")
                extra += "\nalienDeployments: [{type: NEWBORN_TARGET, upgradeRace: RACE_B, alienRaceEvolution: [[3, RACE_B, RACE_C]]}]";
            if (name == "loop")
                extra += "\nalienDeployments: [{type: NEWBORN_TARGET, alienRaceEvolution: [[3, RACE_A, RACE_A]]}]";
            var content = StrategicReadinessTestContent.Load(
                ["strategic-world.rul", "strategic-world-newborn-monthly.rul"], extra);
            var snapshot = CreateSnapshot(content, "Newborn monthly bases") with { MonthsPassed = 3, RandomState = 17 };
            var campaign = CampaignState.Restore(snapshot, content, new SplitMix64RandomSource(17));
            var result = campaign.Execute(new ScheduleWorldScripts());
            if (row[5].GetInt32() != 0)
            {
                Assert.Contains("100-transition", Assert.IsType<CampaignActionBlocked>(Assert.Single(result.Events)).Reason);
                Assert.Equivalent(snapshot, campaign.Capture(), strict: true);
                continue;
            }
            Assert.Empty(result.Events);
            var after = campaign.Capture();
            var born = Assert.Single(after.World.AlienBases);
            var marker = content.RuntimeRules.AlienDeployments[content.RuntimeRules.AlienDeployments.GetRequired(born.DeploymentId)].Value.MarkerName;
            var actual = JsonSerializer.SerializeToUtf8Bytes(new object[]
            { name, born.DeploymentId, born.Race, born.StartMonth, born.MinutesSinceLastHuntMissionGeneration, 0, marker });
            Assert.Equal(CanonicalJson.Normalize(Encoding.UTF8.GetBytes(row.GetRawText())), CanonicalJson.Normalize(actual));
            var link = Assert.Single(after.World.Missions).AlienBase!;
            Assert.Equal(marker, link.TypeId);
            Assert.Equal(born.Id, link.Id);
            campaign = Reload(content, campaign, 17, "newborn-monthly-base.sav");
            Assert.Equivalent(after, campaign.Capture(), strict: true);
        }
    }
}

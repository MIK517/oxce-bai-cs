using Oxce.Core.Random;
using Oxce.Gameplay.Campaigns;
using Oxce.Gameplay.Campaigns.World;
using Oxce.TestSupport;
using Xunit;
using static Oxce.CompatibilityTests.StrategicWorldTestSupport;

namespace Oxce.CompatibilityTests;

public sealed class StrategicWorldDetectionScriptsTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void BaseScriptDetectsOutsideRadarCoverageAndPersists(bool fromCache)
    {
        var content = StrategicReadinessTestContent.Load(["strategic-world.rul", "strategic-detection-scripts.rul"], fromCache);
        var campaign = CreateUfoTransitCampaign(content, HalfHour, "UFO_SCRIPT_BASE", 2200);

        var result = campaign.Execute(new AdvanceCampaignTime(1));

        Assert.DoesNotContain(result.Events, item => item is CampaignActionBlocked);
        Assert.Contains(result.Events, item => item is UfoContactDetected { Hyperwave: true });
        Assert.True(Assert.Single(campaign.Capture().World.Ufos).Detected);
        Assert.True(Assert.Single(campaign.Capture().World.Ufos).HyperDetected);
        Reload(content, campaign, 90, "detection-script.sav");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CraftScriptRunsOutsideRadarRangeAfterLaunch(bool fromCache)
    {
        var content = StrategicReadinessTestContent.Load(["strategic-world.rul", "strategic-detection-scripts.rul"], fromCache);
        var snapshot = CreateUfoTransitCampaign(content, HalfHour, "UFO_SCRIPT_CRAFT", 2200).Capture()
            .WithCraft("SHIP", craft => craft with
            {
                Logistics = craft.Logistics! with
                {
                    Status = "STR_OUT",
                    Longitude = 0.2,
                    Latitude = 0.1,
                    Speed = 0,
                    Destination = null,
                },
            });
        var campaign = CampaignState.Restore(snapshot, content, new SplitMix64RandomSource(91));

        var result = campaign.Execute(new AdvanceCampaignTime(1));

        Assert.DoesNotContain(result.Events, item => item is CampaignActionBlocked);
        Assert.Contains(result.Events, item => item is UfoContactDetected { Hyperwave: false });
        Reload(content, campaign, 91, "craft-detection-script.sav");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ConditionalReadsRadarInputsBeforeFinalRoll(bool radar)
    {
        var content = StrategicReadinessTestContent.Load(["strategic-world.rul", "strategic-detection-scripts.rul"]);
        var snapshot = CreateUfoTransitCampaign(content, HalfHour, "UFO_SCRIPT_INPUTS", 2200).Capture();
        if (radar) snapshot = snapshot.WithFacility("RADAR_TEST");
        var campaign = CampaignState.Restore(snapshot, content, new SplitMix64RandomSource(92));

        var result = campaign.Execute(new AdvanceCampaignTime(1));

        Assert.DoesNotContain(result.Events, item => item is CampaignActionBlocked);
        Assert.Equal(radar, Assert.Single(campaign.Capture().World.Ufos).Detected);
    }

    [Theory]
    [InlineData("loop var i distance; end; return;", "Detection script requires a bounded loop forecast.")]
    [InlineData("div detection_chance distance; return;", "Detection script requires a nonzero divisor forecast.")]
    [InlineData("div detection_chance 0; return;", "Detection script requires a nonzero divisor forecast.")]
    [InlineData("ufo.Stats.getArmor detection_chance; return;", "Detection script requires unsupported binding Ufo.Stats.getArmor.")]
    public void UnsafeScriptsBlockBeforeTickMutates(string code, string reason)
    {
        var content = StrategicReadinessTestContent.Load(["strategic-world.rul"], extraRules: $$"""
            ufos:
              - type: UFO_SCRIPT_UNSAFE
                speedMax: 2200
                damageMax: 50
                scripts:
                  detectUfoFromBase: "{{code}}"
            """);
        var campaign = CreateUfoTransitCampaign(content, HalfHour, "UFO_SCRIPT_UNSAFE", 2200);

        AssertTimeBlocked(campaign, reason);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void GlobalScriptsRunInOrderWithoutRuleScript(bool fromCache)
    {
        var content = StrategicReadinessTestContent.Load(["strategic-world.rul"], fromCache, """
            extended:
              scripts:
                detectUfoFromBase:
                  - new: before-detection
                    offset: -1
                    code: set detection_chance 90; return;
                  - new: after-detection
                    offset: 1
                    code: add detection_chance 10; set detection_type DETECTION_HYPERWAVE; return;
            """);
        var campaign = CreateUfoTransitCampaign(content, HalfHour, "UFO_SCOUT", 2200);

        var result = campaign.Execute(new AdvanceCampaignTime(1));

        Assert.DoesNotContain(result.Events, item => item is CampaignActionBlocked);
        Assert.Contains(result.Events, item => item is UfoContactDetected { Hyperwave: true });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CombinedContactScriptWorkBlocksBeforeMutation(bool fromCache)
    {
        var code = string.Concat(Enumerable.Repeat("add detection_chance 0; ", 5000)) + "return;";
        var content = StrategicReadinessTestContent.Load(["strategic-world.rul"], fromCache, $$"""
            ufos:
              - type: UFO_SCRIPT_WORK
                speedMax: 2200
                damageMax: 50
                scripts:
                  detectUfoFromBase: "{{code}}"
            """);
        var snapshot = CreateUfoTransitCampaign(content, HalfHour, "UFO_SCRIPT_WORK", 2200).Capture();
        snapshot = snapshot with
        {
            World = snapshot.World with
            {
                Missions = [snapshot.World.Missions[0] with { LiveUfos = 205 }],
                Ufos = Enumerable.Range(1, 205).Select(id => snapshot.World.Ufos[0] with
                {
                    UniqueId = id,
                    Id = id,
                }).ToArray(),
            },
        };
        var campaign = CampaignState.Restore(snapshot, content, new SplitMix64RandomSource(96));

        AssertTimeBlocked(campaign, "Detection script work exceeds the supported half-hour limit.");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void GlobalEventProgramsShareContactWorkLimit(bool fromCache)
    {
        var code = string.Concat(Enumerable.Repeat("add detection_chance 0; ", 4000)) + "return;";
        var content = StrategicReadinessTestContent.Load(["strategic-world.rul"], fromCache, $$"""
            extended:
              scripts:
                detectUfoFromBase:
                  - new: before-work
                    offset: -1
                    code: "{{code}}"
                  - new: after-work
                    offset: 1
                    code: "{{code}}"
            """);
        var snapshot = CreateUfoTransitCampaign(content, HalfHour, "UFO_SCOUT", 2200).Capture();
        snapshot = snapshot with
        {
            World = snapshot.World with
            {
                Missions = [snapshot.World.Missions[0] with { LiveUfos = 128 }],
                Ufos = Enumerable.Range(1, 128).Select(id => snapshot.World.Ufos[0] with { UniqueId = id, Id = id }).ToArray(),
            },
        };
        var campaign = CampaignState.Restore(snapshot, content, new SplitMix64RandomSource(97));

        AssertTimeBlocked(campaign, "Detection script work exceeds the supported half-hour limit.");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void LiteralLoopsAndControlFlowMatchThePinnedVmOracle(bool fromCache)
    {
        using var oracle = TestFixtures.ReadVerifiedExpected("script-core");
        foreach (var row in TestFixtures.Rows(oracle.RootElement, "cases").Where(row =>
            row.GetProperty("name").GetString() is "counted-loop" or "loop-control"))
        {
            var expected = row.GetProperty("result").GetInt32();
            var code = row.GetProperty("source").GetString()!.Replace("result", "detection_chance", StringComparison.Ordinal)
                .Replace("return detection_chance;", $"if eq detection_chance {expected}; return DETECTION_RADAR 100; end; return DETECTION_NONE 0;", StringComparison.Ordinal);
            var content = StrategicReadinessTestContent.Load(["strategic-world.rul"], fromCache, $$"""
                ufos:
                  - type: UFO_LITERAL_LOOP
                    speedMax: 2200
                    damageMax: 50
                    scripts:
                      detectUfoFromBase: "{{code}}"
                """);
            var campaign = CreateUfoTransitCampaign(content, HalfHour, "UFO_LITERAL_LOOP", 2200);

            AdvanceUnblocked(campaign, 1);

            Assert.True(Assert.Single(campaign.Capture().World.Ufos).Detected);
            Reload(content, campaign, 110, "literal-loop-detection.sav");
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-10)]
    [InlineData(4)]
    public void NestedLoopsIgnoreVisibleCounterMutationAndZeroBounds(int limit)
    {
        var expected = Math.Max(0, limit) * 6;
        var content = StrategicReadinessTestContent.Load(["strategic-world.rul"], extraRules: $$"""
            ufos:
              - type: UFO_NESTED_LOOP
                speedMax: 2200
                damageMax: 50
                scripts:
                  detectUfoFromBase: |
                    var int sum;
                    loop var outer {{limit}};
                      set outer -100;
                      loop var inner 4;
                        add sum inner;
                        set inner 10000;
                      end;
                    end;
                    if eq sum {{expected}}; return DETECTION_RADAR 100; end;
                    return DETECTION_NONE 0;
            """);
        var campaign = CreateUfoTransitCampaign(content, HalfHour, "UFO_NESTED_LOOP", 2200);

        AdvanceUnblocked(campaign, 1);

        Assert.True(Assert.Single(campaign.Capture().World.Ufos).Detected);
    }

    [Theory]
    [InlineData(300000)]
    [InlineData(2147483647)]
    public void OversizedLiteralLoopStopsBeforeMutation(int limit)
    {
        var content = StrategicReadinessTestContent.Load(["strategic-world.rul"], extraRules: $$"""
            ufos:
              - type: UFO_OVERSIZED_LOOP
                speedMax: 2200
                damageMax: 50
                scripts:
                  detectUfoFromBase: "loop var i {{limit}}; add detection_chance 0; end; return;"
            """);
        var campaign = CreateUfoTransitCampaign(content, HalfHour, "UFO_OVERSIZED_LOOP", 2200);

        AssertTimeBlocked(campaign, "Detection script exceeds the supported instruction limit.");
    }

    private static CampaignTime HalfHour => new(1, 1, 1, 1999, 1, 29, 55);
}

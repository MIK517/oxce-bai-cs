using Oxce.Core.Random;
using Oxce.Gameplay.Campaigns;
using Oxce.TestSupport;
using Xunit;
using static Oxce.CompatibilityTests.StrategicWorldTestSupport;

namespace Oxce.CompatibilityTests;

/// <summary>Read-only Unit metadata from Mod::getUnitScript and Unit::ScriptRegister.</summary>
public sealed class StrategicWorldDetectionUnitRulesTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void UnitLookupAndTypeTextMatchNativeBindings(bool fromCache, bool fromCraft)
    {
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-detection-scripts");
        var code = "var ptr RuleUnit rule; var ptr RuleMod absent null; var text name; ";
        foreach (var row in TestFixtures.Rows(oracle.RootElement, "unitLookup"))
        {
            var id = row.GetProperty("name").GetString()!;
            if (id == "NUL") continue;
            var source = row.GetProperty("source").GetBoolean() ? "rules" : "absent";
            code += $"{source}.getRuleUnit rule \"{id}\"; ";
            code += row.GetProperty("found").GetBoolean()
                ? "if eq rule null; return DETECTION_NONE 0; end; "
                : "if neq rule null; return DETECTION_NONE 0; end; ";
            code += $"rule.getType name; if neq name \"{row.GetProperty("type").GetString()}\"; return DETECTION_NONE 0; end; ";
        }
        code += "return DETECTION_RADAR 100;";
        var parser = fromCraft ? "detectUfoFromCraft" : "detectUfoFromBase";
        var content = StrategicReadinessTestContent.Load(["strategic-world.rul"], fromCache, $$"""
            units:
              - type: UNIT_METADATA
                armor: ARMOR
              - type: STR_NONE
                armor: ARMOR
            ufos:
              - type: UFO_UNIT_METADATA
                speedMax: 2200
                damageMax: 50
                scripts:
                  {{parser}}: '{{code}}'
            """);
        var snapshot = CreateUfoTransitCampaign(content, new CampaignTime(1, 1, 1, 1999, 1, 29, 55), "UFO_UNIT_METADATA", 2200).Capture();
        if (fromCraft)
            snapshot = snapshot.WithCraft("SHIP", craft => craft with
            {
                Logistics = craft.Logistics! with { Status = "STR_OUT", Speed = 0, Destination = null },
            });
        var campaign = CampaignState.Restore(snapshot, content, new SplitMix64RandomSource(211));
        campaign = Reload(content, campaign, 211, "unit-lookup-before.sav");
        AdvanceUnblocked(campaign, 1);
        Assert.True(Assert.Single(campaign.Capture().World.Ufos).Detected);
        Reload(content, campaign, 211, "unit-lookup-after.sav");
        var unit = content.RuntimeRules.Units[content.RuntimeRules.Units.GetRequired("UNIT_METADATA")].Value;
        Assert.Equal("UNIT_METADATA", unit.Type);
        var other = StrategicReadinessTestContent.Load(["strategic-world.rul"], false, """
            units:
              - type: UNIT_METADATA
                armor: ARMOR
            """);
        Assert.Throws<InvalidOperationException>(() => content.RuntimeRules.Units.Validate(
            other.RuntimeRules.Units.GetRequired("UNIT_METADATA")));
    }
}

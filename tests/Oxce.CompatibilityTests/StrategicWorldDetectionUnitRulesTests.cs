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
    [InlineData(false)]
    [InlineData(true)]
    public void SmokeReductionMatchesNativeSignedArithmetic(bool fromCraft)
    {
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-detection-scripts");
        var rows = TestFixtures.Rows(oracle.RootElement, "smokeReduction");
        foreach (var group in rows.GroupBy(row => row[0].GetInt32()))
        {
            var code = "var int smoke; ";
            foreach (var row in group)
                code += $"set smoke {row[1].GetInt32()}; rules.getSmokeReduction smoke; if neq smoke {row[2].GetInt32()}; return DETECTION_NONE 0; end; ";
            code += "return DETECTION_RADAR 100;";
            var parser = fromCraft ? "detectUfoFromCraft" : "detectUfoFromBase";
            var content = StrategicReadinessTestContent.Load(["strategic-world.rul"], $$"""
                maxViewDistance: {{group.Key}}
                ufos:
                  - type: UFO_SMOKE_READ
                    speedMax: 2200
                    damageMax: 50
                    scripts:
                      {{parser}}: '{{code}}'
                """);
            var snapshot = CreateUfoTransitCampaign(content, new CampaignTime(1, 1, 1, 1999, 1, 29, 55), "UFO_SMOKE_READ", 2200).Capture();
            if (fromCraft)
                snapshot = snapshot.WithCraft("SHIP", craft => craft with
                {
                    Logistics = craft.Logistics! with { Status = "STR_OUT", Speed = 0, Destination = null },
                });
            var campaign = CampaignState.Restore(snapshot, content, new SplitMix64RandomSource(213));
            AdvanceUnblocked(campaign, 1);
            Assert.True(Assert.Single(campaign.Capture().World.Ufos).Detected);
            Reload(content, campaign, 213, "smoke-reduction-after.sav");
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SmokeQueriesAcceptProvenAliasesAndRepairNullBranches(bool fromCraft)
    {
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-detection-scripts");
        foreach (var row in TestFixtures.Rows(oracle.RootElement, "smokeReduction"))
        {
            var prefix = "var ptr RuleMod original rules; var ptr RuleMod alias original; " +
                $"var int smoke {row[1].GetInt32()}; ";
            var query = $"alias.getSmokeReduction smoke; if neq smoke {row[2].GetInt32()}; " +
                "return DETECTION_NONE 0; end; return DETECTION_RADAR 100;";
            AssertDetectionReadScript(prefix + "set alias original; " + query, fromCraft,
                $"maxViewDistance: {row[0].GetInt32()}");
        }
        AssertDetectionReadScript("var ptr RuleMod alias rules; var int smoke 15; " +
            "if eq already_tracked 0; set alias null; end; " +
            "if eq alias null; set alias rules; end; alias.getSmokeReduction smoke; " +
            "if neq smoke 5; return DETECTION_NONE 0; end; return DETECTION_RADAR 100;",
            fromCraft, "maxViewDistance: 20");
        AssertDetectionReadScript("var ptr RuleMod alias null; var int smoke 15; " +
            "if neq alias null; alias.getSmokeReduction smoke; end; return DETECTION_RADAR 100;",
            fromCraft);
    }

    [Theory]
    [InlineData("var ptr RuleMod alias rules; if eq already_tracked 0; set alias null; end; alias.getSmokeReduction detection_chance; return;")]
    [InlineData("var ptr RuleMod alias rules; loop var i 2; alias.getSmokeReduction detection_chance; set alias null; end; return;")]
    public void PossibleNullAliasesStillStopBeforeMutation(string code)
    {
        var content = StrategicReadinessTestContent.Load(["strategic-world.rul"], extraRules: $$"""
            ufos:
              - type: UFO_SMOKE_ALIAS_GUARD
                speedMax: 2200
                damageMax: 50
                scripts:
                  detectUfoFromBase: '{{code}}'
            """);
        AssertTimeBlocked(CreateUfoTransitCampaign(content,
            new CampaignTime(1, 1, 1, 1999, 1, 29, 55), "UFO_SMOKE_ALIAS_GUARD", 2200),
            "Detection smoke reduction requires a guaranteed Mod receiver.");
    }

    [Fact]
    public void ModReceiverForecastStopsAtItsWorkBudget()
    {
        var content = StrategicReadinessTestContent.Load(["strategic-world.rul"]);
        var program = Oxce.Scripting.Compilation.ScriptCompiler.Compile("rules.getSmokeReduction detection_chance; return;",
            Oxce.Scripting.Compilation.ScriptParserDefinition.FromCatalog("detectUfoFromBase")).Program!;
        Assert.Equal("Detection Mod receiver forecast exceeds the supported work limit.",
            Oxce.Gameplay.Campaigns.World.WorldDetectionModSafety.UnavailableReason(program, content.RuntimeRules,
                Enumerable.Repeat(1, program.Instructions.Count).ToArray(), 1));
    }

    [Theory]
    [InlineData("var ptr RuleMod absent null; absent.getSmokeReduction detection_chance; return;")]
    [InlineData("var ptr RuleMod absent; absent.getSmokeReduction detection_chance; return;")]
    public void UnsafeSmokeReceiverStopsBeforeTickMutation(string code)
    {
        var content = StrategicReadinessTestContent.Load(["strategic-world.rul"], $$"""
            ufos:
              - type: UFO_UNSAFE_SMOKE
                speedMax: 2200
                damageMax: 50
                scripts:
                  detectUfoFromBase: '{{code}}'
            """);
        var campaign = CreateUfoTransitCampaign(content, new CampaignTime(1, 1, 1, 1999, 1, 29, 55), "UFO_UNSAFE_SMOKE", 2200);
        AssertTimeBlocked(campaign, "Detection smoke reduction requires a guaranteed Mod receiver.");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void LinkedUnitMetadataMatchesNativeNullableAndRecoverySemantics(bool fromCraft)
    {
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-detection-scripts");
        foreach (var row in TestFixtures.Rows(oracle.RootElement, "unitLinks"))
        {
            var scenario = row.GetProperty("case").GetString()!;
            var id = row.GetProperty("type").GetString()!;
            var fields = scenario switch
            {
                "default" or "missing-default" => "",
                "null" => "liveAlien: null\n    civilianRecoveryType: null",
                "empty" => "liveAlien: \"\"",
                "none" => "liveAlien: STR_NONE",
                "explicit" => "liveAlien: LIVE_ITEM\n    civilianRecoveryType: RECOVERY\n    spawnUnit: UNIT_METADATA",
                "literal" => "liveAlien: STR_NULL\n    civilianRecoveryType: STR_NULL",
                "engineer" => "liveAlien: \"\"\n    civilianRecoveryType: STR_ENGINEER",
                "scientist" => "liveAlien: \"\"\n    civilianRecoveryType: STR_SCIENTIST",
                "soldier" => "liveAlien: \"\"\n    civilianRecoveryType: COLLISION",
                _ => throw new InvalidOperationException("Unknown native Unit linkage case."),
            };
            var code = $"var ptr RuleUnit unit; var ptr RuleUnit absent null; var ptr RuleUnit spawned; var ptr RuleItem item; var text name; rules.getRuleUnit unit \"{id}\"; ";
            code += $"unit.getSpawnUnit spawned; spawned.getType name; if neq name \"{row.GetProperty("spawn").GetString()}\"; return DETECTION_NONE 0; end; ";
            code += $"unit.getLiveAlienGeoscape item; item.getType name; if neq name \"{row.GetProperty("live").GetString()}\"; return DETECTION_NONE 0; end; ";
            code += $"unit.getCivilianRecoveryItemType item; item.getType name; if neq name \"{row.GetProperty("recovery").GetString()}\"; return DETECTION_NONE 0; end; ";
            code += "absent.getSpawnUnit spawned; if neq spawned null; return DETECTION_NONE 0; end; absent.getLiveAlienGeoscape item; if neq item null; return DETECTION_NONE 0; end; absent.getCivilianRecoveryItemType item; if neq item null; return DETECTION_NONE 0; end; return DETECTION_RADAR 100;";
            var parser = fromCraft ? "detectUfoFromCraft" : "detectUfoFromBase";
            var content = StrategicReadinessTestContent.Load(["strategic-world.rul"], $$"""
                items:
                  - type: UNIT_METADATA
                    battleType: 0
                  - type: LIVE_ITEM
                    battleType: 0
                  - type: STR_NULL
                    battleType: 0
                  - type: RECOVERY
                    battleType: 0
                  - type: COLLISION
                    battleType: 0
                soldiers:
                  - type: COLLISION
                    armor: ARMOR
                units:
                  - type: {{id}}
                    armor: ARMOR
                    {{fields}}
                ufos:
                  - type: UFO_UNIT_LINKS
                    speedMax: 2200
                    damageMax: 50
                    scripts:
                      {{parser}}: '{{code}}'
                """);
            var snapshot = CreateUfoTransitCampaign(content, new CampaignTime(1, 1, 1, 1999, 1, 29, 55), "UFO_UNIT_LINKS", 2200).Capture();
            if (fromCraft)
                snapshot = snapshot.WithCraft("SHIP", craft => craft with
                {
                    Logistics = craft.Logistics! with { Status = "STR_OUT", Speed = 0, Destination = null },
                });
            var campaign = CampaignState.Restore(snapshot, content, new SplitMix64RandomSource(212));
            campaign = Reload(content, campaign, 212, "unit-links-before.sav");
            AdvanceUnblocked(campaign, 1);
            Assert.True(Assert.Single(campaign.Capture().World.Ufos).Detected);
            Reload(content, campaign, 212, "unit-links-after.sav");
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void UnitLookupAndTypeTextMatchNativeBindings(bool fromCraft)
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
        var content = StrategicReadinessTestContent.Load(["strategic-world.rul"], $$"""
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
        var other = StrategicReadinessTestContent.Load(["strategic-world.rul"], """
            units:
              - type: UNIT_METADATA
                armor: ARMOR
            """);
        Assert.Throws<InvalidOperationException>(() => content.RuntimeRules.Units.Validate(
            other.RuntimeRules.Units.GetRequired("UNIT_METADATA")));
    }
}

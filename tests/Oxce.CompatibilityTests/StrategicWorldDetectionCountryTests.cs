using Oxce.Core.Random;
using Oxce.Gameplay.Campaigns;
using Oxce.TestSupport;
using Xunit;
using static Oxce.CompatibilityTests.StrategicWorldTestSupport;

namespace Oxce.CompatibilityTests;

public sealed class StrategicWorldDetectionCountryTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void OrderedCountryLoopsReadCurrentHistoriesAcrossCacheAndReload(bool fromCache, bool craftOnly)
    {
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-detection-scripts");
        var rows = TestFixtures.Rows(oracle.RootElement, "countryList");
        var code = "var int count; var int value; var ptr Country absent null; " +
            "absent.getCurrentFunding value; if neq value 0; return DETECTION_NONE 0; end; " +
            "loop var country geoscape_game.getCountries.list; ";
        for (var index = 0; index < 2; index++)
        {
            code += $"if eq count {index}; ";
            code += $"country.getCurrentFunding value; if neq value {rows[index][2].GetInt32()}; return DETECTION_NONE 0; end; ";
            code += $"country.getCurrentActivityAlien value; if neq value {rows[index][3].GetInt32()}; return DETECTION_NONE 0; end; ";
            code += $"country.getCurrentActivityXcom value; if neq value {rows[index][4].GetInt32()}; return DETECTION_NONE 0; end; ";
            code += $"country.getPact value; if neq value {rows[index][5].GetInt32()}; return DETECTION_NONE 0; end; end; ";
        }
        code += "add count 1; continue; end; if eq count 2; return DETECTION_RADAR 100; end; return DETECTION_NONE 0;";
        var content = StrategicReadinessTestContent.Load(["strategic-world.rul"], fromCache, $$"""
            countries:
              - type: ENUM_A
              - type: ENUM_B
            ufos:
              - type: UFO_COUNTRY_LOOP
                speedMax: 2200
                damageMax: 50
                scripts:
                  detectUfoFromBase: '{{(craftOnly ? "return DETECTION_NONE 0;" : code)}}'
                  detectUfoFromCraft: '{{code}}'
            """);
        var snapshot = CreateUfoTransitCampaign(content, HalfHour, "UFO_COUNTRY_LOOP", 2200).Capture().WithCraft("SHIP", craft => craft with
        {
            Logistics = craft.Logistics! with { Status = "STR_OUT", Speed = 0, Destination = null },
        });
        snapshot = snapshot with
        {
            Countries =
            [
                new("ENUM_B", [1, rows[0][2].GetInt32()], [2, rows[0][4].GetInt32()], [3, rows[0][3].GetInt32()], true, false, false, []),
                new("ENUM_A", [1, rows[1][2].GetInt32()], [2, rows[1][4].GetInt32()], [3, rows[1][3].GetInt32()], false, true, false, []),
            ],
        };
        var campaign = CampaignState.Restore(snapshot, content, new SplitMix64RandomSource(117));
        campaign = Reload(content, campaign, 117, "country-loop-before.sav");

        AdvanceUnblocked(campaign, 1);

        Assert.True(Assert.Single(campaign.Capture().World.Ufos).Detected);
        Reload(content, campaign, 117, "country-loop-after.sav");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EmptyAndNullCountryListsAndNestedLoopsRemainBounded(bool empty)
    {
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-detection-scripts");
        var rows = TestFixtures.Rows(oracle.RootElement, "countryList");
        Assert.Equal([0, 0, 0], rows[3].EnumerateArray().Select(item => item.GetInt32()).ToArray());
        var code = $$"""
            var int count;
            var ptr GeoscapeGame absent null;
            loop var ignored absent.getCountries.list; add count 100; end;
            loop var outer geoscape_game.getCountries.list;
              loop var inner geoscape_game.getCountries.list;
                add count 1;
              end;
            end;
            if eq count {{(empty ? 0 : 4)}}; return DETECTION_RADAR 100; end;
            return DETECTION_NONE 0;
            """;
        var content = StrategicReadinessTestContent.Load(["strategic-world.rul"], extraRules: $$"""
            countries:
              - type: ENUM_A
              - type: ENUM_B
            ufos:
              - type: UFO_COUNTRY_NESTED
                speedMax: 2200
                damageMax: 50
                scripts:
                  detectUfoFromBase: |
            {{string.Join("\n", code.Split('\n').Select(line => "        " + line))}}
            """);
        var snapshot = CreateUfoTransitCampaign(content, HalfHour, "UFO_COUNTRY_NESTED", 2200).Capture();
        snapshot = snapshot with
        {
            Countries = empty ? [] :
            [
                new("ENUM_A", [1], [0], [0], false, false, false, []),
                new("ENUM_B", [1], [0], [0], false, false, false, []),
            ],
        };
        var campaign = CampaignState.Restore(snapshot, content, new SplitMix64RandomSource(118));

        AdvanceUnblocked(campaign, 1);

        Assert.True(Assert.Single(campaign.Capture().World.Ufos).Detected);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CountryAndRuleTagsRetainOwnerAndFundingUnitsAcrossReload(bool fromCache)
    {
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-detection-scripts");
        foreach (var row in TestFixtures.Rows(oracle.RootElement, "countryRules"))
        {
            var cap = row[0].GetInt32();
            Assert.Equal(1, row[1].GetInt32());
            var content = StrategicReadinessTestContent.Load(["strategic-world.rul"], fromCache, $$"""
                extended:
                  tags:
                    Country: {DETECT_COUNTRY: int}
                    RuleCountry: {DETECT_COUNTRY_RULE: int}
                countries:
                  - type: ENUM_TAGGED
                    fundingCap: {{cap}}
                    tags: {DETECT_COUNTRY_RULE: 7}
                ufos:
                  - type: UFO_COUNTRY_TAGS
                    speedMax: 2200
                    damageMax: 50
                    scripts:
                      detectUfoFromBase: |
                        var int count;
                        var int value;
                        var ptr RuleCountry rule;
                        var ptr RuleCountry absent null;
                        var ptr Country missing null;
                        loop var country geoscape_game.getCountries.list;
                          country.getTag value Tag.DETECT_COUNTRY;
                          if neq value 37; return DETECTION_NONE 0; end;
                          country.getRuleCountry rule;
                          rule.getTag value Tag.DETECT_COUNTRY_RULE;
                          if neq value 7; return DETECTION_NONE 0; end;
                          rule.getFundingCap value;
                          if neq value {{cap}}; return DETECTION_NONE 0; end;
                          absent.getTag value Tag.DETECT_COUNTRY_RULE;
                          if neq value 0; return DETECTION_NONE 0; end;
                          absent.getFundingCap value;
                          if neq value 0; return DETECTION_NONE 0; end;
                          missing.getTag value Tag.DETECT_COUNTRY;
                          if neq value 0; return DETECTION_NONE 0; end;
                          missing.getRuleCountry rule;
                          if neq rule null; return DETECTION_NONE 0; end;
                          add count 1;
                        end;
                        if eq count 1; return DETECTION_RADAR 100; end;
                        return DETECTION_NONE 0;
                """);
            var snapshot = CreateUfoTransitCampaign(content, HalfHour, "UFO_COUNTRY_TAGS", 2200).Capture();
            snapshot = snapshot with
            {
                Countries = [new("ENUM_TAGGED", [1000], [0], [0], false, false, false, [new("DETECT_COUNTRY", "int", 37)])],
            };
            var campaign = CampaignState.Restore(snapshot, content, new SplitMix64RandomSource(119));
            campaign = Reload(content, campaign, 119, "country-tags-before.sav");

            AdvanceUnblocked(campaign, 1);

            Assert.True(Assert.Single(campaign.Capture().World.Ufos).Detected);
            Reload(content, campaign, 119, "country-tags-after.sav");
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CountryLoopVisitsShareWorkAcrossDetectionCallers(bool fromCache)
    {
        var content = StrategicReadinessTestContent.Load(["strategic-world.rul"], fromCache, """
            countries:
              - type: ENUM_A
              - type: ENUM_B
            ufos:
              - type: UFO_COUNTRY_WORK
                speedMax: 2200
                damageMax: 50
                scripts:
                  detectUfoFromBase: "loop var outer 60000; loop var country geoscape_game.getCountries.list; add detection_chance 0; end; end; return;"
            """);
        var snapshot = CreateUfoTransitCampaign(content, HalfHour, "UFO_COUNTRY_WORK", 2200).Capture();
        snapshot = snapshot with
        {
            Countries =
            [
                new("ENUM_A", [1], [0], [0], false, false, false, []),
                new("ENUM_B", [1], [0], [0], false, false, false, []),
            ],
            Bases = [snapshot.Bases[0], snapshot.Bases[0] with { Id = 2, Name = "Beta", Crafts = [], Soldiers = [] }],
        };
        var campaign = CampaignState.Restore(snapshot, content, new SplitMix64RandomSource(120));

        AssertTimeBlocked(campaign, "Detection script work exceeds the supported half-hour limit.");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ExplicitCountryIteratorCallsStopBeforeMutation(bool fromCache)
    {
        var content = StrategicReadinessTestContent.Load(["strategic-world.rul"], fromCache, """
            ufos:
              - type: UFO_COUNTRY_MANUAL
                speedMax: 2200
                damageMax: 50
                scripts:
                  detectUfoFromBase: |
                    var int current 2147483647;
                    var int limit 2147483647;
                    var ptr Country country;
                    geoscape_game.getCountries.list __ current limit __ country;
                    return;
            """);
        var campaign = CreateUfoTransitCampaign(content, HalfHour, "UFO_COUNTRY_MANUAL", 2200);

        AssertTimeBlocked(campaign, "Detection country enumeration requires a compiler-owned loop.");
    }

    private static CampaignTime HalfHour => new(1, 1, 1, 1999, 1, 29, 55);
}

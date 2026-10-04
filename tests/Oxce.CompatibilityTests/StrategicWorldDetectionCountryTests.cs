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

    private static CampaignTime HalfHour => new(1, 1, 1, 1999, 1, 29, 55);
}

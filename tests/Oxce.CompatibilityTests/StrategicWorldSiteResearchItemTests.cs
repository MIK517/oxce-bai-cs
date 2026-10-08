using Oxce.Core.Random;
using Oxce.Gameplay.Campaigns;
using Oxce.Mods.Rulesets.Content;
using Oxce.TestSupport;
using Xunit;
using static Oxce.CompatibilityTests.StrategicWorldTestSupport;

namespace Oxce.CompatibilityTests;

public sealed class StrategicWorldSiteResearchItemTests
{
    [Theory]
    [InlineData(false, 2)]
    [InlineData(true, 2)]
    [InlineData(false, 3)]
    [InlineData(true, 3)]
    public void AmmunitionSavingSelectsActualAutomaticResearchWithoutForecastRng(bool cache, int supply)
    {
        TestFixtures.LoadVerifiedManifest("strategic-hunting");
        var content = Content(cache, supply, """
            items:
              - {type: SUPPLY, clipSize: 3}
            craftWeapons:
              - {type: UNCERTAIN_GUN, ammoMax: 4, rearmRate: 6, bulletSaving: true, launcher: SUPPLY, clip: SUPPLY}
            """);
        var initial = Snapshot(content, "transfer", supply);
        initial = initial with { Bases = [initial.Bases[0] with { Transfers = [], Items = new Dictionary<string, int> { ["SUPPLY"] = supply } }] };
        initial = initial.WithCraft("SHIP_FUEL_ITEM", craft => craft with
        { RuleId = "SHIP", Logistics = craft.Logistics! with { Status = "STR_REARMING", Weapons = [new("UNCERTAIN_GUN", 0, Rearming: true)] } });
        var emptySeen = false;
        var remainingSeen = false;
        for (ulong seed = 1; seed <= 32; seed++)
        {
            var campaign = CampaignState.Restore(initial with { RandomState = seed }, content, new SplitMix64RandomSource(seed));
            var control = CampaignState.Restore(initial with { RandomState = seed, World = initial.World with { MissionSites = [] } }, content, new SplitMix64RandomSource(seed));
            AdvanceUnblocked(campaign, 1);
            AdvanceUnblocked(control, 1);
            var after = campaign.Capture();
            var remaining = after.Bases[0].Items.GetValueOrDefault("SUPPLY");
            Assert.InRange(remaining, supply - 2, supply - 1);
            Assert.Equal(remaining != 0, after.CompletedResearch.Contains("AUTO"));
            Assert.Equal(remaining != 0, after.ResearchDiary.Any(entry => entry.RuleId == "AUTO"));
            Assert.Equivalent(control.Capture().Bases[0].Crafts, after.Bases[0].Crafts, strict: true);
            Assert.Equal(control.Capture().RandomState, after.RandomState);
            emptySeen |= remaining == 0;
            remainingSeen |= remaining != 0;
            for (var pass = 0; pass < 3; pass++)
            {
                campaign = Reload(content, campaign, seed, "site-research-ammo.sav");
                Assert.Equivalent(after, campaign.Capture(), strict: true);
            }
        }
        Assert.Equal(supply == 2, emptySeen);
        Assert.True(remainingSeen);
    }

    [Theory]
    [InlineData(false, "refuel", 1)]
    [InlineData(true, "refuel", 1)]
    [InlineData(false, "refuel", 2)]
    [InlineData(true, "refuel", 2)]
    [InlineData(false, "transfer", 1)]
    [InlineData(true, "transfer", 1)]
    [InlineData(false, "transfer", 2)]
    [InlineData(true, "transfer", 2)]
    [InlineData(false, "production", 1)]
    [InlineData(true, "production", 1)]
    [InlineData(false, "production", 2)]
    [InlineData(true, "production", 2)]
    [InlineData(false, "daily", 1)]
    [InlineData(true, "daily", 1)]
    [InlineData(false, "daily", 2)]
    [InlineData(true, "daily", 2)]
    public void AutomaticSiteResearchUsesStocksAfterEarlierCallers(bool cache, string source, int supply)
    {
        TestFixtures.LoadVerifiedManifest("strategic-hunting");
        TestFixtures.LoadVerifiedManifest("strategic-research-diary-execution");
        var content = Content(cache, supply);
        var initial = Snapshot(content, source, supply);
        var campaign = CampaignState.Restore(initial, content, new SplitMix64RandomSource(17));
        var result = campaign.Execute(new AdvanceCampaignTime(1));
        Assert.Empty(result.Events.OfType<CampaignActionBlocked>());
        var after = campaign.Capture();
        Assert.Empty(after.World.MissionSites);
        Assert.Equal(supply - 1, after.Bases[0].Items.GetValueOrDefault("SUPPLY"));
        Assert.Equal(supply == 2, after.CompletedResearch.Contains("AUTO"));
        Assert.Equal(supply == 2, after.ResearchDiary.Any(entry => entry.RuleId == "AUTO"));
        var craft = after.Bases[0].Crafts.Single(craft => craft.RuleId == "SHIP_FUEL_ITEM").Logistics!;
        Assert.Equal(100, craft.Fuel);
        Assert.Equal("STR_READY", craft.Status);
        for (var pass = 0; pass < 3; pass++)
        {
            campaign = Reload(content, campaign, 17, "site-research-item.sav");
            Assert.Equivalent(after, campaign.Capture(), strict: true);
        }
    }

    internal static RuntimeContent Content(bool cache, int supply, string extraRules = "", string eventInterrupt = "") => StrategicReadinessTestContent.Load(["strategic-world.rul"], cache, $$$"""
        research:
          - {name: UNLOCK, cost: 1}
          - {name: DAY, cost: 1, needItem: true, neededItem: SUPPLY, destroyItem: true, returnsItem: true}
          - {name: CORE, cost: 1}
          - {name: AUTO, cost: 0, dependencies: [CORE], needItem: true, neededItem: SUPPLY}
        manufacture:
          - {name: ITEM_PRODUCT, time: 1, producedItems: {SUPPLY: {{{supply}}}}}
        facilities:
          - {type: STORES, workshops: 2}
        """ + "\n" + StrategicWorldSiteResearchTests.Deployment +
        (eventInterrupt.Length == 0 ? "" : "\n    despawnEvents: {STOCK_EVENT: 1}\nevents:\n  - {name: STOCK_EVENT, interruptResearch: " + eventInterrupt + "}\n  - {name: STOCK_AUTO_EVENT, interruptResearch: AUTO}\n") + "\n" + extraRules);

    internal static CampaignSnapshot Snapshot(RuntimeContent content, string source, int supply)
    {
        var initial = StrategicWorldSiteResearchTests.Snapshot(content);
        initial = initial.WithCraft("SHIP", craft => craft with
        {
            RuleId = "SHIP_FUEL_ITEM",
            Logistics = craft.Logistics! with { Status = "STR_REFUELLING", Fuel = 0, Weapons = [], IsAutoPatrolling = false }
        });
        var owner = initial.Bases[0];
        return initial with
        {
            Time = source == "daily" ? new(1, 1, 1, 1999, 23, 59, 55) : source == "refuel" ? initial.Time : new(1, 1, 1, 1999, 1, 59, 55),
            Bases = [owner with
            {
                Items = source == "refuel" ? new Dictionary<string, int> { ["SUPPLY"] = supply } : source == "daily" && supply > 1 ? new Dictionary<string, int> { ["SUPPLY"] = supply - 1 } : new Dictionary<string, int>(),
                Research = source == "daily" ? [new("DAY", 0, 1, 1)] : [],
                Transfers = source == "transfer" ? [new(1, 1, CampaignTransferKind.Item, "SUPPLY", supply)] : [],
                Productions = source == "production" ? [new("ITEM_PRODUCT", 1, 0, 1, false, false, false, new Dictionary<string, int>())] : [],
            }],
        };
    }
}

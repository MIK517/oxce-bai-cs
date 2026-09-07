using Oxce.Core.Random;
using Oxce.Gameplay.Campaigns;
using Oxce.Mods.Discovery;
using Oxce.Mods.Loading;
using Oxce.Mods.Rulesets;
using Oxce.Mods.Rulesets.Content;
using Oxce.Savegames.Oxce;
using Xunit;

namespace Oxce.UnitTests.Gameplay;

public sealed class CampaignLogisticsTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void StorageOptionControlsCraftTransferButPurchasesAlwaysCheckCapacity(bool enforce)
    {
        var content = LoadFixture();
        var initial = Create(content).Capture();
        var rules = content.RuntimeRules;
        var ship = CraftLogistics.Purchase(rules.Crafts[rules.Crafts.GetRequired("SHIP")].Value, rules, 0, 0)
            with
        { Status = "STR_READY", Weapons = [null, null], Items = new Dictionary<string, int> { ["SUPPLY"] = 1 } };
        var first = initial.Bases[0] with { Crafts = [new("SHIP", 1) { Logistics = ship }] };
        var second = first with { Id = 2, Name = "Beta", Crafts = [], Items = new Dictionary<string, int> { ["SUPPLY"] = 10 } };
        var campaign = CampaignState.Restore(initial with
        {
            Options = new(enforce, true, false),
            Bases = [first, second],
        }, content, new SplitMix64RandomSource(0));
        var purchase = Assert.IsType<LogisticsQuoted>(Assert.Single(campaign.Execute(new PrepareLogisticsQuote(2, LogisticsOperation.Purchase)).Events)).Quote;
        Assert.Equal(0, purchase.Rows.Single(r => r.RuleId == "SUPPLY").MaximumQuantity);
        var quote = Assert.IsType<LogisticsQuoted>(Assert.Single(campaign.Execute(new PrepareLogisticsQuote(0, LogisticsOperation.Transfer, 2)).Events)).Quote;
        var craft = quote.Rows.Single(r => r.Kind == CampaignTransferKind.Craft);
        Assert.Equal(enforce ? 0 : 1, craft.MaximumQuantity);
        var result = campaign.Execute(new SubmitLogisticsOrder(quote.Id, [new(craft.Id, 1)]));
        Assert.Equal(enforce, result.Events.Single() is CampaignActionBlocked);
        var recruitQuote = Quote(campaign);
        var recruit = recruitQuote.Rows.Single(r => r.RuleId == "RECRUIT");
        Assert.IsType<LogisticsOrderCompleted>(Assert.Single(campaign.Execute(new SubmitLogisticsOrder(recruitQuote.Id, [new(recruit.Id, 1)])).Events));
        var snapshot = campaign.Capture();
        Assert.False(snapshot.Bases[0].Transfers.Single(t => t.Kind == CampaignTransferKind.Soldier).Soldier!.Personal!.AllowAutoCombat);
        var loaded = OxceSaveAdapter.Load(OxceSaveAdapter.EmitNewCampaign(snapshot), "options.sav", content,
            new SplitMix64RandomSource(0), new("logistics", new HashSet<string>(StringComparer.Ordinal) { "logistics" }));
        Assert.Equal(snapshot.Options, loaded.Campaign.Options);
        Assert.Equivalent(snapshot, loaded.Campaign.Capture(), strict: true);
    }

    [Fact]
    public void CraftTransferKeepsCrewAndCargoAndSaleUnloadsWithoutDeletingCrew()
    {
        var content = LoadFixture();
        var initial = Create(content).Capture();
        var rules = content.RuntimeRules;
        var ship = CraftLogistics.Purchase(rules.Crafts[rules.Crafts.GetRequired("SHIP")].Value, rules, 0, 0) with
        {
            Status = "STR_READY",
            Fuel = 100,
            Items = new Dictionary<string, int> { ["SUPPLY"] = 2 },
            Weapons = [new("FIXED", 1), null],
        };
        var soldierRule = rules.Soldiers[rules.Soldiers.GetRequired("RECRUIT")].Value;
        var personal = SoldierGeneration.Generate(soldierRule, "ARMOR", 0, new HashSet<string>(StringComparer.Ordinal), new SplitMix64RandomSource(7))
            with
        { CraftType = "SHIP", CraftId = 4, Training = true };
        var first = initial.Bases[0] with
        {
            Name = "Alpha",
            Crafts = [new("SHIP", 4) { Logistics = ship, PreservationKey = "craft-transfer-test" }],
            Soldiers = [new("RECRUIT", 9) { Personal = personal, PreservationKey = "crew-transfer-test" }],
        };
        var second = first with { Name = "Beta", Id = 2, Crafts = [], Soldiers = [] };
        var campaign = CampaignState.Restore(initial with { Bases = [first, second] }, content, new SplitMix64RandomSource(0));
        var quote = Assert.IsType<LogisticsQuoted>(Assert.Single(campaign.Execute(new PrepareLogisticsQuote(0, LogisticsOperation.Transfer, 2)).Events)).Quote;
        Assert.DoesNotContain(quote.Rows, r => r.Kind == CampaignTransferKind.Soldier);
        var row = quote.Rows.Single(r => r.Kind == CampaignTransferKind.Craft);
        Assert.IsType<LogisticsOrderCompleted>(Assert.Single(campaign.Execute(new SubmitLogisticsOrder(quote.Id, [new(row.Id, 1)])).Events));
        var transit = campaign.Capture();
        Assert.Empty(transit.Bases[0].Crafts);
        Assert.Empty(transit.Bases[0].Soldiers);
        Assert.Equal(CampaignTransferKind.Soldier, transit.Bases[1].Transfers[0].Kind);
        Assert.Equal(CampaignTransferKind.Craft, transit.Bases[1].Transfers[1].Kind);
        Assert.Equivalent(ship, transit.Bases[1].Transfers[1].Craft!.Logistics, strict: true);
        var loaded = OxceSaveAdapter.Load(OxceSaveAdapter.EmitNewCampaign(transit), "crew.sav", content,
            new SplitMix64RandomSource(0), new("logistics", new HashSet<string>(StringComparer.Ordinal) { "logistics" }));
        Assert.Single(loaded.Campaign.Execute(new AdvanceCampaignTime(5 * 720 + 1)).Events.OfType<SuppliesArrived>());
        var arrived = loaded.Campaign.Capture();
        Assert.Equal(4, Assert.Single(arrived.Bases[1].Soldiers).Personal!.CraftId);
        quote = Assert.IsType<LogisticsQuoted>(Assert.Single(loaded.Campaign.Execute(new PrepareLogisticsQuote(2, LogisticsOperation.Sell)).Events)).Quote;
        Assert.Equal(7, quote.UsedStores);
        row = quote.Rows.Single(r => r.Kind == CampaignTransferKind.Craft);
        Assert.IsType<LogisticsOrderCompleted>(Assert.Single(loaded.Campaign.Execute(new SubmitLogisticsOrder(quote.Id, [new(row.Id, 1)])).Events));
        var sold = loaded.Campaign.Capture();
        Assert.Empty(sold.Bases[1].Crafts);
        Assert.Equal("", Assert.Single(sold.Bases[1].Soldiers).Personal!.CraftType);
        Assert.Equal(5, sold.Bases[1].Items["SUPPLY"]);
        Assert.Equal(1, sold.Bases[1].Items["BULKY"]);
        Assert.Equal(arrived.Funds[0] + 500, sold.Funds[0]);
        var reloaded = OxceSaveAdapter.Load(OxceSaveAdapter.EmitLoadedCampaign(sold, loaded.Source), "unloaded.sav", content,
            new SplitMix64RandomSource(0), new("logistics", new HashSet<string>(StringComparer.Ordinal) { "logistics" }));
        Assert.Equivalent(sold, reloaded.Campaign.Capture(), strict: true);
    }

    [Fact]
    public void SoldierTransfersKeepIdentityAndDismissalReturnsArmor()
    {
        var content = LoadFixture();
        var initial = Create(content).Capture();
        var rule = content.RuntimeRules.Soldiers[content.RuntimeRules.Soldiers.GetRequired("RECRUIT")].Value;
        var personal = SoldierGeneration.Generate(rule, "ARMOR", 0, new HashSet<string>(StringComparer.Ordinal), new SplitMix64RandomSource(7))
            with
        { Training = true, PsiTraining = true };
        var soldier = new SoldierSnapshot("RECRUIT", 9) { Personal = personal, PreservationKey = "created:transfer-test" };
        var first = initial.Bases[0] with { Name = "Alpha", Soldiers = [soldier] };
        var second = first with { Name = "Beta", Id = 2, Soldiers = [] };
        var campaign = CampaignState.Restore(initial with { Bases = [first, second] }, content, new SplitMix64RandomSource(0));
        var quote = Assert.IsType<LogisticsQuoted>(Assert.Single(campaign.Execute(new PrepareLogisticsQuote(0, LogisticsOperation.Transfer, 2)).Events)).Quote;
        var row = quote.Rows.Single(r => r.Kind == CampaignTransferKind.Soldier);
        Assert.IsType<LogisticsOrderCompleted>(Assert.Single(campaign.Execute(new SubmitLogisticsOrder(quote.Id, [new(row.Id, 1)])).Events));
        var transit = campaign.Capture();
        Assert.Empty(transit.Bases[0].Soldiers);
        var incoming = Assert.Single(transit.Bases[1].Transfers).Soldier!;
        Assert.Equal(9, incoming.Id);
        Assert.Equal(soldier.PreservationKey, incoming.PreservationKey);
        Assert.False(incoming.Personal!.Training);
        Assert.False(incoming.Personal.PsiTraining);
        Assert.True(incoming.Personal.ReturnToTrainingWhenHealed);
        var loaded = OxceSaveAdapter.Load(OxceSaveAdapter.EmitNewCampaign(transit), "soldier-transit.sav", content,
            new SplitMix64RandomSource(0), new("logistics", new HashSet<string>(StringComparer.Ordinal) { "logistics" }));
        Assert.Single(loaded.Campaign.Execute(new AdvanceCampaignTime(5 * 720 + 1)).Events.OfType<SuppliesArrived>());
        Assert.Equivalent(incoming, Assert.Single(loaded.Campaign.Capture().Bases[1].Soldiers), strict: true);
        quote = Assert.IsType<LogisticsQuoted>(Assert.Single(loaded.Campaign.Execute(new PrepareLogisticsQuote(2, LogisticsOperation.Sell)).Events)).Quote;
        row = quote.Rows.Single(r => r.Kind == CampaignTransferKind.Soldier);
        var before = loaded.Campaign.Capture();
        Assert.IsType<LogisticsOrderCompleted>(Assert.Single(loaded.Campaign.Execute(new SubmitLogisticsOrder(quote.Id, [new(row.Id, 1)])).Events));
        var dismissed = loaded.Campaign.Capture();
        Assert.Empty(dismissed.Bases[1].Soldiers);
        Assert.Equal(before.Bases[1].Items["SUPPLY"] + 1, dismissed.Bases[1].Items["SUPPLY"]);
        Assert.Equal(before.Funds, dismissed.Funds);
    }

    [Fact]
    public void CraftPurchaseReservesHangarsAndPreservesWeaponsBeforeArrival()
    {
        var content = LoadFixture();
        var campaign = Create(content);
        var quote = Quote(campaign);
        var ship = quote.Rows.Single(r => r.RuleId == "SHIP");
        Assert.Equal(2, ship.MaximumQuantity);
        Assert.IsType<LogisticsOrderCompleted>(Assert.Single(campaign.Execute(new SubmitLogisticsOrder(
            quote.Id, [new(ship.Id, 2)])).Events));
        Assert.Equal(0, Quote(campaign).Rows.Single(r => r.RuleId == "SHIP").MaximumQuantity);
        var transit = campaign.Capture();
        var loaded = OxceSaveAdapter.Load(OxceSaveAdapter.EmitNewCampaign(transit), "craft-order.sav", content,
            new SplitMix64RandomSource(0), new("logistics", new HashSet<string>(StringComparer.Ordinal) { "logistics" }));
        Assert.Equivalent(transit, loaded.Campaign.Capture(), strict: true);
        var events = loaded.Campaign.Execute(new AdvanceCampaignTime(10));
        Assert.Single(events.Events.OfType<SuppliesArrived>());
        var arrived = loaded.Campaign.Capture();
        Assert.Empty(arrived.Bases[0].Transfers);
        Assert.Equal(2, arrived.Bases[0].Crafts.Count);
        Assert.All(arrived.Bases[0].Crafts, c =>
        {
            Assert.Equal("STR_REARMING", c.Logistics!.Status);
            Assert.Equal("FIXED", c.Logistics.Weapons[0]!.RuleId);
            Assert.True(c.Logistics.Weapons[0]!.Rearming);
            Assert.Null(c.Logistics.Weapons[1]);
        });
        Assert.Single(loaded.Campaign.Execute(new AdvanceCampaignTime(1000)).Events.OfType<CampaignActionBlocked>());
        Assert.Equal(29, loaded.Campaign.Time.Minute);
        Assert.Equal(55, loaded.Campaign.Time.Second);
    }

    [Fact]
    public void UnarmedCraftGetsImmediateRefuellingDuringArrivalFallthrough()
    {
        var content = LoadFixture();
        var initial = Create(content).Capture();
        var rules = content.RuntimeRules;
        var ship = CraftLogistics.Purchase(rules.Crafts[rules.Crafts.GetRequired("SHIP")].Value, rules, 0, 0) with { Weapons = [null, null] };
        var campaign = CampaignState.Restore(initial with
        {
            Bases = [initial.Bases[0] with { Transfers = [new(1, 1, CampaignTransferKind.Craft, "SHIP", 1,
                Craft: new("SHIP", 1) { Logistics = ship })] }],
        }, content, new SplitMix64RandomSource(0));
        var result = campaign.Execute(new AdvanceCampaignTime(2));
        Assert.Equal(1, Assert.Single(result.Events.OfType<CampaignTimeAdvanced>()).Summary.TickCount);
        var arrived = Assert.Single(campaign.Capture().Bases[0].Crafts).Logistics!;
        Assert.Equal(1, arrived.Fuel);
        Assert.Equal("STR_REFUELLING", arrived.Status);
    }

    [Theory]
    [InlineData("RECRUIT")]
    [InlineData("TEMPLATE_RECRUIT")]
    public void RecruitsAreGeneratedBeforeTransitAndReloadDoesNotReroll(string type)
    {
        var content = LoadFixture();
        var campaign = Create(content);
        var quote = Quote(campaign);
        var recruit = quote.Rows.Single(r => r.RuleId == type);
        Assert.IsType<LogisticsOrderCompleted>(Assert.Single(campaign.Execute(new SubmitLogisticsOrder(
            quote.Id, [new(recruit.Id, 2)])).Events));
        var transit = campaign.Capture();
        Assert.Equal(2, transit.Bases[0].Transfers.Count);
        Assert.All(transit.Bases[0].Transfers, t => Assert.Equal("Alex Example", t.Soldier!.Personal!.Name));
        if (type == "TEMPLATE_RECRUIT")
        {
            var personal = transit.Bases[0].Transfers[0].Soldier!.Personal!;
            Assert.Equal(70, personal.InitialStats["tu"]);
            Assert.Equal(50, personal.CurrentStats["tu"]);
            Assert.Equal(0, personal.InitialStats["health"]);
            Assert.Equal(20, personal.CurrentStats["mana"]);
            Assert.Equal(20, personal.InitialStats["mana"]);
            Assert.Equal(2, personal.Rank);
            Assert.Equal(3, personal.Missions);
            Assert.Equal(4, personal.Kills);
            Assert.Equal(1.5f, personal.Recovery);
            Assert.True(personal.Training);
            Assert.Equal(2, personal.PreviousTransformations["PREVIOUS"]);
            Assert.Equal(4, personal.TransformationBonuses["BONUS_A"]);
            Assert.Equal(1, personal.TransformationBonuses["BONUS_B"]);
            Assert.Equal(2, personal.TransformationBonuses.Count);
            Assert.NotEqual(999, transit.Bases[0].Transfers[0].Soldier!.Id);
        }
        Assert.NotEqual(transit.Bases[0].Transfers[0].Soldier!.Id, transit.Bases[0].Transfers[1].Soldier!.Id);
        var loaded = OxceSaveAdapter.Load(OxceSaveAdapter.EmitNewCampaign(transit), "recruits.sav", content,
            new SplitMix64RandomSource(0), new("logistics", new HashSet<string>(StringComparer.Ordinal) { "logistics" }));
        Assert.Equivalent(transit, loaded.Campaign.Capture(), strict: true);
        var randomBefore = loaded.Campaign.Capture().RandomState;
        Assert.Single(loaded.Campaign.Execute(new AdvanceCampaignTime(10)).Events.OfType<SuppliesArrived>());
        var arrived = loaded.Campaign.Capture();
        Assert.Equal(randomBefore, arrived.RandomState);
        Assert.Equivalent(transit.Bases[0].Transfers.Select(t => t.Soldier).ToArray(), arrived.Bases[0].Soldiers, strict: true);
    }

    [Fact]
    public void PurchaseReservesCapacityAndArrivesOnceAfterRestore()
    {
        var content = LoadFixture();
        var campaign = Create(content);
        var quote = Quote(campaign);
        var supply = Assert.Single(quote.Rows, r => r.RuleId == "SUPPLY");
        Assert.Equal(149, supply.UnitCost);
        Assert.Equal(8, supply.MaximumQuantity);
        var locked = Assert.Single(quote.Rows, r => r.RuleId == "LOCKED");
        Assert.Equal(0, locked.MaximumQuantity);
        var before = campaign.Capture();
        Assert.IsType<LogisticsOrderCompleted>(Assert.Single(campaign.Execute(
            new SubmitLogisticsOrder(quote.Id, [new(supply.Id, 3)])).Events));
        var ordered = campaign.Capture();
        Assert.Equal(before.Funds[0] - 447, ordered.Funds[0]);
        Assert.Equal(2, ordered.Bases[0].Items["SUPPLY"]);
        Assert.Equal(3, ordered.MonthlyPurchaseLog["SUPPLY"]);
        Assert.Equal(5, Quote(campaign).Rows.Single(r => r.RuleId == "SUPPLY").MaximumQuantity);
        var loaded = OxceSaveAdapter.Load(OxceSaveAdapter.EmitNewCampaign(ordered), "order.sav", content,
            new SplitMix64RandomSource(0), new("logistics", new HashSet<string>(StringComparer.Ordinal) { "logistics" }));
        var restored = loaded.Campaign;
        Assert.Equivalent(ordered, restored.Capture(), strict: true);
        var arrival = restored.Execute(new AdvanceCampaignTime(100));
        Assert.Single(arrival.Events.OfType<SuppliesArrived>());
        Assert.Equal(1, Assert.Single(arrival.Events.OfType<CampaignTimeAdvanced>()).Summary.TickCount);
        Assert.Equal(5, restored.Capture().Bases[0].Items["SUPPLY"]);
        Assert.Empty(restored.Capture().Bases[0].Transfers);
        Assert.Empty(restored.Execute(new AdvanceCampaignTime(720)).Events.OfType<SuppliesArrived>());
        Assert.Equal(5, restored.Capture().Bases[0].Items["SUPPLY"]);
    }

    [Fact]
    public void CombinedOverCapacityAndDuplicateOrdersDoNotMutateState()
    {
        var campaign = Create(LoadFixture());
        var quote = Quote(campaign);
        var supply = quote.Rows.Single(r => r.RuleId == "SUPPLY");
        var bulky = quote.Rows.Single(r => r.RuleId == "BULKY");
        var before = campaign.Capture();
        Assert.IsType<CampaignActionBlocked>(Assert.Single(campaign.Execute(new SubmitLogisticsOrder(
            quote.Id, [new(supply.Id, 5), new(bulky.Id, 3)])).Events));
        Assert.Equivalent(before, campaign.Capture(), strict: true);
        Assert.IsType<CampaignActionBlocked>(Assert.Single(campaign.Execute(new SubmitLogisticsOrder(
            quote.Id, [new(supply.Id, 1), new(supply.Id, 1)])).Events));
        Assert.Equivalent(before, campaign.Capture(), strict: true);
    }

    [Fact]
    public void ExhaustedTransferIdsRejectWholeOrderAndSellingStillWorks()
    {
        var content = LoadFixture();
        var before = Create(content).Capture() with
        {
            NextIds = new Dictionary<string, int> { ["oxcePortTransfer"] = int.MaxValue },
        };
        var campaign = CampaignState.Restore(before, content, new SplitMix64RandomSource(0));
        var quote = Quote(campaign);
        var supply = quote.Rows.Single(r => r.RuleId == "SUPPLY");
        Assert.IsType<CampaignActionBlocked>(Assert.Single(campaign.Execute(new SubmitLogisticsOrder(
            quote.Id, [new(supply.Id, 1)])).Events));
        Assert.Equivalent(before, campaign.Capture(), strict: true);
        quote = Quote(campaign, LogisticsOperation.Sell);
        supply = quote.Rows.Single(r => r.RuleId == "SUPPLY");
        Assert.IsType<LogisticsOrderCompleted>(Assert.Single(campaign.Execute(new SubmitLogisticsOrder(
            quote.Id, [new(supply.Id, 2)])).Events));
        Assert.Equal(before.Funds[0] + 200, campaign.Capture().Funds[0]);
        Assert.False(campaign.Capture().Bases[0].Items.ContainsKey("SUPPLY"));
    }

    private static LogisticsQuote Quote(CampaignState campaign, LogisticsOperation operation = LogisticsOperation.Purchase) =>
        Assert.IsType<LogisticsQuoted>(Assert.Single(campaign.Execute(new PrepareLogisticsQuote(0, operation)).Events)).Quote;

    [Fact]
    public void StaffAndItemsMoveBetweenBasesThroughSavedTransit()
    {
        var content = LoadFixture();
        var initial = Create(content).Capture();
        var origin = initial.Bases[0] with { Name = "Alpha" };
        var destination = origin with { Id = 7, Name = "Beta", Items = new Dictionary<string, int>(), Scientists = 0, Engineers = 0 };
        var campaign = CampaignState.Restore(initial with { Bases = [origin, destination] }, content, new SplitMix64RandomSource(0));
        var quote = Assert.IsType<LogisticsQuoted>(Assert.Single(campaign.Execute(
            new PrepareLogisticsQuote(0, LogisticsOperation.Transfer, 7)).Events)).Quote;
        var scientist = quote.Rows.Single(r => r.Kind == CampaignTransferKind.Scientist);
        var supply = quote.Rows.Single(r => r.RuleId == "SUPPLY");
        var completed = Assert.IsType<LogisticsOrderCompleted>(Assert.Single(campaign.Execute(
            new SubmitLogisticsOrder(quote.Id, [new(scientist.Id, 1), new(supply.Id, 2)])).Events));
        Assert.Equal(1, completed.Cost); // Reference minimum even for colocated bases.
        var transit = campaign.Capture();
        Assert.Equal(0, transit.Bases[0].Scientists);
        Assert.Empty(transit.Bases[0].Items);
        Assert.Equal(2, transit.Bases[1].Transfers.Count);
        var loaded = OxceSaveAdapter.Load(OxceSaveAdapter.EmitNewCampaign(transit), "transit.sav", content,
            new SplitMix64RandomSource(0), new("logistics", new HashSet<string>(StringComparer.Ordinal) { "logistics" }));
        var result = loaded.Campaign.Execute(new AdvanceCampaignTime(5 * 720 + 1));
        Assert.Single(result.Events.OfType<SuppliesArrived>());
        var arrived = loaded.Campaign.Capture();
        Assert.Equal(1, arrived.Bases[1].Scientists);
        Assert.Equal(2, arrived.Bases[1].Items["SUPPLY"]);
        Assert.Empty(arrived.Bases[1].Transfers);
        var reloaded = OxceSaveAdapter.Load(OxceSaveAdapter.EmitLoadedCampaign(arrived, loaded.Source), "arrived.sav", content,
            new SplitMix64RandomSource(0), new("logistics", new HashSet<string>(StringComparer.Ordinal) { "logistics" }));
        Assert.Equivalent(arrived, reloaded.Campaign.Capture(), strict: true);
    }

    private static CampaignState Create(RuntimeContent content) => CampaignFactory.Create(content,
        CampaignFoundationTests.Request() with { MasterId = "logistics", ActiveMods = ["logistics"] },
        new SplitMix64RandomSource(42), new CampaignFoundationTests.FixedClock());

    internal static RuntimeContent LoadFixture()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "Oxce.slnx"))) root = root.Parent;
        Assert.NotNull(root);
        var discovery = ModDiscovery.ScanDirectory(Path.Combine(root.FullName, "fixtures", "public", "mods", "strategic-logistics"));
        var plan = ModLoadPlanner.Create(ModCatalog.Create(discovery.Mods), [new ModActivation("logistics", true)],
            "logistics", new ModEngineIdentity("Extended", "8.6.1.0"));
        var snapshot = ContentSnapshotBuilder.Build(plan);
        Assert.True(snapshot.Content.Capabilities.Has(ContentLoadStage.RuntimeLinked),
            string.Join(Environment.NewLine, snapshot.Diagnostics.Select(d => d.Message)));
        return snapshot.Content;
    }
}

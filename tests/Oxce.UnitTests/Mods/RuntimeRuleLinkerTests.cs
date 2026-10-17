using Oxce.FixtureSupport;
using Oxce.Core.Diagnostics;
using Oxce.Mods;
using Oxce.Mods.Rulesets;
using Oxce.Mods.Rulesets.Content;
using Oxce.Mods.Rulesets.Runtime;
using Oxce.TestSupport;
using Xunit;

namespace Oxce.UnitTests.Mods;

public sealed class RuntimeRuleLinkerTests
{
    [Fact]
    public void CraftManufactureProjectsOnlyFirstNativeOutput()
    {
        using var fixture = new TemporaryModFixture("crafts: [{type: FIRST}]\nmanufacture: [{name: BUILD, category: STR_CRAFT, time: 1, producedItems: {FIRST: 1, ZZ_IGNORED: 5}}]\n");
        var snapshot = ContentSnapshotBuilder.Build(TestFixtures.CreatePlan(fixture.Root));
        Assert.True(snapshot.Capabilities.Has(ContentLoadStage.RuntimeLinked), Diagnostics(snapshot));
        var rules = snapshot.Content.RuntimeRules;
        var output = Assert.Single(rules.Manufacture[rules.Manufacture.GetRequired("BUILD")].Value.ProducedMaterials);
        Assert.Equal("FIRST", rules.Crafts.GetExternalId(output.Craft!.Value));
        Assert.Equal(1, output.Quantity);
    }

    [Theory]
    [InlineData("")]
    [InlineData(", armor: MISSING")]
    public void MissingUnitArmorKeepsTypedValidationWithoutDuplicateRuntimeDiagnostic(string armor)
    {
        using var fixture = new TemporaryModFixture($"units: [{{type: CORPSE_UNIT{armor}}}]\n");
        var snapshot = ContentSnapshotBuilder.Build(TestFixtures.CreatePlan(fixture.Root));
        Assert.False(snapshot.Capabilities.Has(ContentLoadStage.RuntimeLinked));
        Assert.Contains(snapshot.Diagnostics, diagnostic => diagnostic.Severity >= DiagnosticSeverity.Error &&
            diagnostic.Message.Contains("armor", StringComparison.Ordinal));
        Assert.DoesNotContain(snapshot.Diagnostics, diagnostic => diagnostic.Code == "OXCE-MOD-0030");
        Assert.Null(snapshot.Content.RuntimeRules.Units[snapshot.Content.RuntimeRules.Units.GetRequired("CORPSE_UNIT")].Value.Armor);
    }

    [Theory]
    [InlineData("", true, true)]
    [InlineData(", recover: false", false, true)]
    [InlineData(", recoverCorpse: false", true, false)]
    public void ResearchCorpseRuntimeRelationshipsKeepRecoveryDefaults(string flags, bool recover, bool corpse)
    {
        using var fixture = new TemporaryModFixture($"items: [{{type: CORPSE{flags}}}]\narmors: [{{type: CORPSE_ARMOR, corpseItem: CORPSE, loftempsSet: [0]}}]\nunits: [{{type: CORPSE_UNIT, armor: CORPSE_ARMOR}}]\n");
        var snapshot = ContentSnapshotBuilder.Build(TestFixtures.CreatePlan(fixture.Root));
        Assert.True(snapshot.Capabilities.Has(ContentLoadStage.RuntimeLinked), Diagnostics(snapshot));
        var rules = snapshot.Content.RuntimeRules;
        var unit = rules.Units[rules.Units.GetRequired("CORPSE_UNIT")].Value;
        var armor = rules.Armors[unit.Armor!.Value].Value;
        var item = rules.Items[armor.CorpseGeoscape!.Value].Value;
        Assert.Equal("CORPSE_ARMOR", armor.Type);
        Assert.Equal("CORPSE", item.Type);
        Assert.Equal(recover, item.Recoverable);
        Assert.Equal(corpse, item.CorpseRecoverable);
    }

    [Theory]
    [InlineData("startingBase: {}", 0, 0)]
    [InlineData("startingBase: {scientists: 10, engineers: 20}", 10, 20)]
    [InlineData("startingBase: {scientists: 2147483647, engineers: 2147483647}", int.MaxValue, int.MaxValue)]
    [InlineData("startingBase: {scientists: 4294967296, engineers: 2147483648}", 0, int.MinValue)]
    public void StartingPersonnelProjectionPreservesIntCountsAndZeroDefaults(string yaml, int scientists, int engineers)
    {
        using var fixture = new TemporaryModFixture(yaml);
        var snapshot = ContentSnapshotBuilder.Build(TestFixtures.CreatePlan(fixture.Root));
        Assert.True(snapshot.Capabilities.Has(ContentLoadStage.RuntimeLinked), Diagnostics(snapshot));
        var template = Assert.Single(snapshot.Content.RuntimeRules.Campaign.StartingBases);
        Assert.Equal(scientists, template.Scientists);
        Assert.Equal(engineers, template.Engineers);
    }

    [Fact]
    public void StrategicRulesUseDenseGenerationScopedHandlesAndLinkedRelationships()
    {
        var plan = TestFixtures.CreateRuntimeRuleLinkingPlan();
        var first = ContentSnapshotBuilder.Build(plan);
        var second = ContentSnapshotBuilder.Build(plan);
        var rules = first.Content.RuntimeRules;

        Assert.True(first.Capabilities.Has(ContentLoadStage.RuntimeLinked), Diagnostics(first));
        Assert.Equal(first.Content.Resources.Generation, rules.Generation);
        var countryHandle = rules.Countries.GetRequired("COUNTRY");
        var country = rules.Countries[countryHandle];
        Assert.Equal("COUNTRY", rules.Countries.GetExternalId(countryHandle));
        Assert.Equal(900, country.Value.FundingCap);
        Assert.Equal("runtime-addon", country.LastUpdateSource.ModId);
        Assert.Equal("EVENT", rules.Events.GetExternalId(country.Value.SignedPactEvent!.Value));

        var region = rules.Regions[rules.Regions.GetRequired("REGION")].Value;
        Assert.Equal("REGION", rules.Regions.GetExternalId(region.MissionRegion!.Value));

        var facility = rules.Facilities[rules.Facilities.GetRequired("FACILITY")];
        Assert.Equal(450_000, facility.Value.BuildCost);
        Assert.Equal("runtime-addon", facility.LastUpdateSource.ModId);
        Assert.Equal("ITEM", rules.Items.GetExternalId(Assert.Single(facility.Value.BuildCostItems).Item!.Value));
        Assert.Equal("FACILITY", rules.Facilities.GetExternalId(facility.Value.DestroyedFacility!.Value));
        Assert.Equal("FACILITY", rules.Facilities.GetExternalId(Assert.Single(facility.Value.BuildOverFacilities)));
        Assert.Equal(1000, facility.Value.SpriteShape.RuntimeIndex);
        Assert.True(facility.Value.SpriteShape.Override.HasValue);
        Assert.Equal("extraSprites",
            first.Content.Resources[facility.Value.SpriteShape.Override!.Value].OwnerSection);

        var craft = rules.Crafts[rules.Crafts.GetRequired("CRAFT")].Value;
        Assert.Equal("ITEM", rules.Items.GetExternalId(craft.RefuelItem!.Value));
        Assert.Equal(8, craft.SoldierCapacity);
        var soldier = rules.Soldiers[rules.Soldiers.GetRequired("SOLDIER")].Value;
        Assert.Equal("ARMOR", rules.Armors.GetExternalId(soldier.Armor));
        Assert.Equal("SKILL", rules.Skills.GetExternalId(Assert.Single(soldier.Skills)));

        Assert.Throws<InvalidOperationException>(() =>
            second.Content.RuntimeRules.Countries[countryHandle]);
        Assert.Null(typeof(RuleHandle<CountryRuleFamily>).GetProperty("Index"));
    }

    [Fact]
    public void StartingBaseReferencesAreProjectedWithoutRuntimeStringLookup()
    {
        var snapshot = ContentSnapshotBuilder.Build(TestFixtures.CreateRuntimeRuleLinkingPlan());
        var rules = snapshot.Content.RuntimeRules;
        var template = Assert.Single(rules.Campaign.StartingBases);

        Assert.Equal("FACILITY", rules.Facilities.GetExternalId(Assert.Single(template.Facilities).Rule));
        Assert.Equal("CRAFT", rules.Crafts.GetExternalId(Assert.Single(template.Crafts).Rule));
        Assert.Equal("SOLDIER", rules.Soldiers.GetExternalId(Assert.Single(template.Soldiers).Rule));
        var item = Assert.Single(template.Items);
        Assert.Equal("ITEM", rules.Items.GetExternalId(item.Rule));
        Assert.Equal(5, item.Quantity);
        var random = Assert.Single(template.RandomSoldiers);
        Assert.Equal("SOLDIER", rules.Soldiers.GetExternalId(random.Rule));
        Assert.Equal(2, random.Quantity);
    }

    [Fact]
    public void MissingRuntimeReferenceFailsWithProvenance()
    {
        using var fixture = new TemporaryModFixture("crafts: [{type: CRAFT, refuelItem: MISSING}]");

        var snapshot = ContentSnapshotBuilder.Build(TestFixtures.CreatePlan(fixture.Root));

        Assert.False(snapshot.Capabilities.Has(ContentLoadStage.RuntimeLinked));
        Assert.Contains(snapshot.Diagnostics, diagnostic =>
            diagnostic.Code == ModDiagnosticCodes.InvalidRuntimeRuleLink &&
            diagnostic.Context.RuleId == "CRAFT" &&
            diagnostic.Context.RelatedId == "MISSING");
    }

    [Fact]
    public void EagerAndRuntimeResearchReferencesKeepReferenceTiming()
    {
        using var runtimeFixture = new TemporaryModFixture("crafts: [{type: CRAFT, requires: [MISSING]}]");
        var runtime = ContentSnapshotBuilder.Build(TestFixtures.CreatePlan(runtimeFixture.Root));

        Assert.True(runtime.Capabilities.Has(ContentLoadStage.RuntimeLinked), Diagnostics(runtime));
        var requirement = Assert.Single(runtime.Content.RuntimeRules.Crafts[
            runtime.Content.RuntimeRules.Crafts.GetRequired("CRAFT")].Value.Requirements);
        Assert.Equal("MISSING", requirement.Id);
        Assert.False(requirement.IsResolved);

        using var eagerFixture = new TemporaryModFixture("items: [{type: ITEM, requires: [MISSING]}]");
        var eager = ContentSnapshotBuilder.Build(TestFixtures.CreatePlan(eagerFixture.Root));

        Assert.False(eager.Capabilities.Has(ContentLoadStage.RuntimeLinked));
        Assert.Contains(eager.Diagnostics, diagnostic =>
            diagnostic.Code == ModDiagnosticCodes.InvalidRuntimeRuleLink &&
            diagnostic.Context.RuleId == "ITEM" &&
            diagnostic.Context.RelatedId == "MISSING");
    }

    [Fact]
    public void ResearchProjectionAppliesImplicitItemAndSelfLookupDefaults()
    {
        const string yaml = """
            items: [{type: RESEARCH}]
            research:
              - name: RESEARCH
                needItem: true
                lookup: RESEARCH
            """;
        using var fixture = new TemporaryModFixture(yaml);

        var snapshot = ContentSnapshotBuilder.Build(TestFixtures.CreatePlan(fixture.Root));

        Assert.True(snapshot.Capabilities.Has(ContentLoadStage.RuntimeLinked), Diagnostics(snapshot));
        var rule = snapshot.Content.RuntimeRules.Research[
            snapshot.Content.RuntimeRules.Research.GetRequired("RESEARCH")].Value;
        Assert.Equal(string.Empty, rule.Lookup);
        Assert.Equal("RESEARCH", rule.NeededItem);
    }

    [Fact]
    public void ResearchProjectionTreatsEmptyNeededItemAsImplicit()
    {
        const string yaml = """
            items: [{type: RESEARCH}]
            research:
              - name: RESEARCH
                needItem: true
                neededItem: ""
            """;
        using var fixture = new TemporaryModFixture(yaml);

        var snapshot = ContentSnapshotBuilder.Build(TestFixtures.CreatePlan(fixture.Root));

        Assert.True(snapshot.Capabilities.Has(ContentLoadStage.RuntimeLinked), Diagnostics(snapshot));
        var rule = snapshot.Content.RuntimeRules.Research[
            snapshot.Content.RuntimeRules.Research.GetRequired("RESEARCH")].Value;
        Assert.Equal("RESEARCH", rule.NeededItem);
    }

    [Fact]
    public void ScratchBufferCanBeReusedWithoutPublishingMutableStorage()
    {
        var rules = ContentSnapshotBuilder.Build(TestFixtures.CreateRuntimeRuleLinkingPlan()).Content.RuntimeRules;
        var country = rules.Countries.GetRequired("COUNTRY");
        using var scratch = new RuleHandleScratch<CountryRuleFamily>(1);

        scratch.Add(country);
        scratch.Add(country);
        Assert.Equal(2, scratch.Count);
        Assert.True(scratch.AsSpan().SequenceEqual(new[] { country, country }));

        scratch.Clear();
        Assert.Empty(scratch.AsSpan().ToArray());
    }

    [Theory]
    [InlineData(false, "FINAL_A")]
    [InlineData(true, "FINAL_Z")]
    public void FinalResearchFollowsTheLoadOrderOfTheModThatLastUpdatedIt(bool addOnUpdatesLaterTopic, string expected)
    {
        // Mod::loadAll walks research by name; a marked topic replaces the pick unless the mod that last updated it
        // loads earlier. The master's FINAL_Z comes after the add-on's FINAL_A by name but loses to it, unless the
        // add-on updates FINAL_Z too.
        var root = Path.Combine(Path.GetTempPath(), $"oxce-final-research-{Guid.NewGuid():N}");
        try
        {
            TestFixtures.CopyDirectory(TestFixtures.PublicModsPath("runtime-rule-linking"), root);
            File.WriteAllText(Path.Combine(root, "runtime-master", "Ruleset", "final.rul"),
                "research: [{name: FINAL_Z, cost: 1, unlockFinalMission: true}]");
            File.WriteAllText(Path.Combine(root, "runtime-addon", "Ruleset", "final.rul"), addOnUpdatesLaterTopic
                ? "research: [{name: FINAL_A, cost: 1, unlockFinalMission: true}, {name: FINAL_Z, cost: 2}]"
                : "research: [{name: FINAL_A, cost: 1, unlockFinalMission: true}]");
            var snapshot = ContentSnapshotBuilder.Build(
                TestFixtures.CreatePlan(root, TestFixtures.RuntimeRuleLinkingMods[0], TestFixtures.RuntimeRuleLinkingMods));
            Assert.True(snapshot.Capabilities.Has(ContentLoadStage.RuntimeLinked), Diagnostics(snapshot));
            Assert.Equal([expected], snapshot.Content.RuntimeRules.Research.Rules
                .Where(static rule => rule.Value.UnlockFinalMission).Select(static rule => rule.Id));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    private static string Diagnostics(ContentSnapshot snapshot) => string.Join(
        Environment.NewLine,
        snapshot.Diagnostics.Select(static diagnostic => diagnostic.Message));
}

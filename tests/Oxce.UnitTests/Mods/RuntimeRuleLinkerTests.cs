using Oxce.Core.Diagnostics;
using Oxce.Mods;
using Oxce.Mods.Discovery;
using Oxce.Mods.Loading;
using Oxce.Mods.Rulesets;
using Oxce.Mods.Rulesets.Content;
using Oxce.Mods.Rulesets.Runtime;
using Xunit;

namespace Oxce.UnitTests.Mods;

public sealed class RuntimeRuleLinkerTests
{
    [Fact]
    public void StrategicRulesUseDenseGenerationScopedHandlesAndLinkedRelationships()
    {
        var plan = CreateFixturePlan();
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
        var snapshot = ContentSnapshotBuilder.Build(CreateFixturePlan());
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
        using var fixture = new TemporaryMod("crafts: [{type: CRAFT, refuelItem: MISSING}]");

        var snapshot = ContentSnapshotBuilder.Build(CreatePlan(fixture.Root, "fixture", ["fixture"]));

        Assert.False(snapshot.Capabilities.Has(ContentLoadStage.RuntimeLinked));
        Assert.Contains(snapshot.Diagnostics, diagnostic =>
            diagnostic.Code == ModDiagnosticCodes.InvalidRuntimeRuleLink &&
            diagnostic.Context.RuleId == "CRAFT" &&
            diagnostic.Context.RelatedId == "MISSING");
    }

    [Fact]
    public void EagerAndRuntimeResearchReferencesKeepReferenceTiming()
    {
        using var runtimeFixture = new TemporaryMod("crafts: [{type: CRAFT, requires: [MISSING]}]");
        var runtime = ContentSnapshotBuilder.Build(CreatePlan(runtimeFixture.Root, "fixture", ["fixture"]));

        Assert.True(runtime.Capabilities.Has(ContentLoadStage.RuntimeLinked), Diagnostics(runtime));
        var requirement = Assert.Single(runtime.Content.RuntimeRules.Crafts[
            runtime.Content.RuntimeRules.Crafts.GetRequired("CRAFT")].Value.Requirements);
        Assert.Equal("MISSING", requirement.Id);
        Assert.False(requirement.IsResolved);

        using var eagerFixture = new TemporaryMod("items: [{type: ITEM, requires: [MISSING]}]");
        var eager = ContentSnapshotBuilder.Build(CreatePlan(eagerFixture.Root, "fixture", ["fixture"]));

        Assert.False(eager.Capabilities.Has(ContentLoadStage.RuntimeLinked));
        Assert.Contains(eager.Diagnostics, diagnostic =>
            diagnostic.Code == ModDiagnosticCodes.InvalidRuntimeRuleLink &&
            diagnostic.Context.RuleId == "ITEM" &&
            diagnostic.Context.RelatedId == "MISSING");
    }

    [Fact]
    public void ScratchBufferCanBeReusedWithoutPublishingMutableStorage()
    {
        var rules = ContentSnapshotBuilder.Build(CreateFixturePlan()).Content.RuntimeRules;
        var country = rules.Countries.GetRequired("COUNTRY");
        using var scratch = new RuleHandleScratch<CountryRuleFamily>(1);

        scratch.Add(country);
        scratch.Add(country);
        Assert.Equal(2, scratch.Count);
        Assert.True(scratch.AsSpan().SequenceEqual(new[] { country, country }));

        scratch.Clear();
        Assert.Empty(scratch.AsSpan().ToArray());
    }

    private static ModLoadPlan CreateFixturePlan()
    {
        var root = FindRepositoryRoot();
        return CreatePlan(
            Path.Combine(root, "fixtures", "public", "mods", "runtime-rule-linking"),
            "runtime-master",
            ["runtime-master", "runtime-addon"]);
    }

    private static ModLoadPlan CreatePlan(string root, string master, IReadOnlyList<string> mods)
    {
        var discovery = ModDiscovery.ScanDirectory(root);
        return ModLoadPlanner.Create(
            ModCatalog.Create(discovery.Mods),
            mods.Select(static id => new ModActivation(id, true)).ToArray(),
            master,
            new ModEngineIdentity("Extended", "8.6.1.0"));
    }

    private static string Diagnostics(ContentSnapshot snapshot) => string.Join(
        Environment.NewLine,
        snapshot.Diagnostics.Select(static diagnostic => diagnostic.Message));

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Oxce.slnx")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("Could not locate the repository root.");
    }

    private sealed class TemporaryMod : IDisposable
    {
        public TemporaryMod(string rules)
        {
            Root = Path.Combine(Path.GetTempPath(), "oxce-runtime-rule-linking-" + Guid.NewGuid().ToString("N"));
            var mod = Path.Combine(Root, "fixture");
            Directory.CreateDirectory(Path.Combine(mod, "Ruleset"));
            File.WriteAllText(Path.Combine(mod, "metadata.yml"),
                "id: fixture\nname: Fixture\nversion: 1.0\nisMaster: true\n");
            File.WriteAllText(Path.Combine(mod, "Ruleset", "fixture.rul"), rules);
        }

        public string Root { get; }

        public void Dispose() => Directory.Delete(Root, recursive: true);
    }
}

using Oxce.Core.Random;
using Oxce.Formats.Yaml;
using Oxce.Gameplay.Campaigns;
using Oxce.Savegames.Oxce;
using Oxce.UnitTests.Gameplay;
using System.Text.RegularExpressions;
using Xunit;

namespace Oxce.UnitTests.Savegames;

public sealed class OxceSaveAdapterTests
{
    [Fact]
    public void NewCampaignSaveReloadsSemanticallyAndUsesTwoDocumentSchema()
    {
        var content = CampaignFoundationTests.LoadFixture();
        var campaign = CampaignFactory.Create(
            content,
            CampaignFoundationTests.Request(),
            new SplitMix64RandomSource(42),
            new CampaignFoundationTests.FixedClock());
        campaign.Execute(new PlaceStartingBase(0, "Alpha", 1.25, -0.5));
        var before = campaign.Capture();

        var yaml = OxceSaveAdapter.EmitNewCampaign(before);
        var loaded = OxceSaveAdapter.Load(
            yaml,
            "campaign.sav",
            content,
            new SplitMix64RandomSource(0),
            Options());

        Assert.Contains("\n---\n", yaml, StringComparison.Ordinal);
        Assert.Equivalent(before, loaded.Campaign.Capture(), strict: true);
        Assert.Equal(yaml, OxceSaveAdapter.EmitLoadedCampaign(loaded.Campaign.Capture(), loaded.Source));
    }

    [Fact]
    public void UnicodeSaveDirectoryAndFilenameRoundTrip()
    {
        var content = CampaignFoundationTests.LoadFixture();
        var campaign = CampaignFactory.Create(
            content,
            CampaignFoundationTests.Request(),
            new SplitMix64RandomSource(42),
            new CampaignFoundationTests.FixedClock());
        var directory = Path.Combine(
            Path.GetTempPath(),
            "oxce-save-MÖD-Δ-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "Кампания-CAFÉ.sav");
            OxceSaveAdapter.WriteNewCampaignAtomic(
                path,
                campaign.Capture(),
                cancellationToken: TestContext.Current.CancellationToken);
            var loaded = OxceSaveAdapter.LoadFile(
                path,
                content,
                new SplitMix64RandomSource(0),
                Options());

            Assert.Equivalent(campaign.Capture(), loaded.Campaign.Capture(), strict: true);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void EligibleUnknownFieldsSurviveKnownFieldOverlay()
    {
        var content = CampaignFoundationTests.LoadFixture();
        var campaign = CampaignFactory.Create(
            content,
            CampaignFoundationTests.Request(),
            new SplitMix64RandomSource(42),
            new CampaignFoundationTests.FixedClock());
        var yaml = OxceSaveAdapter.EmitNewCampaign(campaign.Capture())
            .Replace("name: Campaign", "futureHeader: retained\nname: Campaign", StringComparison.Ordinal)
            .Replace("difficulty: 0", "futureBody: {answer: 42}\ndifficulty: 0", StringComparison.Ordinal)
            .Replace("type: COUNTRY", "type: COUNTRY\n    futureCountry: yes", StringComparison.Ordinal);

        var loaded = OxceSaveAdapter.Load(
            yaml, "future.sav", content, new SplitMix64RandomSource(0), Options());
        loaded.Campaign.Execute(new AdvanceCampaignTime(1));
        var emitted = OxceSaveAdapter.EmitLoadedCampaign(loaded.Campaign.Capture(), loaded.Source);

        Assert.Contains("futureHeader: retained", emitted, StringComparison.Ordinal);
        Assert.Contains("futureBody:", emitted, StringComparison.Ordinal);
        Assert.Contains("futureCountry: yes", emitted, StringComparison.Ordinal);
    }

    [Fact]
    public void LoadedAtomicRewriteRequiresAndPreservesSourceDocument()
    {
        var content = CampaignFoundationTests.LoadFixture();
        var campaign = CampaignFactory.Create(
            content,
            CampaignFoundationTests.Request(),
            new SplitMix64RandomSource(42),
            new CampaignFoundationTests.FixedClock());
        var yaml = OxceSaveAdapter.EmitNewCampaign(campaign.Capture())
            .Replace("name: Campaign", "futureHeader: retained\nname: Campaign", StringComparison.Ordinal);
        var loaded = OxceSaveAdapter.Load(
            yaml, "rewrite.sav", content, new SplitMix64RandomSource(0), Options());
        var directory = Path.Combine(Path.GetTempPath(), "oxce-save-rewrite-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "campaign.sav");
            OxceSaveAdapter.RewriteLoadedCampaignAtomic(
                path,
                loaded.Campaign.Capture(),
                loaded.Source,
                cancellationToken: TestContext.Current.CancellationToken);

            Assert.Contains("futureHeader: retained", File.ReadAllText(path), StringComparison.Ordinal);
            Assert.Throws<ArgumentNullException>(() =>
                OxceSaveAdapter.EmitLoadedCampaign(loaded.Campaign.Capture(), null!));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void BaseAndFacilityOpaqueFieldsFollowSemanticIdentityAcrossMutations()
    {
        var content = CampaignFoundationTests.LoadFixture();
        var campaign = CampaignFactory.Create(
            content,
            CampaignFoundationTests.Request(),
            new SplitMix64RandomSource(42),
            new CampaignFoundationTests.FixedClock());
        var snapshot = campaign.Capture();
        var templateBase = Assert.Single(snapshot.Bases);
        var templateFacility = Assert.Single(templateBase.Facilities);
        var alpha = templateBase with
        {
            Id = 7,
            Name = "Alpha",
            Facilities = Array.AsReadOnly([
                templateFacility with { X = 0, Y = 0 },
                templateFacility with { X = 2, Y = 0, BuildTime = 3 },
            ]),
        };
        var beta = new BaseSnapshot(
            12,
            "Beta",
            2.5,
            0.5,
            Array.AsReadOnly([templateFacility with { X = 4, Y = 4 }]),
            Array.AsReadOnly(Array.Empty<CraftSnapshot>()),
            Array.AsReadOnly(Array.Empty<SoldierSnapshot>()),
            new Dictionary<string, int>(StringComparer.Ordinal),
            0,
            0);
        var initial = snapshot with { Bases = Array.AsReadOnly([alpha, beta]) };
        var yaml = OxceSaveAdapter.EmitNewCampaign(initial)
            .Replace("name: Alpha", "name: Alpha\n    opaqueBase: alpha", StringComparison.Ordinal)
            .Replace("name: Beta", "name: Beta\n    opaqueBase: beta", StringComparison.Ordinal)
            .Replace(
                $"type: {templateFacility.RuleId}\n        x: 0\n        y: 0",
                $"type: {templateFacility.RuleId}\n        x: 0\n        y: 0\n        opaqueFacility: alpha-zero",
                StringComparison.Ordinal)
            .Replace(
                $"type: {templateFacility.RuleId}\n        x: 2\n        y: 0",
                $"type: {templateFacility.RuleId}\n        x: 2\n        y: 0\n        opaqueFacility: alpha-two",
                StringComparison.Ordinal)
            .Replace(
                $"type: {templateFacility.RuleId}\n        x: 4\n        y: 4",
                $"type: {templateFacility.RuleId}\n        x: 4\n        y: 4\n        opaqueFacility: beta-four",
                StringComparison.Ordinal);
        Assert.Contains("opaqueFacility: alpha-two", yaml, StringComparison.Ordinal);
        var loaded = OxceSaveAdapter.Load(
            yaml, "identity.sav", content, new SplitMix64RandomSource(0), Options());
        var loadedSnapshot = loaded.Campaign.Capture();
        var loadedAlpha = Assert.Single(loadedSnapshot.Bases, item => item.Id == 7);
        var loadedBeta = Assert.Single(loadedSnapshot.Bases, item => item.Id == 12);
        var retainedFacility = Assert.Single(loadedAlpha.Facilities, item => item.X == 2 && item.Y == 0);
        var changedAlpha = loadedAlpha with
        {
            Facilities = Array.AsReadOnly([
                retainedFacility,
                templateFacility with { X = 3, Y = 0 },
            ]),
        };
        var gamma = beta with { Id = 20, Name = "Gamma", Facilities = Array.AsReadOnly(Array.Empty<FacilitySnapshot>()) };
        var changed = loadedSnapshot with { Bases = Array.AsReadOnly([loadedBeta, gamma, changedAlpha]) };

        var emitted = OxceSaveAdapter.EmitLoadedCampaign(changed, loaded.Source);
        var emittedBases = ReadMaps(ReadBody(emitted), "bases").ToDictionary(ReadId);
        var emittedBeta = emittedBases[12];
        var emittedGamma = emittedBases[20];
        var emittedAlpha = emittedBases[7];

        Assert.Equal("beta", ReadString(emittedBeta, "opaqueBase"));
        Assert.False(emittedGamma.TryGet("opaqueBase", out _));
        Assert.Equal("alpha", ReadString(emittedAlpha, "opaqueBase"));
        var alphaFacilities = ReadMaps(emittedAlpha, "facilities").ToDictionary(FacilityKey);
        Assert.Equal("alpha-two", ReadString(alphaFacilities[(templateFacility.RuleId, 2, 0)], "opaqueFacility"));
        Assert.False(alphaFacilities[(templateFacility.RuleId, 3, 0)].TryGet("opaqueFacility", out _));
        Assert.DoesNotContain("alpha-zero", emitted, StringComparison.Ordinal);

        var afterRemoval = OxceSaveAdapter.EmitLoadedCampaign(
            loadedSnapshot with { Bases = Array.AsReadOnly([loadedAlpha]) }, loaded.Source);
        Assert.DoesNotContain("opaqueBase: beta", afterRemoval, StringComparison.Ordinal);
    }

    [Fact]
    public void MissingReferenceBaseIdsBecomeStableAndDuplicateIdsAreRejected()
    {
        var content = CampaignFoundationTests.LoadFixture();
        var campaign = CampaignFactory.Create(
            content,
            CampaignFoundationTests.Request(),
            new SplitMix64RandomSource(42),
            new CampaignFoundationTests.FixedClock());
        var snapshot = campaign.Capture();
        var original = Assert.Single(snapshot.Bases);
        var second = original with
        {
            Id = 1,
            Name = "Second",
            Facilities = Array.AsReadOnly(Array.Empty<FacilitySnapshot>()),
            Crafts = Array.AsReadOnly(Array.Empty<CraftSnapshot>()),
            Soldiers = Array.AsReadOnly(Array.Empty<SoldierSnapshot>()),
            Items = new Dictionary<string, int>(StringComparer.Ordinal),
        };
        var yaml = Regex.Replace(
            OxceSaveAdapter.EmitNewCampaign(snapshot with { Bases = Array.AsReadOnly([original, second]) }),
            "^    id: [01]\\r?$",
            string.Empty,
            RegexOptions.Multiline | RegexOptions.CultureInvariant);

        var loaded = OxceSaveAdapter.Load(
            yaml, "legacy-bases.sav", content, new SplitMix64RandomSource(0), Options());
        var ids = loaded.Campaign.Capture().Bases.Select(static item => item.Id).ToArray();
        var emitted = OxceSaveAdapter.EmitLoadedCampaign(loaded.Campaign.Capture(), loaded.Source);

        Assert.Equal([0, 1], ids);
        Assert.Contains("    id: 0", emitted, StringComparison.Ordinal);
        Assert.Contains("    id: 1", emitted, StringComparison.Ordinal);
        var duplicate = Regex.Replace(
            emitted,
            "^    id: 1\\r?$",
            "    id: 0",
            RegexOptions.Multiline | RegexOptions.CultureInvariant);
        Assert.Throws<InvalidDataException>(() => OxceSaveAdapter.Load(
            duplicate, "duplicate-bases.sav", content, new SplitMix64RandomSource(0), Options()));
    }

    [Fact]
    public void MissingModsAndIdentityCollisionsFailTransactionally()
    {
        var content = CampaignFoundationTests.LoadFixture();
        var campaign = CampaignFactory.Create(
            content,
            CampaignFoundationTests.Request(),
            new SplitMix64RandomSource(42),
            new CampaignFoundationTests.FixedClock());
        var yaml = OxceSaveAdapter.EmitNewCampaign(campaign.Capture());
        var missing = new OxceSaveLoadOptions(
            "runtime-master", new HashSet<string>(["runtime-master"], StringComparer.Ordinal));
        Assert.Throws<InvalidDataException>(() =>
            OxceSaveAdapter.Load(yaml, "missing.sav", content, new SplitMix64RandomSource(0), missing));

        var snapshot = campaign.Capture();
        var baseState = Assert.Single(snapshot.Bases);
        var soldier = Assert.Single(baseState.Soldiers, item => item.Id == 4);
        var duplicate = baseState with
        {
            Soldiers = Array.AsReadOnly(baseState.Soldiers.Append(soldier).ToArray()),
        };
        Assert.Throws<InvalidDataException>(() => CampaignState.Restore(
            snapshot with { Bases = Array.AsReadOnly(new[] { duplicate }) },
            content,
            new SplitMix64RandomSource(0)));
    }

    [Fact]
    public void CancelledAtomicWriteLeavesExistingSaveUntouched()
    {
        var content = CampaignFoundationTests.LoadFixture();
        var campaign = CampaignFactory.Create(
            content,
            CampaignFoundationTests.Request(),
            new SplitMix64RandomSource(42),
            new CampaignFoundationTests.FixedClock());
        var directory = Path.Combine(Path.GetTempPath(), "oxce-save-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "campaign.sav");
            File.WriteAllText(path, "original");
            Assert.Throws<OperationCanceledException>(() => OxceSaveAdapter.WriteNewCampaignAtomic(
                path, campaign.Capture(), cancellationToken: new CancellationToken(canceled: true)));
            Assert.Equal("original", File.ReadAllText(path));
            Assert.Empty(Directory.GetFiles(directory, "*.tmp"));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void OversizedFileIsRejectedBeforeParsing()
    {
        var path = Path.Combine(Path.GetTempPath(), "oxce-oversized-save-" + Guid.NewGuid().ToString("N"));
        try
        {
            File.WriteAllText(path, "0123456789");
            var options = Options() with { Yaml = new YamlReadOptions { MaxBytes = 5 } };
            Assert.Throws<InvalidDataException>(() => OxceSaveAdapter.LoadFile(
                path,
                CampaignFoundationTests.LoadFixture(),
                new SplitMix64RandomSource(0),
                options));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void RepeatedSaveCyclesRemainSemanticallyAndByteStable()
    {
        var content = CampaignFoundationTests.LoadFixture();
        var campaign = CampaignFactory.Create(
            content,
            CampaignFoundationTests.Request(),
            new SplitMix64RandomSource(42),
            new CampaignFoundationTests.FixedClock());
        campaign.Execute(new PlaceStartingBase(0, "Alpha", 1.25, -0.5));
        var expected = campaign.Capture();
        string? stable = null;

        for (var index = 0; index < 100; index++)
        {
            var yaml = OxceSaveAdapter.EmitNewCampaign(campaign.Capture());
            stable ??= yaml;
            Assert.Equal(stable, yaml);
            campaign = OxceSaveAdapter.Load(
                yaml, "cycle.sav", content, new SplitMix64RandomSource(0), Options()).Campaign;
            Assert.Equivalent(expected, campaign.Capture(), strict: true);
        }
    }

    private static OxceSaveLoadOptions Options() => new(
        "runtime-master",
        new HashSet<string>(["runtime-master", "runtime-addon"], StringComparer.Ordinal));

    private static YamlMappingNode ReadBody(string yaml) => Assert.IsType<YamlMappingNode>(
        Assert.Single(YamlCompatibilityReader.Parse(yaml, "emitted.sav").Documents.Skip(1)).Root);

    private static YamlMappingNode[] ReadMaps(YamlMappingNode owner, string key)
    {
        Assert.True(owner.TryGet(key, out var node));
        return YamlValueReader.ReadSequence(node!, item => Assert.IsType<YamlMappingNode>(item));
    }

    private static int ReadId(YamlMappingNode value) =>
        YamlValueReader.ReadInt32(Required(value, "id"));

    private static (string Type, int X, int Y) FacilityKey(YamlMappingNode value) => (
        YamlValueReader.ReadString(Required(value, "type")),
        YamlValueReader.ReadInt32(Required(value, "x")),
        YamlValueReader.ReadInt32(Required(value, "y")));

    private static string ReadString(YamlMappingNode owner, string key) =>
        YamlValueReader.ReadString(Required(owner, key));

    private static YamlNode Required(YamlMappingNode owner, string key)
    {
        Assert.True(owner.TryGet(key, out var node));
        return node!;
    }
}

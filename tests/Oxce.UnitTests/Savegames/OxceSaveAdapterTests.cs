using Oxce.Core.Random;
using Oxce.Formats.Yaml;
using Oxce.Gameplay.Campaigns;
using Oxce.Savegames.Oxce;
using Oxce.UnitTests.Gameplay;
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

        var yaml = OxceSaveAdapter.Emit(before);
        var loaded = OxceSaveAdapter.Load(
            yaml,
            "campaign.sav",
            content,
            new SplitMix64RandomSource(0),
            Options());

        Assert.Contains("\n---\n", yaml, StringComparison.Ordinal);
        Assert.Equivalent(before, loaded.Campaign.Capture(), strict: true);
        Assert.Equal(yaml, OxceSaveAdapter.Emit(loaded.Campaign.Capture()));
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
        var yaml = OxceSaveAdapter.Emit(campaign.Capture())
            .Replace("name: Campaign", "futureHeader: retained\nname: Campaign", StringComparison.Ordinal)
            .Replace("difficulty: 0", "futureBody: {answer: 42}\ndifficulty: 0", StringComparison.Ordinal)
            .Replace("type: COUNTRY", "type: COUNTRY\n    futureCountry: yes", StringComparison.Ordinal);

        var loaded = OxceSaveAdapter.Load(
            yaml, "future.sav", content, new SplitMix64RandomSource(0), Options());
        loaded.Campaign.Execute(new AdvanceCampaignTime(1));
        var emitted = OxceSaveAdapter.Emit(loaded.Campaign.Capture(), loaded.Source);

        Assert.Contains("futureHeader: retained", emitted, StringComparison.Ordinal);
        Assert.Contains("futureBody:", emitted, StringComparison.Ordinal);
        Assert.Contains("futureCountry: yes", emitted, StringComparison.Ordinal);
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
        var yaml = OxceSaveAdapter.Emit(campaign.Capture());
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
            Assert.Throws<OperationCanceledException>(() => OxceSaveAdapter.WriteAtomic(
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
            var yaml = OxceSaveAdapter.Emit(campaign.Capture());
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
}

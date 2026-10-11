using System.Text.Json;
using Oxce.Core.Random;
using Oxce.FixtureSupport;
using Oxce.Formats.Yaml;
using Oxce.Gameplay.Campaigns;
using Oxce.Mods.Rulesets.Content;
using Oxce.Savegames.Oxce;
using Oxce.TestSupport;
using Xunit;
using static Oxce.CompatibilityTests.StrategicWorldTestSupport;

namespace Oxce.CompatibilityTests;

public sealed class StrategicResearchDiaryTests
{
    [Fact]
    public void DiarySaveFieldsMatchNativeSourcesDatesAndMissingLinks()
    {
        using var oracle = TestFixtures.ReadVerifiedExpected("strategic-research-diary");
        var content = Content();
        var snapshot = CreateSnapshot(content, "Research diary");
        var rows = TestFixtures.Rows(oracle.RootElement, "rows").ToArray();
        var yaml = OxceSaveAdapter.EmitNewCampaign(snapshot) + "researchDiary:\n" +
            string.Join("\n", rows.Select(row => "  - " + row.GetRawText())) + "\n" +
            "  - {name: REMOVED_TOPIC, date: invalid, sourceType: invalid, sourceName: invalid, oxcePortEntityKey: 'research-diary:legacy:0'}\n" +
            "  - {name: REMOVED_TOPIC, oxcePortEntityKey: {future: ignored}}\n";
        var loaded = TestFixtures.LoadLogisticsSave(yaml, content, 17, "research-diary.sav");
        Assert.True(oracle.RootElement.GetProperty("skippedUndefined").GetBoolean());
        for (var pass = 0; pass < 3; pass++)
        {
            var entries = loaded.Campaign.Capture().ResearchDiary;
            var actual = JsonSerializer.SerializeToUtf8Bytes(entries.Select(entry => new
            { name = entry.RuleId, date = new[] { entry.Year, entry.Month, entry.Day }, sourceType = entry.SourceType, sourceName = entry.SourceName }));
            Assert.Equal(CanonicalJson.Normalize(JsonSerializer.SerializeToUtf8Bytes(rows)), CanonicalJson.Normalize(actual));
            Assert.Equal(entries.Count, entries.Select(entry => entry.PreservationKey).Distinct(StringComparer.Ordinal).Count());
            Assert.DoesNotContain(entries, entry => entry.RuleId == "REMOVED_TOPIC");
            var rewritten = OxceSaveAdapter.EmitLoadedCampaign(loaded.Campaign.Capture(), loaded.Source);
            loaded = TestFixtures.LoadLogisticsSave(rewritten, content, 17, "research-diary.sav");
        }
    }

    [Fact]
    public void RepeatedDiaryOccurrencesRetainOnlyTheirOwnSurvivingSidecars()
    {
        TestFixtures.LoadVerifiedManifest("strategic-research-diary");
        var content = Content();
        var yaml = OxceSaveAdapter.EmitNewCampaign(CreateSnapshot(content, "Research diary sidecars")) + """
            researchDiary:
              - {name: DIARY_TOPIC, date: [1999, 1, 2], sourceType: 3, sourceName: SOURCE, futureDiary: deleted}
              - {name: DIARY_TOPIC, date: [1999, 1, 2], sourceType: 3, sourceName: SOURCE, futureDiary: survivor}
            futureResearchRoot: retained
            """;
        var loaded = TestFixtures.LoadLogisticsSave(yaml, content, 17, "research-diary.sav");
        var captured = loaded.Campaign.Capture();
        var changed = captured with
        {
            ResearchDiary = [captured.ResearchDiary[1], new("DIARY_TOPIC", 1999, 1, 2, 3, "SOURCE")],
        };
        var campaign = CampaignState.Restore(changed, content, new SplitMix64RandomSource(17));
        for (var pass = 0; pass < 3; pass++)
        {
            var rewritten = OxceSaveAdapter.EmitLoadedCampaign(campaign.Capture(), loaded.Source);
            Assert.Contains("futureDiary: survivor", rewritten, StringComparison.Ordinal);
            Assert.DoesNotContain("futureDiary: deleted", rewritten, StringComparison.Ordinal);
            Assert.Contains("futureResearchRoot: retained", rewritten, StringComparison.Ordinal);
            Assert.Equal(1, rewritten.Split("futureDiary:", StringSplitOptions.None).Length - 1);
            loaded = TestFixtures.LoadLogisticsSave(rewritten, content, 17, "research-diary.sav");
            campaign = loaded.Campaign;
            Assert.Equal(2, campaign.Capture().ResearchDiary.Count);
            Assert.Equal(1UL, campaign.Capture().ResearchDiarySequence);
        }
        var empty = campaign.Capture() with { ResearchDiary = [] };
        var final = OxceSaveAdapter.EmitLoadedCampaign(empty, loaded.Source);
        Assert.DoesNotContain("futureDiary:", final, StringComparison.Ordinal);
        Assert.Empty(TestFixtures.LoadLogisticsSave(final, content, 17, "research-diary.sav").Campaign.Capture().ResearchDiary);
    }

    [Theory]
    [InlineData("{name: DIARY_TOPIC, date: [1999, 1], sourceType: 3}", false)]
    [InlineData("{name: DIARY_TOPIC, date: [-1, 1, 2], sourceType: 3}", true)]
    [InlineData("{name: DIARY_TOPIC, date: [4294967296, 1, 2], sourceType: 3}", false)]
    [InlineData("{name: DIARY_TOPIC, date: [1999, 1, 2], sourceType: 256}", false)]
    [InlineData("{name: DIARY_TOPIC, date: [1999, 1, 2], sourceType: -1}", true)]
    [InlineData("{name: DIARY_TOPIC, date: [1999, 1, 2]}", false)]
    public void MalformedDiaryWidthsAndMissingFieldsFailIntentionally(string entry, bool yamlError)
    {
        TestFixtures.LoadVerifiedManifest("strategic-research-diary");
        var content = Content();
        var yaml = OxceSaveAdapter.EmitNewCampaign(CreateSnapshot(content, "Invalid diary")) + $"researchDiary:\n  - {entry}\n";
        if (yamlError)
            Assert.Throws<YamlFormatException>(() => TestFixtures.LoadLogisticsSave(yaml, content, 17, "research-diary.sav"));
        else
            Assert.Throws<InvalidDataException>(() => TestFixtures.LoadLogisticsSave(yaml, content, 17, "research-diary.sav"));
    }

    [Fact]
    public void DiarySnapshotRejectsDuplicateKeysAndExhaustedIdentityWithoutChangingRandomState()
    {
        TestFixtures.LoadVerifiedManifest("strategic-research-diary");
        var content = Content();
        var snapshot = CreateSnapshot(content, "Invalid diary identity");
        var random = new SplitMix64RandomSource(17);
        var entry = new ResearchDiaryEntrySnapshot("DIARY_TOPIC", 1999, 1, 2, 3, "SOURCE") { PreservationKey = "duplicate" };
        Assert.Throws<InvalidDataException>(() => CampaignState.Restore(snapshot with { ResearchDiary = [entry, entry] }, content, random));
        Assert.Equal(17UL, random.State);
        Assert.Throws<InvalidDataException>(() => CampaignState.Restore(snapshot with
        { ResearchDiary = [entry with { PreservationKey = "" }], ResearchDiarySequence = ulong.MaxValue }, content, random));
        Assert.Equal(17UL, random.State);
        Assert.Throws<InvalidDataException>(() => CampaignState.Restore(snapshot with
        { ResearchDiary = Enumerable.Repeat(entry, CampaignState.MaximumResearchDiaryEntries + 1).ToArray() }, content, random));
        Assert.Equal(17UL, random.State);
        Assert.Throws<InvalidDataException>(() => CampaignState.Restore(snapshot with
        { ResearchDiary = [entry with { PreservationKey = "research-diary:new:1" }] }, content, random));
        Assert.Equal(17UL, random.State);
    }

    private static RuntimeContent Content() => StrategicReadinessTestContent.Load(["strategic-world.rul"], "research: [{name: DIARY_TOPIC, cost: 1}, {name: SOURCE, cost: 1}]");
}

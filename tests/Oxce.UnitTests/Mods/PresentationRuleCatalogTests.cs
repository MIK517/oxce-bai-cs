using Oxce.FixtureSupport;
using Oxce.Core.Diagnostics;
using Oxce.Formats.Yaml;
using Oxce.Mods;
using Oxce.Mods.Discovery;
using Oxce.Mods.Loading;
using Oxce.Mods.Rulesets;
using Oxce.Mods.Rulesets.Presentation;
using Xunit;

namespace Oxce.UnitTests.Mods;

public sealed class PresentationRuleCatalogTests
{
    [Fact]
    public void ReplaysNamedAndSpecialPresentationRulesWithReferenceMergeSemantics()
    {
        const string yaml = """
            fontName: InterfaceFonts.dat
            interfaces:
              - type: IFACE
                refNode:
                  palette: PAL_OLD
                  elements:
                    - id: button
                      pos: [1, 2]
                      color: 3
                palette: PAL_NEW
                sound: {index: 5, mod: current}
                elements:
                  - id: button
                    size: [10, 20]
                    TFTDMode: true
            musics:
              - type: TRACK
              - update: TRACK
                normalization: 0.5
            soundDefs:
              - type: GEO.CAT
                file: SAMPLE.CAT
                soundRanges: [[1, 3]]
                sounds: [8]
              - update: GEO.CAT
                sounds: [9]
            customPalettes:
              - type: PAL_CUSTOM
                target: PAL_BASESCAPE
                palette: {1: [10, 20, 30]}
            cutscenes:
              - type: winGame
                useUfoAudioSequence: true
                videos: [A.FLC]
                slideshow:
                  slides:
                    - imagePath: Resources/slide.png
              - update: winGame
                videos: [B.FLC]
            extraStrings:
              - type: en-US
                strings:
                  STR_ONE: One
                  STR_COUNT: {one: Singular, other: Plural}
              - type: en-US
                strings: {STR_ONE: Replaced}
            extraSprites:
              - type: SET
                width: 64
                files: {0: Resources/a.png}
              - delete: SET
              - typeSingle: SET
                fileSingle: Resources/b.png
            extraSounds:
              - type: GEO.CAT
                files: {150: Resources/c.wav}
            """;
        using var fixture = new TemporaryModFixture(yaml);
        var diagnostics = new DiagnosticCollector();

        var content = PresentationRuleCatalog.Load(CreatePlan(fixture.Root), diagnostics);
        Assert.Equal("InterfaceFonts.dat", content.Special.FontName);

        var interfaceRule = Assert.Single(content.Interfaces.Rules).Value;
        Assert.Equal("PAL_NEW", interfaceRule.Palette);
        Assert.Equal(new RuleIndexReference(5, "fixture"), interfaceRule.Sound);
        var button = Assert.Single(interfaceRule.Elements).Value;
        Assert.Equal((1, 2, 10, 20), (button.X, button.Y, button.Width, button.Height));
        Assert.Equal(3, button.Color);
        Assert.True(button.TftdMode);

        var music = Assert.Single(content.Music.Rules).Value;
        Assert.Equal(int.MaxValue, music.CatalogPosition);
        Assert.Equal(0.5f, music.Normalization);
        Assert.Equal("TRACK", music.ResolveName("TRACK"));

        var sound = Assert.Single(content.SoundDefinitions.Rules).Value;
        Assert.Equal([(1, 3)], sound.SoundRanges);
        Assert.Equal([8, 9], sound.Sounds);
        var palette = Assert.Single(content.CustomPalettes.Rules).Value;
        Assert.Equal(new PaletteColor(10, 20, 30), palette.Palette[1]);

        var video = Assert.Single(content.Videos.Rules).Value;
        Assert.True(video.WinGame);
        Assert.False(video.UseUfoAudioSequence);
        Assert.Equal(["A.FLC", "B.FLC"], video.Videos);
        var slide = Assert.Single(video.Slides);
        Assert.Equal((320, 200, 0, 0), (slide.Width, slide.Height, slide.X, slide.Y));

        Assert.Equal("Replaced", content.Special.Strings["en-US"]["STR_ONE"]);
        Assert.Equal("Singular", content.Special.Strings["en-US"]["STR_COUNT_one"]);
        Assert.Equal("Plural", content.Special.Strings["en-US"]["STR_COUNT_other"]);
        var sprite = Assert.Single(content.Special.Sprites["SET"]);
        Assert.True(sprite.SingleImage);
        Assert.Equal("Resources/b.png", sprite.Files[0]);
        Assert.Equal("Resources/c.wav", Assert.Single(content.Special.Sounds).Files[150]);
        Assert.DoesNotContain(diagnostics.Snapshot(), item => item.Code == ModDiagnosticCodes.UnconsumedRuleProperty);
        Assert.True(content.Capabilities.Has(ContentLoadStage.Typed));
        Assert.False(content.Capabilities.Has(ContentLoadStage.Linked));
    }

    [Fact]
    public void ValidatesExplicitFilesAndFoldersWithoutClaimingResourceCapability()
    {
        const string yaml = """
            extraSprites:
              - type: SET
                files: {0: Resources/images/}
            extraSounds:
              - type: GEO.CAT
                files: {0: Resources/missing.wav}
            soundDefs:
              - type: GEO.CAT
                file: SAMPLE.CAT
            """;
        using var fixture = new TemporaryModFixture(yaml);
        fixture.WriteResource("Resources/images/frame.png", "image");
        fixture.WriteResource("SOUND/SAMPLE.CAT", "cat");
        var plan = CreatePlan(fixture.Root);
        var content = PresentationRuleCatalog.Load(plan);
        var diagnostics = new DiagnosticCollector();

        var result = content.ValidateDeclaredResources(plan.CreateVirtualFileCatalog(), diagnostics);

        var missing = Assert.Single(result.Missing);
        Assert.Equal("Resources/missing.wav", missing.Path);
        Assert.False(result.IsValid);
        Assert.Contains(diagnostics.Snapshot(), item => item.Code == ModDiagnosticCodes.MissingDeclaredResource);
        Assert.False(content.Capabilities.Has(ContentLoadStage.ResourcesResolved));
    }

    [Fact]
    public void RejectsMalformedSpecialSections()
    {
        using var fixture = new TemporaryModFixture("extraStrings: {type: en-US}");

        var exception = Assert.Throws<YamlFormatException>(
            () => PresentationRuleCatalog.Load(CreatePlan(fixture.Root)));
        Assert.Contains("extraStrings", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void SeparatelyPublishesResourceConfigSoundDefinitions()
    {
        using var fixture = new TemporaryModFixture("soundDefs: [{type: REGULAR, file: regular.cat}]");
        fixture.SetResourceConfig("preload.rul", "soundDefs: [{type: PRELOAD, file: preload.cat}]");

        var content = PresentationRuleCatalog.Load(CreatePlan(fixture.Root));

        Assert.Equal(["PRELOAD"], content.ResourceConfigSoundDefinitions.Rules.Select(rule => rule.Id));
        Assert.Equal(["PRELOAD", "REGULAR"], content.SoundDefinitions.Rules.Select(rule => rule.Id));
    }

    private static ModLoadPlan CreatePlan(string root)
    {
        var discovery = ModDiscovery.ScanDirectory(root);
        var catalog = ModCatalog.Create(discovery.Mods);
        return ModLoadPlanner.Create(
            catalog,
            [new ModActivation("fixture", true)],
            "fixture",
            new ModEngineIdentity("Extended", "8.6.1.0"));
    }

}

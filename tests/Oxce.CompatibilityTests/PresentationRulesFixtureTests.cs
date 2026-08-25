using System.Text.Json;
using Oxce.Core.Diagnostics;
using Oxce.Mods;
using Oxce.Mods.Discovery;
using Oxce.Mods.Loading;
using Oxce.Mods.Rulesets.Presentation;
using Xunit;

namespace Oxce.CompatibilityTests;

public sealed class PresentationRulesFixtureTests
{
    [Fact]
    public void PresentationRulesMatchPinnedReferenceFixture()
    {
        var root = FindRepositoryRoot();
        var fixture = Path.Combine(root, "fixtures", "public", "mods", "presentation-rules");
        var expectedPath = Path.Combine(root, "fixtures", "expected", "mods", "presentation-rules.expected.json");
        var diagnostics = new DiagnosticCollector();
        var discovery = ModDiscovery.ScanDirectory(fixture, diagnostics);
        var catalog = ModCatalog.Create(discovery.Mods, diagnostics);
        var plan = ModLoadPlanner.Create(
            catalog,
            [new ModActivation("fixture", true)],
            "fixture",
            new ModEngineIdentity("Extended", "8.6.1.0"),
            diagnostics);

        var actual = PresentationRuleCatalog.Load(plan, diagnostics);
        using var expected = JsonDocument.Parse(File.ReadAllText(expectedPath));

        var expectedInterface = expected.RootElement.GetProperty("interface");
        var interfaceRule = Assert.Single(actual.Interfaces.Rules).Value;
        Assert.Equal(expectedInterface.GetProperty("palette").GetString(), interfaceRule.Palette);
        var expectedButton = expectedInterface.GetProperty("button");
        var button = interfaceRule.Elements["button"];
        Assert.Equal(expectedButton.GetProperty("x").GetInt32(), button.X);
        Assert.Equal(expectedButton.GetProperty("y").GetInt32(), button.Y);
        Assert.Equal(expectedButton.GetProperty("width").GetInt32(), button.Width);
        Assert.Equal(expectedButton.GetProperty("height").GetInt32(), button.Height);
        Assert.Equal(expectedButton.GetProperty("color").GetInt32(), button.Color);
        Assert.Equal(expectedButton.GetProperty("tftdMode").GetBoolean(), button.TftdMode);

        var expectedMusic = expected.RootElement.GetProperty("music");
        var music = Assert.Single(actual.Music.Rules);
        Assert.Equal(expectedMusic.GetProperty("catalogPosition").GetInt32(), music.Value.CatalogPosition);
        Assert.Equal(expectedMusic.GetProperty("normalization").GetSingle(), music.Value.Normalization);
        Assert.Equal(expectedMusic.GetProperty("resolvedName").GetString(), music.Value.ResolveName(music.Id));

        var expectedVideo = expected.RootElement.GetProperty("video");
        var video = Assert.Single(actual.Videos.Rules).Value;
        Assert.Equal(expectedVideo.GetProperty("winGame").GetBoolean(), video.WinGame);
        Assert.Equal(expectedVideo.GetProperty("useUfoAudioSequence").GetBoolean(), video.UseUfoAudioSequence);
        Assert.Equal(expectedVideo.GetProperty("videos").EnumerateArray().Select(item => item.GetString()), video.Videos);
        var expectedSlide = expectedVideo.GetProperty("slide");
        var slide = Assert.Single(video.Slides);
        Assert.Equal(expectedSlide.GetProperty("width").GetInt32(), slide.Width);
        Assert.Equal(expectedSlide.GetProperty("height").GetInt32(), slide.Height);
        Assert.Equal(expectedSlide.GetProperty("x").GetInt32(), slide.X);
        Assert.Equal(expectedSlide.GetProperty("y").GetInt32(), slide.Y);
        Assert.Equal(expectedSlide.GetProperty("color").GetInt32(), slide.Color);

        var strings = actual.Special.Strings["en-US"];
        foreach (var property in expected.RootElement.GetProperty("strings").EnumerateObject())
        {
            Assert.Equal(property.Value.GetString(), strings[property.Name]);
        }
        var expectedSprite = expected.RootElement.GetProperty("sprite");
        var sprite = Assert.Single(actual.Special.Sprites["SET"]);
        Assert.Equal(expectedSprite.GetProperty("singleImage").GetBoolean(), sprite.SingleImage);
        Assert.Equal(expectedSprite.GetProperty("width").GetInt32(), sprite.Width);
        Assert.Equal(expectedSprite.GetProperty("height").GetInt32(), sprite.Height);
        Assert.Equal(expectedSprite.GetProperty("file").GetString(), sprite.Files[0]);
        Assert.DoesNotContain(diagnostics.Snapshot(), item => item.Severity >= DiagnosticSeverity.Error);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Oxce.slnx")))
        {
            directory = directory.Parent;
        }
        return directory?.FullName ?? throw new DirectoryNotFoundException("Could not locate the repository root.");
    }
}

using Oxce.Mods.Files;
using Oxce.ResourceBrowser;
using Xunit;

namespace Oxce.CompatibilityTests;

public sealed class PrivateResourceBrowserTests
{
    [Theory]
    [InlineData("UFO")]
    [InlineData("TFTD")]
    public void OwnedInstallationsProduceGeoscapeAndBattlescapePreview(string game)
    {
        var root = Oxce.FixtureSupport.FixturePaths.FindRepositoryRoot();
        var gameData = Path.Combine(root, "data", game);
        Assert.SkipUnless(
            Directory.Exists(gameData),
            $"Owned {game} assets are not available in this checkout.");
        var catalog = new VirtualFileCatalog(
            [VirtualFileLayer.ScanDirectory(gameData, game.ToLowerInvariant(), options: new DirectoryScanOptions { IgnoreRulesets = true })]);

        var preview = ResourcePreviewBuilder.Build(catalog);

        Assert.Equal(ResourcePreviewBuilder.PreviewWidth, preview.Surface.Width);
        Assert.Equal(ResourcePreviewBuilder.PreviewHeight, preview.Surface.Height);
        Assert.Equal(17, preview.CursorFrameCount);
        Assert.True(preview.TerrainFrameCount > 1);
        Assert.Contains(preview.Surface.Pixels.ToArray(), pixel => pixel != 0);
        using var output = new MemoryStream();
        ResourcePreviewBuilder.WritePortablePixmap(preview, output);
        Assert.True(output.Length > preview.Surface.Pixels.Length * 3);
    }

}

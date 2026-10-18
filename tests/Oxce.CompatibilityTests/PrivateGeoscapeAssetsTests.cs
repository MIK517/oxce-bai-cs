using Oxce.Engine.Geoscape;
using Oxce.Mods.Bootstrap;
using Oxce.TestSupport;
using Xunit;

namespace Oxce.CompatibilityTests;

public sealed class PrivateGeoscapeAssetsTests
{
    [Theory]
    [InlineData("xcom1", "-", 239, 162, 3)]
    [InlineData("xcom2", "-", 245, 111, 3)]
    // ROSIGMA replaces the globe markers with 7x7 frames.
    [InlineData("40k", "40k_ROSIGMA_edits", -1, -1, 7)]
    public void OwnedInstallationsProvideTheGlobeTexturesMarkersAndPalette(string master, string addon,
        int countryColor, int lineColor, int markerSize)
    {
        var installation = TestFixtures.RequirePrivateInstallation();
        var request = InstallationLoadRequest.ForMasterAndAddOn(installation, master, addon, new("Extended", "8.6.1.0"));
        var loaded = InstallationContentLoader.Load(request, cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(loaded.IsSuccess, loaded.DescribeFailure());
        var content = loaded.Content!;
        var globe = content.RuntimeRules.Campaign.Globe;

        var assets = GeoscapeAssets.Load(loaded.VirtualFiles!, content.Resources, globe);

        // GEOGRAPH/TEXTURE.DAT holds 13 textures at three zoom detail levels.
        Assert.True(assets.Textures.Count >= 39, $"Only {assets.Textures.Count} globe texture frames.");
        Assert.All(Enumerable.Range(0, 39), frame => Assert.True(assets.Textures.ContainsKey(frame)));
        Assert.All(globe.Polygons, polygon => Assert.True(assets.Textures.ContainsKey(polygon.Texture),
            $"Polygon texture {polygon.Texture} has no frame."));
        Assert.All(Enumerable.Range(0, 9), frame =>
        {
            Assert.True(assets.Markers.TryGetValue(frame, out var marker), $"Marker {frame} is missing.");
            Assert.Equal((markerSize, markerSize), (marker!.Width, marker.Height));
        });
        Assert.NotEmpty(globe.Polylines);
        Assert.Contains(Enumerable.Range(1, 255), index => assets.Palette[(byte)index].Red + assets.Palette[(byte)index].Green +
            assets.Palette[(byte)index].Blue != 0);
        if (countryColor >= 0) Assert.Equal((countryColor, lineColor), (assets.Colors.CountryLabel, assets.Colors.Line));
    }
}

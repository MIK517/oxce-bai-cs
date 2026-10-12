using System.Collections.ObjectModel;
using Oxce.Engine.Geoscape;
using Oxce.Formats.Terrain;
using Oxce.Gameplay.Campaigns;
using Oxce.Mods.Rulesets.Runtime;
using Oxce.Rendering;
using Xunit;

namespace Oxce.UnitTests.Engine;

public sealed class GlobeRendererTests
{
    private static readonly CampaignTime Noon = new(1, 1, 1, 1999, 12, 0, 0);
    private static readonly CampaignTime Midnight = new(1, 1, 1, 1999, 0, 0, 0);

    [Fact]
    public void TexturedPolygonsFollowTheSdlGfxSpanRuleAndTileInScreenSpace()
    {
        var surface = new IndexedSurface(6, 6);
        var texture = new IndexedSurface(2, 2);
        texture.Pixels[0] = 1;
        texture.Pixels[1] = 2;
        texture.Pixels[2] = 3;
        texture.Pixels[3] = 4;
        IndexedPoint[] square = [new(0, 0), new(3, 0), new(3, 3), new(0, 3)];

        surface.FillTexturedPolygon(square, texture, 1, 0, new int[4]);

        // Both bottom and right edges are inclusive; x is sampled at (x - dx) mod width.
        for (var y = 0; y < 6; y++)
            for (var x = 0; x < 6; x++)
                Assert.Equal(x <= 3 && y <= 3 ? texture.GetPixel((x + 1) % 2, y % 2) : (byte)0, surface.GetPixel(x, y));
        Assert.Throws<ArgumentException>(() => surface.FillTexturedPolygon(square, texture, 0, 0, new int[3]));

        // Colour 0 is the texture's colour key: the surface underneath shows through.
        surface.Clear(9);
        texture.Pixels[0] = 0;
        surface.FillTexturedPolygon(square, texture, 0, 0, new int[4]);
        Assert.Equal(9, surface.GetPixel(0, 0));
        Assert.Equal(2, surface.GetPixel(1, 0));
    }

    [Fact]
    public void MarkerShadesWrappingToZeroLeaveTheGlobeVisible()
    {
        var view = new GlobeView(64, 64);
        var marker = new IndexedSurface(3, 3);
        marker.Clear(255);
        var renderer = new GlobeRenderer(view, new GeoscapeAssets(IndexedPalette.CreateGrayscale(),
            new Dictionary<int, IndexedSurface>(), new Dictionary<int, IndexedSurface> { [0] = marker },
            RuntimeGlobeColors.Default), Globe());
        renderer.Surface.Clear(7);
        renderer.DrawMarker(new GlobeMarker(0, 0, 0), blinkOn: false);
        Assert.Equal(7, renderer.Surface.GetPixel(32, 32));
        renderer.DrawMarker(new GlobeMarker(0, 0, 0), blinkOn: true);
        Assert.Equal(255, renderer.Surface.GetPixel(32, 32));
    }

    [Theory]
    [InlineData(3)]
    [InlineData(7)]
    public void MarkersCentreOnTheirMiddleCellAtEveryScale(int size)
    {
        // Globe::drawTarget blits at x - width / 2 in reference pixels; at 2x each cell is 2x2.
        var view = new GlobeView(128, 128, pixelScale: 2);
        var marker = new IndexedSurface(size, size);
        marker.Clear(20);
        marker.GetRow(size / 2)[size / 2] = 90;
        var renderer = new GlobeRenderer(view, new GeoscapeAssets(IndexedPalette.CreateGrayscale(),
            new Dictionary<int, IndexedSurface>(), new Dictionary<int, IndexedSurface> { [0] = marker },
            RuntimeGlobeColors.Default), Globe());
        renderer.Surface.Clear(7);
        renderer.DrawMarker(new GlobeMarker(0, 0, 0), blinkOn: true);
        var (x, y) = view.Project(0, 0);
        Assert.Equal([90, 90, 90, 90], new[]
        {
            renderer.Surface.GetPixel(x, y), renderer.Surface.GetPixel(x + 1, y),
            renderer.Surface.GetPixel(x, y + 1), renderer.Surface.GetPixel(x + 1, y + 1),
        });
        Assert.Equal(20, renderer.Surface.GetPixel(x - 2, y - 2));
    }

    [Fact]
    public void ShadowRangesFromLitToNightForEveryNoiseValue()
    {
        var sun = (1.0, 0.0, 0.0);
        for (short noise = 0; noise < 4096; noise++)
        {
            Assert.Equal(0, GlobeShading.ShadowValue(1, 0, 0, sun, noise));
            Assert.Equal(31, GlobeShading.ShadowValue(-1, 0, 0, sun, noise));
            Assert.InRange(GlobeShading.ShadowValue(0, 0, 1, sun, noise), 1, 30);
        }
        // Golden values from the C++ GlobeStaticData and getShadowValue formulas.
        Assert.Equal(((short)-1, (short)32, (short)0), GlobeShading.Table(0));
        Assert.Equal(((short)11, (short)10, (short)21), GlobeShading.Table(120));
        Assert.Equal(((short)14, (short)16, (short)16), GlobeShading.Table(127));
        Assert.Equal(((short)17, (short)16, (short)0), GlobeShading.Table(128));
        Assert.Equal(((short)29, (short)2, (short)22), GlobeShading.Table(200));
        Assert.Equal(((short)32, (short)32, (short)0), GlobeShading.Table(255));
        Assert.Equal(17, GlobeShading.ShadowValue(0, 0, 1, sun, 0));
        Assert.Equal(11, GlobeShading.ShadowValue(0, 0, 1, sun, 0xFFF));
        Assert.Equal(17, GlobeShading.ShadowValue(0, 0, 1, sun, 0x123));
        Assert.Equal(1, GlobeShading.ShadowValue(0.3, 0.2, Math.Sqrt(1 - 0.13), sun, 0x7A5));
        Assert.Equal(0x2D, GlobeShading.LandShadow(0x23, 30));
        Assert.Equal(0x2F, GlobeShading.LandShadow(0x2E, 31));
        Assert.Equal(0x2E, GlobeShading.LandShadow(0x2E, 0));
    }

    [Fact]
    public void SunFollowsGameTimeDaylightAndSeasons()
    {
        // GameTime::getDaylight is offset by 18 hours: at noon the sun faces longitude 0.
        var (x, y, z) = GlobeShading.SunDirection(Noon, 0, 0, seasons: false);
        Assert.Equal(0, x, 12);
        Assert.Equal(0, y, 12);
        Assert.Equal(1, z, 12);
        (x, _, _) = GlobeShading.SunDirection(Noon with { Hour = 6 }, 0, 0, seasons: false);
        Assert.Equal(1, x, 12);
        var summer = GlobeShading.SunDirection(Noon with { Month = 6, Day = 21 }, 0, 0, seasons: true);
        var winter = GlobeShading.SunDirection(Noon with { Month = 12, Day = 21 }, 0, 0, seasons: true);
        // Northern summer moves the sun north (negative latitude is north), winter south.
        Assert.True(summer.Y < -0.2);
        Assert.True(winter.Y > 0.2);
        Assert.Equal(1, summer.X * summer.X + summer.Y * summer.Y + summer.Z * summer.Z, 12);
    }

    [Fact]
    public void RendersLandOceanShadowAndBlinkingMarkersWithoutAllocating()
    {
        var view = new GlobeView(512, 400, 2);
        var renderer = new GlobeRenderer(view, Assets(), Globe());
        var scene = GlobeSceneFor(new GlobeMarker(0.01, 0, GlobeScene.BaseMarker));

        renderer.Render(scene, Noon, new GlobeRenderOptions(), blinkOn: true);

        var surface = renderer.Surface;
        // Zoom 0 uses the coarsest of three texture levels: frame texture + 2 * (3 / 3).
        Assert.Equal(0x61, surface.GetPixel(256, 230));
        Assert.Equal(192, surface.GetPixel(256 + 120, 200));
        Assert.Equal(0, surface.GetPixel(256 + 182, 200));
        Assert.Equal(0, surface.GetPixel(2, 2));
        var (markerX, markerY) = view.Project(0.01, 0);
        Assert.Equal(0x10, surface.GetPixel(markerX, markerY));
        Assert.Equal(0x10, surface.GetPixel(markerX - 1, markerY - 1));

        renderer.Render(scene, Noon, new GlobeRenderOptions(), blinkOn: false);
        Assert.Equal(0x11, surface.GetPixel(markerX, markerY));

        view.SetZoom(2);
        renderer.Render(scene, Midnight, new GlobeRenderOptions(), blinkOn: true);
        // Zoom 2 uses the middle level; at midnight land darkens within its colour group.
        var (landX, landY) = view.Project(0, 0.05);
        Assert.Equal(0x51 + 10, surface.GetPixel(landX, landY));
        Assert.InRange(surface.GetPixel(256 + 200, 200), 192 + 25, 192 + 31);

        view.Center(Math.PI, 0);
        renderer.Render(scene, Noon, new GlobeRenderOptions(), blinkOn: true);
        Assert.DoesNotContain(surface.Pixels.ToArray(), color => color is 0x41 or 0x51 or 0x61 or 0x10 or 0x11);

        var options = new GlobeRenderOptions();
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var frame = 0; frame < 3; frame++) renderer.Render(scene, Noon, options, blinkOn: frame % 2 == 0);
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }

    [Fact]
    public void RadarFlightAndDetailLayersShadeFromTheGlobeAndFollowZoom()
    {
        var view = new GlobeView(512, 400, 2);
        var assets = Assets(withCity: true);
        var globe = Globe() with
        {
            Polylines = [new RuntimeGlobePolyline([new(-0.1, 0.1), new(0.1, 0.1)])],
        };
        var renderer = new GlobeRenderer(view, assets, globe, IndexedInterfaceFont.Create());
        var scene = GlobeScene.FromParts([],
            [new GlobeCircle(0, 0, 0.5, 48)],
            [new GlobePath(-0.2, 0, 0.2, 0)],
            [new GlobeLabel(0, -0.2, "CITY", 138, 3, 100, 2, City: true)]);

        renderer.Render(scene, Noon, new GlobeRenderOptions(), blinkOn: true);
        var surface = renderer.Surface;
        // Flight paths over lit land take the land shade (+8 within the group) of the unshaded globe.
        Assert.Equal(0x61 + 8, surface.GetPixel(256, 200));
        // The radar ring crosses lit ocean: ocean colour + 6 + 8.
        Assert.Contains(Around(surface, view.Project(0, 0.5)), color => color == 192 + 14);
        // Borders only from zoom 1, labels and city markers only from zoom 3.
        Assert.DoesNotContain(Around(surface, view.Project(0, 0.1)), color => color == 162);
        Assert.NotEqual(0x30, surface.GetPixel(view.Project(0, -0.2).X, view.Project(0, -0.2).Y));

        renderer.Render(scene, Noon, new GlobeRenderOptions(RadarLines: false, FlightPaths: false), blinkOn: true);
        Assert.Equal(0x61, surface.GetPixel(256, 200));
        Assert.DoesNotContain(Around(surface, view.Project(0, 0.5)), color => color == 192 + 14);

        view.SetZoom(1);
        renderer.Render(scene, Noon, new GlobeRenderOptions(), blinkOn: true);
        Assert.Contains(Around(surface, view.Project(0, 0.1)), color => color == 162);
        renderer.Render(scene, Noon, new GlobeRenderOptions(Detail: false), blinkOn: true);
        Assert.DoesNotContain(Around(surface, view.Project(0, 0.1)), color => color == 162);

        view.SetZoom(3);
        renderer.Render(scene, Noon, new GlobeRenderOptions(), blinkOn: false);
        var (cityX, cityY) = view.Project(0, -0.2);
        // City markers never blink; the label below is coloured 138 + glyph value.
        Assert.Equal(0x30, surface.GetPixel(cityX, cityY));
        var labelPixels = 0;
        for (var y = cityY + 4; y < cityY + 16; y++)
            for (var x = cityX - 60; x < cityX + 60; x++)
                if (surface.GetPixel(x, y) is > 138 and < 138 + 16) labelPixels++;
        Assert.True(labelPixels > 5);

        var options = new GlobeRenderOptions();
        var craftRange = new GlobeCircle(0, 0, 0.4, 64);
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var frame = 0; frame < 3; frame++) renderer.Render(scene, Noon, options, frame % 2 == 0, craftRange);
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }

    private static IEnumerable<byte> Around(IndexedSurface surface, (int X, int Y) point)
    {
        for (var y = point.Y - 2; y <= point.Y + 2; y++)
            for (var x = point.X - 2; x <= point.X + 2; x++)
                yield return surface.GetPixel(x, y);
    }

    private static RuntimeGlobe Globe() => new(
        [new WorldPolygon(0, [new(-0.3, -0.3), new(0.3, -0.3), new(0.3, 0.3), new(-0.3, 0.3)])],
        new ReadOnlyDictionary<int, RuntimeGlobeTexture>(new Dictionary<int, RuntimeGlobeTexture>()));

    private static GeoscapeAssets Assets(bool withCity = false)
    {
        var textures = new Dictionary<int, IndexedSurface>();
        for (var frame = 0; frame < 3; frame++)
        {
            var texture = new IndexedSurface(32, 32);
            texture.Clear((byte)(0x41 + frame * 16));
            textures[frame] = texture;
        }
        var marker = new IndexedSurface(3, 3);
        marker.Clear(0x10);
        var markers = new Dictionary<int, IndexedSurface> { [GlobeScene.BaseMarker] = marker };
        if (withCity)
        {
            var city = new IndexedSurface(3, 3);
            city.Clear(0x30);
            markers[GlobeScene.CityMarker] = city;
        }
        return new GeoscapeAssets(IndexedPalette.CreateGrayscale(), textures, markers, RuntimeGlobeColors.Default);
    }

    private static GlobeScene GlobeSceneFor(params GlobeMarker[] markers) => GlobeScene.FromMarkers(markers);
}

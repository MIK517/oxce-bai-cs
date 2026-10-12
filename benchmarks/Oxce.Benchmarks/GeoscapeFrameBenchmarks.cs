using System.Collections.ObjectModel;
using BenchmarkDotNet.Attributes;
using Oxce.Core.Random;
using Oxce.Engine;
using Oxce.Engine.Geoscape;
using Oxce.Engine.Input;
using Oxce.Formats.Terrain;
using Oxce.Gameplay.Campaigns;
using Oxce.Mods.Rulesets.Runtime;
using Oxce.Rendering;
using static Oxce.Benchmarks.PublicCampaignFixtures;

namespace Oxce.Benchmarks;

/// <summary>
/// Geoscape frame construction (performance target: interactive frame p95 &lt;= 8 ms). The globe
/// workload is a synthetic world of 1,152 textured polygons (the size class of xcom1's
/// WORLD.DAT) with 64 markers, 16 radar circles, 12 flight paths and 48 labels at dusk; the client
/// workload redraws the whole 640x400 geoscape with the Intercept list open over a public
/// campaign holding 32 detected UFOs.
/// </summary>
[MemoryDiagnoser]
public class GeoscapeFrameBenchmarks
{
    private GlobeRenderer _renderer = null!;
    private GlobeScene _scene = null!;
    private GeoscapeClient _client = null!;
    private static readonly CampaignTime Dusk = new(1, 1, 3, 1999, 18, 30, 0);

    /// <summary>Globe zoom level: the closest views draw the most land and detail.</summary>
    [Params(0, 5)]
    public int Zoom { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        var assets = Assets();
        var view = new GlobeView(GeoscapeClient.GlobeWidth, GeoscapeClient.Height, GeoscapeClient.PixelScale);
        view.Center(0.4, 0.3);
        view.SetZoom(Zoom);
        _renderer = new GlobeRenderer(view, assets, Globe(), IndexedInterfaceFont.Create());
        var random = new Random(7);
        _scene = GlobeScene.FromParts(
            Enumerable.Range(0, 64).Select(index => new GlobeMarker(random.NextDouble() * Math.Tau,
                random.NextDouble() * 2.4 - 1.2, index % 9)),
            Enumerable.Range(0, 16).Select(index => new GlobeCircle(random.NextDouble() * Math.Tau,
                random.NextDouble() * 2 - 1, 0.1 + random.NextDouble() * 0.3, index % 2 == 0 ? 48 : 24)),
            Enumerable.Range(0, 12).Select(_ => new GlobePath(random.NextDouble() * Math.Tau, random.NextDouble() - 0.5,
                random.NextDouble() * Math.Tau, random.NextDouble() - 0.5)),
            Enumerable.Range(0, 48).Select(index => new GlobeLabel(random.NextDouble() * Math.Tau, random.NextDouble() * 2 - 1,
                "COUNTRY " + index, 239, index % 2 == 0 ? 2 : 0, 0, 0)));

        var content = LoadContent(["strategic-world.rul"]);
        var snapshot = PlacedCampaign(content, Dusk);
        snapshot = snapshot with
        {
            World = snapshot.World with
            {
                Missions = [Mission(4, "MISSION_SCOUT", 32)],
                Ufos = [.. Enumerable.Range(0, 32).Select(index => FlyingUfo(100 + index, index + 1, "UFO_HUNTER", 4,
                    0.1 + index * 0.05, 0.2 - index * 0.01, 2200) with { Detected = true })],
            },
        };
        var campaign = CampaignState.Restore(snapshot, content, new SplitMix64RandomSource(3));
        _client = new GeoscapeClient(new GeoscapeClientSetup(CampaignUiSession.ForCampaign(campaign, campaign, campaign),
            content.RuntimeRules, assets, IndexedInterfaceFont.Create(), static text => text)
        {
            Stopwatch = static () => TimeSpan.Zero,
        });
        _client.View.SetZoom(Zoom);
        _client.HandleInput(GameInputEvent.Key(GameInputEventKind.KeyPressed, 0, 0, 0, 'i', InputKeyModifiers.None));
        if (_client.Popups.Count == 0) throw new InvalidOperationException("The Intercept list did not open.");
    }

    /// <summary>The globe layers alone: land, shadow, radars, flights, detail and markers.</summary>
    [Benchmark]
    public void RenderBusyGlobe() => _renderer.Render(_scene, Dusk, new GlobeRenderOptions(), blinkOn: true);

    /// <summary>
    /// A whole geoscape frame: with a popup open time stands still, and each 100 ms blink redraws
    /// the globe, the sidebar and the popup.
    /// </summary>
    [Benchmark]
    public void RedrawGeoscapeWithPopup() => _client.Tick(TimeSpan.FromMilliseconds(100));

    private static RuntimeGlobe Globe()
    {
        // 48 x 24 quads over the sphere, cycling the 13 base textures.
        var polygons = new List<WorldPolygon>();
        const int columns = 48, rows = 24;
        for (var row = 0; row < rows; row++)
        {
            var south = -Math.PI / 2 + row * Math.PI / rows;
            var north = south + Math.PI / rows;
            for (var column = 0; column < columns; column++)
            {
                var west = column * Math.Tau / columns;
                var east = west + Math.Tau / columns;
                polygons.Add(new WorldPolygon((row * columns + column) % 13,
                    [new(west, south), new(east, south), new(east, north), new(west, north)]));
            }
        }
        return new RuntimeGlobe(polygons, new ReadOnlyDictionary<int, RuntimeGlobeTexture>(new Dictionary<int, RuntimeGlobeTexture>()));
    }

    private static GeoscapeAssets Assets()
    {
        var textures = new Dictionary<int, IndexedSurface>();
        for (var frame = 0; frame < 39; frame++)
        {
            var texture = new IndexedSurface(GeoscapeAssets.TextureSize, GeoscapeAssets.TextureSize);
            var pixels = texture.Pixels;
            for (var index = 0; index < pixels.Length; index++) pixels[index] = (byte)(0x40 + (index + frame) % 32);
            textures[frame] = texture;
        }
        var markers = new Dictionary<int, IndexedSurface>();
        for (var frame = 0; frame < 9; frame++)
        {
            var marker = new IndexedSurface(3, 3);
            marker.Clear((byte)(0x10 + frame * 8));
            markers[frame] = marker;
        }
        return new GeoscapeAssets(IndexedPalette.CreateGrayscale(), textures, markers, RuntimeGlobeColors.Default);
    }
}

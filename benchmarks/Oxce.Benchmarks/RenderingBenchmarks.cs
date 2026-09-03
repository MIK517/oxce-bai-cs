using BenchmarkDotNet.Attributes;
using Oxce.Rendering;

namespace Oxce.Benchmarks;

[MemoryDiagnoser]
public class RenderingBenchmarks
{
    private IndexedSurface _frame = null!;
    private IndexedSurface _opaqueSource = null!;
    private IndexedSurface _transparentSprite = null!;
    private IndexedPalette _palette = null!;
    private byte[] _rgba = null!;
    private IndexedPoint[] _polygon = null!;

    [GlobalSetup]
    public void Setup()
    {
        _frame = new IndexedSurface(640, 400);
        _opaqueSource = new IndexedSurface(320, 200);
        FillPattern(_opaqueSource, includeTransparency: false);
        _transparentSprite = new IndexedSurface(64, 80);
        FillPattern(_transparentSprite, includeTransparency: true);
        _palette = IndexedPalette.CreateGrayscale();
        _rgba = new byte[_frame.Pixels.Length * IndexedFrameConverter.RgbaBytesPerPixel];
        _polygon =
        [
            new IndexedPoint(80, 40), new IndexedPoint(560, 30), new IndexedPoint(620, 180),
            new IndexedPoint(500, 360), new IndexedPoint(120, 380), new IndexedPoint(20, 190),
        ];
        FillPattern(_frame, includeTransparency: false);
    }

    [Benchmark]
    public void OpaqueScreenBlit() => _frame.Blit(_opaqueSource, 160, 100, transparent: false);

    [Benchmark]
    public void TransparentSpriteBlit() => _frame.Blit(_transparentSprite, 288, 160);

    [Benchmark]
    public void ConvertIndexedFrameToRgba() =>
        IndexedFrameConverter.ConvertToRgba32(_frame, _palette, _rgba);

    [Benchmark]
    public void FillSixPointPolygon() => _frame.FillPolygon(_polygon, 42);

    private static void FillPattern(IndexedSurface surface, bool includeTransparency)
    {
        var pixels = surface.Pixels;
        for (var index = 0; index < pixels.Length; ++index)
        {
            pixels[index] = includeTransparency && index % 4 == 0
                ? (byte)0
                : (byte)((index * 31 % 255) + 1);
        }
    }
}

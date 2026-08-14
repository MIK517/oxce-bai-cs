using Oxce.Platform.Sdl;
using Oxce.Rendering;

if (args.Length == 1 && string.Equals(args[0], "--sdl-smoke", StringComparison.Ordinal))
{
    const int width = 160;
    const int height = 100;
    var surface = new IndexedSurface(width, height);
    for (var y = 0; y < height; ++y)
    {
        var row = surface.GetRow(y);
        for (var x = 0; x < width; ++x)
        {
            row[x] = (byte)((x + y) & 0xff);
        }
    }

    SdlIndexedFramePresenter.ShowFrame(
        surface,
        IndexedPalette.CreateGrayscale(),
        "OXCE .NET SDL3 event-loop smoke",
        scale: 4,
        duration: TimeSpan.FromSeconds(2));
    Console.WriteLine("SDL3 indexed-window event loop completed.");
    return;
}

Console.WriteLine("OXCE .NET compatibility port");
Console.WriteLine("Use --sdl-smoke to run the indexed-window SDL3 event-loop smoke test.");

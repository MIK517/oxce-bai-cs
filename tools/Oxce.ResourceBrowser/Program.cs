using Oxce.Mods.Files;
using Oxce.Platform.Sdl;
using Oxce.ResourceBrowser;

try
{
    var options = BrowserOptions.Parse(args);
    if (options.Roots.Count == 0)
    {
        BrowserOptions.WriteUsage(Console.Error);
        return 2;
    }

    var layers = options.Roots.Select((root, index) =>
        VirtualFileLayer.ScanDirectory(
            root,
            $"layer-{index}",
            options: new DirectoryScanOptions { IgnoreRulesets = true }));
    var catalog = new VirtualFileCatalog(layers);
    var preview = ResourcePreviewBuilder.Build(catalog);
    Console.WriteLine($"Loaded {catalog.Layers.Count} resource layer(s).");
    Console.WriteLine($"Palette:   {preview.PaletteEntry.CanonicalPath} <- {preview.PaletteEntry.SourcePath}");
    Console.WriteLine($"Geoscape:  {preview.GeoscapeEntry.CanonicalPath} <- {preview.GeoscapeEntry.SourcePath}");
    Console.WriteLine($"Battlescape cursors: {preview.CursorFrameCount} frame(s) <- {preview.CursorEntry.SourcePath}");
    Console.WriteLine($"Battlescape terrain: {preview.TerrainFrameCount} frame(s) <- {preview.TerrainEntry.SourcePath}");

    if (options.OutputPath is not null)
    {
        var outputPath = Path.GetFullPath(options.OutputPath);
        using var output = new FileStream(outputPath, FileMode.Create, FileAccess.Write, FileShare.None);
        ResourcePreviewBuilder.WritePortablePixmap(preview, output);
        Console.WriteLine($"Wrote preview: {outputPath}");
    }

    if (options.Show)
    {
        SdlIndexedFramePresenter.ShowFrame(
            preview.Surface,
            preview.Palette,
            "OXCE resource browser",
            scale: options.Scale,
            duration: options.Duration);
    }

    return 0;
}
catch (Exception error) when (error is
    ArgumentException or
    FormatException or
    IOException or
    OverflowException or
    SdlException or
    UnauthorizedAccessException)
{
    Console.Error.WriteLine(error.Message);
    return 1;
}

internal sealed class BrowserOptions
{
    private BrowserOptions(
        IReadOnlyList<string> roots,
        string? outputPath,
        bool show,
        int scale,
        TimeSpan duration)
    {
        Roots = roots;
        OutputPath = outputPath;
        Show = show;
        Scale = scale;
        Duration = duration;
    }

    public IReadOnlyList<string> Roots { get; }

    public string? OutputPath { get; }

    public bool Show { get; }

    public int Scale { get; }

    public TimeSpan Duration { get; }

    public static BrowserOptions Parse(string[] args)
    {
        var roots = new List<string>();
        string? output = null;
        var show = false;
        var scale = 2;
        var duration = TimeSpan.FromSeconds(10);
        for (var index = 0; index < args.Length; index++)
        {
            switch (args[index])
            {
                case "--root":
                    roots.Add(ReadValue(args, ref index, "--root"));
                    break;
                case "--output":
                    output = ReadValue(args, ref index, "--output");
                    break;
                case "--show":
                    show = true;
                    break;
                case "--scale":
                    scale = int.Parse(ReadValue(args, ref index, "--scale"), System.Globalization.CultureInfo.InvariantCulture);
                    ArgumentOutOfRangeException.ThrowIfNegativeOrZero(scale);
                    break;
                case "--duration":
                    var seconds = double.Parse(ReadValue(args, ref index, "--duration"), System.Globalization.CultureInfo.InvariantCulture);
                    duration = TimeSpan.FromSeconds(seconds);
                    if (duration < TimeSpan.Zero)
                    {
                        throw new ArgumentOutOfRangeException(nameof(args), "Duration cannot be negative.");
                    }

                    break;
                default:
                    throw new ArgumentException($"Unknown resource-browser option '{args[index]}'.", nameof(args));
            }
        }

        return new BrowserOptions(roots, output, show, scale, duration);
    }

    public static void WriteUsage(TextWriter output)
    {
        output.WriteLine("Usage: Oxce.ResourceBrowser --root <game-data> [--root <overlay>] [--output <preview.ppm>] [--show] [--scale <n>] [--duration <seconds>]");
        output.WriteLine("Roots are layered in command-line order; later roots override earlier resources.");
    }

    private static string ReadValue(string[] args, ref int index, string option)
    {
        if (++index >= args.Length)
        {
            throw new ArgumentException($"Option '{option}' requires a value.", nameof(args));
        }

        return args[index];
    }
}

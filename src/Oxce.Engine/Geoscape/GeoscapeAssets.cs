using System.Collections.Frozen;
using Oxce.Formats.Binary;
using Oxce.Formats.Images;
using Oxce.Mods.Files;
using Oxce.Mods.Resources;
using Oxce.Mods.Rulesets.Runtime;
using Oxce.Rendering;
using Oxce.Resources;

namespace Oxce.Engine.Geoscape;

/// <summary>
/// Indexed resources the globe draws with. Reference: Mod::loadVanillaResources (PAL_GEOSCAPE,
/// GEOGRAPH/TEXTURE.DAT as 32x32 frames) and ExtraSprites::loadSurfaceSet for mod frames,
/// including the <c>GlobeMarkers</c> set that the standard mods supply.
/// </summary>
public sealed class GeoscapeAssets
{
    public const int TextureSize = 32;
    public const int MarkerSize = 3;
    public const string TextureSet = "TEXTURE.DAT";
    public const string MarkerSet = "GlobeMarkers";
    private const string PalettePath = "GEODATA/PALETTES.DAT";
    private const string TexturePath = "GEOGRAPH/TEXTURE.DAT";

    public GeoscapeAssets(IndexedPalette palette, IReadOnlyDictionary<int, IndexedSurface> textures,
        IReadOnlyDictionary<int, IndexedSurface> markers, RuntimeGlobeColors colors)
    {
        ArgumentNullException.ThrowIfNull(palette);
        ArgumentNullException.ThrowIfNull(textures);
        ArgumentNullException.ThrowIfNull(markers);
        ArgumentNullException.ThrowIfNull(colors);
        Palette = palette;
        Textures = textures.ToFrozenDictionary();
        Markers = markers.ToFrozenDictionary();
        Colors = colors;
    }

    public IndexedPalette Palette { get; }

    /// <summary>Texture frames by runtime index; vanilla frames first, then mod frames.</summary>
    public FrozenDictionary<int, IndexedSurface> Textures { get; }

    /// <summary>Globe marker frames by runtime index.</summary>
    public FrozenDictionary<int, IndexedSurface> Markers { get; }

    public RuntimeGlobeColors Colors { get; }

    public static GeoscapeAssets Load(VirtualFileCatalog files, ResolvedResourceCatalog resources, RuntimeGlobe globe)
    {
        ArgumentNullException.ThrowIfNull(files);
        ArgumentNullException.ThrowIfNull(resources);
        ArgumentNullException.ThrowIfNull(globe);
        var vanillaPaths = new List<(string, string, ResourceKind, ResourceLoadPolicy)>();
        if (files.TryGet(PalettePath, out _))
            vanillaPaths.Add(("palette", PalettePath, ResourceKind.Palette, ResourceLoadPolicy.Cache));
        if (files.TryGet(TexturePath, out _))
            vanillaPaths.Add(("textures", TexturePath, ResourceKind.Binary, ResourceLoadPolicy.Cache));
        var vanilla = ResolvedResourceCatalog.FromPaths(files, vanillaPaths);
        using var vanillaRuntime = new ResourceRuntime(files, vanilla);
        using var modRuntime = new ResourceRuntime(files, resources);

        // PAL_GEOSCAPE is palette 0 of PALETTES.DAT.
        var palette = vanilla.TryGet("palette", out var paletteHandle)
            ? new IndexedPalette(XcomPaletteCodec.Decode(new BinaryDataReader(vanillaRuntime.LoadBytes(paletteHandle)),
                XcomPaletteCodec.ColorsPerPalette, XcomPaletteCodec.GetPaletteOffset(0)))
            : IndexedPalette.CreateGrayscale();

        var textures = new Dictionary<int, IndexedSurface>();
        if (vanilla.TryGet("textures", out var textureHandle))
            ReadDatFrames(vanillaRuntime.LoadBytes(textureHandle).Span, TextureSize, TextureSize, textures);
        var markers = new Dictionary<int, IndexedSurface>();
        AddExtraFrames(modRuntime, resources, TextureSet, textures);
        AddExtraFrames(modRuntime, resources, MarkerSet, markers);
        return new GeoscapeAssets(palette, textures, markers, globe.Colors);
    }

    /// <summary>SurfaceSet::loadDat: whole frames of row-major pixels; a trailing partial frame is ignored.</summary>
    public static void ReadDatFrames(ReadOnlySpan<byte> data, int width, int height, Dictionary<int, IndexedSurface> frames)
    {
        var frameBytes = checked(width * height);
        for (var frame = 0; (frame + 1) * frameBytes <= data.Length; frame++)
        {
            var surface = new IndexedSurface(width, height);
            data.Slice(frame * frameBytes, frameBytes).CopyTo(surface.Pixels);
            frames[frame] = surface;
        }
    }

    private static void AddExtraFrames(ResourceRuntime runtime, ResolvedResourceCatalog resources, string set,
        Dictionary<int, IndexedSurface> frames)
    {
        foreach (var descriptor in resources.Descriptors)
        {
            if (descriptor.Kind != ResourceKind.Sprite || descriptor.RuntimeIndex is not { } index ||
                descriptor.OwnerSection != "extraSprites" || descriptor.OwnerId != set) continue;
            var image = runtime.LoadIndexedImage(descriptor.Handle);
            // Surface::loadImage replaces a whole frame with the image at its own size.
            frames[index] = descriptor.SubFrame is { } cell
                ? Copy(image.Pixels.Span, image.Width, image.Height, cell.X, cell.Y, cell.Width, cell.Height)
                : Copy(image.Pixels.Span, image.Width, image.Height, 0, 0, image.Width, image.Height);
        }
    }

    /// <summary>Copies a cell of an image; cell pixels outside the image stay transparent.</summary>
    public static IndexedSurface Copy(ReadOnlySpan<byte> pixels, int imageWidth, int imageHeight,
        int x, int y, int width, int height)
    {
        var surface = new IndexedSurface(width, height);
        var columns = Math.Min(imageWidth - x, width);
        var rows = Math.Min(imageHeight - y, height);
        for (var row = 0; row < rows; row++)
        {
            if (columns <= 0) break;
            pixels.Slice((y + row) * imageWidth + x, columns).CopyTo(surface.GetRow(row));
        }
        return surface;
    }
}

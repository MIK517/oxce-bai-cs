using Oxce.Formats.Binary;
using Oxce.Formats.Images;
using Oxce.Mods.Files;
using Oxce.Mods.Resources;
using Oxce.Rendering;
using Oxce.Resources;

namespace Oxce.ResourceBrowser;

public static class ResourcePreviewBuilder
{
    public const int PreviewWidth = 640;
    public const int PreviewHeight = 240;

    public static ResourcePreview Build(VirtualFileCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        var terrainPath = FindTerrainPath(catalog);
        var terrainOffsetsPath = Path.ChangeExtension(terrainPath, ".tab").Replace('\\', '/');
        var resources = ResolvedResourceCatalog.FromPaths(catalog,
        [
            ("palette", "GEODATA/PALETTES.DAT", ResourceKind.Palette, ResourceLoadPolicy.Cache),
            ("geoscape", "GEOGRAPH/BACK01.SCR", ResourceKind.IndexedImage, ResourceLoadPolicy.Cache),
            ("cursor", "UFOGRAPH/CURSOR.PCK", ResourceKind.Sprite, ResourceLoadPolicy.Cache),
            ("cursor-offsets", "UFOGRAPH/CURSOR.TAB", ResourceKind.Binary, ResourceLoadPolicy.Cache),
            ("terrain", terrainPath, ResourceKind.Terrain, ResourceLoadPolicy.Cache),
            ("terrain-offsets", terrainOffsetsPath, ResourceKind.Binary, ResourceLoadPolicy.Cache),
        ]);
        using var runtime = new ResourceRuntime(catalog, resources);
        var paletteEntry = resources[resources.GetRequired("palette")];
        var geoscapeEntry = resources[resources.GetRequired("geoscape")];
        var cursorEntry = resources[resources.GetRequired("cursor")];
        var cursorOffsetsEntry = resources[resources.GetRequired("cursor-offsets")];
        var terrainEntry = resources[resources.GetRequired("terrain")];
        var terrainOffsetsEntry = resources[resources.GetRequired("terrain-offsets")];

        var colors = XcomPaletteCodec.Decode(new BinaryDataReader(runtime.LoadBytes(paletteEntry.Handle)), 256);
        var palette = new IndexedPalette(colors);
        var preview = new IndexedSurface(PreviewWidth, PreviewHeight);
        var geoscape = new IndexedSurface(320, 200);
        RawIndexedImageCodec.Decode(
            new BinaryDataReader(runtime.LoadBytes(geoscapeEntry.Handle)),
            geoscape.Pixels);
        preview.Blit(geoscape, 0, 0, transparent: false);

        var cursors = PckSpriteSetCodec.Decode(
            new BinaryDataReader(runtime.LoadBytes(cursorEntry.Handle)),
            new BinaryDataReader(runtime.LoadBytes(cursorOffsetsEntry.Handle)),
            width: 32,
            height: 40);
        const int cursorStartY = 200;
        for (var index = 0; index < cursors.Count; index++)
        {
            var x = index * 36;
            if (x + 32 > PreviewWidth)
            {
                break;
            }

            var cursor = new IndexedSurface(32, 40);
            cursors[index].CopyTo(cursor.Pixels);
            preview.Blit(cursor, x, cursorStartY);
        }

        var terrainFrames = PckSpriteSetCodec.Decode(
            new BinaryDataReader(runtime.LoadBytes(terrainEntry.Handle)),
            new BinaryDataReader(runtime.LoadBytes(terrainOffsetsEntry.Handle)),
            width: 32,
            height: 40);
        for (var index = 0; index < Math.Min(terrainFrames.Count, 60); index++)
        {
            var tile = new IndexedSurface(32, 40);
            terrainFrames[index].CopyTo(tile.Pixels);
            preview.Blit(tile, 320 + ((index % 10) * 32), (index / 10) * 40);
        }

        return new ResourcePreview(
            preview,
            palette,
            paletteEntry,
            geoscapeEntry,
            cursorEntry,
            cursorOffsetsEntry,
            cursors.Count,
            terrainEntry,
            terrainOffsetsEntry,
            terrainFrames.Count);
    }

    public static void WritePortablePixmap(ResourcePreview preview, Stream output)
    {
        ArgumentNullException.ThrowIfNull(preview);
        ArgumentNullException.ThrowIfNull(output);
        if (!output.CanWrite)
        {
            throw new ArgumentException("Preview output stream must be writable.", nameof(output));
        }

        using var writer = new BinaryWriter(output, System.Text.Encoding.ASCII, leaveOpen: true);
        writer.Write(System.Text.Encoding.ASCII.GetBytes(
            $"P6\n{preview.Surface.Width} {preview.Surface.Height}\n255\n"));
        foreach (var pixel in preview.Surface.Pixels)
        {
            var color = preview.Palette[pixel];
            writer.Write(color.Red);
            writer.Write(color.Green);
            writer.Write(color.Blue);
        }
    }

    private static string FindTerrainPath(VirtualFileCatalog catalog)
    {
        foreach (var name in catalog.List("terrain"))
        {
            if (!name.EndsWith(".pck", StringComparison.Ordinal)
                || string.Equals(name, "blanks.pck", StringComparison.Ordinal))
            {
                continue;
            }

            var path = $"terrain/{name}";
            var offsets = Path.ChangeExtension(path, ".tab").Replace('\\', '/');
            if (catalog.TryGet(offsets, out _))
            {
                return catalog.GetRequired(path).CanonicalPath;
            }
        }

        throw new FileNotFoundException("No paired TERRAIN PCK/TAB resource was found.");
    }
}

public sealed record ResourcePreview(
    IndexedSurface Surface,
    IndexedPalette Palette,
    ResolvedResourceDescriptor PaletteEntry,
    ResolvedResourceDescriptor GeoscapeEntry,
    ResolvedResourceDescriptor CursorEntry,
    ResolvedResourceDescriptor CursorOffsetsEntry,
    int CursorFrameCount,
    ResolvedResourceDescriptor TerrainEntry,
    ResolvedResourceDescriptor TerrainOffsetsEntry,
    int TerrainFrameCount);

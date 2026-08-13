using Oxce.Formats.Binary;
using Oxce.Formats.Images;
using Oxce.Mods.Files;
using Oxce.Rendering;

namespace Oxce.ResourceBrowser;

public static class ResourcePreviewBuilder
{
    public const int PreviewWidth = 640;
    public const int PreviewHeight = 240;

    public static ResourcePreview Build(VirtualFileCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        var paletteEntry = catalog.GetRequired("GEODATA/PALETTES.DAT");
        var geoscapeEntry = catalog.GetRequired("GEOGRAPH/BACK01.SCR");
        var cursorEntry = catalog.GetRequired("UFOGRAPH/CURSOR.PCK");
        var cursorOffsetsEntry = catalog.GetRequired("UFOGRAPH/CURSOR.TAB");
        var terrainEntry = FindTerrainEntry(catalog);
        var terrainOffsetsEntry = catalog.GetRequired(
            Path.ChangeExtension(terrainEntry.CanonicalPath, ".tab").Replace('\\', '/'));

        var colors = XcomPaletteCodec.Decode(BinaryDataReader.FromFile(paletteEntry.SourcePath), 256);
        var palette = new IndexedPalette(colors);
        var preview = new IndexedSurface(PreviewWidth, PreviewHeight);
        var geoscape = new IndexedSurface(320, 200);
        RawIndexedImageCodec.Decode(
            BinaryDataReader.FromFile(geoscapeEntry.SourcePath),
            geoscape.Pixels);
        preview.Blit(geoscape, 0, 0, transparent: false);

        var cursors = PckSpriteSetCodec.Decode(
            BinaryDataReader.FromFile(cursorEntry.SourcePath),
            BinaryDataReader.FromFile(cursorOffsetsEntry.SourcePath),
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
            BinaryDataReader.FromFile(terrainEntry.SourcePath),
            BinaryDataReader.FromFile(terrainOffsetsEntry.SourcePath),
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

    private static VirtualFileEntry FindTerrainEntry(VirtualFileCatalog catalog)
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
                return catalog.GetRequired(path);
            }
        }

        throw new FileNotFoundException("No paired TERRAIN PCK/TAB resource was found.");
    }
}

public sealed record ResourcePreview(
    IndexedSurface Surface,
    IndexedPalette Palette,
    VirtualFileEntry PaletteEntry,
    VirtualFileEntry GeoscapeEntry,
    VirtualFileEntry CursorEntry,
    VirtualFileEntry CursorOffsetsEntry,
    int CursorFrameCount,
    VirtualFileEntry TerrainEntry,
    VirtualFileEntry TerrainOffsetsEntry,
    int TerrainFrameCount);

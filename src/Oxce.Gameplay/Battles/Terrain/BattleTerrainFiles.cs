using Oxce.Formats.Binary;
using Oxce.Mods.Files;

namespace Oxce.Gameplay.Battles.Terrain;

/// <summary>
/// The installation files battles read (<c>FileMap::getIStream</c>): terrain data sets, map blocks and routes. The
/// host attaches them to the campaign; they are not campaign state and are never saved.
/// </summary>
public interface IBattleTerrainFiles
{
    /// <summary>The whole file at a virtual path such as <c>TERRAIN/CULTIVAT.MCD</c>, or null when it does not exist.</summary>
    ReadOnlyMemory<byte>? Read(string virtualPath);
}

/// <summary>Terrain files from the installation's virtual file catalog (mods override the master's files).</summary>
public sealed class VirtualBattleTerrainFiles(VirtualFileCatalog files) : IBattleTerrainFiles
{
    /// <summary>The largest terrain file the port reads; reference files are a few kilobytes.</summary>
    public const int MaximumFileBytes = 16 * 1024 * 1024;

    private readonly VirtualFileCatalog _files = files ?? throw new ArgumentNullException(nameof(files));

    public ReadOnlyMemory<byte>? Read(string virtualPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(virtualPath);
        if (!_files.TryGet(virtualPath, out var entry) || entry is null) return null;
        using var stream = entry.OpenRead();
        var reader = BinaryDataReader.FromStream(stream, MaximumFileBytes);
        return reader.ReadMemory(reader.Remaining);
    }
}

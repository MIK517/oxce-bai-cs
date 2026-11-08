using Oxce.Formats.Binary;
using Oxce.Formats.Terrain;

namespace Oxce.Gameplay.Battles.Terrain;

/// <summary>
/// The line of fire templates (<c>Mod::getVoxelData</c>, <c>GEODATA/LOFTEMPS.DAT</c>): 16 rows of 16 voxel bits for each template, which
/// terrain objects (twelve layers of two voxels each) and units (one template per tile) refer to. The reference reads it with
/// <c>vector::at</c>, which throws for a row past the file's end; the port checks the templates a battle uses before it uses them.
/// </summary>
internal sealed class BattleVoxelData(IReadOnlyList<ushort> values)
{
    /// <summary>The rows of one template.</summary>
    public const int RowsPerTemplate = 16;

    private readonly IReadOnlyList<ushort> _values = values ?? throw new ArgumentNullException(nameof(values));

    /// <summary>The virtual path of the file.</summary>
    public const string VirtualPath = "GEODATA/LOFTEMPS.DAT";

    public int Count => _values.Count;

    /// <summary>The number of whole templates.</summary>
    public int Templates => _values.Count / RowsPerTemplate;

    /// <summary>The row <paramref name="row"/> of a template: bit <c>15 - x</c> is the voxel at <c>x</c>.</summary>
    public int Row(int template, int row)
    {
        var index = template * RowsPerTemplate + row;
        return (uint)template < (uint)Templates && (uint)row < RowsPerTemplate
            ? _values[index] : throw new InvalidOperationException($"Line of fire template {template} row {row} lies outside {VirtualPath}.");
    }

    /// <summary>Whether a template exists (so every row can be read).</summary>
    public bool Has(int template) => (uint)template < (uint)Templates;

    internal static BattleVoxelData Decode(ReadOnlyMemory<byte> bytes) => new(LoftempsCodec.Decode(new BinaryDataReader(bytes)).Values);
}

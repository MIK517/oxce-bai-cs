using System.Collections.ObjectModel;
using Oxce.Formats.Terrain;
using Oxce.Formats.Yaml;
using Oxce.Mods.Files;

namespace Oxce.Mods.Rulesets.Runtime;

public sealed record RuntimeGlobeTexture(bool IsOcean = false, bool FakeUnderwater = false);
public sealed record RuntimeGlobe(IReadOnlyList<WorldPolygon> Polygons, IReadOnlyDictionary<int, RuntimeGlobeTexture> Textures)
{
    public static RuntimeGlobe Empty { get; } = new([], new ReadOnlyDictionary<int, RuntimeGlobeTexture>(new Dictionary<int, RuntimeGlobeTexture>()));
}

internal static class RuntimeGlobeLoader
{
    public static RuntimeGlobe Load(IReadOnlyList<YamlMappingNode> layers, VirtualFileCatalog? files)
    {
        IReadOnlyList<WorldPolygon> polygons = [];
        var textures = new Dictionary<int, RuntimeGlobeTexture>();
        foreach (var layer in layers)
        {
            if (layer.TryGet("data", out var data))
            {
                var file = files?.GetRequired(YamlValueReader.ReadString(data!)) ?? throw new InvalidDataException("Globe DAT requires a virtual file catalog.");
                using var stream = file.OpenRead();
                using var buffer = new MemoryStream();
                var block = new byte[8192];
                int count;
                while ((count = stream.Read(block)) != 0)
                {
                    if (buffer.Length + count > WorldMapCodec.MaximumPolygons * 20) throw new InvalidDataException("Globe DAT exceeds the polygon limit.");
                    buffer.Write(block, 0, count);
                }
                polygons = WorldMapCodec.Decode(buffer.GetBuffer().AsSpan(0, (int)buffer.Length));
            }
            if (layer.TryGet("polygons", out var shapeNode))
            {
                var shapes = Sequence(shapeNode!);
                if (shapes.Items.Count > WorldMapCodec.MaximumPolygons) throw new InvalidDataException("Too many globe polygons.");
                polygons = Array.AsReadOnly(shapes.Items.Select(shape =>
                {
                    var values = Sequence(shape).Items;
                    if (values.Count < 7 || values.Count > 513 || values.Count % 2 != 1) throw new InvalidDataException("Globe polygon requires a texture and 3 to 256 coordinate pairs.");
                    var texture = YamlValueReader.ReadDouble(values[0]);
                    if (!double.IsFinite(texture) || texture < int.MinValue || texture > int.MaxValue) throw new InvalidDataException("Invalid globe texture index.");
                    var points = new WorldPoint[(values.Count - 1) / 2];
                    for (var i = 0; i < points.Length; i++)
                    {
                        var lon = YamlValueReader.ReadDouble(values[i * 2 + 1]);
                        var lat = YamlValueReader.ReadDouble(values[i * 2 + 2]);
                        if (!double.IsFinite(lon) || !double.IsFinite(lat) || Math.Abs(lon) > 360 || Math.Abs(lat) > 90)
                            throw new InvalidDataException("Globe coordinates exceed their supported range.");
                        points[i] = new(lon * Math.PI / 180, lat * Math.PI / 180);
                    }
                    return new WorldPolygon((int)texture, Array.AsReadOnly(points));
                }).ToArray());
            }
            if (layer.TryGet("textures", out var textureNode))
                foreach (var node in Sequence(textureNode!).Items)
                {
                    var map = node as YamlMappingNode ?? throw new InvalidDataException("Globe texture requires a mapping.");
                    if (map.TryGet("id", out var idNode))
                    {
                        var id = YamlValueReader.ReadInt32(idNode!);
                        var previous = textures.GetValueOrDefault(id) ?? new();
                        textures[id] = new(map.TryGet("isOcean", out var ocean) ? YamlValueReader.ReadBoolean(ocean!) : previous.IsOcean,
                            map.TryGet("fakeUnderwater", out var water) ? YamlValueReader.ReadBoolean(water!) : previous.FakeUnderwater);
                    }
                    else if (map.TryGet("delete", out var deleted)) textures.Remove(YamlValueReader.ReadInt32(deleted!));
                }
        }
        return new(polygons, new ReadOnlyDictionary<int, RuntimeGlobeTexture>(textures));
    }
    private static YamlSequenceNode Sequence(YamlNode node) => node as YamlSequenceNode ?? throw new InvalidDataException("Globe field requires a sequence.");
}

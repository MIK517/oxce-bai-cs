using System.Collections.ObjectModel;
using Oxce.Formats.Terrain;
using Oxce.Formats.Yaml;
using Oxce.Mods.Files;

namespace Oxce.Mods.Rulesets.Runtime;

/// <summary>
/// Reference: <c>Mod/Texture.cpp</c>. <c>Deployments</c> is the weighted alien-deployment table a
/// mission site uses when its area has no explicit site type; <c>Terrains</c> and <c>BaseTerrains</c> are the
/// weighted terrain criteria the battle generator picks from.
/// </summary>
public sealed record RuntimeGlobeTexture(bool IsOcean = false, bool FakeUnderwater = false)
{
    public IReadOnlyDictionary<string, ulong> Deployments { get; init; } = EmptyDeployments;
    /// <summary>Texture::getStartingCondition: used when the deployment names none.</summary>
    public string StartingCondition { get; init; } = string.Empty;

    /// <summary>Texture::_terrain, for Texture::getRandomTerrain.</summary>
    public IReadOnlyList<RuntimeTerrainCriterion> Terrains { get; init; } = [];

    /// <summary>Texture::_baseTerrain, for Texture::getRandomBaseTerrain.</summary>
    public IReadOnlyList<RuntimeTerrainCriterion> BaseTerrains { get; init; } = [];

    internal static readonly IReadOnlyDictionary<string, ulong> EmptyDeployments =
        new ReadOnlyDictionary<string, ulong>(new Dictionary<string, ulong>(StringComparer.Ordinal));
}
/// <summary>
/// TerrainCriteria: a terrain name with a weight and a globe area in radians. Without an <c>area</c> the bounds are
/// the constructor's 0..360 and -90..90, compared with radians, so they accept every target.
/// </summary>
public sealed record RuntimeTerrainCriterion(string Name, int Weight, double LongitudeMinimum, double LongitudeMaximum,
    double LatitudeMinimum, double LatitudeMaximum)
{
    /// <summary>
    /// Texture::getRandomTerrain and getRandomBaseTerrain: the weighted pick among criteria with a positive weight whose
    /// area contains the target, drawing 1..total; "" when none applies.
    /// </summary>
    public static string Pick(IReadOnlyList<RuntimeTerrainCriterion> criteria, double longitude, double latitude,
        Func<int, int, int> generate)
    {
        ArgumentNullException.ThrowIfNull(criteria);
        ArgumentNullException.ThrowIfNull(generate);
        var total = 0;
        foreach (var criterion in criteria)
            if (criterion.Applies(longitude, latitude)) total = checked(total + criterion.Weight);
        if (total <= 0) return "";
        var pick = generate(1, total);
        foreach (var criterion in criteria)
        {
            if (!criterion.Applies(longitude, latitude)) continue;
            if (pick <= criterion.Weight) return criterion.Name;
            pick -= criterion.Weight;
        }
        return "";
    }

    private bool Applies(double longitude, double latitude) => Weight > 0 &&
        longitude >= LongitudeMinimum && longitude < LongitudeMaximum && latitude >= LatitudeMinimum && latitude < LatitudeMaximum;
}

public sealed record RuntimeGlobe(IReadOnlyList<WorldPolygon> Polygons, IReadOnlyDictionary<int, RuntimeGlobeTexture> Textures)
{
    public static RuntimeGlobe Empty { get; } = new([], new ReadOnlyDictionary<int, RuntimeGlobeTexture>(new Dictionary<int, RuntimeGlobeTexture>()));

    /// <summary>Border lines drawn by Globe::drawDetail. Reference: <c>Mod/Polyline.cpp</c>.</summary>
    public IReadOnlyList<RuntimeGlobePolyline> Polylines { get; init; } = [];

    public RuntimeGlobeColors Colors { get; init; } = RuntimeGlobeColors.Default;
}

public sealed record RuntimeGlobePolyline(IReadOnlyList<WorldPoint> Points);

/// <summary>
/// Globe palette indices. Mod::Mod sets the defaults and RuleGlobe::load overrides each one that a
/// layer names; <c>oceanPalette</c> is a palette block, stored here as its first index.
/// </summary>
public sealed record RuntimeGlobeColors(byte Ocean, bool OceanShading, byte CountryLabel, byte CityLabel,
    byte BaseLabel, byte Line)
{
    public static RuntimeGlobeColors Default { get; } = new(12 * 16, true, 239, 138, 133, 162);
}

internal static class RuntimeGlobeLoader
{
    public static RuntimeGlobe Load(IReadOnlyList<YamlMappingNode> layers, VirtualFileCatalog? files)
    {
        IReadOnlyList<WorldPolygon> polygons = [];
        IReadOnlyList<RuntimeGlobePolyline> polylines = [];
        var colors = RuntimeGlobeColors.Default;
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
            if (layer.TryGet("polylines", out var lineNode))
            {
                var lines = Sequence(lineNode!);
                if (lines.Items.Count > WorldMapCodec.MaximumPolygons) throw new InvalidDataException("Too many globe polylines.");
                polylines = Array.AsReadOnly(lines.Items.Select(line =>
                {
                    var values = Sequence(line).Items;
                    // Polyline::load reads coordinate pairs; an odd count would read past the vector.
                    if (values.Count < 2 || values.Count > 2 * MaximumPolylinePoints || values.Count % 2 != 0)
                        throw new InvalidDataException($"Globe polyline requires 1 to {MaximumPolylinePoints} coordinate pairs.");
                    var points = new WorldPoint[values.Count / 2];
                    for (var i = 0; i < points.Length; i++)
                        points[i] = new(Degrees(values[i * 2], 360), Degrees(values[i * 2 + 1], 90));
                    return new RuntimeGlobePolyline(Array.AsReadOnly(points));
                }).ToArray());
            }
            colors = colors with
            {
                Ocean = layer.TryGet("oceanPalette", out var oceanBlock) ? checked((byte)(Byte(oceanBlock!, 15) * 16)) : colors.Ocean,
                OceanShading = layer.TryGet("oceanShading", out var shading) ? YamlValueReader.ReadBoolean(shading!) : colors.OceanShading,
                CountryLabel = layer.TryGet("countryColor", out var country) ? Byte(country!, 255) : colors.CountryLabel,
                CityLabel = layer.TryGet("cityColor", out var city) ? Byte(city!, 255) : colors.CityLabel,
                BaseLabel = layer.TryGet("baseColor", out var baseColor) ? Byte(baseColor!, 255) : colors.BaseLabel,
                Line = layer.TryGet("lineColor", out var line) ? Byte(line!, 255) : colors.Line,
            };
            if (layer.TryGet("textures", out var textureNode))
                foreach (var node in Sequence(textureNode!).Items)
                {
                    var map = node as YamlMappingNode ?? throw new InvalidDataException("Globe texture requires a mapping.");
                    if (map.TryGet("id", out var idNode))
                    {
                        var id = YamlValueReader.ReadInt32(idNode!);
                        var previous = textures.GetValueOrDefault(id) ?? new();
                        textures[id] = new(map.TryGet("isOcean", out var ocean) ? YamlValueReader.ReadBoolean(ocean!) : previous.IsOcean,
                            map.TryGet("fakeUnderwater", out var water) ? YamlValueReader.ReadBoolean(water!) : previous.FakeUnderwater)
                        {
                            Deployments = map.TryGet("deployments", out var deployments)
                                ? ReadDeployments(deployments!) : previous.Deployments,
                            StartingCondition = map.TryGet("startingCondition", out var condition)
                                ? YamlValueReader.ReadString(condition!) : previous.StartingCondition,
                            Terrains = map.TryGet("terrain", out var terrain) ? ReadTerrainCriteria(terrain!) : previous.Terrains,
                            BaseTerrains = map.TryGet("baseTerrain", out var baseTerrain) ? ReadTerrainCriteria(baseTerrain!) : previous.BaseTerrains,
                        };
                    }
                    else if (map.TryGet("delete", out var deleted)) textures.Remove(YamlValueReader.ReadInt32(deleted!));
                }
        }
        return new(polygons, new ReadOnlyDictionary<int, RuntimeGlobeTexture>(textures))
        {
            Polylines = polylines,
            Colors = colors,
        };
    }

    private const int MaximumPolylinePoints = 4096;

    private static double Degrees(YamlNode node, double limit)
    {
        var value = YamlValueReader.ReadDouble(node);
        if (!double.IsFinite(value) || Math.Abs(value) > limit)
            throw new InvalidDataException("Globe coordinates exceed their supported range.");
        return value * Math.PI / 180;
    }

    private static byte Byte(YamlNode node, int maximum)
    {
        var value = YamlValueReader.ReadInt32(node);
        if (value < 0 || value > maximum) throw new InvalidDataException($"Globe colour must be between 0 and {maximum}.");
        return (byte)value;
    }
    private static ReadOnlyDictionary<string, ulong> ReadDeployments(YamlNode node)
    {
        var map = node as YamlMappingNode ?? throw new InvalidDataException("Globe texture deployments require a mapping.");
        var result = new Dictionary<string, ulong>(StringComparer.Ordinal);
        foreach (var entry in map.Entries)
        {
            var id = entry.ScalarKey ?? throw new InvalidDataException("Globe texture deployment requires an ID.");
            result[id] = YamlValueReader.ReadUInt64(entry.Value);
        }
        return new ReadOnlyDictionary<string, ulong>(result);
    }

    /// <summary>The TerrainCriteria reader: name, weight (1) and an optional area of four degree values.</summary>
    private static ReadOnlyCollection<RuntimeTerrainCriterion> ReadTerrainCriteria(YamlNode node) => Array.AsReadOnly(Sequence(node).Items
        .Select(static item =>
        {
            var map = item as YamlMappingNode ?? throw new InvalidDataException("Globe texture terrain requires a mapping.");
            var name = map.TryGet("name", out var nameNode) ? YamlValueReader.ReadString(nameNode!) : "";
            var weight = map.TryGet("weight", out var weightNode) ? YamlValueReader.ReadInt32(weightNode!) : 1;
            if (!map.TryGet("area", out var areaNode)) return new RuntimeTerrainCriterion(name, weight, 0, 360, -90, 90);
            var area = Sequence(areaNode!).Items.Select(YamlValueReader.ReadDouble).ToArray();
            // The reference reads four values without checking how many there are.
            if (area.Length < 4 || area.Any(static value => !double.IsFinite(value)))
                throw new InvalidDataException("Globe texture terrain area requires four finite values.");
            return new RuntimeTerrainCriterion(name, weight, area[0] * Math.PI / 180, area[1] * Math.PI / 180,
                area[2] * Math.PI / 180, area[3] * Math.PI / 180);
        }).ToArray());

    private static YamlSequenceNode Sequence(YamlNode node) => node as YamlSequenceNode ?? throw new InvalidDataException("Globe field requires a sequence.");
}

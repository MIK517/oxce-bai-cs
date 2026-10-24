using System.Collections.ObjectModel;
using Oxce.Formats.Yaml;
using Oxce.Mods.Rulesets.TerrainDeployment;

namespace Oxce.Mods.Rulesets.Runtime;

/// <summary>A map script (Mod::getMapScript): its commands in order.</summary>
public sealed record RuntimeMapScript(string Id, IReadOnlyList<RuntimeMapScriptCommand> Commands);

/// <summary>
/// One MapScript command as <c>MapScript::load</c> leaves it: block selection (groups or blocks, frequencies, maximum
/// uses), rectangles, conditions and repetition, the digTunnel data and the vertical levels. <see cref="VerticalLevelsInvalid"/>
/// holds the reason a malformed verticalLevels node is unusable (the reference throws reading it).
/// </summary>
public sealed record RuntimeMapScriptCommand(
    MapScriptCommandType Type,
    IReadOnlyList<MapRectangle> Rectangles,
    IReadOnlyList<int> CraftGroups,
    IReadOnlyList<int> Conditionals,
    int SizeX,
    int SizeY,
    int SizeZ,
    IReadOnlyList<int> Groups,
    IReadOnlyList<int> Blocks,
    IReadOnlyList<int> Frequencies,
    IReadOnlyList<int> MaximumUses,
    MapDirection Direction,
    int VerticalGroup,
    int HorizontalGroup,
    int CrossingGroup,
    bool CanBeSkipped,
    bool MarkAsReinforcementsBlock,
    int ExecutionChances,
    int Executions,
    string UfoName,
    string CraftName,
    IReadOnlyList<string> RandomTerrain,
    int Label,
    RuntimeTunnelData? Tunnel,
    IReadOnlyList<RuntimeVerticalLevel> VerticalLevels,
    string? VerticalLevelsInvalid);

/// <summary>VerticalLevelType.</summary>
public enum VerticalLevelType
{
    Ground,
    Middle,
    Ceiling,
    Empty,
    Decoration,
    Craft,
    Line,
}

/// <summary>
/// VerticalLevel as <c>VerticalLevel::load</c> leaves it: type (an unknown type loads as middle), groups or blocks (reading
/// "blocks" clears the groups), size (1, 1, -1; a decoration's height defaults to 0), maximum repeats (-1) and terrain.
/// </summary>
public sealed record RuntimeVerticalLevel(
    VerticalLevelType Type,
    IReadOnlyList<int> Groups,
    IReadOnlyList<int> Blocks,
    int SizeX,
    int SizeY,
    int SizeZ,
    int MaximumRepeats,
    string Terrain);

/// <summary>
/// MapScript <c>tunnelData</c>: the level to drill on and the MCD replacements by part (westWall, northWall, corner,
/// floor) as data set and entry of the command's terrain, -1 when not given. <see cref="Invalid"/> holds the reason
/// a malformed node is unusable (the reference throws reading it).
/// </summary>
public sealed record RuntimeTunnelData(int Level, IReadOnlyDictionary<string, (int Set, int Entry)> Replacements)
{
    public string? Invalid { get; init; }
}

internal static class RuntimeMapScriptProjection
{
    public static RuntimeMapScript Project(MapScriptRule rule) => new(rule.Id, Array.AsReadOnly(rule.Commands.Select(Command).ToArray()));

    private static RuntimeMapScriptCommand Command(MapScriptCommand command)
    {
        // MapScript::load clears the groups when it reads "blocks"; the frequencies then follow the blocks, which is
        // how a present but empty "blocks" list shows.
        var blocksRead = command.Blocks.Count != 0 || command.Frequencies.Count != command.Groups.Count;
        return new(command.Type, List(command.Rectangles), List(command.CraftGroups), List(command.Conditionals),
            command.Size[0], command.Size[1], command.Size[2], blocksRead ? List(Array.Empty<int>()) : List(command.Groups),
            List(command.Blocks), List(command.Frequencies), List(command.MaximumUses), command.Direction,
            command.VerticalGroup, command.HorizontalGroup, command.CrossingGroup, command.CanBeSkipped,
            command.MarkAsReinforcementsBlock, command.ExecutionChances, command.Executions, command.UfoName,
            command.CraftName, List(command.RandomTerrain), command.Label, Tunnel(command.TunnelData),
            VerticalLevels(command.VerticalLevels, out var invalid), invalid);
    }

    /// <summary>MapScript::load's and RuleBaseFacility::load's verticalLevels: the entries that have a "type", in order.</summary>
    internal static ReadOnlyCollection<RuntimeVerticalLevel> VerticalLevels(YamlNode? node, out string? invalid)
    {
        invalid = null;
        var levels = new List<RuntimeVerticalLevel>();
        if (node is null) return List(levels);
        try
        {
            foreach (var item in YamlValueReader.ReadSequence(node, static item => item))
            {
                if (item is not YamlMappingNode map) throw new YamlFormatException("verticalLevels entries must be mappings.", item.Span);
                if (!map.TryGet("type", out var typeNode)) continue;
                var type = YamlValueReader.ReadString(typeNode!) switch
                {
                    "ground" => VerticalLevelType.Ground,
                    "ceiling" => VerticalLevelType.Ceiling,
                    "empty" => VerticalLevelType.Empty,
                    "decoration" => VerticalLevelType.Decoration,
                    "craft" => VerticalLevelType.Craft,
                    "line" => VerticalLevelType.Line,
                    _ => VerticalLevelType.Middle,
                };
                int[] size = [1, 1, type == VerticalLevelType.Decoration ? 0 : -1];
                if (map.TryGet("size", out var sizeNode))
                {
                    if (sizeNode is YamlSequenceNode sequence)
                    {
                        var values = YamlValueReader.ReadSequence(sequence, YamlValueReader.ReadInt32);
                        for (var index = 0; index < Math.Min(3, values.Length); index++) size[index] = values[index];
                    }
                    else
                    {
                        size[0] = size[1] = YamlValueReader.ReadInt32(sizeNode!);
                    }
                }
                var groups = map.TryGet("groups", out var groupNode) ? Integers(groupNode!) : [];
                var blocks = new List<int>();
                if (map.TryGet("blocks", out var blockNode))
                {
                    groups = [];
                    blocks.AddRange(Integers(blockNode!));
                }
                levels.Add(new RuntimeVerticalLevel(type, List(groups), List(blocks), size[0], size[1], size[2],
                    map.ReadInt32("maxRepeats", -1), map.ReadString("terrain", "")));
            }
        }
        catch (Exception exception) when (exception is YamlFormatException or FormatException or InvalidDataException or OverflowException)
        {
            invalid = exception.Message;
        }
        return List(levels);
    }

    private static int[] Integers(YamlNode node) => node is YamlSequenceNode
        ? YamlValueReader.ReadSequence(node, YamlValueReader.ReadInt32)
        : [YamlValueReader.ReadInt32(node)];

    /// <summary>MapScript::load's tunnelData: level (0) and MCDReplacements of type, entry (-1) and set (-1).</summary>
    private static RuntimeTunnelData? Tunnel(YamlNode? node)
    {
        if (node is null) return null;
        var replacements = new Dictionary<string, (int Set, int Entry)>(StringComparer.Ordinal);
        try
        {
            if (node is not YamlMappingNode map) throw new YamlFormatException("tunnelData must be a mapping.", node.Span);
            var level = map.ReadInt32("level", 0);
            if (map.TryGet("MCDReplacements", out var list))
                foreach (var item in YamlValueReader.ReadSequence(list!, static item => item))
                {
                    if (item is not YamlMappingNode replacement) throw new YamlFormatException("MCDReplacements entries must be mappings.", item.Span);
                    replacements[replacement.ReadString("type", "")] = (replacement.ReadInt32("set", -1), replacement.ReadInt32("entry", -1));
                }
            return new RuntimeTunnelData(level, new ReadOnlyDictionary<string, (int Set, int Entry)>(replacements));
        }
        catch (Exception exception) when (exception is YamlFormatException or FormatException or InvalidDataException or OverflowException)
        {
            return new RuntimeTunnelData(0, new ReadOnlyDictionary<string, (int Set, int Entry)>(replacements)) { Invalid = exception.Message };
        }
    }

    private static ReadOnlyCollection<T> List<T>(IEnumerable<T> source) => Array.AsReadOnly(source.ToArray());
}

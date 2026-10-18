using System.Collections.ObjectModel;
using Oxce.Mods.Rulesets.TerrainDeployment;

namespace Oxce.Mods.Rulesets.Runtime;

/// <summary>A map script (Mod::getMapScript): its commands in order.</summary>
public sealed record RuntimeMapScript(string Id, IReadOnlyList<RuntimeMapScriptCommand> Commands);

/// <summary>
/// One MapScript command as <c>MapScript::load</c> leaves it: block selection (groups or blocks, frequencies, maximum
/// uses), rectangles, conditions and repetition. <see cref="HasTunnelData"/> and <see cref="HasVerticalLevels"/> mark
/// the parts the generator does not run yet.
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
    bool HasTunnelData,
    bool HasVerticalLevels);

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
            command.CraftName, List(command.RandomTerrain), command.Label, command.TunnelData is not null,
            command.VerticalLevels is not null);
    }

    private static ReadOnlyCollection<T> List<T>(IEnumerable<T> source) => Array.AsReadOnly(source.ToArray());
}

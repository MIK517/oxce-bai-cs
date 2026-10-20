using Oxce.Core.Random;
using Oxce.Mods.Rulesets.Runtime;

namespace Oxce.Gameplay.Battles.Generation;

/// <summary>
/// The block selection state of one MapScript command (<c>MapScript::init</c>, <c>getGroupNumber</c>,
/// <c>getBlockNumber</c>, <c>getNextBlock</c>): weighted picks that use up their maximum uses.
/// </summary>
internal sealed class MapScriptSelection
{
    /// <summary>MapBlockType: MT_UNDEFINED, MT_DEFAULT.</summary>
    public const int Undefined = -1;
    public const int DefaultGroup = 0;

    private List<int> _baseGroups;
    private List<int> _baseBlocks;
    private readonly int _sizeX;
    private readonly int _sizeY;
    private List<int> _groups;
    private List<int> _blocks;
    private List<int> _frequencies;
    private List<int> _maximumUses;
    private int _cumulativeFrequency;

    /// <summary>MapScript::init.</summary>
    public MapScriptSelection(RuntimeMapScriptCommand command)
    {
        _baseGroups = [.. command.Groups];
        _baseBlocks = [.. command.Blocks];
        _sizeX = command.SizeX;
        _sizeY = command.SizeY;
        _groups = [.. command.Groups];
        _blocks = [.. command.Blocks];
        _frequencies = [.. command.Frequencies];
        _maximumUses = [.. command.MaximumUses];
        foreach (var frequency in command.Frequencies) _cumulativeFrequency = checked(_cumulativeFrequency + frequency);
    }

    /// <summary>
    /// MapScript::initVerticalLevel: the level's blocks and groups replace the command's own selection (not only the
    /// scratch copies), each with weight 1 and no use limit.
    /// </summary>
    public void InitVerticalLevel(IReadOnlyList<int> blocks, IReadOnlyList<int> groups)
    {
        _baseBlocks = [.. blocks];
        _baseGroups = [.. groups];
        _cumulativeFrequency = Math.Max(blocks.Count, groups.Count);
        _blocks = [.. blocks];
        _groups = [.. groups];
        _frequencies = [.. Enumerable.Repeat(1, _cumulativeFrequency)];
        _maximumUses = [.. Enumerable.Repeat(-1, _cumulativeFrequency)];
    }

    /// <summary>MapScript::getNextBlock: a block by index, or a random block of the command's size from a picked group.</summary>
    public RuntimeMapBlock? NextBlock(RuntimeTerrain terrain, IRandomSource random)
    {
        if (_baseBlocks.Count == 0)
            return RandomBlock(terrain, _sizeX * 10, _sizeY * 10, GroupNumber(random), force: true, random);
        var result = Pick(_blocks, random);
        return result != Undefined && result < terrain.MapBlocks.Count && result >= 0 ? terrain.MapBlocks[result] : null;
    }

    /// <summary>
    /// RuleTerrain::getRandomMapBlock: blocks in the group whose size equals the maximum (or, unless forced, is smaller);
    /// the pick draws only when there is a choice to make.
    /// </summary>
    public static RuntimeMapBlock? RandomBlock(RuntimeTerrain terrain, int maximumX, int maximumY, int group, bool force, IRandomSource random)
    {
        var compliant = new List<RuntimeMapBlock>();
        foreach (var block in terrain.MapBlocks)
            if ((block.Width == maximumX || !force && block.Width < maximumX) &&
                (block.Length == maximumY || !force && block.Length < maximumY) && block.Groups.Contains(group))
                compliant.Add(block);
        return compliant.Count == 0 ? null : compliant[random.NextInclusive(0, compliant.Count - 1)];
    }

    private int GroupNumber(IRandomSource random) => _baseGroups.Count == 0 ? DefaultGroup : Pick(_groups, random);

    private int Pick(List<int> values, IRandomSource random)
    {
        if (_cumulativeFrequency <= 0) return Undefined;
        var pick = random.NextInclusive(0, _cumulativeFrequency - 1);
        for (var index = 0; index != values.Count; index++)
        {
            if (pick < _frequencies[index])
            {
                var value = values[index];
                if (_maximumUses[index] > 0 && --_maximumUses[index] == 0)
                {
                    values.RemoveAt(index);
                    _cumulativeFrequency -= _frequencies[index];
                    _frequencies.RemoveAt(index);
                    _maximumUses.RemoveAt(index);
                }
                return value;
            }
            pick -= _frequencies[index];
        }
        return Undefined;
    }
}

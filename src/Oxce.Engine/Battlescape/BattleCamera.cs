using Oxce.Gameplay.Battles;

namespace Oxce.Engine.Battlescape;

/// <summary>
/// The battlescape camera's map-side state (<c>Camera</c>: <c>up</c>, <c>down</c>, <c>setViewLevel</c>, <c>centerOnPosition</c>,
/// <c>toggleShowAllLayers</c>): the tile at the screen center and the level shown. Pixel scrolling is the screen's; this is
/// per-player client state and no part of the battle.
/// </summary>
public sealed class BattleCamera
{
    private int _width;
    private int _length;
    private int _levels = 1;

    /// <summary>The map the camera looks at; the level and center are cut to it.</summary>
    public void SetMap(int width, int length, int levels)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(length);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(levels);
        (_width, _length, _levels) = (width, length, levels);
        ViewLevel = Math.Clamp(ViewLevel, 0, _levels - 1);
        Center = Center with { X = Math.Clamp(Center.X, -1, _width), Y = Math.Clamp(Center.Y, -1, _length) };
    }

    public int ViewLevel { get; private set; }

    /// <summary>The tile at the center of the screen (<c>_center</c>: x and y may lie one tile outside the map).</summary>
    public BattlePosition Center { get; private set; }

    /// <summary>Camera::toggleShowAllLayers: all levels are drawn, not only those up to the view level.</summary>
    public bool ShowAllLayers { get; private set; }

    /// <summary>Camera::up; false at the top level.</summary>
    public bool Up() => SetLevel(ViewLevel + 1);

    /// <summary>Camera::down; false at the ground level.</summary>
    public bool Down() => SetLevel(ViewLevel - 1);

    private bool SetLevel(int level)
    {
        if (level < 0 || level >= _levels) return false;
        ViewLevel = level;
        return true;
    }

    /// <summary>Camera::setViewLevel.</summary>
    public void SetViewLevel(int level) => ViewLevel = Math.Clamp(level, 0, _levels - 1);

    /// <summary>Camera::centerOnPosition: the center is cut to one tile outside the map, and the view level follows the position's.</summary>
    public void CenterOn(BattlePosition position)
    {
        Center = position with { X = Math.Clamp(position.X, -1, _width), Y = Math.Clamp(position.Y, -1, _length) };
        ViewLevel = Math.Clamp(position.Z, 0, _levels - 1);
    }

    public bool ToggleShowAllLayers() => ShowAllLayers = !ShowAllLayers;
}

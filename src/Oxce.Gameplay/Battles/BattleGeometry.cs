namespace Oxce.Gameplay.Battles;

/// <summary>
/// The battle's shared geometry: the ten step directions of <c>Pathfinding</c> (<c>dir_x</c>, <c>dir_y</c> and <c>dir_z</c>: 0 north, clockwise to
/// 7 north-west, then <see cref="BattleUnit.DirectionUp"/> and <see cref="BattleUnit.DirectionDown"/>), <c>vectorToDirection</c>,
/// <c>BattleUnit::directionTo</c>'s eighths of the circle, the horizontal distance the field of view compares, the part offsets of a 2x2 unit
/// and the voxel size of a tile.
/// </summary>
internal static class BattleGeometry
{
    /// <summary>The voxels of a tile across (x and y) and up (z).</summary>
    public const int VoxelsPerTile = 16;
    public const int VoxelsPerLevel = 24;

    private static readonly int[] DirectionX = [0, 1, 1, 1, 0, -1, -1, -1, 0, 0];
    private static readonly int[] DirectionY = [-1, -1, 0, 1, 1, 1, 0, -1, 0, 0];
    private static readonly int[] DirectionZ = [0, 0, 0, 0, 0, 0, 0, 0, 1, -1];

    /// <summary>The part offsets of a 2x2 unit's tiles, in the reference's part order.</summary>
    public static ReadOnlySpan<(int X, int Y)> BigUnitOffsets => Offsets;

    private static readonly (int X, int Y)[] Offsets = [(0, 0), (1, 0), (0, 1), (1, 1)];

    /// <summary>Pathfinding::directionToVector for the eight compass directions.</summary>
    public static (int X, int Y) DirectionVector(int direction) => (DirectionX[direction], DirectionY[direction]);

    /// <summary>The position one step in a direction (0-9), up and down included.</summary>
    public static BattlePosition Step(BattlePosition position, int direction) =>
        new(position.X + DirectionX[direction], position.Y + DirectionY[direction], position.Z + DirectionZ[direction]);

    /// <summary>Pathfinding::vectorToDirection of a horizontal vector (0 north, clockwise), or -1 for none.</summary>
    public static int VectorToDirection(int x, int y)
    {
        for (var direction = 0; direction < 8; direction++)
            if (DirectionX[direction] == x && DirectionY[direction] == y) return direction;
        return -1;
    }

    /// <summary>
    /// BattleUnit::directionTo and TileEngine::getDirectionTo: the compass direction from one tile to another, by dividing the pie into
    /// eighths centred on the directions.
    /// </summary>
    public static int DirectionTo(BattlePosition from, BattlePosition to)
    {
        double ox = to.X - from.X;
        double oy = to.Y - from.Y;
        var angle = Math.Atan2(ox, -oy);
        // Divide the pie in 4 angles each at 1/8th before each quarter.
        ReadOnlySpan<double> pie =
        [
            Math.PI / 4.0 * 4.0 - Math.PI / 4.0 / 2.0,
            Math.PI / 4.0 * 3.0 - Math.PI / 4.0 / 2.0,
            Math.PI / 4.0 * 2.0 - Math.PI / 4.0 / 2.0,
            Math.PI / 4.0 * 1.0 - Math.PI / 4.0 / 2.0,
        ];
        if (angle > pie[0] || angle < -pie[0]) return 4;
        if (angle > pie[1]) return 3;
        if (angle > pie[2]) return 2;
        if (angle > pie[3]) return 1;
        if (angle < -pie[1]) return 5;
        if (angle < -pie[2]) return 6;
        if (angle < -pie[3]) return 7;
        return 0;
    }

    /// <summary>The squared distance of two positions on the map plane (the level is not counted), as the field of view compares it.</summary>
    public static int HorizontalDistanceSquared(BattlePosition one, BattlePosition other) =>
        (one.X - other.X) * (one.X - other.X) + (one.Y - other.Y) * (one.Y - other.Y);

    /// <summary>The tile a voxel lies in.</summary>
    public static BattlePosition TileOfVoxel(BattlePosition voxel) => new(voxel.X / VoxelsPerTile, voxel.Y / VoxelsPerTile, voxel.Z / VoxelsPerLevel);
}

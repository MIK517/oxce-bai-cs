using Oxce.Gameplay.Battles.Terrain;
using Oxce.Mods.Rulesets.Runtime;

namespace Oxce.Gameplay.Battles;

/// <summary>
/// BattleActionMove without <c>BAM_MISSILE</c>, which guided missiles use (Phase 7 branch 5): how a unit moves along a path. A
/// player's walk is always <see cref="Normal"/>; the AI runs, strafes and sneaks.
/// </summary>
internal enum BattleActionMove
{
    Normal = 0,
    Run = 1,
    Strafe = 2,
    Sneak = 3,
}

/// <summary>PathfindingCost: time units and energy, 16 bits each as the reference's <c>Sint16</c> pair (sums wrap).</summary>
internal readonly record struct PathfindingCost(short Time, short Energy)
{
    public static PathfindingCost operator +(PathfindingCost left, PathfindingCost right) =>
        new(unchecked((short)(left.Time + right.Time)), unchecked((short)(left.Energy + right.Energy)));

    /// <summary>PathfindingCost::operator&lt;=: both components, so two costs may be unordered.</summary>
    public bool NoMoreThan(PathfindingCost other) => Time <= other.Time && Energy <= other.Energy;

    /// <summary>PathfindingCost::operator&lt;: both components.</summary>
    public bool LessThan(PathfindingCost other) => Time < other.Time && Energy < other.Energy;
}

/// <summary>PathfindingStep: the cost of one step, its pathfinding penalty (not a real cost) and the position the step ends at.</summary>
internal readonly record struct PathfindingStep(PathfindingCost Cost, PathfindingCost Penalty, BattlePosition Position)
{
    public bool IsValid => Cost.Time != BattlePathing.InvalidMoveCost;
}

/// <summary>A tile of <see cref="BattlePathfinder.FindReachableNodes"/>: its position, the cost of the cheapest way and how it was reached.</summary>
internal readonly record struct BattleReachableNode(BattlePosition Position, PathfindingCost Cost, BattlePosition? PreviousPosition, int PreviousDirection);

/// <summary>What <see cref="BattlePathfinder.FindReachableNodes"/> found: the nodes by ascending cost (the first is the start) and whether any step was refused for cost.</summary>
internal sealed record BattleReachability(IReadOnlyList<BattleReachableNode> Nodes, bool RanOutOfTimeUnits)
{
    /// <summary>Pathfinding::findReachable: the tile indexes of the nodes.</summary>
    public IReadOnlyList<int> Tiles(BattleMap map) => [.. Nodes.Select(node => map.TileIndex(node.Position))];
}

/// <summary>
/// Pathfinding (<c>src/Battlescape/Pathfinding.cpp</c>, <c>PathfindingNode</c>, <c>PathfindingOpenSet</c>) on the port's tile arrays: the cost
/// of one step (<see cref="GetTuCost"/>), blocked tiles and directions, the Bresenham shortcut and A* behind <see cref="Calculate"/>, and the
/// reachable tiles (<see cref="FindReachableNodes"/>). One instance holds the nodes and the last path, as the reference's does; it reads
/// the battle as it is and changes nothing in it. Guided missiles (<c>BAM_MISSILE</c>, a missile target) belong to Phase 7 branch 5 and are
/// not here; <c>previewPath</c> is the client's display and not here either.
/// </summary>
internal sealed class BattlePathfinder
{
    /// <summary>Pathfinding::DEFAULT_MOVE_COST, DEFAULT_MOVE_FLY_COST, MAX_MOVE_COST, INVALID_MOVE_COST and FIRE_PREVIEW_MOVE_COST.</summary>
    public const int DefaultMoveCost = 4;
    public const int DefaultMoveFlyCost = 8;
    public const int MaximumMoveCost = 100;
    public const int InvalidMoveCost = BattlePathing.InvalidMoveCost;
    public const int FirePreviewMoveCost = 32;

    private const int Up = BattleUnit.DirectionUp;
    private const int Down = BattleUnit.DirectionDown;

    /// <summary>Pathfinding::dir_x, dir_y and dir_z: the vectors of the ten directions (the eight compass ones, up and down).</summary>
    private static readonly int[] DirX = [0, 1, 1, 1, 0, -1, -1, -1, 0, 0];
    private static readonly int[] DirY = [-1, -1, 0, 1, 1, 1, 0, -1, 0, 0];
    private static readonly int[] DirZ = [0, 0, 0, 0, 0, 0, 0, 0, 1, -1];

    // The order the reference looks for the ladder: the object, the north wall, the west wall.
    private static readonly BattleTilePart[] LadderParts = [BattleTilePart.ObjectPart, BattleTilePart.NorthWall, BattleTilePart.WestWall];

    private static readonly PathfindingStep Invalid = new(new PathfindingCost(InvalidMoveCost, 0), default, default);

    // The part offsets of a 2x2 unit's tiles.
    private static readonly (int X, int Y)[] Offsets = [(0, 0), (1, 0), (0, 1), (1, 1)];

    private struct Node
    {
        public PathfindingCost Cost;
        public int Previous;
        public int PreviousDirection;
        public short Guess;
        public bool Checked;
        public byte OpenEntry;

        public readonly bool InOpenSet => OpenEntry != 0;
    }

    private readonly BattleState _battle;
    private readonly BattleMap _map;
    private readonly RuntimeRuleCatalog _rules;
    private readonly BattlePathingOptions _options;
    private readonly Node[] _nodes;
    private Node[]? _altNodes;
    private readonly int _layer;
    private readonly List<int> _path = [];
    private BattleUnit? _unit;
    private PathfindingCost _totalCost;

    public BattlePathfinder(BattleState battle, RuntimeRuleCatalog rules, BattlePathingOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(battle);
        ArgumentNullException.ThrowIfNull(rules);
        _battle = battle;
        _map = battle.Map;
        _rules = rules;
        _options = options ?? BattleOptions.Pathing;
        _nodes = new Node[_map.TileCount];
        _layer = _map.Width * _map.Length;
    }

    /// <summary>Pathfinding::setIgnoreFriends: units in the way do not block (the AI's second look).</summary>
    public bool IgnoreFriends { get; set; }

    /// <summary>Pathfinding::getStrafeMove: the last <see cref="Calculate"/> was a one-step strafe.</summary>
    public bool StrafeMove { get; private set; }

    /// <summary>The last path, first step first (the reference keeps it reversed, <c>getPath</c>).</summary>
    public IReadOnlyList<int> Path => [.. Enumerable.Reverse(_path)];

    /// <summary>
    /// Pathfinding::getTotalTUCost: the time units of the last Bresenham path. After A* it is the last cost A* tried, not the path's
    /// (the reference's behaviour, kept).
    /// </summary>
    public int TotalTimeUnitCost => _totalCost.Time;

    /// <summary>Pathfinding::getStartDirection: the first direction of the path, -1 for none.</summary>
    public int StartDirection => _path.Count == 0 ? -1 : _path[^1];

    /// <summary>Pathfinding::dequeuePath.</summary>
    public int DequeuePath()
    {
        if (_path.Count == 0) return -1;
        var last = _path[^1];
        _path.RemoveAt(_path.Count - 1);
        return last;
    }

    /// <summary>Pathfinding::abortPath.</summary>
    public void AbortPath()
    {
        _totalCost = default;
        _path.Clear();
    }

    /// <summary>Pathfinding::getMovementType without a missile: sneaking walks.</summary>
    private static int MovementTypeOf(BattleUnit unit, BattleActionMove bam) => bam == BattleActionMove.Sneak ? 0 : unit.MovementType;

    private int Tile(BattlePosition position) => _map.Contains(position) ? _map.TileIndex(position) : -1;

    private int Z(int tile) => tile / _layer;

    private static BattlePosition Add(BattlePosition position, int x, int y, int z) => new(position.X + x, position.Y + y, position.Z + z);

    // ---- calculate ----

    /// <summary>
    /// Pathfinding::calculate: the shortest path from the unit's position to a position. The path is empty when there is none (the end is
    /// blocked or outside the map, no route, or more than <paramref name="maxTimeUnitCost"/> time units). A destination on the stairs'
    /// foot goes up for a player's unit, an object of terrain level -24 or a hole is walked past, as the reference does.
    /// </summary>
    public IReadOnlyList<int> Calculate(BattleUnit unit, BattlePosition endPosition, BattleActionMove bam, int maxTimeUnitCost = 1000) =>
        Calculate(unit, unit.Position ?? throw new InvalidOperationException($"Battle unit {unit.Id} is not on the map."), endPosition, bam, maxTimeUnitCost);

    /// <summary>Pathfinding::calculate from another start position.</summary>
    public IReadOnlyList<int> Calculate(BattleUnit unit, BattlePosition startPosition, BattlePosition endPosition, BattleActionMove bam,
        int maxTimeUnitCost = 1000)
    {
        ArgumentNullException.ThrowIfNull(unit);
        _totalCost = default;
        _path.Clear();
        CheckSize(unit);
        var size = unit.ArmorSize;

        // The reference's own guard against out of bounds destinations (the level is checked by the tile lookups).
        if (endPosition.X > _map.Width - size || endPosition.Y > _map.Length - size || endPosition.X < 0 || endPosition.Y < 0) return [];

        var sneak = _options.SneakyAi && unit.Faction == BattleFaction.Hostile && !unit.UsesBrutalAi(_rules, _options);
        var movementType = MovementTypeOf(unit, bam);
        _unit = unit;

        // check if destination is not blocked
        if (IsBlockedFloorOrObject(unit, Tile(endPosition), BattleTilePart.Floor, bam) || IsBlockedFloorOrObject(unit, Tile(endPosition), BattleTilePart.ObjectPart, bam))
            return [];

        // Going up a stairs: aim at the level above, so the unit does not walk behind the stairs.
        if (IsOnStairs(startPosition, endPosition) && unit.Faction == BattleFaction.Player) endPosition = endPosition with { Z = endPosition.Z + 1 };
        var destination = Tile(endPosition);
        while (endPosition.Z != _map.Height && destination >= 0 && _map.TerrainLevel(destination) == -24)
        {
            endPosition = endPosition with { Z = endPosition.Z + 1 };
            destination = Tile(endPosition);
        }
        // An object with terrain level -24 on the top floor leaves nowhere to stand.
        if (endPosition.Z == _map.Height) return [];
        if (movementType != BattleState.MovementTypeFly)
        {
            // No floor: lower the destination (a flying unit never reached this place).
            while (CanFallDown(destination, size) && !(size == 1 && _map.HasLadder(destination)))
            {
                endPosition = endPosition with { Z = endPosition.Z - 1 };
                destination = Tile(endPosition);
            }
        }

        // Strafing is only to adjacent squares on the same level.
        StrafeMove = bam == BattleActionMove.Strafe && startPosition.Z == endPosition.Z &&
            Math.Abs(startPosition.X - endPosition.X) <= 1 && Math.Abs(startPosition.Y - endPosition.Y) <= 1;
        if (StrafeMove)
        {
            var direction = VectorToDirection(endPosition.X - startPosition.X, endPosition.Y - startPosition.Y);
            if (direction == -1 || Math.Min(Math.Abs(8 + direction - unit.Direction),
                    Math.Min(Math.Abs(unit.Direction - direction), Math.Abs(8 + unit.Direction - direction))) > 2)
                StrafeMove = false; // Strafing backwards-ish is unsupported.
            else if (!GetTuCost(startPosition, direction, unit, bam).IsValid) StrafeMove = false; // not reachable in one step
        }

        // A fast and accurate Bresenham path skips A*, except for a brutal unit.
        if (!unit.UsesBrutalAi(_rules, _options) && BresenhamPath(startPosition, endPosition, bam, sneak))
        {
            _path.Reverse(); // paths are stored in reverse order
            return Path;
        }
        AbortPath(); // a failed Bresenham path must not stay if A* fails too
        if (!AStarPath(startPosition, endPosition, bam, sneak, maxTimeUnitCost)) AbortPath();
        return Path;
    }

    private static int VectorToDirection(int x, int y)
    {
        for (var direction = 0; direction < 8; direction++)
            if (DirX[direction] == x && DirY[direction] == y) return direction;
        return -1;
    }

    private static void CheckSize(BattleUnit unit)
    {
        if (unit.ArmorSize is < 1 or > 2)
            throw new InvalidOperationException($"Battle unit {unit.Id} has armor size {unit.ArmorSize}; pathfinding handles sizes 1 and 2.");
    }

    // ---- A* ----

    private bool AStarPath(BattlePosition startPosition, BattlePosition endPosition, BattleActionMove bam, bool sneak, int maxTimeUnitCost)
    {
        var unit = _unit!;
        foreach (ref var node in _nodes.AsSpan()) node.Checked = false;
        foreach (ref var node in _nodes.AsSpan()) node.OpenEntry = 0;
        var start = _map.TileIndex(startPosition);
        Connect(_nodes, start, default, -1, 0, endPosition);
        var open = new OpenSet(_nodes);
        open.Push(start);
        while (!open.IsEmpty)
        {
            var current = open.Pop();
            var currentPosition = _map.PositionOf(current);
            _nodes[current].Checked = true;
            if (currentPosition == endPosition)
            {
                _path.Clear();
                for (var step = current; _nodes[step].Previous >= 0; step = _nodes[step].Previous) _path.Add(_nodes[step].PreviousDirection);
                return true;
            }

            for (var direction = 0; direction < 10; direction++)
            {
                var r = GetTuCost(currentPosition, direction, unit, bam);
                if (!r.IsValid) continue;
                var cost = r.Cost;
                var nextTile = _map.TileIndex(r.Position);
                if (sneak && _map.IsVisible(nextTile)) cost = cost with { Time = unchecked((short)(cost.Time * 2)) };
                if (_nodes[nextTile].Checked) continue; // already at its minimum cost
                _totalCost = _nodes[current].Cost + cost + r.Penalty;
                // Unvisited, or only visited from inferior paths.
                if ((!_nodes[nextTile].InOpenSet || _nodes[nextTile].Cost.Time > _totalCost.Time) && _totalCost.Time <= maxTimeUnitCost)
                {
                    Connect(_nodes, nextTile, _totalCost, current, direction, endPosition);
                    open.Push(nextTile);
                }
            }
        }
        return false;
    }

    /// <summary>PathfindingNode::connect with a target: the guess is four time units a tile of distance, set once.</summary>
    private void Connect(Node[] nodes, int tile, PathfindingCost cost, int previous, int previousDirection, BattlePosition target)
    {
        ref var node = ref nodes[tile];
        node.Cost = cost;
        node.Previous = previous;
        node.PreviousDirection = previousDirection;
        if (!node.InOpenSet)
        {
            var from = _map.PositionOf(tile);
            double x = target.X - from.X, y = target.Y - from.Y, z = target.Z - from.Z;
            var distance = (float)Math.Sqrt(unchecked((int)(x * x + y * y + z * z)));
            node.Guess = unchecked((short)(4 * distance));
        }
    }

    /// <summary>PathfindingNode::connect without a target (a visit): no guess.</summary>
    private static void Connect(Node[] nodes, int tile, PathfindingCost cost, int previous, int previousDirection)
    {
        ref var node = ref nodes[tile];
        node.Cost = cost;
        node.Previous = previous;
        node.PreviousDirection = previousDirection;
        node.Guess = 0;
    }

    /// <summary>
    /// PathfindingOpenSet: a priority queue of nodes by four times their time cost plus their guess, the lowest first. It is a binary heap
    /// with the standard library's sift order, so equal costs leave in the order the reference's <c>std::priority_queue</c> does. An entry
    /// whose node has been pushed again since is discarded when it reaches the top.
    /// </summary>
    private sealed class OpenSet(Node[] nodes)
    {
        private readonly List<(int Node, short Cost, byte Entry)> _heap = [];

        public bool IsEmpty => _heap.Count == 0;

        public void Push(int tile)
        {
            ref var node = ref nodes[tile];
            node.OpenEntry = unchecked((byte)(node.OpenEntry + 1)); // the next unique number, which checks that an old record is still valid
            var cost = unchecked((short)(node.Cost.Time * 4 + node.Guess));
            _heap.Add(default);
            PushUp(_heap.Count - 1, 0, (tile, cost, node.OpenEntry));
        }

        public int Pop()
        {
            var tile = _heap[0].Node;
            PopTop();
            nodes[tile].OpenEntry = 0;
            // Discarded entries might be visible now.
            while (_heap.Count != 0 && nodes[_heap[0].Node].OpenEntry != _heap[0].Entry) PopTop();
            return tile;
        }

        // std::pop_heap: move the last entry into the hole left at the top by walking the hole down to a leaf (always to the cheaper child,
        // the right one on a tie) and sifting the entry up from there.
        private void PopTop()
        {
            var last = _heap[^1];
            _heap.RemoveAt(_heap.Count - 1);
            var length = _heap.Count;
            if (length == 0) return;
            var hole = 0;
            var child = 0;
            while (child < (length - 1) / 2)
            {
                child = 2 * (child + 1);
                if (_heap[child - 1].Cost < _heap[child].Cost) child--;
                _heap[hole] = _heap[child];
                hole = child;
            }
            if ((length & 1) == 0 && child == (length - 2) / 2)
            {
                child = 2 * (child + 1);
                _heap[hole] = _heap[child - 1];
                hole = child - 1;
            }
            PushUp(hole, 0, last);
        }

        // std::push_heap's sift up: while the entry beats its parent (strictly lower cost).
        private void PushUp(int hole, int top, (int Node, short Cost, byte Entry) value)
        {
            var parent = (hole - 1) / 2;
            while (hole > top && value.Cost < _heap[parent].Cost)
            {
                _heap[hole] = _heap[parent];
                hole = parent;
                parent = (hole - 1) / 2;
            }
            _heap[hole] = value;
        }
    }

    // ---- the cost of one step ----

    /// <summary>
    /// Pathfinding::getTUCost: the cost of one step of a unit from a position in a direction (0 north, clockwise; 8 up, 9 down), and
    /// where it ends, which differs from the neighbouring tile when the unit goes up stairs or falls. The cost's time is 255 when the
    /// step is impossible.
    /// </summary>
    public PathfindingStep GetTuCost(BattlePosition startPosition, int direction, BattleUnit unit, BattleActionMove bam)
    {
        ArgumentNullException.ThrowIfNull(unit);
        if (direction is < 0 or > Down) throw new ArgumentOutOfRangeException(nameof(direction));
        CheckSize(unit);
        var pos = Add(startPosition, DirX[direction], DirY[direction], DirZ[direction]);

        var movementType = MovementTypeOf(unit, bam);
        var fly = movementType == BattleState.MovementTypeFly;
        var armor = BattleState.ArmorOf(_rules, unit);
        var size = unit.ArmorSize - 1;
        var numberOfParts = unit.ArmorSize * unit.ArmorSize;
        var maskOfPartsGoingUp = 0;
        var maskOfPartsHoleUp = 0;
        var maskOfPartsGoingDown = 0;
        var maskOfPartsFalling = 0;
        var maskOfPartsFlying = 0;
        var maskOfPartsGround = 0;
        var maskOfPartsClimb = 0;
        var maskArmor = size != 0 ? 0xF : 0x1;

        Span<int> startTile = stackalloc int[4];
        Span<int> aboveStart = stackalloc int[4];
        Span<int> belowStart = stackalloc int[4];
        Span<int> destinationTile = stackalloc int[4];
        Span<int> aboveDestination = stackalloc int[4];
        Span<int> belowDestination = stackalloc int[4];

        for (var i = 0; i < numberOfParts; i++)
        {
            var st = Tile(Add(startPosition, Offsets[i].X, Offsets[i].Y, 0));
            var dt = Tile(Add(pos, Offsets[i].X, Offsets[i].Y, 0));
            if (st < 0 || dt < 0) return Invalid;
            startTile[i] = st;
            aboveStart[i] = _map.TileAbove(st);
            belowStart[i] = _map.TileBelow(st);
            destinationTile[i] = dt;
            aboveDestination[i] = _map.TileAbove(dt);
            belowDestination[i] = _map.TileBelow(dt);
        }

        // check move up or down
        for (var i = 0; i < numberOfParts; i++)
        {
            var maskCurrentPart = 1 << i;
            var checkClimbing = i == 0 && numberOfParts == 1 && !fly;

            if (direction < Up && _map.TerrainLevel(startTile[i]) > -16)
            {
                // check if we can go this way
                if (IsBlockedDirection(unit, startTile[i], direction, bam)) return Invalid;
                if (_map.TerrainLevel(startTile[i]) - _map.TerrainLevel(destinationTile[i]) > 8) return Invalid;
            }

            // if we are on a stairs try to go up a level
            if (direction < Up && _map.TerrainLevel(startTile[i]) <= -16 && aboveDestination[i] >= 0 && !_map.HasNoFloor(aboveDestination[i]))
                maskOfPartsGoingUp |= maskCurrentPart;
            else if (direction < Up && !fly && belowDestination[i] >= 0 && CanFallDown(destinationTile[i]) && _map.TerrainLevel(belowDestination[i]) <= -12)
                maskOfPartsGoingDown |= maskCurrentPart;
            else if (fly && OverlappingUnit(destinationTile[i]) is { } overlapping)
            {
                // 2 or more voxels poking into this tile = no go
                var knowsOfOverlapping = false;
                if (unit.Faction == BattleFaction.Player && overlapping.IsVisibleToPlayer(_rules)) knowsOfOverlapping = true; // the player knows all visible units
                if (unit.Faction == overlapping.Faction && !IgnoreFriends) knowsOfOverlapping = true;
                if (unit.Faction != BattleFaction.Player && unit.UnitsSpottedThisTurn.Contains(overlapping.Id)) knowsOfOverlapping = true;
                if (overlapping != unit && knowsOfOverlapping) return Invalid;
            }

            if (aboveStart[i] >= 0 && _map.HasNoFloor(aboveStart[i])) maskOfPartsHoleUp |= maskCurrentPart;
            // check if we have floor, else fall down
            if (!fly && CanFallDown(startTile[i]))
            {
                if (!checkClimbing || (!_map.HasLadder(startTile[i]) &&
                    !(direction < Up && belowStart[i] >= 0 && _map.HasLadder(belowStart[i]) && !CanFallDown(destinationTile[i]))))
                    maskOfPartsFalling |= maskCurrentPart;
            }
            if (!fly && checkClimbing && direction >= Up && _map.HasLadder(startTile[i])) maskOfPartsClimb |= maskCurrentPart;
            if (fly && (CanFallDown(startTile[i]) || CanFallDown(destinationTile[i]))) maskOfPartsFlying |= maskCurrentPart;
            if (direction != Down && !CanFallDown(destinationTile[i])) maskOfPartsGround |= maskCurrentPart;
        }

        var triedStairs = maskOfPartsGoingUp != 0 && (maskOfPartsGoingUp | maskOfPartsHoleUp) == maskArmor;
        var triedStairsDown = maskOfPartsGround == 0 && (maskOfPartsGoingDown | maskOfPartsFalling) == maskArmor;
        var fallingDown = maskOfPartsFalling == maskArmor;
        var flying = maskOfPartsFlying == maskArmor;
        var climb = maskOfPartsClimb == maskArmor;

        if (!fly && fallingDown && direction != Down) return Invalid; // cannot walk on air

        for (var i = 0; i < numberOfParts; i++)
        {
            if (triedStairs) destinationTile[i] = aboveDestination[i];
            else if (direction != Down && triedStairsDown) destinationTile[i] = belowDestination[i];

            // check if the destination tile can be walked over
            if (IsBlockedFloorOrObject(unit, destinationTile[i], BattleTilePart.Floor, bam) ||
                IsBlockedFloorOrObject(unit, destinationTile[i], BattleTilePart.ObjectPart, bam))
                return Invalid;
        }

        // don't let tanks phase through doors.
        if (size != 0)
        {
            var t = destinationTile[3];
            if (_map.IsDoor(t, BattleTilePart.NorthWall) || _map.IsDoor(t, BattleTilePart.WestWall)) return Invalid;
        }

        // pre-calculate fire penalty (to make it consistent for 2x2 units)
        var firePenaltyCost = 0;
        if (unit.Faction != BattleFaction.Player && unit.AvoidsFire(_rules))
        {
            for (var i = 0; i < numberOfParts; i++)
                if (_map.Fire(destinationTile[i]) > 0) firePenaltyCost = FirePreviewMoveCost; // try to find a better path, but don't exclude this one
        }

        // calculate cost and some final checks
        var totalCost = 0;
        for (var i = 0; i < numberOfParts; i++)
        {
            var cost = 0;
            var upperLevel = Z(destinationTile[i]) > Z(startTile[i]);
            var sameLevel = Z(destinationTile[i]) == Z(startTile[i]);
            if (direction < Up && sameLevel)
            {
                // check if we can go this way
                if (IsBlockedDirection(unit, startTile[i], direction, bam)) return Invalid;
                if (_map.TerrainLevel(startTile[i]) - _map.TerrainLevel(destinationTile[i]) > 8) return Invalid;
            }
            else if (direction >= Up && !triedStairsDown)
            {
                // check if we can go up or down through a grav lift or by flying
                if (ValidateUpDown(unit, startTile[i], direction))
                {
                    if (climb)
                    {
                        var ladderTile = startTile[i];
                        var minCost = InvalidMoveCost;
                        foreach (var part in LadderParts)
                        {
                            if (!_map.HasLadderOn(ladderTile, part)) continue;
                            var value = _map.TimeUnitCost(ladderTile, part, movementType);
                            minCost = Math.Min(minCost, value != 0 ? value : DefaultMoveFlyCost);
                        }
                        if (minCost >= InvalidMoveCost) return Invalid;
                        cost = minCost;
                    }
                    else cost = DefaultMoveFlyCost; // vertical movement by flying suit or grav lift
                }
                else return Invalid;
            }
            if (upperLevel)
            {
                startTile[i] = _map.TileAbove(startTile[i]);
                if (startTile[i] < 0) return Invalid;
                if (direction < Up)
                {
                    // check if we can go this way
                    if (IsBlockedDirection(unit, startTile[i], direction, bam)) return Invalid;
                    if (_map.TerrainLevel(startTile[i]) - _map.TerrainLevel(destinationTile[i]) > 8) return Invalid;
                }
            }

            // Walking through rubble walls: the average of the walls in the way, but not for doors diagonally (impossible, so if it can
            // be walked past diagonally there is no wall).
            var wallCounter = 0;
            var wallcost = 0;
            if ((direction == 0 || direction == 7 || direction == 1) && !_map.HasLadderOn(startTile[i], BattleTilePart.NorthWall))
            {
                var wallTmp = _map.TimeUnitCost(startTile[i], BattleTilePart.NorthWall, movementType);
                if (wallTmp > 0)
                {
                    wallcost += wallTmp;
                    wallCounter++;
                }
            }
            if (!triedStairsDown && (direction == 2 || direction == 1 || direction == 3) && !_map.HasLadderOn(destinationTile[i], BattleTilePart.WestWall))
            {
                var wallTmp = _map.TimeUnitCost(destinationTile[i], BattleTilePart.WestWall, movementType);
                if (wallTmp > 0)
                {
                    wallcost += wallTmp;
                    wallCounter++;
                }
            }
            if (!triedStairsDown && (direction == 4 || direction == 3 || direction == 5) && !_map.HasLadderOn(destinationTile[i], BattleTilePart.NorthWall))
            {
                var wallTmp = _map.TimeUnitCost(destinationTile[i], BattleTilePart.NorthWall, movementType);
                if (wallTmp > 0)
                {
                    wallcost += wallTmp;
                    wallCounter++;
                }
            }
            if ((direction == 6 || direction == 5 || direction == 7) && !_map.HasLadderOn(startTile[i], BattleTilePart.WestWall))
            {
                var wallTmp = _map.TimeUnitCost(startTile[i], BattleTilePart.WestWall, movementType);
                if (wallTmp > 0)
                {
                    wallcost += wallTmp;
                    wallCounter++;
                }
            }

            // "average" cost
            if (wallCounter > 0) wallcost /= wallCounter;

            // for backward compatibility (100 + 100 + 100 > 255) or for (255 + 10 > 255)
            if (wallcost >= InvalidMoveCost) return Invalid;

            // if we don't want to fall down and there is no floor, we can't know the TUs so it's default to 4
            if (direction < Up && !triedStairsDown && _map.HasNoFloorOnly(destinationTile[i])) cost = DefaultMoveCost;

            // calculate the cost by adding floor walk cost and object walk cost
            if (direction < Up)
            {
                cost += _map.TimeUnitCost(destinationTile[i], BattleTilePart.Floor, movementType);
                if (!triedStairsDown && !triedStairs && _map.MapData(destinationTile[i], BattleTilePart.ObjectPart) is not null &&
                    !_map.HasLadderOn(destinationTile[i], BattleTilePart.ObjectPart))
                    cost += _map.TimeUnitCost(destinationTile[i], BattleTilePart.ObjectPart, movementType);
                // some mods have broken tiles with 0 move cost, so it is the default cost.
                if (cost == 0) cost = DefaultMoveCost;
                // climbing up a level costs one extra
                if (upperLevel) cost++;
            }

            // diagonal walking (uneven directions) costs 50% more tu's
            if (direction < Up && (direction & 1) != 0) cost = (int)(cost * 1.5);

            cost += wallcost;

            // TFTD thing: underwater tiles on fire or filled with smoke cost 2 TUs more.
            if (_battle.Depth > 0 && (_map.Fire(destinationTile[i]) > 0 || _map.Smoke(destinationTile[i]) > 0)) cost += 2;

            // Strafing costs +1 for forwards-ish or sidewards.
            if (StrafeMove && bam == BattleActionMove.Strafe && unit.Direction != direction) cost += 1;

            // cap move cost to given limit
            cost = Math.Min(cost, MaximumMoveCost);
            totalCost += cost;
        }

        // because the unit moves up or down we adjust the final position
        if (triedStairs) pos = pos with { Z = pos.Z + 1 };
        else if (direction != Down && triedStairsDown) pos = pos with { Z = pos.Z - 1 };

        // for bigger units, check the path between parts in an X shape at the end position
        if (size != 0)
        {
            totalCost /= numberOfParts;
            var originTile = Tile(Add(pos, 1, 1, 0));
            var finalTile = Tile(pos);
            if (originTile < 0 || finalTile < 0) return Invalid;
            if (IsBlockedDirection(unit, originTile, 7, bam)) return Invalid;
            if (!triedStairsDown && Math.Abs(_map.TerrainLevel(originTile) - _map.TerrainLevel(finalTile)) > 10) return Invalid;
            originTile = Tile(Add(pos, 1, 0, 0));
            finalTile = Tile(Add(pos, 0, 1, 0));
            if (originTile < 0 || finalTile < 0) return Invalid;
            if (IsBlockedDirection(unit, originTile, 5, bam)) return Invalid;
            if (!triedStairsDown && Math.Abs(_map.TerrainLevel(originTile) - _map.TerrainLevel(finalTile)) > 10) return Invalid;
        }

        if (direction == Down && fallingDown) return new PathfindingStep(default, new PathfindingCost((short)firePenaltyCost, 0), pos);

        return Price(unit, armor, direction, bam, totalCost, climb, flying, firePenaltyCost, pos);
    }

    /// <summary>The end of getTUCost: the base cost percentages of the unit and the armor's percentage of the kind of step, then rounded.</summary>
    private PathfindingStep Price(BattleUnit unit, RuntimeBattleArmor armor, int direction, BattleActionMove bam, int totalCost, bool climb, bool flying,
        int firePenaltyCost, BattlePosition pos)
    {
        // The reference multiplies plain ints, so a mod's huge percentages wrap.
        unchecked
        {
            const int costDiv = 100 * 100 * 100;
            var time = totalCost;
            var energy = totalCost;
            void Times((int Time, int Energy) percent)
            {
                time *= percent.Time;
                energy *= percent.Energy;
            }

            var steps = armor.StepCosts;
            Times(unit.MoveCosts.Base);
            if (climb) Times(unit.MoveCosts.Climb);
            else if (flying) Times(unit.MoveCosts.Fly);
            else Times(unit.MoveCosts.Normal);

            if (direction >= Up)
            {
                if (climb) Times(direction == Up ? steps.ClimbUp : steps.ClimbDown);
                else if (flying) Times(direction == Up ? steps.FlyUp : steps.FlyDown);
                else Times(steps.GravLift); // the unit uses a grav lift
            }
            else switch (bam)
                {
                    case BattleActionMove.Normal: Times(flying ? steps.FlyWalk : steps.Walk); break;
                    case BattleActionMove.Run: Times(flying ? steps.FlyRun : steps.Run); break;
                    case BattleActionMove.Strafe: Times(flying ? steps.FlyStrafe : steps.Strafe); break;
                    default: Times(steps.Sneak); break; // no flight
                }

            var rounding = _rules.Campaign.ExtendedMovementCostRounding;
            int Round(int percent) => rounding == 0 ? percent / costDiv
                : rounding == 1 ? (percent + costDiv / 2) / costDiv
                : (percent - 1 + costDiv / 2) / costDiv;
            var timeCost = Round(time);
            var energyCost = Round(energy);
            return new PathfindingStep(new PathfindingCost((short)Math.Clamp(timeCost, 1, InvalidMoveCost - 1), (short)Math.Clamp(energyCost, 0, InvalidMoveCost)),
                new PathfindingCost((short)firePenaltyCost, 0), pos);
        }
    }

    // ---- what blocks ----

    /// <summary>Pathfinding::isBlockedDirection: whether something blocks a move from the tile in a direction.</summary>
    public bool IsBlockedDirection(BattleUnit unit, BattlePosition start, int direction, BattleActionMove bam = BattleActionMove.Normal) =>
        BattlePathing.IsBlockedDirection(_map, MovementTypeOf(unit, bam), start, direction, _options.StrictBlockedChecking);

    private bool IsBlockedDirection(BattleUnit unit, int startTile, int direction, BattleActionMove bam) =>
        IsBlockedDirection(unit, _map.PositionOf(startTile), direction, bam);

    /// <summary>
    /// Pathfinding::isBlocked for the floor and the object part of a tile (-1 is no tile, outside the map): a unit in the way that the
    /// mover knows of (a visible unit for the player, any unit of its own side, a unit it spotted this turn), the fall onto or of a big
    /// unit through a tile without floor, and an impassable part.
    /// </summary>
    private bool IsBlockedFloorOrObject(BattleUnit unit, int tile, BattleTilePart part, BattleActionMove bam)
    {
        if (tile < 0) return true; // probably outside the map here
        var movementType = MovementTypeOf(unit, bam);
        if (part == BattleTilePart.Floor)
        {
            if (_map.UnitAt(tile) is { } standing && !IgnoreFriends)
            {
                var other = _battle.UnitById(standing)!;
                if (other == unit || other.IsOut) return false;
                if (unit.Faction == BattleFaction.Player && other.IsVisibleToPlayer(_rules)) return true; // the player knows all visible units
                if (unit.Faction == other.Faction) return true;
                if (unit.Faction != BattleFaction.Player && unit.UnitsSpottedThisTurn.Contains(other.Id)) return true;
            }
            else if (_map.HasNoFloorOnly(tile) && movementType != BattleState.MovementTypeFly)
            {
                // This whole section keeps large units out of any falling.
                for (var below = tile; below >= 0; below -= _layer)
                {
                    if (_map.UnitAt(below) is { } id && id != unit.Id)
                    {
                        var other = _battle.UnitById(id)!;
                        // don't let large units fall on other units
                        if (unit.IsBigUnit) return true;
                        // don't let any units fall on large units
                        if (!other.IsOut && other.IsBigUnit) return true;
                    }
                    // not going to fall any further, so we can stop checking.
                    if (!_map.HasNoFloorOnly(below)) break;
                }
            }
        }
        return _map.TimeUnitCost(tile, part, movementType) == InvalidMoveCost; // blocking part
    }

    /// <summary>Tile::getOverlappingUnit with <c>TUO_IGNORE_SMALL</c> (26): the unit on the tile, else one below whose voxels poke into it.</summary>
    private BattleUnit? OverlappingUnit(int tile)
    {
        const int ignoreSmall = 26;
        var id = _map.UnitAt(tile);
        if (id is null && tile >= _layer && _map.HasNoFloor(tile) && _map.MapData(tile, BattleTilePart.ObjectPart) is null)
        {
            var below = tile - _layer;
            id = _map.UnitAt(below);
            if (id is { } belowId)
            {
                var other = _battle.UnitById(belowId)!;
                var heights = BattleState.HeightsOf(_rules, other);
                // if the unit below has no voxels poking into the tile, don't select it.
                if ((other.Kneeled ? heights.Kneel : heights.Stand) + heights.Floating - _map.TerrainLevel(below) <= ignoreSmall) id = null;
            }
        }
        return id is { } found ? _battle.UnitById(found) : null;
    }

    /// <summary>Pathfinding::canFallDown: a unit can fall from a tile that is above the ground level and has no floor.</summary>
    private bool CanFallDown(int tile) => tile >= _layer && _map.HasNoFloor(tile);

    /// <summary>Pathfinding::canFallDown for a unit of a size: every tile under it must have no floor.</summary>
    private bool CanFallDown(int tile, int size)
    {
        var position = _map.PositionOf(tile);
        for (var x = 0; x != size; x++)
            for (var y = 0; y != size; y++)
            {
                var check = Tile(Add(position, x, y, 0));
                if (check < 0 || !CanFallDown(check)) return false;
            }
        return true;
    }

    /// <summary>
    /// Pathfinding::isOnStairs: whether going to the end position from the start is going up a stairs (the start on the stairs or in front
    /// of them, the stairs' terrain levels -16 then -8, or TFTD's -18 then -12).
    /// </summary>
    public bool IsOnStairs(BattlePosition startPosition, BattlePosition endPosition)
    {
        int Level(int x, int y) => Tile(Add(endPosition, x, y, 0)) is var t and >= 0 ? _map.TerrainLevel(t) : int.MinValue;
        bool Exists(int x, int y) => Tile(Add(endPosition, x, y, 0)) >= 0;
        bool StartAt(int x, int y) => startPosition == Add(endPosition, x, y, 0);

        // The upper part of the stairs to the south (-16), the lower part (-8) one further; the start on one of the three tiles.
        if (Exists(0, 1) && Level(0, 1) == -16)
        {
            if (Exists(0, 2) && Level(0, 2) != -8) return false;
            if (StartAt(0, 1) || StartAt(0, 2) || StartAt(0, 3)) return true;
        }
        // The same for the east-west oriented stairs.
        if (Exists(1, 0) && Level(1, 0) == -16)
        {
            if (Exists(2, 0) && Level(2, 0) != -8) return false;
            if (StartAt(1, 0) || StartAt(2, 0) || StartAt(3, 0)) return true;
        }
        // TFTD's stairs: -18 then -12.
        if (Exists(0, 1) && Level(0, 1) == -18)
        {
            if (Exists(0, 2) && Level(0, 2) != -12) return false;
            if (StartAt(0, 1) || StartAt(0, 2) || StartAt(0, 3)) return true;
        }
        if (Exists(1, 0) && Level(1, 0) == -18)
        {
            if (Exists(2, 0) && Level(2, 0) != -12) return false;
            if (StartAt(1, 0) || StartAt(2, 0) || StartAt(3, 0)) return true;
        }
        return false;
    }

    /// <summary>
    /// Pathfinding::validateUpDown: whether a unit can go up or down (direction 8 or 9) from the tile: a grav lift floor on both levels, or
    /// a flying unit or a ladder with no roof above (going up) or no floor here (going down).
    /// </summary>
    public bool ValidateUpDown(BattleUnit unit, BattlePosition startPosition, int direction) => ValidateUpDown(unit, Tile(startPosition), direction);

    private bool ValidateUpDown(BattleUnit unit, int startTile, int direction)
    {
        var endPosition = Add(_map.PositionOf(startTile), DirX[direction], DirY[direction], DirZ[direction]);
        var destination = Tile(endPosition);
        if (destination < 0) return false;
        if (_map.HasGravLiftFloor(startTile) && _map.HasGravLiftFloor(destination)) return true;
        if (unit.MovementType == BattleState.MovementTypeFly || _map.HasLadder(startTile))
        {
            if ((direction == Up && _map.HasNoFloor(destination)) || (direction == Down && _map.HasNoFloor(startTile))) return true;
        }
        return false;
    }

    // ---- Bresenham ----

    /// <summary>Pathfinding::bresenhamPath: a straight line across the map, which counts only when every step is valid and costs about the same.</summary>
    private bool BresenhamPath(BattlePosition origin, BattlePosition target, BattleActionMove bam, bool sneak)
    {
        var unit = _unit!;
        int[] xd = [0, 1, 1, 1, 0, -1, -1, -1];
        int[] yd = [-1, -1, 0, 1, 1, 1, 0, -1];
        var lastPoint = origin;
        var lastTuCost = -1;
        _totalCost = default;

        // start and end points
        int x0 = origin.X, x1 = target.X, y0 = origin.Y, y1 = target.Y, z0 = origin.Z, z1 = target.Z;

        // 'steep' xy line: make the longest delta x plane
        var swapXy = Math.Abs(y1 - y0) > Math.Abs(x1 - x0);
        if (swapXy)
        {
            (x0, y0) = (y0, x0);
            (x1, y1) = (y1, x1);
        }
        // do the same for xz
        var swapXz = Math.Abs(z1 - z0) > Math.Abs(x1 - x0);
        if (swapXz)
        {
            (x0, z0) = (z0, x0);
            (x1, z1) = (z1, x1);
        }

        // delta is the length in each plane
        var deltaX = Math.Abs(x1 - x0);
        var deltaY = Math.Abs(y1 - y0);
        var deltaZ = Math.Abs(z1 - z0);
        // drift controls when to step in the 'shallow' planes; the starting value keeps the line centred
        var driftXy = deltaX / 2;
        var driftXz = deltaX / 2;
        var stepX = x0 > x1 ? -1 : 1;
        var stepY = y0 > y1 ? -1 : 1;
        var stepZ = z0 > z1 ? -1 : 1;
        var y = y0;
        var z = z0;

        // step through the longest delta (which has been swapped to x)
        for (var x = x0; x != x1 + stepX; x += stepX)
        {
            int cx = x, cy = y, cz = z;
            // unswap (in reverse)
            if (swapXz) (cx, cz) = (cz, cx);
            if (swapXy) (cx, cy) = (cy, cx);

            if (x != x0 || y != y0 || z != z0)
            {
                var realNextPoint = new BattlePosition(cx, cy, cz);
                // get the direction
                int dir;
                for (dir = 0; dir < 8; dir++)
                    if (xd[dir] == cx - lastPoint.X && yd[dir] == cy - lastPoint.Y) break;
                if (dir == 8) dir = cz - lastPoint.Z > 0 ? Up : Down;
                var r = GetTuCost(lastPoint, dir, unit, bam);
                var nextPoint = r.Position;
                var tuCost = r.Cost.Time + r.Penalty.Time;

                if (sneak && Tile(nextPoint) is var nextTile and >= 0 && _map.IsVisible(nextTile)) return false;

                var isDiagonal = (dir & 1) != 0;
                var lastTuCostDiagonal = lastTuCost + lastTuCost / 2;
                var tuCostDiagonal = tuCost + tuCost / 2;
                if (nextPoint == realNextPoint && r.IsValid &&
                    (tuCost == lastTuCost || (isDiagonal && tuCost == lastTuCostDiagonal) || (!isDiagonal && tuCostDiagonal == lastTuCost) || lastTuCost == -1) &&
                    !IsBlockedDirection(unit, lastPoint, dir, bam))
                    _path.Add(dir);
                else return false;
                if (r.IsValid)
                {
                    lastTuCost = tuCost;
                    _totalCost = _totalCost + r.Cost + r.Penalty;
                }
                lastPoint = new BattlePosition(cx, cy, cz);
            }
            // update progress in the other planes
            driftXy -= deltaY;
            driftXz -= deltaZ;
            // step in the y plane
            if (driftXy < 0)
            {
                y += stepY;
                driftXy += deltaX;
            }
            // same in z
            if (driftXz < 0)
            {
                z += stepZ;
                driftXz += deltaX;
            }
        }
        return true;
    }

    // ---- reachable tiles ----

    /// <summary>
    /// Pathfinding::findReachable: the tiles a unit can reach within its time units and energy less the cost (the cost of the action it
    /// keeps them for), cheapest first; the first is the start.
    /// </summary>
    public IReadOnlyList<int> FindReachable(BattleUnit unit, BattleActionCost cost, out bool ranOutOfTimeUnits)
    {
        var reachability = FindReachableNodes(unit, cost);
        ranOutOfTimeUnits = reachability.RanOutOfTimeUnits;
        return reachability.Tiles(_map);
    }

    /// <summary>
    /// Pathfinding::findReachablePathFindingNodes (Dijkstra): the nodes within the unit's time units and energy less the cost, or within its
    /// full statistics (<paramref name="useMaxTimeUnits"/>), or the whole map (<paramref name="entireMap"/>), cheapest first. The
    /// reference sorts them with a comparison that orders only costs smaller in both time and energy, which no sort is defined for; the port
    /// orders by time, then energy, and keeps the visiting order of equal costs (ADR 0040).
    /// </summary>
    public BattleReachability FindReachableNodes(BattleUnit unit, BattleActionCost cost, bool entireMap = false, BattlePosition? alternateStart = null,
        bool justCheckIfAnyMovementIsPossible = false, bool useMaxTimeUnits = false, BattleActionMove bam = BattleActionMove.Normal)
    {
        ArgumentNullException.ThrowIfNull(unit);
        ArgumentNullException.ThrowIfNull(cost);
        CheckSize(unit);
        _unit = unit;
        var ranOutOfTimeUnits = false;
        var start = alternateStart ?? unit.Position ?? throw new InvalidOperationException($"Battle unit {unit.Id} is not on the map.");
        var tuMax = unit.TimeUnits - cost.Time;
        var energyMax = unit.Energy - cost.Energy;
        if (useMaxTimeUnits)
        {
            tuMax = unit.Stats.TimeUnits;
            energyMax = unit.Stats.Stamina;
        }
        var costMax = new PathfindingCost(unchecked((short)tuMax), unchecked((short)energyMax));

        var nodes = _nodes;
        if (alternateStart is not null) nodes = _altNodes ??= new Node[_map.TileCount];
        foreach (ref var node in nodes.AsSpan()) node.Checked = false;
        foreach (ref var node in nodes.AsSpan()) node.OpenEntry = 0;
        var startTile = Tile(start);
        if (startTile < 0) throw new InvalidOperationException($"The start {start} is outside the map.");
        Connect(nodes, startTile, default, -1, 0);
        var unvisited = new OpenSet(nodes);
        unvisited.Push(startTile);
        var reachable = new List<int>();
        var size = _map.TileCount;
        var maxTilesToReturn = size;
        if (_options.AiPerformanceOptimization)
        {
            var myUnits = _battle.Units.Count(other => other.Faction == unit.Faction && !other.IsOut);
            var scaleFactor = (float)60 * 60 * 4 * 30 / (_map.TileCount * myUnits);
            if (scaleFactor < 1) maxTilesToReturn = (int)(maxTilesToReturn * scaleFactor);
        }
        while (!unvisited.IsEmpty)
        {
            var current = unvisited.Pop();
            var currentPosition = _map.PositionOf(current);
            if (reachable.Count > maxTilesToReturn) entireMap = false;

            for (var direction = 0; direction < 10; direction++)
            {
                var r = GetTuCost(currentPosition, direction, unit, bam);
                if (!r.IsValid) continue;
                var totalTuCost = nodes[current].Cost + r.Cost + r.Penalty;
                if (!totalTuCost.NoMoreThan(costMax) && !entireMap)
                {
                    ranOutOfTimeUnits = true; // out of time units or energy
                    continue;
                }
                var next = _map.TileIndex(r.Position);
                if (nodes[next].Checked) continue; // already at its minimum cost
                // Unvisited, or visited from a better path.
                if (!nodes[next].InOpenSet || nodes[next].Cost.Time > totalTuCost.Time)
                {
                    Connect(nodes, next, totalTuCost, current, direction);
                    unvisited.Push(next);
                }
            }
            nodes[current].Checked = true;
            reachable.Add(current);
            if (justCheckIfAnyMovementIsPossible && reachable.Count > 1) break;
        }

        var sorted = reachable.OrderBy(tile => nodes[tile].Cost.Time).ThenBy(tile => nodes[tile].Cost.Energy)
            .Select(tile => new BattleReachableNode(_map.PositionOf(tile), nodes[tile].Cost,
                nodes[tile].Previous >= 0 ? _map.PositionOf(nodes[tile].Previous) : null, nodes[tile].PreviousDirection))
            .ToArray();
        return new BattleReachability(sorted, ranOutOfTimeUnits);
    }
}

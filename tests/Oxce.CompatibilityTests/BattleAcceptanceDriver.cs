using System.Diagnostics;
using System.Text.Json;
using Oxce.Engine.Battlescape;
using Oxce.TestSupport;
using Oxce.Gameplay.Battles;
using Oxce.Gameplay.Battles.Terrain;
using Oxce.Gameplay.Campaigns;
using Xunit;

namespace Oxce.CompatibilityTests;

/// <summary>
/// Phase 7 branch 4 S11: the scripted soldier of the acceptance battles. It plays through the battlescape client's model (the commands the
/// screen sends: a tile click, the right click, the kneel button), so the rules answer as they do for a player.
/// </summary>
internal static class BattleAcceptanceDriver
{
    /// <summary>What a battle's scripted turn did; <see cref="Refusal"/> is the named reason when a command was refused.</summary>
    internal sealed record Outcome(int UnitId, BattlePosition From, BattlePosition To, int Steps, bool Turned, bool KneltAndStood,
        bool OpponentsSpent, string? Refusal);

    private const int LeftAfterWalk = 28;

    // OpenXcom directions: 0 north, then clockwise.
    private static readonly (int X, int Y)[] Vectors = [(0, -1), (1, -1), (1, 0), (1, 1), (0, 1), (-1, 1), (-1, 0), (-1, -1)];

    /// <summary>
    /// A free player unit (selected first, then the others) leaves its place along the computed path to the farthest reachable tile, turns,
    /// kneels and stands. Returns null when no player unit can walk anywhere.
    /// </summary>
    internal static Outcome? Walk(BattlescapeClient screen, BattleState battle)
    {
        // Time units are kept for the turn, kneel and stand after the walk; a unit with few has less kept.
        Outcome? outcome = null;
        foreach (var kept in new[] { LeftAfterWalk, 16, 0 })
            if ((outcome = Walk(screen, battle, tries: 3, soldiers: 3, kept)) is not null) break;
        if (outcome is not { Refusal: not null }) return outcome;
        // Every walk is refused where an opposing unit within the view distance could react (reaction fire is branch 5's, plan D4). The
        // acceptance then walks with the opposing units out of time units (their reaction score is 0), which the rules accept, and gives
        // them their time units back.
        var spent = WithOpponentsSpent(battle, () => Walk(screen, battle, tries: 8, soldiers: 3, LeftAfterWalk));
        return spent is { Refusal: null } ? spent with { OpponentsSpent = true } : outcome;
    }

    private static T WithOpponentsSpent<T>(BattleState battle, Func<T> action)
    {
        var opponents = battle.Units.Where(static unit => unit.Faction != BattleFaction.Player).Select(static unit => (unit, unit.TimeUnits)).ToList();
        foreach (var (unit, _) in opponents) unit.TimeUnits = 0;
        try { return action(); }
        finally
        {
            foreach (var (unit, timeUnits) in opponents) unit.TimeUnits = timeUnits;
        }
    }

    private static Outcome? Walk(BattlescapeClient screen, BattleState battle, int tries, int soldiers, int kept)
    {
        var model = screen.Model;
        model.Refresh();
        var candidates = model.Selection?.SelectableUnitIds.ToList() ?? [];
        if (model.SelectedUnitId is { } first) candidates.Remove(first);
        if (model.SelectedUnitId is { } own) candidates.Insert(0, own);
        Outcome? refused = null;
        var tried = 0;
        foreach (var id in candidates)
        {
            if (!model.Select(id))
            {
                // Commands on a battle whose armors bind a visibility script are refused (branch 8), selecting included.
                RequireLaterBranch(model.Message);
                refused ??= Refused(id, battle.Units.Single(candidate => candidate.Id == id).Position ?? default, model.Message);
                continue;
            }
            var unit = battle.Units.Single(candidate => candidate.Id == id);
            if (unit.Position is not { } from) continue;
            model.Camera.SetViewLevel(from.Z);
            model.ShowReachable = true;
            model.Refresh();
            var reach = model.Reachable;
            model.ShowReachable = false;
            if (reach is null || reach.Tiles.Count < 2) continue;
            if (tried++ == soldiers) break;
            // The farthest tiles first: a walk that an opposing unit could react to is refused (reaction fire is branch 5's), so nearer ones follow.
            var targets = reach.Tiles.Where(tile => tile.Z == from.Z && tile != from)
                .OrderByDescending(tile => Math.Max(Math.Abs(tile.X - from.X), Math.Abs(tile.Y - from.Y))).Take(200);
            var before = unit.TimeUnits;
            BattlePosition target = default;
            var walks = 0;
            var walked = false;
            foreach (var candidate in targets)
            {
                model.Camera.SetViewLevel(candidate.Z);
                model.Preview(null, null);
                model.Preview(candidate.X, candidate.Y);
                var path = model.Path;
                // A route through tiles the viewer has not discovered has no preview (the query keeps what the viewer cannot see to itself).
                if (path is null || path.Steps.Count == 0) continue;
                // Time units are left to turn, kneel and stand after the walk (the reserve of the kneel, its standing and a turn).
                if (path.TimeUnitsCost > unit.TimeUnits - kept) continue;
                if (walks++ == tries) break;
                var ok = model.Walk(candidate.X, candidate.Y, ignoreSpottedEnemies: true);
                if (ok)
                {
                    (target, walked) = (path.Steps[^1].Position, true);
                    break;
                }
                // A refusal of a later branch leaves the battle as it was.
                RequireLaterBranch(model.Message);
                refused ??= Refused(id, from, model.Message);
            }
            if (!walked) continue;
            var steps = model.LastEvents.OfType<UnitStepped>().Count();
            Assert.True(steps > 0, "The walk made no step.");
            Assert.True(unit.TimeUnits < before);
            // The unit stands where the walk ended; it followed the path to the end unless an event names why it stopped (a unit it spotted,
            // time units or energy that the next step lacked, the reserve).
            var ended = model.LastEvents.OfType<UnitWalkEnded>().Single();
            Assert.Equal(unit.Position!.Value, ended.Position);
            // (A tile without a floor ends in a fall, which the events show.)
            if (ended.Stop == BattleWalkStop.Arrived && !model.LastEvents.OfType<UnitFell>().Any()) Assert.Equal(target, unit.Position!.Value);
            // Turn towards the tile beside it, then kneel and stand again (the right click and the kneel button).
            var at = unit.Position!.Value;
            var kneeled = model.Kneel();
            if (kneeled)
            {
                Assert.True(model.LastEvents.OfType<UnitKneeled>().Single().Kneeling);
                Assert.True(unit.Kneeled);
                kneeled = model.Kneel();
                if (kneeled) Assert.False(unit.Kneeled);
            }
            var turned = false;
            for (var offset = 3; offset < 11 && !turned; offset++)
            {
                var wanted = (unit.Direction + offset) % 8;
                var (dx, dy) = Vectors[wanted];
                if (wanted == unit.Direction || battle.Map.TileAt(at.X + dx, at.Y + dy, at.Z) < 0 || !model.Turn(at.X + dx, at.Y + dy)) continue;
                var turn = model.LastEvents.OfType<UnitTurned>().Single();
                Assert.Equal(unit.Direction, turn.Direction);
                turned = turn.Stop == BattleTurnStop.Arrived;
                if (turned) Assert.Equal(wanted, unit.Direction);
            }
            return new(id, from, at, steps, turned, kneeled, false, null);
        }
        return refused;
    }

    /// <summary>The cost of the queries and the field of view on one battle, in microseconds (medians) of a Debug build.</summary>
    internal sealed record Timing(string Label, string Size, int Tiles, int Units, int PathSteps, double PathUs, double ReachableUs, double FovUs, double LevelUs);

    private static readonly Dictionary<string, object> Walks = [];

    /// <summary>Records what a battle's scripted turn did in <c>artifacts/private-battle-acceptance-walks.json</c>.</summary>
    internal static void Record(string label, Outcome walk, string? door)
    {
        lock (Walks)
        {
            Walks[label] = new { walk, Door = door ?? "none in reach" };
            var path = TestFixtures.RepositoryPath("artifacts", "private-battle-acceptance-walks.json");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, JsonSerializer.Serialize(Walks.OrderBy(static item => item.Key, StringComparer.Ordinal).ToDictionary(), Indented));
        }
    }

    private static readonly List<Timing> Timings = [];
    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

    /// <summary>
    /// Times <c>QueryPath</c> (to the farthest reachable tile), <c>QueryReachable</c>, a whole <c>RecalculateFov</c> and one level's query for the
    /// local player's selected unit, and records them in <c>artifacts/private-battle-acceptance-timings.json</c>. Changes nothing but the battle's
    /// revision (the field of view is recomputed over itself).
    /// </summary>
    internal static void Measure(string label, CampaignState campaign, BattleState battle)
    {
        var view = campaign.GetQuery<IBattleViewQuery>()!;
        var viewer = BattleViewer.LocalPlayer;
        var selection = view.QuerySelection(viewer);
        // The selected unit, or the first of the side that can reach a tile beside its own (a unit that boxed in or tired measures nothing).
        var ids = (selection?.SelectedUnitId is { } own ? [own] : Enumerable.Empty<int>()).Concat(selection?.SelectableUnitIds ?? []);
        int? unit = null;
        BattleReachableView? reach = null;
        foreach (var id in ids)
            if (view.QueryReachable(viewer, id) is { Tiles.Count: > 1 } candidate)
            {
                (unit, reach) = (id, candidate);
                break;
            }
        if (unit is null) return;
        var from = battle.Units.Single(candidate => candidate.Id == unit).Position;
        // The farthest tile that has a path the viewer may see.
        var target = reach!.Tiles.OrderByDescending(tile => from is { } f ? Distance(tile, f) : 0)
            .Select(tile => (Tile: tile, Path: view.QueryPath(viewer, unit.Value, tile))).FirstOrDefault(static pair => pair.Path is { Steps.Count: > 0 });
        // A unit with no tile that has a visible path measures no path query.
        if (target.Path is null) return;
        var pathSteps = target.Path.Steps.Count;
        double Median(Action action)
        {
            action();
            var samples = new double[9];
            for (var i = 0; i < samples.Length; i++)
            {
                var start = Stopwatch.GetTimestamp();
                action();
                samples[i] = Stopwatch.GetElapsedTime(start).TotalMicroseconds;
            }
            return Math.Round(samples.Order().ElementAt(4), 1);
        }
        var timing = new Timing(label, $"{battle.Width}x{battle.Length}x{battle.Height}", battle.Width * battle.Length * battle.Height, battle.Units.Count,
            pathSteps, Median(() => view.QueryPath(viewer, unit.Value, target.Tile)), Median(() => view.QueryReachable(viewer, unit.Value)),
            Median(battle.RecalculateFov), Median(() => view.QueryLevel(viewer, from?.Z ?? 0)));
        lock (Timings)
        {
            Timings.RemoveAll(existing => existing.Label == label);
            Timings.Add(timing);
            var path = TestFixtures.RepositoryPath("artifacts", "private-battle-acceptance-timings.json");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, JsonSerializer.Serialize(Timings.OrderBy(static item => item.Label, StringComparer.Ordinal),
                Indented));
        }
    }

    /// <summary>
    /// What a reload must keep: every viewer's discovered tiles with their parts, shade, smoke, fire, items and visible units on every level,
    /// and the units each viewer sees (lighting and the discovered tiles are derived on a load, so equal views mean equal derived state; spotting may only grow).
    /// </summary>
    internal static void AssertSameSight(CampaignState expected, CampaignState actual, BattleState battle)
    {
        var before = expected.GetQuery<IBattleViewQuery>()!;
        var after = actual.GetQuery<IBattleViewQuery>()!;
        foreach (var faction in Enum.GetValues<BattleFaction>())
        {
            var viewer = new BattleViewer(faction);
            for (var level = 0; level < battle.Height; level++)
            {
                var x = before.QueryLevel(viewer, level)!.Tiles;
                var y = after.QueryLevel(viewer, level)!.Tiles;
                var diff = new List<string>();
                var byPos = y.ToDictionary(static t => t.Position);
                foreach (var t in x)
                {
                    if (!byPos.TryGetValue(t.Position, out var u)) { diff.Add($"{t.Position} only before (shade {t.Shade}, parts {t.Parts.Count})"); continue; }
                    if (t.Shade != u.Shade) diff.Add($"{t.Position} shade {t.Shade} vs {u.Shade}");
                    if (t.UnitId is not null && t.UnitId != u.UnitId || t.Smoke != u.Smoke || t.Fire != u.Fire || !t.Parts.SequenceEqual(u.Parts) || !t.Items.SequenceEqual(u.Items)) diff.Add($"{t.Position} other");
                }
                diff.AddRange(y.Where(t => !x.Any(o => o.Position == t.Position)).Select(t => $"{t.Position} only after"));
                Assert.True(diff.Count == 0, $"{faction} level {level}: {diff.Count} differ: {string.Join("; ", diff.Take(8))}");
            }
            // The load's field of view looks from every unit at once, so it may spot units that the moves of one soldier had not shown; it never
            // drops one (the same as the generated battle's reload, AssertReloads).
            var seen = actual.GetQuery<IBattleQuery>()!.QueryBattle(viewer)!.Units;
            Assert.All(expected.GetQuery<IBattleQuery>()!.QueryBattle(viewer)!.Units, unit => Assert.Contains(unit, seen));
        }
    }

    /// <summary>A refusal the acceptance accepts is one that names a later branch's unported rule (<c>battle.*-unsupported</c>), not one of this branch's own rules.</summary>
    internal static void RequireLaterBranch(string code) =>
        Assert.True(code.StartsWith("battle.", StringComparison.Ordinal) && code.EndsWith("-unsupported", StringComparison.Ordinal), $"Refusal {code} is not a later branch's.");

    private static Outcome Refused(int id, BattlePosition from, string code)
    {
        RequireLaterBranch(code);
        return new(id, from, from, 0, false, false, false, code);
    }

    /// <summary>
    /// Opens a door where the map has one that a soldier can reach on its turn: the soldier walks to a tile beside it and turns towards it
    /// until the right click opens it. Returns what the door answered (<c>"Opened"</c> or <c>"UfoDoorOpened"</c>), or null when no soldier
    /// can reach a tile beside a door (or the map has none).
    /// </summary>
    internal static string? OpenDoor(BattlescapeClient screen, BattleState battle) =>
        OpenDoorOnce(screen, battle) is { } door ? door : WithOpponentsSpent(battle, () => OpenDoorOnce(screen, battle) is { } spent ? spent + " (opponents spent)" : null);

    private static string? OpenDoorOnce(BattlescapeClient screen, BattleState battle)
    {
        var model = screen.Model;
        var map = battle.Map;
        var doors = new List<(BattlePosition Stand, int Direction)>();
        for (var tile = 0; tile < map.TileCount; tile++)
            foreach (var part in new[] { BattleTilePart.NorthWall, BattleTilePart.WestWall })
            {
                if (!map.IsDoor(tile, part) && map.MapData(tile, part) is not { IsUfoDoor: true }) continue;
                if (map.IsUfoDoorOpen(tile, part)) continue;
                var at = map.PositionOf(tile);
                var north = part == BattleTilePart.NorthWall;
                doors.Add((at, north ? 0 : 6));
                doors.Add((north ? at with { Y = at.Y - 1 } : at with { X = at.X - 1 }, north ? 4 : 2));
            }
        if (doors.Count == 0) return null;
        model.Refresh();
        foreach (var id in model.Selection?.SelectableUnitIds.ToList() ?? [])
        {
            if (!model.Select(id)) continue;
            var unit = battle.Units.Single(candidate => candidate.Id == id);
            if (unit.Position is not { } from) continue;
            model.Camera.SetViewLevel(from.Z);
            model.ShowReachable = true;
            model.Refresh();
            var reach = model.Reachable;
            model.ShowReachable = false;
            if (reach is null) continue;
            var reachable = reach.Tiles.ToHashSet();
            foreach (var (stand, direction) in doors.Where(door => reachable.Contains(door.Stand)).OrderBy(door => Distance(door.Stand, from)))
            {
                if (stand != from && !model.Walk(stand.X, stand.Y, ignoreSpottedEnemies: true)) continue;
                if (unit.Position != stand) continue;
                var (dx, dy) = Vectors[direction];
                for (var attempt = 0; attempt < 4; attempt++)
                {
                    if (!model.Turn(stand.X + dx, stand.Y + dy)) break;
                    if (model.LastEvents.OfType<UnitTurned>().FirstOrDefault() is { Door: BattleDoorResult.Opened or BattleDoorResult.UfoDoorOpened } opened)
                    {
                        Assert.True(opened.DoorsOpened > 0);
                        return opened.Door.ToString();
                    }
                }
            }
        }
        return null;
    }

    private static int Distance(BattlePosition a, BattlePosition b) => Math.Max(Math.Abs(a.X - b.X), Math.Abs(a.Y - b.Y));
}

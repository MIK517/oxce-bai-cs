using Oxce.Gameplay.Battles;
using Oxce.Gameplay.Campaigns;

namespace Oxce.Engine.Battlescape;

/// <summary>
/// One beat of a replayed action: the unit it concerns, where a unit that moved now stands (a step or a fall; null for the others) and the line
/// the screen shows. A command resolves fully in the rules (ADR 0037); the replay is presentation that plays its events back one after another.
/// </summary>
public readonly record struct BattleReplayFrame(int UnitId, BattlePosition? Position, string Text, BattlePosition? Marker = null);

/// <summary>
/// Turns the events of a command into the frames the battlescape replays: the walk's steps and the shots, hits, blasts, deaths and reactions of an attack (a unit that
/// is not on turn fires in a reaction; its shot shows the same way). <see cref="BattleReplayFrame.Marker"/> is the tile a shot ended in or a blast centred on.
/// </summary>
public static class BattleReplay
{
    /// <summary>The frames of the events, in order; events that are not part of a movement are skipped.</summary>
    public static IReadOnlyList<BattleReplayFrame> From(IReadOnlyList<ICampaignEvent> events)
    {
        ArgumentNullException.ThrowIfNull(events);
        var frames = new List<BattleReplayFrame>();
        foreach (var raised in events)
            switch (raised)
            {
                case UnitKneeled kneeled:
                    frames.Add(new(kneeled.UnitId, null, $"{(kneeled.Kneeling ? "Kneels" : "Stands up")} ({kneeled.TimeUnitsSpent} TU)"));
                    break;
                case UnitTurned turned:
                    var door = turned.Door == BattleDoorResult.None ? "" : $", door {turned.Door} ({turned.DoorsOpened} opened, {turned.TimeUnitsSpent} TU)";
                    frames.Add(new(turned.UnitId, null, turned.Stop == BattleTurnStop.NoTurn
                        ? $"Faces {turned.Direction}{door}"
                        : $"Turns to {turned.Direction}: {turned.Stop} ({turned.Eighths} eighths, {turned.TimeUnitsSpent} TU)"));
                    break;
                case UnitWalkTurned walkTurned:
                    frames.Add(new(walkTurned.UnitId, null, $"Turns to {walkTurned.Direction}"));
                    break;
                case UnitWalkOpenedDoor opened:
                    frames.Add(new(opened.UnitId, null, $"Door {opened.Door} ({opened.DoorsOpened} opened, {opened.TimeUnitsSpent} TU)"));
                    break;
                case UnitStepped step:
                    frames.Add(new(step.UnitId, step.To, $"Step {step.Direction} ({step.TimeUnitsSpent} TU, {step.EnergySpent} En)"));
                    break;
                case UnitFell fell:
                    frames.Add(new(fell.UnitId, fell.To, "Falls"));
                    break;
                case UnitsSpotted spotted:
                    frames.Add(new(spotted.UnitId, null, $"Spots {spotted.Spotted.Count} unit(s)"));
                    break;
                case UnitFired fired:
                    frames.Add(new(fired.UnitId, null, $"Unit {fired.UnitId} fires {fired.Kind} ({Impact(fired.Impact)})", fired.Trajectory.Count == 0 ? null : TileOf(fired.Trajectory[^1])));
                    break;
                case UnitAttackStopped stopped:
                    frames.Add(new(stopped.UnitId, null, stopped.Reason.Length == 0 ? $"{stopped.Kind} stops" : $"{stopped.Kind} stops: {stopped.Reason}"));
                    break;
                case UnitCloseQuartersFailed close:
                    frames.Add(new(close.UnitId, null, $"Close quarters: unit {close.DefenderId} strikes first", close.Target));
                    break;
                case UnitMeleeAttacked melee:
                    frames.Add(new(melee.UnitId, null, $"Unit {melee.UnitId} strikes {(melee.Hit ? "and hits" : "and misses")}", TileOf(melee.Voxel)));
                    break;
                case BattleExploded blast:
                    frames.Add(new(-1, null, $"{(blast.Terrain ? "Terrain explosion" : "Explosion")} power {blast.Power} radius {blast.Radius}", TileOf(blast.Center)));
                    break;
                case UnitHit hit:
                    frames.Add(new(hit.UnitId, null, $"Unit {hit.UnitId} takes {hit.HealthLost} damage ({hit.Health} left)"));
                    break;
                case UnitDied died:
                    frames.Add(new(died.UnitId, null, $"Unit {died.UnitId} dies"));
                    break;
                case UnitKnockedOut knockedOut:
                    frames.Add(new(knockedOut.UnitId, null, $"Unit {knockedOut.UnitId} is knocked out"));
                    break;
                case UnitConverted converted:
                    frames.Add(new(converted.UnitId, null, $"Unit {converted.UnitId} turns into unit {converted.NewUnitId}"));
                    break;
                case UnitIgnited ignited:
                    frames.Add(new(ignited.UnitId, null, $"Unit {ignited.UnitId} burns"));
                    break;
                case ItemPrimed primed:
                    frames.Add(new(primed.UnitId, null, $"Primed: {primed.Turns} turns"));
                    break;
                case ItemUnprimed unprimed:
                    frames.Add(new(unprimed.UnitId, null, "Unprimed"));
                    break;
                case UnitWalkEnded ended:
                    frames.Add(new(ended.UnitId, null, $"Walk ends: {ended.Stop} after {ended.Steps} steps ({ended.TimeUnitsSpent} TU, {ended.EnergySpent} En)"));
                    break;
            }
        return frames;
    }

    private static BattlePosition TileOf(BattlePosition voxel) => new(voxel.X / 16, voxel.Y / 16, voxel.Z / 24);

    private static string Impact(int voxelType) => voxelType switch
    {
        0 => "floor",
        1 or 2 => "wall",
        3 => "object",
        4 => "unit",
        5 => "out of the map",
        _ => "nothing",
    };
}

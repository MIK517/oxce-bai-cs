using Oxce.Gameplay.Battles;
using Oxce.Gameplay.Campaigns;

namespace Oxce.Engine.Battlescape;

/// <summary>
/// One beat of a replayed action: the unit it concerns, where a unit that moved now stands (a step or a fall; null for the others) and the line
/// the screen shows. A command resolves fully in the rules (ADR 0037); the replay is presentation that plays its events back one after another.
/// </summary>
public readonly record struct BattleReplayFrame(int UnitId, BattlePosition? Position, string Text);

/// <summary>Turns the events of a movement command into the frames the battlescape replays.</summary>
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
                case UnitWalkEnded ended:
                    frames.Add(new(ended.UnitId, null, $"Walk ends: {ended.Stop} after {ended.Steps} steps ({ended.TimeUnitsSpent} TU, {ended.EnergySpent} En)"));
                    break;
            }
        return frames;
    }
}

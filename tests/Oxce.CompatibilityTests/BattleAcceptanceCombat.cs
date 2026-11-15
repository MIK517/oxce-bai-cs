using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using Oxce.Engine.Battlescape;
using Oxce.Gameplay.Battles;
using Oxce.Gameplay.Campaigns;
using Oxce.Mods.Rulesets.Runtime;
using Oxce.TestSupport;
using Xunit;

namespace Oxce.CompatibilityTests;

/// <summary>
/// Phase 7 branch 5 S11: the scripted combat of the acceptance battles. After the first turn (or on a loaded active-battle save) a player soldier
/// fires its weapon at a spotted unit, or force fires at a discovered tile when none is spotted, throws a primed grenade and strikes in melee where
/// a unit can reach another. Shots and the hand buttons go through the battlescape client's model; the throw is sent as the command the client sends,
/// so its cost can be timed on its own. A refusal the acceptance accepts is a named one, never a silent no-op.
/// </summary>
internal static class BattleAcceptanceCombat
{
    /// <summary>What a battle's combat did. A refusal is the code the first refused attempt gave (null when none was refused).</summary>
    internal sealed record Outcome(string? Shot, int ShotCount, int Hits, int Deaths, string? ShotRefusal, string? Throw, int Blasts, int ThrowDeaths,
        string? ThrowRefusal, string? Strike, string? StrikeRefusal, int ReloadDiscovered = 0);

    /// <summary>
    /// The cost of the attack commands, in microseconds (medians, one sample per command; a Debug build) and of the one copy of the battle that is left (the briefing's spawn
    /// check, <c>CampaignBattles.TrialRefusal</c>: a restore of the battle).
    /// </summary>
    internal sealed record Timing(string Label, string Size, int Tiles, int Units, int ShotSamples, double ShotUs, int ThrowSamples, double ThrowUs, int BlastSamples, double BlastUs, double TrialCopyUs);

    private static readonly int[] Spread = [2, 3, 4, 5, 6, 7];
    private static readonly List<Timing> Timings = [];
    private static readonly Dictionary<string, Outcome> Outcomes = [];
    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

    /// <summary>Fires, throws and strikes with the player's soldiers; returns what happened. Never changes a battle that is not on the player's turn.</summary>
    internal static Outcome Fight(string label, BattleUiContext context, BattlescapeClient screen, CampaignState campaign, BattleState battle)
    {
        var selected = screen.Model.SelectedUnitId;
        // The soldiers have their time units back, as at the start of a turn (the walk and the other commands of the acceptance spent them).
        foreach (var soldier in battle.Units.Where(static unit => unit.Faction == BattleFaction.Player && !unit.IsOut)) soldier.TimeUnits = soldier.Stats.TimeUnits;
        var shot = Shoot(context, screen, campaign, battle, out var shotTimes);
        var thrown = Throw(context, screen, battle, out var throwTimes, out var blastTimes);
        var strike = Strike(screen, battle);
        // The player's selection is where it was (the saved selected unit goes into the battle's save).
        // (A soldier the fight lost is out: the first one left is selected then.)
        screen.Model.Refresh();
        var left = selected is { } own && !battle.Units.Single(unit => unit.Id == own).IsOut ? selected : screen.Model.Selection?.SelectableUnitIds.Cast<int?>().FirstOrDefault();
        if (left is { } keep && screen.Model.SelectedUnitId != keep) screen.Model.Select(keep);
        var outcome = new Outcome(shot.Description, shot.Shots, shot.Hits, shot.Deaths, shot.Refusal, thrown.Description, thrown.Blasts, thrown.Deaths, thrown.Refusal,
            strike.Description, strike.Refusal);
        Record(label, outcome, campaign, battle, shotTimes, throwTimes, blastTimes);
        return outcome;
    }

    private sealed record Part(string? Description, int Shots, int Hits, int Deaths, string? Refusal, int Blasts = 0);

    // ---- the shot ----

    private static Part Shoot(BattleUiContext context, BattlescapeClient screen, CampaignState campaign, BattleState battle, out List<double> times)
    {
        times = [];
        var model = screen.Model;
        var view = campaign.GetQuery<IBattleViewQuery>()!;
        string? refusal = null;
        model.Refresh();
        foreach (var id in (model.Selection?.SelectableUnitIds ?? []).ToList())
        {
            if (!model.Select(id))
            {
                BattleAcceptanceDriver.RequireLaterBranch(model.Message);
                refusal ??= model.Message;
                continue;
            }
            var unit = battle.Units.Single(candidate => candidate.Id == id);
            if (unit.Position is not { } at) continue;
            foreach (var right in new[] { true, false })
            {
                model.Camera.SetViewLevel(at.Z);
                if (!model.OpenMenu(right) || model.Menu is not { Usable: true } menu)
                {
                    model.CancelAim();
                    continue;
                }
                var kind = new[] { BattleAimKind.AimedShot, BattleAimKind.SnapShot, BattleAimKind.AutoShot }
                    .Cast<BattleAimKind?>().FirstOrDefault(candidate => menu.Items.Any(line => line.Kind == candidate));
                if (kind is not { } chosen || !model.Choose(chosen))
                {
                    model.CancelAim();
                    continue;
                }
                var tries = 0;
                foreach (var (target, force) in Targets(battle, view, unit))
                {
                    model.Camera.SetViewLevel(target.Z);
                    if (model.Target(target.X, target.Y, force))
                    {
                        // The command went through but made no shot (the unit only turned, or the shot stopped with a reason): the try counts and its reason is kept.
                        if (!model.LastEvents.OfType<UnitFired>().Any())
                        {
                            if (model.LastEvents.OfType<UnitAttackStopped>().FirstOrDefault() is { } stopped) refusal ??= stopped.Reason;
                            if (++tries == 10) break;
                            continue;
                        }
                        var hits = model.LastEvents.OfType<UnitHit>().Count();
                        var deaths = model.LastEvents.OfType<UnitDied>().Count();
                        var shots = model.LastEvents.OfType<UnitFired>().Count();
                        var attack = new Attack(id, menu.ItemId, chosen, Target: target, ForceFire: force);
                        model.CancelAim();
                        SampleShots(context, battle, unit, attack, times);
                        return new($"{unit.Id} {chosen} {(force ? "at tile" : "at unit")} {target.X},{target.Y},{target.Z}", shots, hits, deaths, refusal);
                    }
                    refusal ??= model.Message;
                    Assert.True(model.Message.Length != 0, "A refused shot names its reason.");
                    if (++tries == 10) break;
                }
                model.CancelAim();
            }
        }
        return new(null, 0, 0, 0, refusal);
    }

    /// <summary>Four more shots of the same command with the unit's time units made up between them (each samples the command and the reactions it draws).</summary>
    private static void SampleShots(BattleUiContext context, BattleState battle, BattleUnit unit, Attack attack, List<double> times)
    {
        for (var sample = 0; sample < 4 && !unit.IsOut; sample++)
        {
            unit.TimeUnits = unit.Stats.TimeUnits;
            var start = Stopwatch.GetTimestamp();
            var result = context.Commands.Execute(attack);
            var elapsed = Stopwatch.GetElapsedTime(start).TotalMicroseconds;
            if (result.Events.OfType<CampaignActionBlocked>().Any()) break;
            times.Add(elapsed);
        }
    }

    /// <summary>The spotted hostile units nearest first (not forced), then the discovered tiles two to seven away on the unit's level, nearest first (forced).</summary>
    private static IEnumerable<(BattlePosition Target, bool Force)> Targets(BattleState battle, IBattleViewQuery view, BattleUnit unit)
    {
        var at = unit.Position!.Value;
        foreach (var other in battle.Units.Where(other => other.Faction == BattleFaction.Hostile && !other.IsOut && other.Position is not null && other.IsSpottedBy(BattleFaction.Player))
                     .OrderBy(other => Distance(other.Position!.Value, at)).Take(3))
            yield return (other.Position!.Value, false);
        var tiles = view.QueryLevel(BattleViewer.LocalPlayer, at.Z)?.Tiles ?? [];
        foreach (var tile in tiles.Where(tile => tile.UnitId is null && Spread.Contains(Distance(tile.Position, at))).OrderBy(tile => Distance(tile.Position, at)).ThenBy(tile => tile.Position.X)
                     .ThenBy(tile => tile.Position.Y).Take(12))
            yield return (tile.Position, true);
    }

    // ---- the throw ----

    private static Part Throw(BattleUiContext context, BattlescapeClient screen, BattleState battle, out List<double> times, out List<double> blastTimes)
    {
        (times, blastTimes) = ([], []);
        var model = screen.Model;
        var view = context.View;
        string? refusal = null;
        string? description = null;
        var blasts = 0;
        var deaths = 0;
        var thrown = 0;
        var grenades = battle.Items.Where(item => item.OwnerUnitId is not null && IsGrenade(battle, item)
            && battle.Units.Single(unit => unit.Id == item.OwnerUnitId).Faction == BattleFaction.Player).ToList();
        foreach (var grenade in grenades)
        {
            if (thrown == 5) break;
            // (A soldier an earlier blast killed has dropped its grenade.)
            if (grenade.OwnerUnitId is not { } ownerId) continue;
            var unit = battle.Units.Single(candidate => candidate.Id == ownerId);
            if (unit.Position is not { } at || unit.IsOut) continue;
            // The soldier throws at full time units from its place; the grenade goes into a hand if it is not in one (a hand with another item drops it).
            unit.TimeUnits = unit.Stats.TimeUnits;
            if (!model.Select(unit.Id))
            {
                BattleAcceptanceDriver.RequireLaterBranch(model.Message);
                refusal ??= model.Message;
                continue;
            }
            var right = HandWithGrenade(context, battle, unit, grenade, out var moveRefusal);
            if (right is null)
            {
                refusal ??= moveRefusal;
                continue;
            }
            model.Camera.SetViewLevel(at.Z);
            model.PrimeTurns = 0;
            // A grenade the menu offers to prime is primed for 0 turns; one it does not (a mod's item with no prime cost) is thrown as it is.
            var primed = false;
            if (model.OpenMenu(right.Value) && model.Menu is { } menu && menu.Items.Any(line => line.Kind == BattleAimKind.Prime) && model.Choose(BattleAimKind.Prime))
            {
                Assert.Contains(model.LastEvents, static evt => evt is ItemPrimed);
                primed = true;
            }
            else
            {
                refusal ??= model.Message.Length != 0 ? model.Message : $"no prime line ({string.Join(",", model.Menu?.Items.Select(line => line.Kind) ?? [])}; {model.Menu?.Reason})";
                model.CancelAim();
            }
            var targets = (view.QueryLevel(BattleViewer.LocalPlayer, at.Z)?.Tiles ?? []).Where(tile => tile.UnitId is null && Distance(tile.Position, at) is >= 3 and <= 9)
                .OrderBy(tile => Distance(tile.Position, at)).ThenBy(tile => tile.Position.X).ThenBy(tile => tile.Position.Y).Take(30).ToList();
            var landed = false;
            if (targets.Count == 0) refusal ??= "no discovered tile to throw at";
            foreach (var tile in targets)
            {
                var attack = new Attack(unit.Id, grenade.Id, BattleAimKind.Throw, Target: tile.Position);
                var start = Stopwatch.GetTimestamp();
                var result = context.Commands.Execute(attack);
                var elapsed = Stopwatch.GetElapsedTime(start).TotalMicroseconds;
                model.Refresh();
                if (result.Events.OfType<CampaignActionBlocked>().FirstOrDefault() is { } blocked)
                {
                    Assert.True(blocked.Code.Length != 0, "A refused throw names its reason.");
                    refusal ??= blocked.Code;
                    continue;
                }
                // A throw the unit turned for and could not make (a ceiling in the way) stops with the reason; the next tile follows.
                if (result.Events.OfType<UnitAttackStopped>().FirstOrDefault() is { } stopped)
                {
                    refusal ??= stopped.Reason;
                    continue;
                }
                Assert.Contains(result.Events, static evt => evt is UnitFired);
                Assert.Null(grenade.OwnerUnitId);
                times.Add(elapsed);
                description ??= $"{unit.Id} throws to {tile.Position.X},{tile.Position.Y},{tile.Position.Z}";
                landed = true;
                // The fuse is 0 and the grenade goes off when the turn ends (branch 6's endTurn pushes an ExplosionBState for it): the acceptance pushes the same state at the
                // grenade's tile, where the throw left it, and times the explosion with its casualties and falls. (An approximation of endTurn's BA_TRIGGER_TIMED_GRENADE, which
                // uses the tile's terrain level for the height: a grenade primed in the corpus, such as TFTD's dye grenades, offers no prime line and is thrown unprimed, so no blast follows.)
                if (primed && grenade.Tile is { } where && battle.Items.Contains(grenade))
                {
                    battle.RemoveItem(grenade);
                    battle.Events.Drain();
                    var voxel = new BattlePosition(where.X * 16 + 8, where.Y * 16 + 8, where.Z * 24 + 2);
                    var blastStart = Stopwatch.GetTimestamp();
                    battle.PushExplosion(new BattleAttack(BattleActionType.Throw, unit, grenade, grenade), voxel);
                    battle.RunStates();
                    battle.ResolveFalls();
                    blastTimes.Add(Stopwatch.GetElapsedTime(blastStart).TotalMicroseconds);
                    var events = battle.Events.Drain();
                    blasts += events.OfType<BattleExploded>().Count();
                    deaths += events.OfType<UnitDied>().Count();
                    Assert.NotEmpty(events.OfType<BattleExploded>());
                    model.Refresh();
                }
                break;
            }
            model.CancelAim();
            if (landed) thrown++;
        }
        return new(description, thrown, 0, deaths, refusal, blasts);
    }

    /// <summary>Whether a player unit carries a grenade.</summary>
    internal static bool HasGrenade(BattleState battle) => battle.Items.Any(item => item.OwnerUnitId is { } owner && IsGrenade(battle, item) &&
        battle.Units.Single(unit => unit.Id == owner).Faction == BattleFaction.Player);

    /// <summary>Whether a player unit holds a firearm in a hand (a base defense's soldiers may hold nothing).</summary>
    internal static bool HasFirearm(BattleState battle) => battle.Items.Any(item => item.Slot is "STR_RIGHT_HAND" or "STR_LEFT_HAND" && item.OwnerUnitId is { } owner &&
        battle.Units.Single(unit => unit.Id == owner).Faction == BattleFaction.Player && battle.RequiredRules.Items.TryGet(item.ItemType, out var handle) &&
        battle.RequiredRules.Items[handle].Value.BattleType == RuntimeBattleItem.FirearmBattleType);

    /// <summary>Whether the item's rules make it a grenade (battle type 4).</summary>
    internal static bool IsGrenade(BattleState battle, BattleItem item) =>
        battle.RequiredRules.Items.TryGet(item.ItemType, out var handle) && battle.RequiredRules.Items[handle].Value.BattleType == RuntimeBattleItem.GrenadeBattleType;

    /// <summary>Puts the grenade into an empty hand (or in the place of the left hand's item, which goes to the ground); returns whether it is the right hand, or null with the refusal.</summary>
    private static bool? HandWithGrenade(BattleUiContext context, BattleState battle, BattleUnit unit, BattleItem grenade, out string? refusal)
    {
        refusal = null;
        if (grenade.Slot is "STR_RIGHT_HAND" or "STR_LEFT_HAND") return grenade.Slot == "STR_RIGHT_HAND";
        bool Held(string slot) => battle.Items.Any(item => item.OwnerUnitId == unit.Id && item.Slot == slot);
        foreach (var slot in new[] { "STR_RIGHT_HAND", "STR_LEFT_HAND" })
        {
            if (Held(slot)) continue;
            var moved = context.Commands.Execute(new MoveInventoryItem(unit.Id, grenade.Id, slot, 0, 0));
            if (moved.Events.OfType<CampaignActionBlocked>().FirstOrDefault() is { } blocked)
            {
                refusal = blocked.Code;
                continue;
            }
            return slot == "STR_RIGHT_HAND";
        }
        var other = battle.Items.FirstOrDefault(item => item.OwnerUnitId == unit.Id && item.Slot == "STR_LEFT_HAND");
        if (other is null) return null;
        var dropped = context.Commands.Execute(new MoveInventoryItem(unit.Id, other.Id, BattleState.GroundSlot, 0, 0));
        if (dropped.Events.OfType<CampaignActionBlocked>().FirstOrDefault() is { } droppedRefusal)
        {
            refusal = droppedRefusal.Code;
            return null;
        }
        var again = context.Commands.Execute(new MoveInventoryItem(unit.Id, grenade.Id, "STR_LEFT_HAND", 0, 0));
        if (again.Events.OfType<CampaignActionBlocked>().FirstOrDefault() is { } blockedAgain)
        {
            refusal = blockedAgain.Code;
            return null;
        }
        return false;
    }

    // ---- the strike ----

    /// <summary>
    /// A soldier walks to a tile beside a live hostile unit it can reach (the discovered tiles are all it may walk to), turns to it and hits. Null when no soldier can
    /// reach one: at the start of a battle the opposing units are usually far away.
    /// </summary>
    private static Part Strike(BattlescapeClient screen, BattleState battle)
    {
        var model = screen.Model;
        string? refusal = null;
        var enemies = battle.Units.Where(unit => unit.Faction == BattleFaction.Hostile && !unit.IsOut && unit.Position is not null).ToList();
        if (enemies.Count == 0) return new(null, 0, 0, 0, null);
        model.Refresh();
        foreach (var id in (model.Selection?.SelectableUnitIds ?? []).Take(4).ToList())
        {
            // A soldier next to an enemy may be lost to it (the battle's end is branch 7's): two soldiers always stay behind to carry the acceptance on.
            if (battle.Units.Count(static candidate => candidate.Faction == BattleFaction.Player && !candidate.IsOut) < 3) break;
            var unit = battle.Units.Single(candidate => candidate.Id == id);
            if (unit.Position is not { } from || !model.Select(id)) continue;
            model.Camera.SetViewLevel(from.Z);
            model.ShowReachable = true;
            model.Refresh();
            var reach = model.Reachable;
            model.ShowReachable = false;
            if (reach is null) continue;
            var reachable = reach.Tiles.ToHashSet();
            foreach (var enemy in enemies.OrderBy(enemy => Distance(enemy.Position!.Value, from)))
            {
                var there = enemy.Position!.Value;
                var beside = reachable.Where(tile => tile.Z == there.Z && Distance(tile, there) == 1 && tile != there).OrderBy(tile => Distance(tile, from)).FirstOrDefault();
                if (beside == default) continue;
                model.Camera.SetViewLevel(beside.Z);
                if (beside != from && !model.Walk(beside.X, beside.Y, ignoreSpottedEnemies: true))
                {
                    refusal ??= model.Message;
                    continue;
                }
                if (unit.Position != beside || enemy.IsOut || !model.Turn(there.X, there.Y)) continue;
                foreach (var right in new[] { true, false })
                {
                    model.Camera.SetViewLevel(beside.Z);
                    if (!model.OpenMenu(right) || model.Menu is not { Usable: true } menu || menu.Items.All(line => line.Kind != BattleAimKind.Hit)) continue;
                    if (!model.Choose(BattleAimKind.Hit))
                    {
                        refusal ??= model.Message;
                        continue;
                    }
                    var struck = model.LastEvents.OfType<UnitMeleeAttacked>().FirstOrDefault();
                    if (struck is not null) return new($"{id} strikes {struck.TargetUnitId} ({(struck.Hit ? "hit" : "miss")})", 1, struck.Hit ? 1 : 0, model.LastEvents.OfType<UnitDied>().Count(), refusal);
                    refusal ??= model.Message;
                }
            }
        }
        return new(null, 0, 0, 0, refusal);
    }

    // ---- the record ----

    /// <summary>Records what the combat did and the timings in <c>artifacts</c> (private-battle-acceptance-attacks.json and private-battle-acceptance-attack-timings.json).</summary>
    private static void Record(string label, Outcome outcome, CampaignState campaign, BattleState battle, List<double> shotTimes, List<double> throwTimes, List<double> blastTimes)
    {
        var trial = TrialCopyMicroseconds(campaign, battle);
        lock (Timings)
        {
            Outcomes[label] = outcome;
            Timings.RemoveAll(existing => existing.Label == label);
            Timings.Add(new(label, $"{battle.Width}x{battle.Length}x{battle.Height}", battle.Width * battle.Length * battle.Height, battle.Units.Count, shotTimes.Count, Median(shotTimes),
                throwTimes.Count, Median(throwTimes), blastTimes.Count, Median(blastTimes), trial));
            var path = TestFixtures.RepositoryPath("artifacts", "private-battle-acceptance-attacks.json");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, JsonSerializer.Serialize(Outcomes.OrderBy(static item => item.Key, StringComparer.Ordinal).ToDictionary(), Indented));
            File.WriteAllText(TestFixtures.RepositoryPath("artifacts", "private-battle-acceptance-attack-timings.json"),
                JsonSerializer.Serialize(Timings.OrderBy(static item => item.Label, StringComparer.Ordinal), Indented));
        }
    }

    /// <summary>Records how many tiles the reload of the battle discovered that the live battle had not (the fight's blasts and shots change what a local field of view sees).</summary>
    internal static void RecordReloadDiscovered(string label, int tiles)
    {
        lock (Timings)
        {
            if (!Outcomes.TryGetValue(label, out var outcome)) return;
            Outcomes[label] = outcome with { ReloadDiscovered = tiles };
            var path = TestFixtures.RepositoryPath("artifacts", "private-battle-acceptance-attacks.json");
            File.WriteAllText(path, JsonSerializer.Serialize(Outcomes.OrderBy(static item => item.Key, StringComparer.Ordinal).ToDictionary(), Indented));
        }
    }

    private static double Median(List<double> samples) => samples.Count == 0 ? 0 : Math.Round(samples.Order().ElementAt(samples.Count / 2), 1);

    /// <summary>
    /// The cost of the copy the briefing's spawn check makes (<c>CampaignBattles.TrialRefusal</c>: a restore of the battle), five runs after one. The walk, kneel and attack commands
    /// made one before S11 and no longer do.
    /// </summary>
    private static double TrialCopyMicroseconds(CampaignState campaign, BattleState battle)
    {
        var battles = campaign.GetQuery<IBattleQuery>() as CampaignBattles ?? throw new InvalidOperationException("The campaign has no battles.");
        var trial = typeof(CampaignBattles).GetMethod("TrialRefusal", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var run = new Func<BattleState, CampaignReason?>(static _ => null);
        trial.Invoke(battles, [battle, run]);
        var samples = new List<double>();
        for (var i = 0; i < 5; i++)
        {
            var start = Stopwatch.GetTimestamp();
            trial.Invoke(battles, [battle, run]);
            samples.Add(Stopwatch.GetElapsedTime(start).TotalMicroseconds);
        }
        return Median(samples);
    }

    private static int Distance(BattlePosition a, BattlePosition b) => Math.Max(Math.Abs(a.X - b.X), Math.Abs(a.Y - b.Y));
}

using Oxce.Gameplay.Battles;
using Oxce.Gameplay.Battles.Terrain;
using Xunit;
using static Oxce.CompatibilityTests.BattleSightTests;
using static Oxce.CompatibilityTests.BattleTestSupport;

namespace Oxce.CompatibilityTests;

/// <summary>
/// Phase 7 branch 5 S9, first part: the reactor list of reaction fire (<c>TileEngine::getSpottingUnits</c> with the reaction fire threshold,
/// <c>determineReactionType</c>, <c>getReactor</c>, <c>BattleUnit::getWeaponForReactions</c>). The arena is the turn and kneel tests' one: the soldier
/// at (10, 10) facing east, the alien five tiles east facing west, both seeing each other in daylight; the reactions themselves are the next part.
/// </summary>
public sealed class BattleReactionTests
{
    private const string GunText = "- type: GUN\n    battleType: 1\n    compatibleAmmo: [CLIP]\n    weight: 1";

    /// <summary>A flat arena with snap shots of 20, a melee hit of 30 for the RAM and a reactive alien (100 reactions over 50 time units).</summary>
    private static Arena Make(Func<string, string>? edit = null, int distance = 5, bool bigAlien = false, string soldierExtra = "", string? unitExtra = null,
        bool soldierAtWest = false, int shade = 0, string alienExtra = "")
    {
        string Edit(string rules)
        {
            rules = Replaced(rules, "stats: {tu: 50, firing: 61}", "stats: {tu: 50, firing: 61, reactions: 100}");
            rules = Replaced(rules, GunText, GunText + "\n    tuSnap: 20\n    tuAimed: 50\n    accuracySnap: 60");
            rules = Replaced(rules, "  - type: RAM\n    battleType: 3\n    clipSize: -1", "  - type: RAM\n    battleType: 3\n    clipSize: -1\n    tuMelee: 30\n    specialUseEmptyHand: true");
            return edit is null ? rules : edit(rules);
        }

        return Flat(shade: shade, distance: distance, edit: Edit, bigAlien: bigAlien, soldierExtra: soldierExtra, alienExtra: alienExtra, unitExtra: unitExtra,
            soldierAtWest: soldierAtWest);
    }

    /// <summary>A special weapon or a hand's melee weapon: a RAM with its one hit ready (or spent, when asked).</summary>
    private static BattleItem Ram(Arena arena, BattleUnit unit, string slot = "", bool special = true, bool spent = false)
    {
        var ram = Hold(arena, unit, "RAM", slot, special);
        ram.IsWeaponWithAmmo = true;
        ram.AmmoSlots[0] = ram.Id;
        ram.AmmoQuantity = spent ? 0 : 1;
        return ram;
    }

    /// <summary>A hit chance table for a unit size that is the accuracy itself at one distance (in tiles) and nothing at every other.</summary>
    private static string HitTable(params (int Size, int Distance)[] tables)
    {
        var rows = new System.Text.StringBuilder("\nhitChancesTable:\n");
        foreach (var (size, identity) in tables)
        {
            rows.Append(System.Globalization.CultureInfo.InvariantCulture, $"  - unitSize: {size}\n    distances:\n");
            for (var distance = 1; distance <= 40; distance++)
                rows.Append(System.Globalization.CultureInfo.InvariantCulture,
                    $"      {distance}: [{string.Join(", ", Enumerable.Range(0, 61).Select(step => distance == identity ? 2 * step : 0))}]\n");
        }
        return rows.ToString();
    }

    private static void SetDepth(BattleState battle, int depth) =>
        typeof(BattleState).GetField("<Depth>k__BackingField", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.SetValue(battle, depth);

    private static string Threshold(int hostile, int civilians = 0) => $"\nai:\n  reactionFireThreshold: {hostile}\n  reactionFireThresholdCiv: {civilians}\n";

    private static BattleItem Hold(Arena arena, BattleUnit unit, string type, string slot, bool special = false)
    {
        var item = new BattleItem(arena.Battle.TakeItemId(), type) { OwnerUnitId = unit.Id, Slot = special ? "" : slot, IsSpecialWeapon = special };
        arena.Battle.AddItem(item);
        if (special) unit.SpecialWeaponIds.Add(item.Id);
        return item;
    }

    /// <summary>Gives the unit a gun in a hand, loaded with a clip when asked.</summary>
    private static BattleItem Arm(Arena arena, BattleUnit unit, string slot = "STR_RIGHT_HAND", bool loaded = true)
    {
        var gun = Hold(arena, unit, "GUN", slot);
        gun.IsWeaponWithAmmo = true;
        if (loaded)
        {
            var clip = new BattleItem(arena.Battle.TakeItemId(), "CLIP") { AmmoQuantity = 5, IsAmmo = true };
            arena.Battle.AddItem(clip);
            gun.AmmoSlots[0] = clip.Id;
        }
        return gun;
    }

    /// <summary>Takes away what the unit carries (the soldier of the arena starts with a built-in gun and clip).</summary>
    private static void Strip(Arena arena, BattleUnit unit)
    {
        foreach (var item in arena.Battle.InventoryOf(unit).ToList()) arena.Battle.RemoveItem(item);
    }

    private static List<BattleReaction> Spotters(Arena arena, BattleUnit mover) => arena.Battle.SpottingUnits(arena.Battle.Sight(), mover);

    [Fact]
    public void AnArmedSpotterIsListedWithItsSnapShotAndWhatEachReactionTakesOffItsScore()
    {
        var arena = Make();
        var gun = Arm(arena, arena.Alien);
        var spotter = Assert.Single(Spotters(arena, arena.Soldier));
        Assert.Equal((arena.Alien, gun, BattleActionType.SnapShot, 1), (spotter.Unit, spotter.Weapon, spotter.AttackType, spotter.Count));
        Assert.Equal(arena.Alien.ReactionScore, spotter.Score);
        // The snap shot's 20 percent of the unit's time units (13 of 65), as a share of its 100 reactions over those 65 time units.
        Assert.Equal(20.0, spotter.Reduction);
        // The spotting itself still happens for the unarmed: it sees the mover and is not listed.
        var unarmed = Make();
        unarmed.Forget();
        Assert.Empty(Spotters(unarmed, unarmed.Soldier));
        Assert.Equal([unarmed.Soldier.Id], unarmed.Alien.VisibleUnits);
    }

    [Fact]
    public void ANoReactionIsAScoreOfNothingAndAUnitWithoutTimeUnitsOrAmmunitionHasNone()
    {
        var arena = Make();
        var gun = Arm(arena, arena.Alien);
        Assert.Equal(BattleActionType.SnapShot, arena.Battle.DetermineReactionType(arena.Alien, arena.Soldier).AttackType);
        // Too few time units for the snap shot (the score is still above 0).
        arena.Alien.TimeUnits = 10;
        var poor = arena.Battle.DetermineReactionType(arena.Alien, arena.Soldier);
        Assert.Equal((BattleActionType.None, null), (poor.AttackType, poor.Weapon));
        Assert.Equal(arena.Alien.ReactionScore, poor.Score);
        // No time units: a score that is not worth a reaction, so nothing is looked at.
        arena.Alien.TimeUnits = 0;
        Assert.Equal(BattleActionType.None, arena.Battle.DetermineReactionType(arena.Alien, arena.Soldier).AttackType);
        // An empty gun.
        arena.Alien.TimeUnits = 50;
        gun.AmmoSlots[0] = null;
        Assert.Equal(BattleActionType.None, arena.Battle.DetermineReactionType(arena.Alien, arena.Soldier).AttackType);
        // A target outside the weapon's range.
        var far = Make(rules => Replaced(rules, GunText + "\n    tuSnap: 20", GunText + "\n    maxRange: 3\n    tuSnap: 20"));
        Arm(far, far.Alien);
        Assert.Equal(BattleActionType.None, far.Battle.DetermineReactionType(far.Alien, far.Soldier).AttackType);
    }

    [Fact]
    public void AHitInReachComesBeforeASnapShotAndExtendedMeleeReactionsAllowAnyDirection()
    {
        var arena = Make(rules => Replaced(rules, "tuSnap: 20\n    tuAimed: 50", "tuSnap: 20\n    tuAimed: 50\n    tuMelee: 30"), distance: 1);
        var gun = Arm(arena, arena.Alien);
        var reaction = arena.Battle.DetermineReactionType(arena.Alien, arena.Soldier);
        // The hit's 30 percent of 65 time units is 19.
        Assert.Equal((gun, BattleActionType.Hit, 19 * 100.0 / 65), (reaction.Weapon, reaction.AttackType, reaction.Reduction));
        // Facing away, the alien can only shoot, unless melee reactions work from any side.
        arena.Alien.SetDirection(2);
        Assert.Equal(BattleActionType.SnapShot, arena.Battle.DetermineReactionType(arena.Alien, arena.Soldier).AttackType);
        var extended = Make(rules => Replaced(rules, "tuSnap: 20\n    tuAimed: 50", "tuSnap: 20\n    tuAimed: 50\n    tuMelee: 30") + "\nconstants:\n  - extendedMeleeReactions: 2\n", distance: 1);
        Arm(extended, extended.Alien);
        extended.Alien.SetDirection(2);
        Assert.Equal(BattleActionType.Hit, extended.Battle.DetermineReactionType(extended.Alien, extended.Soldier).AttackType);
        // Two tiles away it is no reach.
        var apart = Make(rules => Replaced(rules, "tuSnap: 20\n    tuAimed: 50", "tuSnap: 20\n    tuAimed: 50\n    tuMelee: 30"), distance: 2);
        Arm(apart, apart.Alien);
        Assert.Equal(BattleActionType.SnapShot, apart.Battle.DetermineReactionType(apart.Alien, apart.Soldier).AttackType);
    }

    [Fact]
    public void ThePlayersPreferredHandComesFirstAndAHandItDisabledIsSkipped()
    {
        var arena = Make();
        var soldier = arena.Soldier;
        Strip(arena, soldier);
        var right = Arm(arena, soldier);
        var left = Arm(arena, soldier, "STR_LEFT_HAND");
        BattleReaction Reaction() => arena.Battle.DetermineReactionType(soldier, arena.Alien);

        // The selected (right) hand's weapon by default.
        Assert.Equal(right, Reaction().Weapon);
        soldier.PreferredHandForReactions = "STR_LEFT_HAND";
        Assert.Equal(left, Reaction().Weapon);
        // A disabled hand is skipped, even the preferred one; the main hand weapon never comes from a disabled hand either.
        soldier.ReactionsDisabledForLeftHand = true;
        Assert.Equal(right, Reaction().Weapon);
        soldier.ReactionsDisabledForRightHand = true;
        Assert.Equal(BattleActionType.None, Reaction().AttackType);
        // The preference is the player's: a hostile unit does not read it.
        var hostile = Make();
        var alienRight = Arm(hostile, hostile.Alien);
        var alienLeft = Arm(hostile, hostile.Alien, "STR_LEFT_HAND");
        hostile.Alien.PreferredHandForReactions = "STR_LEFT_HAND";
        hostile.Alien.ReactionsDisabledForRightHand = true;
        var hostileReaction = hostile.Battle.DetermineReactionType(hostile.Alien, hostile.Soldier);
        Assert.Equal((alienRight, BattleActionType.SnapShot), (hostileReaction.Weapon, hostileReaction.AttackType));
        Assert.NotNull(alienLeft);
    }

    [Fact]
    public void TheWeaponForReactionsIsThePreferredHandsAndNeedsAmmunitionAndASnapShotUnlessItIsMelee()
    {
        var arena = Make();
        var soldier = arena.Soldier;
        Strip(arena, soldier);
        Assert.Null(arena.Battle.WeaponForReactions(soldier));
        var gun = Arm(arena, soldier, loaded: false);
        soldier.PreferredHandForReactions = "STR_RIGHT_HAND";
        Assert.Null(arena.Battle.WeaponForReactions(soldier));
        var clip = new BattleItem(arena.Battle.TakeItemId(), "CLIP") { AmmoQuantity = 5, IsAmmo = true };
        arena.Battle.AddItem(clip);
        gun.AmmoSlots[0] = clip.Id;
        Assert.Equal(gun, arena.Battle.WeaponForReactions(soldier));
        // The other hand is empty: a special weapon that uses an empty hand stands in for it.
        soldier.PreferredHandForReactions = "STR_LEFT_HAND";
        Assert.Null(arena.Battle.WeaponForReactions(soldier));
        var ram = Hold(arena, soldier, "RAM", "", special: true);
        ram.IsWeaponWithAmmo = true;
        ram.AmmoSlots[0] = ram.Id;
        ram.AmmoQuantity = 1;
        Assert.Equal(ram, arena.Battle.WeaponForReactions(soldier));
        // A melee weapon in the hand counts without a clip (a melee weapon has none to count).
        var melee = Make();
        Strip(melee, melee.Soldier);
        melee.Soldier.PreferredHandForReactions = "STR_LEFT_HAND";
        var club = Hold(melee, melee.Soldier, "RAM", "STR_LEFT_HAND");
        Assert.Equal(club, melee.Battle.WeaponForReactions(melee.Soldier));
        // Without ammunition or a snap shot a gun is no preferred weapon either.
        var free = Make(rules => Replaced(rules, "tuSnap: 20\n    tuAimed: 50", "tuSnap: 0\n    tuAimed: 50"));
        free.Soldier.PreferredHandForReactions = "STR_RIGHT_HAND";
        Strip(free, free.Soldier);
        Arm(free, free.Soldier);
        Assert.Null(free.Battle.WeaponForReactions(free.Soldier));
    }

    [Fact]
    public void AMeleeSpecialWeaponReactsWithAHitBeforeAGun()
    {
        var arena = Make(distance: 1);
        var gun = Arm(arena, arena.Alien);
        var ram = Hold(arena, arena.Alien, "RAM", "", special: true);
        ram.IsWeaponWithAmmo = true;
        ram.AmmoSlots[0] = ram.Id;
        ram.AmmoQuantity = 1;
        var reaction = arena.Battle.DetermineReactionType(arena.Alien, arena.Soldier);
        Assert.Equal((ram, BattleActionType.Hit, 19 * 100.0 / 65), (reaction.Weapon, reaction.AttackType, reaction.Reduction));
        // Out of its reach the melee weapon cannot and the gun shoots.
        var apart = Make(distance: 3);
        var apartGun = Arm(apart, apart.Alien);
        var apartRam = Hold(apart, apart.Alien, "RAM", "", special: true);
        apartRam.IsWeaponWithAmmo = true;
        apartRam.AmmoSlots[0] = apartRam.Id;
        apartRam.AmmoQuantity = 1;
        var shot = apart.Battle.DetermineReactionType(apart.Alien, apart.Soldier);
        Assert.Equal((apartGun, BattleActionType.SnapShot), (shot.Weapon, shot.AttackType));
        Assert.NotNull(gun);
    }

    [Fact]
    public void AThresholdKeepsOutASpotterWhoseHitChanceDoesNotReachIt()
    {
        // X-COM's threshold is the user option: the soldier reacts to the alien on the alien's turn.
        var arena = Make();
        arena.Battle.Side = BattleFaction.Hostile;
        Strip(arena, arena.Soldier);
        arena.Soldier.Stats = arena.Soldier.Stats with { Reactions = 200 };
        var gun = Arm(arena, arena.Soldier);
        var accuracy = arena.Battle.FiringAccuracy(arena.Soldier, gun, BattleActionType.SnapShot);
        Assert.True(accuracy > 0);
        Assert.Single(Spotters(arena, arena.Alien));
        arena.Battle.Options = arena.Battle.Options with { ReactionFireThreshold = accuracy };
        Assert.Single(Spotters(arena, arena.Alien));
        arena.Battle.Options = arena.Battle.Options with { ReactionFireThreshold = accuracy + 1 };
        Assert.Empty(Spotters(arena, arena.Alien));
        // The spotting still happened.
        Assert.Contains(arena.Alien.Id, arena.Soldier.VisibleUnits);

        // The hostile threshold is the mod's.
        var probe = Make();
        var probeAccuracy = probe.Battle.FiringAccuracy(probe.Alien, Arm(probe, probe.Alien), BattleActionType.SnapShot);
        var reached = Make(rules => rules + $"\nai:\n  reactionFireThreshold: {probeAccuracy}\n");
        Arm(reached, reached.Alien);
        Assert.Single(Spotters(reached, reached.Soldier));
        var missed = Make(rules => rules + $"\nai:\n  reactionFireThreshold: {probeAccuracy + 1}\n");
        Arm(missed, missed.Alien);
        Assert.Empty(Spotters(missed, missed.Soldier));
    }

    [Fact]
    public void AThresholdUsesTheHitChanceTableAndTheWeaponsDropoffAtTheMoversDistance()
    {
        // A table that gives half the accuracy at every distance: a threshold above that keeps the spotter out.
        var rows = new System.Text.StringBuilder("\nhitChancesTable:\n  - unitSize: 1\n    distances:\n");
        for (var distance = 1; distance <= 40; distance++)
            rows.Append(System.Globalization.CultureInfo.InvariantCulture, $"      {distance}: [{string.Join(", ", Enumerable.Range(0, 61).Select(static step => step))}]\n");
        var probe = Make();
        var accuracy = probe.Battle.FiringAccuracy(probe.Alien, Arm(probe, probe.Alien), BattleActionType.SnapShot);
        Assert.True(accuracy >= 2);
        var tabled = Make(rules => rules + $"\nai:\n  reactionFireThreshold: {accuracy}\n" + rows);
        Arm(tabled, tabled.Alien);
        Assert.Empty(Spotters(tabled, tabled.Soldier));
        // The same threshold without the table is the accuracy itself, which reaches it.
        var untabled = Make(rules => rules + $"\nai:\n  reactionFireThreshold: {accuracy}\n");
        Arm(untabled, untabled.Alien);
        Assert.Single(Spotters(untabled, untabled.Soldier));

        // A weapon whose minimum range is above the distance loses accuracy by the dropoff for each tile short of it: five tiles, three short, 9 less.
        string Near(string rules) => Replaced(rules, GunText + "\n    tuSnap: 20", GunText + "\n    minRange: 8\n    dropoff: 3\n    tuSnap: 20");
        var nearProbe = Make(Near);
        var nearGun = Arm(nearProbe, nearProbe.Alien);
        var plain = nearProbe.Battle.FiringAccuracy(nearProbe.Alien, nearGun, BattleActionType.SnapShot);
        Assert.Equal(3, nearProbe.Battle.CalculateLimits(nearGun, BattleActionType.SnapShot, out _, out var lowerLimit));
        Assert.Equal(8, lowerLimit);
        Assert.True(plain - 9 > 0);
        var kept = Make(rules => Near(rules) + $"\nai:\n  reactionFireThreshold: {plain - 9}\n");
        Arm(kept, kept.Alien);
        Assert.Single(Spotters(kept, kept.Soldier));
        var dropped = Make(rules => Near(rules) + $"\nai:\n  reactionFireThreshold: {plain - 8}\n");
        Arm(dropped, dropped.Alien);
        Assert.Empty(Spotters(dropped, dropped.Soldier));
    }

    [Fact]
    public void TheReactorIsTheSpotterWithTheHighestScoreThatReachesTheMoversAndAnXComReactorIsTrained()
    {
        var arena = Make();
        var (soldier, alien) = (arena.Soldier, arena.Alien);
        Assert.Null(arena.Battle.GetReactor([], soldier));
        var slow = new BattleReaction(alien, 20);
        var quick = new BattleReaction(alien, 60);
        // The best of the list, if it reaches the mover's score: the score of the soldier, 0 reactions, is below.
        soldier.Stats = soldier.Stats with { Reactions = 40 };
        soldier.TimeUnits = soldier.Stats.TimeUnits;
        Assert.Equal(40, soldier.ReactionScore);
        Assert.Same(quick, arena.Battle.GetReactor([slow, quick], soldier));
        Assert.Null(arena.Battle.GetReactor([slow], soldier));
        // A score equal to the mover's reacts too.
        var equal = new BattleReaction(alien, 40);
        Assert.Same(equal, arena.Battle.GetReactor([equal], soldier));
        soldier.Stats = soldier.Stats with { Reactions = 20 };
        Assert.Same(slow, arena.Battle.GetReactor([slow], soldier));
        // An unconscious or respawning unit never reacts, even with the best score.
        alien.Status = BattleUnitStatus.Unconscious;
        Assert.Null(arena.Battle.GetReactor([quick], soldier));
        alien.Status = BattleUnitStatus.Standing;
        alien.Respawn = true;
        Assert.Null(arena.Battle.GetReactor([quick], soldier));
        alien.Respawn = false;

        // X-COM's unit that is asked for gets a point of reactions experience each time, not an alien.
        var before = arena.Soldier.Experience.Reactions;
        soldier.Stats = soldier.Stats with { Reactions = 10 };
        var xcom = new BattleReaction(soldier, 60);
        arena.Alien.Stats = arena.Alien.Stats with { Reactions = 10 };
        arena.Alien.TimeUnits = alien.Stats.TimeUnits;
        var revision = arena.Battle.Revision;
        Assert.Same(xcom, arena.Battle.GetReactor([xcom], alien));
        // The training is a change of the battle.
        Assert.True(arena.Battle.Revision > revision);
        Assert.Same(xcom, arena.Battle.GetReactor([xcom], alien));
        Assert.Equal(before + 2, soldier.Experience.Reactions);
        Assert.Null(arena.Battle.GetReactor([new BattleReaction(soldier, 1)], alien));
        Assert.Equal(before + 2, soldier.Experience.Reactions);
        var hostile = new BattleReaction(alien, 60);
        soldier.TimeUnits = soldier.Stats.TimeUnits;
        Assert.Same(hostile, arena.Battle.GetReactor([hostile], soldier));
        Assert.Equal(0, alien.Experience.Reactions);
        // A hostile reactor changes nothing.
        var steady = arena.Battle.Revision;
        Assert.Same(hostile, arena.Battle.GetReactor([hostile], soldier));
        Assert.Equal(steady, arena.Battle.Revision);
    }

    [Fact]
    public void TheCiviliansThresholdIsTheModsCivilianKeyAndNeitherTheHostileOneNorTheOption()
    {
        var probe = Make();
        Strip(probe, probe.Soldier);
        probe.Soldier.Stats = probe.Soldier.Stats with { Reactions = 200 };
        var accuracy = probe.Battle.FiringAccuracy(probe.Soldier, Arm(probe, probe.Soldier), BattleActionType.SnapShot);
        Assert.True(accuracy > 0);

        // A civilian (neutral) spotter of the alien's move, with the user option and the hostile threshold out of its reach.
        List<BattleReaction> Neutral(string ai)
        {
            var arena = Make(rules => rules + ai);
            arena.Battle.Side = BattleFaction.Hostile;
            arena.Battle.Options = arena.Battle.Options with { ReactionFireThreshold = accuracy + 50 };
            Strip(arena, arena.Soldier);
            arena.Soldier.Stats = arena.Soldier.Stats with { Reactions = 200 };
            Arm(arena, arena.Soldier);
            arena.Soldier.Faction = BattleFaction.Neutral;
            return Spotters(arena, arena.Alien);
        }

        Assert.Single(Neutral(Threshold(accuracy + 50, accuracy)));
        Assert.Empty(Neutral(Threshold(0, accuracy + 1)));
        // No civilian threshold is none, whatever the hostile one is.
        Assert.Single(Neutral(Threshold(accuracy + 50)));
    }

    [Fact]
    public void AThresholdRoundsTheMoversDistanceUpAndTheUpperLimitAndTheMoversSizeDecideTheHitChance()
    {
        var probe = Make();
        var accuracy = probe.Battle.FiringAccuracy(probe.Alien, Arm(probe, probe.Alien), BattleActionType.SnapShot);
        Assert.True(accuracy > 0);

        // The alien is diagonal to the soldier: three tiles over and three down, a squared distance of 18 and a distance of 4.24, so 5 tiles.
        List<BattleReaction> Diagonal(int row, int threshold)
        {
            var arena = Make(rules => rules + Threshold(threshold) + HitTable((1, row)));
            arena.Stand(arena.Alien, 13, 13, 0);
            arena.Alien.SetDirection(BattleGeometry.DirectionTo(arena.Alien.RequiredPosition, arena.Soldier.RequiredPosition));
            arena.See();
            Arm(arena, arena.Alien);
            return Spotters(arena, arena.Soldier);
        }

        Assert.Single(Diagonal(5, accuracy));
        Assert.Empty(Diagonal(4, accuracy));
        Assert.Empty(Diagonal(6, accuracy));

        // Past the aimed range the accuracy drops by the weapon's dropoff for each tile: five tiles, two past 3, 6 less.
        string Short(string rules) => Replaced(rules, GunText + "\n    tuSnap: 20", GunText + "\n    aimRange: 3\n    dropoff: 3\n    tuSnap: 20");
        var shortProbe = Make(Short);
        var shortGun = Arm(shortProbe, shortProbe.Alien);
        var plain = shortProbe.Battle.FiringAccuracy(shortProbe.Alien, shortGun, BattleActionType.SnapShot);
        Assert.Equal(3, shortProbe.Battle.CalculateLimits(shortGun, BattleActionType.SnapShot, out var upperLimit, out _));
        Assert.Equal(3, upperLimit);
        Assert.True(plain - 6 > 0);
        var kept = Make(rules => Short(rules) + Threshold(plain - 6));
        Arm(kept, kept.Alien);
        Assert.Single(Spotters(kept, kept.Soldier));
        var dropped = Make(rules => Short(rules) + Threshold(plain - 5));
        Arm(dropped, dropped.Alien);
        Assert.Empty(Spotters(dropped, dropped.Soldier));

        // The table is the mover's size (the soldier's, 1): the reactor is a big alien whose own size's table gives nothing.
        var bigProbe = Make(bigAlien: true);
        var bigAccuracy = bigProbe.Battle.FiringAccuracy(bigProbe.Alien, Arm(bigProbe, bigProbe.Alien), BattleActionType.SnapShot);
        Assert.True(bigAccuracy > 0);
        var big = Make(rules => rules + Threshold(bigAccuracy) + HitTable((1, 5), (2, 0)), bigAlien: true);
        Arm(big, big.Alien);
        Assert.Single(Spotters(big, big.Soldier));
    }

    [Fact]
    public void AHitReactionUnderAThresholdNeedsItsAccuracyAndTheReach()
    {
        string Melee(string rules) => Replaced(Replaced(rules, "tuSnap: 20\n    tuAimed: 50", "tuSnap: 20\n    tuAimed: 50\n    tuMelee: 30\n    accuracyMelee: 70"),
            "stats: {tu: 50, firing: 61, reactions: 100}", "stats: {tu: 50, firing: 61, reactions: 100, melee: 60}");
        string Reach(string rules, int maxRange) => Melee(Replaced(rules, GunText + "\n    tuSnap: 20", GunText + $"\n    maxRange: {maxRange}\n    tuSnap: 20"));

        var probe = Make(Melee, distance: 1);
        var hit = probe.Battle.FiringAccuracy(probe.Alien, Arm(probe, probe.Alien), BattleActionType.Hit);
        Assert.True(hit > 0);
        List<BattleReaction> Hit(int maxRange, int threshold)
        {
            var arena = Make(rules => Reach(rules, maxRange) + Threshold(threshold), distance: 1);
            Arm(arena, arena.Alien);
            return Spotters(arena, arena.Soldier);
        }

        // The hit's accuracy is what a threshold asks for, when the weapon reaches (one tile, a diagonal step allowed).
        var listed = Assert.Single(Hit(1, hit));
        Assert.Equal(BattleActionType.Hit, listed.AttackType);
        Assert.Empty(Hit(1, hit + 1));
        // A weapon that does not reach the tile is out of range for a threshold, though without one the hit is listed.
        Assert.Equal(BattleActionType.Hit, Assert.Single(Hit(0, 0)).AttackType);
        Assert.Empty(Hit(0, 1));
    }

    [Fact]
    public void AWeaponTheUnitMayNotUseIsNoReactionWeapon()
    {
        string Melee(string rules, string extra) => Replaced(rules, GunText + "\n    tuSnap: 20", GunText + "\n    tuMelee: 30" + extra + "\n    tuSnap: 20");

        // A hostile unit waits for the weapon's AI use delay: neither the hit nor the snap shot is allowed before it.
        var delayed = Make(rules => Melee(rules, "\n    ai: {useDelay: 99}"), distance: 1);
        Arm(delayed, delayed.Alien);
        Assert.Equal(BattleActionType.None, delayed.Battle.DetermineReactionType(delayed.Alien, delayed.Soldier).AttackType);
        var ready = Make(rules => Melee(rules, "\n    ai: {useDelay: 0}"), distance: 1);
        Arm(ready, ready.Alien);
        Assert.Equal(BattleActionType.Hit, ready.Battle.DetermineReactionType(ready.Alien, ready.Soldier).AttackType);

        // An X-COM unit without psi skill cannot use a psi weapon.
        var psi = Make(rules => Melee(rules, "\n    psiRequired: true"), distance: 1);
        Strip(psi, psi.Soldier);
        Arm(psi, psi.Soldier);
        Assert.Equal(BattleActionType.None, psi.Battle.DetermineReactionType(psi.Soldier, psi.Alien).AttackType);
        psi.Soldier.Stats = psi.Soldier.Stats with { PsiSkill = 50 };
        Assert.Equal(BattleActionType.Hit, psi.Battle.DetermineReactionType(psi.Soldier, psi.Alien).AttackType);

        // Under water land equipment is refused by the ammunition of the action: the snap shot's clip, not the weapon the hit uses.
        string LandClip(string rules) => Replaced(Melee(rules, ""), "- type: CLIP\n    battleType: 2\n    clipSize: 12", "- type: CLIP\n    battleType: 2\n    clipSize: 12\n    landOnly: true");
        var far = Make(LandClip, distance: 3);
        Arm(far, far.Alien);
        Assert.Equal(BattleActionType.SnapShot, far.Battle.DetermineReactionType(far.Alien, far.Soldier).AttackType);
        SetDepth(far.Battle, 1);
        Assert.Equal(BattleActionType.None, far.Battle.DetermineReactionType(far.Alien, far.Soldier).AttackType);
        var near = Make(LandClip, distance: 1);
        var gun = Arm(near, near.Alien);
        SetDepth(near.Battle, 1);
        var reaction = near.Battle.DetermineReactionType(near.Alien, near.Soldier);
        Assert.Equal((gun, BattleActionType.Hit), (reaction.Weapon, reaction.AttackType));
    }

    [Fact]
    public void ADisabledEmptyHandDisablesTheSpecialWeaponThatUsesIt()
    {
        var arena = Make(distance: 1);
        var soldier = arena.Soldier;
        Strip(arena, soldier);
        var ram = Ram(arena, soldier);
        var reaction = arena.Battle.DetermineReactionType(soldier, arena.Alien);
        Assert.Equal((ram, BattleActionType.Hit), (reaction.Weapon, reaction.AttackType));
        // Both hands are empty and one is disabled: the special weapon that uses an empty hand goes with it.
        soldier.ReactionsDisabledForLeftHand = true;
        Assert.Equal(BattleActionType.None, arena.Battle.DetermineReactionType(soldier, arena.Alien).AttackType);
        soldier.ReactionsDisabledForLeftHand = false;
        soldier.ReactionsDisabledForRightHand = true;
        Assert.Equal(BattleActionType.None, arena.Battle.DetermineReactionType(soldier, arena.Alien).AttackType);

        // With a gun in the right hand and the empty left hand disabled, the special weapon is out and the gun shoots.
        soldier.ReactionsDisabledForRightHand = false;
        var gun = Arm(arena, soldier);
        soldier.ReactionsDisabledForLeftHand = true;
        reaction = arena.Battle.DetermineReactionType(soldier, arena.Alien);
        Assert.Equal((gun, BattleActionType.SnapShot), (reaction.Weapon, reaction.AttackType));
    }

    [Fact]
    public void ThePreferredHandsEmptyFallbackTakesAMeleeThenAPsiThenAFirearmSpecialWeaponAndNeverReactsWithThePsiAmp()
    {
        string Special(string rules)
        {
            rules = Replaced(rules, GunText + "\n    tuSnap: 20", GunText + "\n    specialUseEmptyHand: true\n    tuSnap: 20");
            return Replaced(rules, "  - type: AMP\n    battleType: 9", "  - type: AMP\n    battleType: 9\n    specialUseEmptyHand: true\n    tuSnap: 20");
        }

        var arena = Make(Special);
        var soldier = arena.Soldier;
        Strip(arena, soldier);
        soldier.PreferredHandForReactions = "STR_RIGHT_HAND";
        var gun = Hold(arena, soldier, "GUN", "", special: true);
        gun.IsWeaponWithAmmo = true;
        var clip = new BattleItem(arena.Battle.TakeItemId(), "CLIP") { AmmoQuantity = 5, IsAmmo = true };
        arena.Battle.AddItem(clip);
        gun.AmmoSlots[0] = clip.Id;
        // A firearm that uses an empty hand stands in for it.
        Assert.Equal(gun, arena.Battle.WeaponForReactions(soldier));
        // A psi amp is found before it, and the amp has no hit nor snap shot to react with: nothing.
        Hold(arena, soldier, "AMP", "", special: true);
        Assert.Null(arena.Battle.WeaponForReactions(soldier));
        // A melee weapon is found before both.
        var ram = Ram(arena, soldier);
        Assert.Equal(ram, arena.Battle.WeaponForReactions(soldier));
    }

    [Fact]
    public void AMeleeWeaponInAHandIsTheMeleeCandidateRightHandFirstAndNeedsItsHitAmmunition()
    {
        var arena = Make(distance: 1);
        var soldier = arena.Soldier;
        Strip(arena, soldier);
        // No preferred hand: the melee weapon is the first candidate, the right hand's before the left's.
        soldier.PreferredHandForReactions = "";
        var left = Ram(arena, soldier, "STR_LEFT_HAND", special: false);
        var right = Ram(arena, soldier, "STR_RIGHT_HAND", special: false);
        var reaction = arena.Battle.DetermineReactionType(soldier, arena.Alien);
        Assert.Equal((right, BattleActionType.Hit), (reaction.Weapon, reaction.AttackType));
        arena.Battle.RemoveItem(right);
        reaction = arena.Battle.DetermineReactionType(soldier, arena.Alien);
        Assert.Equal((left, BattleActionType.Hit), (reaction.Weapon, reaction.AttackType));
        // A melee weapon in the left hand with a gun in the right one: the melee weapon is not the main hand weapon, and still the one that hits.
        var gun = Arm(arena, soldier);
        reaction = arena.Battle.DetermineReactionType(soldier, arena.Alien);
        Assert.Equal((left, BattleActionType.Hit), (reaction.Weapon, reaction.AttackType));
        Assert.NotNull(gun);

        // A spent hit has no ammunition: no hit, and a melee weapon has no snap shot.
        var spent = Make(distance: 1);
        Strip(spent, spent.Soldier);
        spent.Soldier.PreferredHandForReactions = "";
        Ram(spent, spent.Soldier, "STR_RIGHT_HAND", special: false, spent: true);
        Assert.Equal(BattleActionType.None, spent.Battle.DetermineReactionType(spent.Soldier, spent.Alien).AttackType);
    }

    // ---- the AI's score of a reaction (AIModule::brutalScoreFiringMode with reactionCheck, explosiveEfficacy, brutalExplosiveEfficacy) ----

    private const string ClipText = "- type: CLIP\n    battleType: 2\n    clipSize: 12\n    weight: 1";

    /// <summary>An armor piercing bullet of power 40 with no random damage.</summary>
    private const string Bullet = "power: 40\ndamageType: 1\ndamageAlter: " + Flat3;

    /// <summary>A damage type that deals the same every time: no random type and no random conversion.</summary>
    private const string Flat3 = "{RandomType: 3, RandomHealth: false, RandomWound: false, RandomStun: false, RandomArmorPre: false, RandomArmor: false, " +
        "RandomMorale: false, RandomEnergy: false, RandomTime: false, RandomMana: false}";

    private static Func<string, string> Clip(string extra) =>
        rules => Replaced(rules, ClipText, ClipText + "\n    " + extra.Replace("\n", "\n    ", StringComparison.Ordinal));

    private static Func<string, string> Gun(string extra) =>
        rules => Replaced(rules, "accuracySnap: 60", "accuracySnap: 60\n    " + extra.Replace("\n", "\n    ", StringComparison.Ordinal));

    private static Func<string, string> Both(params Func<string, string>[] edits) => rules => edits.Aggregate(rules, static (current, edit) => edit(current));

    private static float Score(Arena arena, BattleItem weapon, BattleActionType type = BattleActionType.SnapShot, BattleUnit? unit = null, BattleUnit? target = null)
    {
        unit ??= arena.Alien;
        target ??= arena.Soldier;
        return arena.Battle.ReactionScore(new BattleAimAction(unit, type, weapon, target.RequiredPosition), target);
    }

    private static BattleUnit Add(Arena arena, int id, int x, int y, BattleFaction faction)
    {
        var unit = new BattleUnit(id, faction, faction == BattleFaction.Player ? BattleController.Human(0) : BattleController.Ai)
        {
            Stats = arena.Alien.Stats,
            ArmorId = arena.Alien.ArmorId,
            ArmorSize = 1,
            UnitType = arena.Alien.UnitType,
            Health = 100,
            MovementType = 0,
        };
        arena.Battle.AddUnit(unit);
        arena.Battle.SetTile(unit, new BattlePosition(x, y, 0));
        unit.SetDirection(6);
        unit.Morale = 100;
        return unit;
    }

    [Fact]
    public void TheScoreIsTheDamageTimesTheChanceToHitTimesTheShotsTheTimeUnitsAllow()
    {
        var arena = Make(Clip(Bullet));
        var gun = Arm(arena, arena.Alien);
        arena.Alien.TimeUnits = 26;
        var two = Score(arena, gun);
        Assert.True(two > 0);
        // A snap shot costs 13 of the 65 time units: twice the time units, twice the shots.
        arena.Alien.TimeUnits = 52;
        Assert.Equal(2 * two, Score(arena, gun));
        // Fewer time units than one shot is no score.
        arena.Alien.TimeUnits = 12;
        Assert.Equal(0, Score(arena, gun));
        // No ammunition, and no action type, are none either.
        var empty = Make(Clip(Bullet));
        Assert.Equal(0, Score(empty, Arm(empty, empty.Alien, loaded: false)));
        Assert.Equal(0, Score(arena, gun, BattleActionType.None));
        // A weapon with no auto shot has no score for one (no time units for it).
        Assert.Equal(0, Score(arena, gun, BattleActionType.AutoShot));
    }

    [Fact]
    public void TheArmorOfTheSideThatFacesTheReactorTakesOffTheDamageAndAReactionIsWorthAtLeastOnePoint()
    {
        const string Armor = "    frontArmor: 30\n    sideArmor: 20\n    rearArmor: 10\n    underArmor: 5\n";
        var arena = Make(Clip(Bullet), soldierExtra: Armor);
        var gun = Arm(arena, arena.Alien);
        // The soldier faces east and the alien stands east: its front. The damage is (40 times the range of 2, less the armor) over 2, and the score is
        // proportional to it. The sides in turn from the front: the front, left front (the average of the two), left, left rear, rear, and the right ones.
        var soldier = arena.Soldier;
        var scores = new float[8];
        for (var relative = 0; relative < 8; relative++)
        {
            // The alien is at direction 2 of the soldier; the soldier faces (2 - relative).
            soldier.SetDirection((2 - relative + 8) % 8);
            scores[relative] = Score(arena, gun);
        }
        var front = scores[0];
        // The armor drops from the front (30) through the sides (20) to the rear (10), the corners between them the average of the two, and the
        // score with it: the same on both sides, and the corners in between.
        Assert.True(scores[0] < scores[1] && scores[1] < scores[2] && scores[2] < scores[3] && scores[3] < scores[4]);
        Assert.Equal(scores[1], scores[7]);
        Assert.Equal(scores[2], scores[6]);
        Assert.Equal(scores[3], scores[5]);
        // An armor with a thicker left side (30 against the right's 20) takes more off the shots that come from the left: the relative directions 1, 2 and 3
        // are the left front, left and left rear, 5, 6 and 7 the right ones, and each corner is the average of its two sides.
        var lopsided = Make(Clip(Bullet), soldierExtra: "    frontArmor: 30\n    sideArmor: 20\n    leftArmorDiff: 10\n    rearArmor: 10\n    underArmor: 5\n");
        var lopsidedGun = Arm(lopsided, lopsided.Alien);
        var sides = new float[8];
        for (var relative = 0; relative < 8; relative++)
        {
            lopsided.Soldier.SetDirection((2 - relative + 8) % 8);
            sides[relative] = Score(lopsided, lopsidedGun);
        }
        Assert.True(sides[2] < sides[6], "left (30) against right (20)");
        Assert.True(sides[1] < sides[7], "left front (30) against right front (25)");
        Assert.True(sides[3] < sides[5], "left rear (20) against right rear (15)");
        Assert.Equal(scores[0], sides[0]);
        Assert.Equal(scores[4], sides[4]);
        Assert.Equal(scores[6], sides[6]);
        // A rear unit's armor equal to its front's gives the same score as a front hit.
        var even = Make(Clip(Bullet), soldierExtra: "    frontArmor: 10\n    rearArmor: 10\n    sideArmor: 10\n");
        var evenGun = Arm(even, even.Alien);
        var evenScore = Score(even, evenGun);
        even.Soldier.SetDirection(6);
        Assert.Equal(evenScore, Score(even, evenGun));
        Assert.Equal(scores[4], evenScore);

        // Armor past the damage still leaves a point of damage to score.
        var heavy = Make(Clip(Bullet), soldierExtra: "    frontArmor: 1000\n");
        var heavyScore = Score(heavy, Arm(heavy, heavy.Alien));
        Assert.True(heavyScore > 0);
        Assert.True(heavyScore < front / 20);
    }

    [Fact]
    public void ATargetBeyondTheWeaponsRangeOrBehindAWallIsNoScoreAndAnArcingShotNeedsAnArc()
    {
        var far = Make(Both(Clip(Bullet), Gun("maxRange: 3")));
        Assert.Equal(0, Score(far, Arm(far, far.Alien)));
        var near = Make(Both(Clip(Bullet), Gun("maxRange: 5")));
        Assert.True(Score(near, Arm(near, near.Alien)) > 0);

        var walled = Make(Clip(Bullet));
        var gun = Arm(walled, walled.Alien);
        var open = Score(walled, gun);
        walled.Put(12, 10, 0, BattleTilePart.ObjectPart, Wall);
        Assert.Equal(0, Score(walled, gun));
        walled.Clear(12, 10, 0, BattleTilePart.ObjectPart);
        Assert.Equal(open, Score(walled, gun));

        // An arcing weapon throws its bullet over the open field the same way (and is scored by the arc, not the line).
        var arcing = Make(Both(Clip(Bullet), Gun("arcingShot: true")));
        Assert.Equal(open, Score(arcing, Arm(arcing, arcing.Alien)));
    }

    [Fact]
    public void ATargetBeyondTheViewDistanceHasTheNoLineOfSightPenaltyOnItsAccuracy()
    {
        float Run(string penalty, int distance)
        {
            var arena = Make(Both(Clip(Bullet), Gun(penalty)), distance: distance, soldierAtWest: true);
            return Score(arena, Arm(arena, arena.Alien));
        }

        // The view distance is 20 tiles: a target 26 tiles away is out of sight, and one 5 tiles away is not.
        var unpenalised = Run("noLOSAccuracyPenalty: 100", 26);
        var penalised = Run("noLOSAccuracyPenalty: 50", 26);
        Assert.True(unpenalised > 0);
        Assert.True(penalised < unpenalised);
        Assert.Equal(Run("noLOSAccuracyPenalty: 100", 5), Run("noLOSAccuracyPenalty: 50", 5));
        // A weapon with no penalty (-1, the mod's default) is the same as one of 100.
        Assert.Equal(unpenalised, Run("weight: 1", 26));
    }

    [Fact]
    public void AHitIsScoredWithTheMeleeAccuracyAndNeedsTheTargetInReach()
    {
        string Melee(string rules) => Replaced(Replaced(rules, "stats: {tu: 50, firing: 61, reactions: 100}", "stats: {tu: 50, firing: 61, reactions: 100, melee: 80}"),
            "tuMelee: 30", "tuMelee: 30\n    accuracyMelee: 100\n    power: 30\n    damageType: 1\n    damageAlter: " + Flat3);
        var near = Make(Melee, distance: 1);
        var ram = Ram(near, near.Alien);
        var hit = Score(near, ram, BattleActionType.Hit);
        Assert.True(hit > 0);
        // No dodge and no distance modifier: the accuracy is the melee stat; a farther target is out of reach.
        var far = Make(Melee, distance: 2);
        Assert.Equal(0, Score(far, Ram(far, far.Alien), BattleActionType.Hit));
        // The target's dodge takes accuracy off a hit and, past it, leaves a hundredth.
        var dodging = Make(Melee, distance: 1, soldierExtra: "    meleeDodge:\n      flatOne: 30\n");
        var dodged = Score(dodging, Ram(dodging, dodging.Alien), BattleActionType.Hit);
        Assert.True(dodged > 0);
        Assert.True(dodged < hit);
        var untouchable = Make(Melee, distance: 1, soldierExtra: "    meleeDodge:\n      flatOne: 300\n");
        var barely = Score(untouchable, Ram(untouchable, untouchable.Alien), BattleActionType.Hit);
        Assert.True(barely > 0);
        Assert.True(barely < dodged / 10);
    }

    [Fact]
    public void ACloseQuartersThreatIsNotShotAtPointBlankAndAMeleeWeaponHasNoRangeToAvoid()
    {
        const string Threat = "    createsMeleeThreat: true\n";
        string Cqc(string rules) => rules + "\nenableCloseQuartersCombat: 1\n";
        var close = Make(Both(Clip(Bullet), Cqc), distance: 1, soldierExtra: Threat);
        Assert.Equal(0, Score(close, Arm(close, close.Alien)));
        var apart = Make(Both(Clip(Bullet), Cqc), distance: 2, soldierExtra: Threat);
        Assert.True(Score(apart, Arm(apart, apart.Alien)) > 0);
        var off = Make(Clip(Bullet), distance: 1, soldierExtra: Threat);
        Assert.True(Score(off, Arm(off, off.Alien)) > 0);
        // An armor that does not say is a melee threat (createsMeleeThreat is true unless it is set or the unit is big); one that says no is not.
        var plain = Make(Both(Clip(Bullet), Cqc), distance: 1);
        Assert.Equal(0, Score(plain, Arm(plain, plain.Alien)));
        var harmless = Make(Both(Clip(Bullet), Cqc), distance: 1, soldierExtra: "    createsMeleeThreat: false\n");
        Assert.True(Score(harmless, Arm(harmless, harmless.Alien)) > 0);
        // The shooter's own armor can ignore the threat.
        var ignoring = Make(Both(Clip(Bullet), Cqc), distance: 1, alienExtra: "    ignoresMeleeThreat: true\n");
        Assert.True(Score(ignoring, Arm(ignoring, ignoring.Alien)) > 0);
        // A weapon that reaches only one tile never avoids it.
        var short1 = Make(Both(Clip(Bullet), Cqc, Gun("maxRange: 1")), distance: 1, soldierExtra: Threat);
        Assert.True(Score(short1, Arm(short1, short1.Alien)) > 0);
    }

    [Fact]
    public void AnExplosiveShotIsWorthTheEnemiesItWouldHitLessTheAlliesAndAHostileShotThatWouldOnlyHitItsOwnIsNone()
    {
        var Blast = "power: 40\ndamageType: 2\ndamageAlter: {FixRadius: 2, " + Flat3[1..];
        var alone = Make(Clip(Blast));
        var gun = Arm(alone, alone.Alien);
        var single = Score(alone, gun);
        Assert.True(single > 0);

        // Another X-COM unit one tile from the target is hit too: its share of the blast, (2 - 1 / 2) / 2.
        var enemy = Make(Clip(Blast));
        var enemyGun = Arm(enemy, enemy.Alien);
        Add(enemy, 40, 10, 11, BattleFaction.Player);
        Assert.Equal(single * 1.75f, Score(enemy, enemyGun), 3);

        // An ally next to the target costs a whole point: the target's own is cancelled, so the shot is worth nothing.
        var friend = Make(Clip(Blast));
        var friendGun = Arm(friend, friend.Alien);
        Add(friend, 41, 11, 10, BattleFaction.Hostile);
        Assert.Equal(0, Score(friend, friendGun));
        // An ally outside the radius, and one the blast cannot reach (a wall between), do not count.
        var outside = Make(Clip(Blast));
        Add(outside, 42, 10, 13, BattleFaction.Hostile);
        Assert.Equal(single, Score(outside, Arm(outside, outside.Alien)));
        var shielded = Make(Clip(Blast));
        Add(shielded, 43, 10, 12, BattleFaction.Hostile);
        shielded.Put(10, 11, 0, BattleTilePart.ObjectPart, Wall);
        Assert.Equal(single, Score(shielded, Arm(shielded, shielded.Alien)));
    }

    [Fact]
    public void BrutalExplosiveEfficacyCountsEnemiesByTheirShareOfTheBlastAndTheUnitItselfWhenItStandsInIt()
    {
        var arena = Make();
        var (battle, alien, soldier) = (arena.Battle, arena.Alien, arena.Soldier);
        var target = soldier.RequiredPosition;
        Assert.Equal(1.0f, battle.BrutalExplosiveEfficacy(target, alien, 3, false));
        // The alien stands 5 tiles from the target: a blast of 6 reaches it, with (6 - 5 / 2) / 6 of the blast taken off.
        Assert.Equal(1.0f - (6 - 5 / 2.0f) / 6, battle.BrutalExplosiveEfficacy(target, alien, 6, false), 5);
        // A unit that changed sides adds its own share instead (and, on X-COM's side now, takes the soldier for an ally).
        alien.Faction = BattleFaction.Player;
        Assert.Equal(-1.0f + (6 - 5 / 2.0f) / 6, battle.BrutalExplosiveEfficacy(target, alien, 6, false), 5);
        alien.Faction = BattleFaction.Hostile;
        // An enemy and an ally in the blast: +(3 - 1 / 2) / 3 and -1.
        Add(arena, 50, 10, 11, BattleFaction.Player);
        Assert.Equal(1.0f + (3 - 1 / 2.0f) / 3, battle.BrutalExplosiveEfficacy(target, alien, 3, false), 5);
        Add(arena, 51, 9, 10, BattleFaction.Hostile);
        Assert.Equal(1.0f + (3 - 1 / 2.0f) / 3 - 1, battle.BrutalExplosiveEfficacy(target, alien, 3, false), 5);
        // No grenade at a flying target, and the unit on the target tile (here the ally) counts as itself: the soldier beside it takes half a blast.
        Assert.Equal(0, battle.BrutalExplosiveEfficacy(new BattlePosition(10, 10, 1), alien, 3, true));
        Assert.Equal(-1.0f + 0.5f, battle.BrutalExplosiveEfficacy(new BattlePosition(9, 10, 0), alien, 1, false), 5);
    }

    [Fact]
    public void ExplosiveEfficacyWeighsTheTargetsAgainstTheUnitsDesperationAndTheDifficulty()
    {
        var arena = Make();
        var (battle, alien, soldier) = (arena.Battle, arena.Alien, arena.Soldier);
        var target = soldier.RequiredPosition;
        // The alien knows the soldier (it spotted it this turn): a weight of 100 with no desperation and the easiest difficulty.
        Assert.Equal(1, battle.ExplosiveEfficacy(target, alien, 3, 0));
        // The difficulty adds half its coefficient in whole targets, the morale lost a tenth of a target each point (and 3 when badly hurt).
        Assert.Equal(3, battle.ExplosiveEfficacy(target, alien, 3, 4));
        alien.Morale = 30;
        Assert.Equal(8, battle.ExplosiveEfficacy(target, alien, 3, 0));
        alien.Morale = 100;
        alien.Stats = alien.Stats with { Health = 90 };
        alien.Health = 20;
        Assert.Equal(4, battle.ExplosiveEfficacy(target, alien, 3, 0));
        alien.Health = 30;
        Assert.Equal(1, battle.ExplosiveEfficacy(target, alien, 3, 0));
        alien.Health = 90;
        // The campaign's difficulty coefficient is the default.
        var coefficient = arena.Rules.Campaign.Interception.DifficultyCoefficient(battle.CampaignLink!.Difficulty);
        Assert.Equal(battle.ExplosiveEfficacy(target, alien, 3, coefficient), battle.ExplosiveEfficacy(target, alien, 3));
        Assert.Equal(battle.ExplosiveEfficacy(target, alien, 3, 0) + coefficient / 2, battle.ExplosiveEfficacy(target, alien, 3));
        // Don't go kamikaze: a blast that reaches the unit takes four whole targets off.
        Assert.Equal(0, battle.ExplosiveEfficacy(target, alien, 5, 0));
        alien.Morale = 30;
        Assert.Equal(4, battle.ExplosiveEfficacy(target, alien, 5, 0));
        alien.Morale = 100;
        // A grenade at a single target is none unless the unit is desperate.
        Assert.Equal(0, battle.ExplosiveEfficacy(target, alien, 3, 0, grenade: true));
        alien.Morale = 30;
        Assert.Equal(8, battle.ExplosiveEfficacy(target, alien, 3, 0, grenade: true));
        alien.Morale = 100;
        // A flying target is no target for a grenade.
        Assert.Equal(0, battle.ExplosiveEfficacy(new BattlePosition(10, 10, 1), alien, 3, 0, grenade: true));

        // An X-COM unit next to the target is a second enemy and adds its weight; an ally subtracts its (negative) one; one out of the blast does nothing.
        Add(arena, 60, 10, 11, BattleFaction.Player);
        battle.UnitById(60)!.TurnsSinceSpotted[(int)BattleFaction.Hostile] = 0;
        Assert.Equal(2, battle.ExplosiveEfficacy(target, alien, 3, 0));
        Add(arena, 61, 11, 10, BattleFaction.Hostile);
        Assert.Equal(0, battle.ExplosiveEfficacy(target, alien, 3, 0));
        Add(arena, 62, 20, 20, BattleFaction.Player);
        Assert.Equal(0, battle.ExplosiveEfficacy(target, alien, 3, 0));
        // Ten enemies are a good trade whatever the allies cost.
        var ring = new (int X, int Y)[] { (13, 10), (7, 10), (10, 13), (10, 7), (12, 12), (8, 12), (12, 8), (8, 8), (11, 13) };
        for (var i = 0; i < ring.Length; i++) Add(arena, 70 + i, ring[i].X, ring[i].Y, BattleFaction.Player).TurnsSinceSpotted[(int)BattleFaction.Hostile] = 0;
        // (nine more, and the one beside the target, 10 in all; the ally and the unit itself, in the blast of 5, are not counted against them)
        Assert.Equal(11, battle.ExplosiveEfficacy(target, alien, 5, 0));
    }

    [Fact]
    public void ATargetWeightComesFromTheArmorThenTheModAndAUnitTheAiDoesNotKnowHasNone()
    {
        var armored = Make(soldierExtra: "    ai:\n      targetWeightAsHostile: 300\n");
        Assert.Equal(3, armored.Battle.ExplosiveEfficacy(armored.Soldier.RequiredPosition, armored.Alien, 3, 0));
        var modded = Make(rules => rules + "\nai:\n  targetWeightAsHostile: 250\n");
        Assert.Equal(2, modded.Battle.ExplosiveEfficacy(modded.Soldier.RequiredPosition, modded.Alien, 3, 0));

        // A unit not seen for more turns than the alien's intelligence is not known: no weight, no efficacy.
        var forgetful = Make(unitExtra: "    intelligence: 1\n");
        forgetful.Soldier.TurnsSinceSpotted[(int)BattleFaction.Hostile] = 1;
        Assert.Equal(1, forgetful.Battle.ExplosiveEfficacy(forgetful.Soldier.RequiredPosition, forgetful.Alien, 3, 0));
        forgetful.Soldier.TurnsSinceSpotted[(int)BattleFaction.Hostile] = 2;
        Assert.Equal(0, forgetful.Battle.ExplosiveEfficacy(forgetful.Soldier.RequiredPosition, forgetful.Alien, 3, 0));
        // ... unless it is a sniper and its spotters still know the target.
        var sniper = Make(unitExtra: "    intelligence: 1\n    sniper: 50\n");
        sniper.Soldier.TurnsSinceSpotted[(int)BattleFaction.Hostile] = 2;
        Assert.Equal(0, sniper.Battle.ExplosiveEfficacy(sniper.Soldier.RequiredPosition, sniper.Alien, 3, 0));
        sniper.Soldier.TurnsLeftSpottedForSnipers[(int)BattleFaction.Hostile] = 1;
        Assert.Equal(1, sniper.Battle.ExplosiveEfficacy(sniper.Soldier.RequiredPosition, sniper.Alien, 3, 0));

        // The civilians' weight is the one for a target that is not the side the unit aims at (the alien aims at X-COM).
        var civilians = Make(rules => rules + "\nai:\n  targetWeightAsHostileCivilians: 400\n");
        var civilian = Add(civilians, 80, 10, 11, BattleFaction.Neutral);
        civilian.TurnsSinceSpotted[(int)BattleFaction.Hostile] = 0;
        Assert.Equal(5, civilians.Battle.ExplosiveEfficacy(civilians.Soldier.RequiredPosition, civilians.Alien, 3, 0));
        // The script of the armor of the unit that weighs gets the last word.
        var scripted = Make();
        scripted.Battle.Hooks = new WeightHook(750);
        Assert.Equal(7, scripted.Battle.ExplosiveEfficacy(scripted.Soldier.RequiredPosition, scripted.Alien, 3, 0));
    }

    /// <summary>A damage type of the conversions given over a base of health only (and no random part): each other share then adds its own to the score.</summary>
    private static string Alter(params (string Name, string Value)[] set)
    {
        var values = new Dictionary<string, string>
        {
            ["ToHealth"] = "1",
            ["ToWound"] = "0",
            ["ToStun"] = "0",
            ["ToArmor"] = "0",
            ["ToArmorPre"] = "0",
            ["ToMorale"] = "0",
            ["ToEnergy"] = "0",
            ["ToMana"] = "0",
            ["ToTime"] = "0",
        };
        foreach (var (name, value) in set) values[name] = value;
        return "{RandomType: 3, RandomHealth: false, RandomWound: false, RandomStun: false, RandomArmorPre: false, RandomArmor: false, RandomMorale: false, " +
            "RandomEnergy: false, RandomTime: false, RandomMana: false, " + string.Join(", ", values.Select(static pair => $"{pair.Key}: {pair.Value}")) + "}";
    }

    private static string Shot(string alter, int power = 40) => $"power: {power}\ndamageType: 1\ndamageAlter: {alter}";

    private static void Near(float expected, float actual) => Assert.True(Math.Abs(expected - actual) <= 1e-3f * Math.Max(1, Math.Abs(expected)), $"{expected} against {actual}");

    private static string Cqc(string rules) => rules + "\nenableCloseQuartersCombat: 1\n";

    [Fact]
    public void TheViewDistanceOfTheDayTheDarkSmokeAndCamouflageDecideTheNoLineOfSightPenalty()
    {
        // The alien views the soldier, or (where the aliens' view in the dark is the whole 20 tiles) the soldier views the alien.
        bool Penalised(int distance, int shade = 0, string soldierExtra = "", string alienExtra = "", Action<Arena, BattleUnit>? setup = null, bool soldierViews = false)
        {
            float Run(string penalty)
            {
                var arena = Make(Both(Clip(Bullet), Gun(penalty)), distance: distance, soldierAtWest: true, shade: shade, soldierExtra: soldierExtra, alienExtra: alienExtra);
                var (viewer, target) = soldierViews ? (arena.Soldier, arena.Alien) : (arena.Alien, arena.Soldier);
                setup?.Invoke(arena, target);
                return Score(arena, Arm(arena, viewer), unit: viewer, target: target);
            }

            return Run("noLOSAccuracyPenalty: 50") < Run("noLOSAccuracyPenalty: 100");
        }

        // By day the view is 20 tiles.
        Assert.False(Penalised(15));
        Assert.True(Penalised(22));
        // On a dark tile (shade 15, above the 9 a unit can still be seen at) it is the dark view, 9 tiles ...
        Assert.True(Penalised(12, shade: 15, soldierViews: true));
        Assert.False(Penalised(8, shade: 15, soldierViews: true));
        // ... unless the tile is burning, which lights it.
        Assert.False(Penalised(12, shade: 15, soldierViews: true, setup: (arena, target) => arena.Map.SetFire(arena.Battle.TileIndex(target.RequiredPosition), 1)));
        // Smoke cuts the view: 3 of it halves the 20 tiles.
        Assert.True(Penalised(12, setup: (arena, target) => arena.Map.SetSmoke(arena.Battle.TileIndex(target.RequiredPosition), 3)));
        Assert.False(Penalised(8, setup: (arena, target) => arena.Map.SetSmoke(arena.Battle.TileIndex(target.RequiredPosition), 3)));
        // The target armor's camouflage fixes the view at 6, and the viewer's anti-camouflage adds 3 to it.
        Assert.True(Penalised(8, soldierExtra: "    camouflageAtDay: 6\n"));
        Assert.False(Penalised(8, soldierExtra: "    camouflageAtDay: 6\n", alienExtra: "    antiCamouflageAtDay: 3\n"));
        Assert.True(Penalised(10, soldierExtra: "    camouflageAtDay: 6\n", alienExtra: "    antiCamouflageAtDay: 3\n"));
    }

    [Fact]
    public void TheMeleeDodgeBackPenaltyTakesDodgeOffAHitOnAUnitFacingAway()
    {
        string Melee(string rules) => Replaced(Replaced(rules, "stats: {tu: 50, firing: 61, reactions: 100}", "stats: {tu: 50, firing: 61, reactions: 100, melee: 80}"),
            "tuMelee: 30", "tuMelee: 30\n    accuracyMelee: 100\n    power: 30\n    damageType: 1\n    damageAlter: " + Flat3);
        float Hit(string penalty, int direction)
        {
            var arena = Make(Melee, distance: 1, soldierExtra: "    meleeDodge:\n      flatOne: 30\n" + penalty);
            arena.Soldier.SetDirection(direction);
            return Score(arena, Ram(arena, arena.Alien), BattleActionType.Hit);
        }

        // The soldier faces the alien (east) or away from it: a unit's back counts for the penalty, its front does not.
        var front = Hit("", 2);
        Assert.Equal(front, Hit("    meleeDodgeBackPenalty: 0.5\n", 2));
        var back = Hit("", 6);
        Assert.Equal(front, back);
        Assert.True(Hit("    meleeDodgeBackPenalty: 0.5\n", 6) > back);
    }

    [Fact]
    public void EachPartOfTheDamageTypeAddsItsShareToTheScoreAndTheArmorsModifierScalesTheDamage()
    {
        float Run(string soldierExtra = "", params (string, string)[] set)
        {
            var arena = Make(Clip(Shot(Alter(set))), soldierExtra: soldierExtra);
            return Score(arena, Arm(arena, arena.Alien));
        }

        var health = Run();
        Assert.True(health > 0);
        // A share that is as large as the health's adds as much again: wounds, stun (a half), armor (a third), morale (a fifth), energy, mana and time (a tenth).
        Near(2 * health, Run("", ("ToWound", "1")));
        Near(2 * health, Run("", ("ToStun", "2")));
        Near(2 * health, Run("", ("ToArmor", "3")));
        Near(2 * health, Run("", ("ToMorale", "5")));
        Near(2 * health, Run("", ("ToEnergy", "10")));
        Near(2 * health, Run("", ("ToMana", "10")));
        Near(2 * health, Run("", ("ToTime", "10")));
        // The part that goes to the armor before the damage counts as damage too.
        Assert.True(Run("", ("ToArmorPre", "1")) > health);
        // The target's modifier for the damage type (1) scales the power (the sides of the armor then take their part).
        Near(health / 2, Run("    damageModifier: [1, 0.5]\n"));
        // A weapon's own power replaces the ammunition's, and the shotgun's pellets multiply the shots.
        var weak = Make(Clip(Shot(Alter(), 20)));
        var weakScore = Score(weak, Arm(weak, weak.Alien));
        var strong = Make(Both(Clip(Shot(Alter(), 40)), Gun("power: 20")));
        Near(weakScore, Score(strong, Arm(strong, strong.Alien)));
        Assert.True(weakScore < health);
        var pellets = Make(Clip(Shot(Alter()) + "\nshotgunPellets: 3"));
        Near(3 * health, Score(pellets, Arm(pellets, pellets.Alien)));
    }

    [Fact]
    public void TheEnergyAnActionCostsLimitsTheAttacksAndAnArcingShotNeedsAnArcOverWhatIsInTheWay()
    {
        var arena = Make(Both(Clip(Shot(Alter())), Gun("costSnap: {time: 20, energy: 100}")));
        var gun = Arm(arena, arena.Alien);
        var cost = BattleActionCost.For(arena.Rules, BattleActionType.SnapShot, arena.Alien, gun);
        Assert.True(cost.Energy > 0);
        arena.Alien.TimeUnits = cost.Time;
        arena.Alien.Energy = 1000;
        var one = Score(arena, gun);
        Assert.True(one > 0);
        // Four shots of time units, but energy for two.
        arena.Alien.TimeUnits = 4 * cost.Time;
        arena.Alien.Energy = 2 * cost.Energy;
        Near(2 * one, Score(arena, gun));
        arena.Alien.Energy = cost.Energy - 1;
        Assert.Equal(0, Score(arena, gun));

        // An arcing shot goes over a wall of one level, but not over one that rises to the top of the map.
        var arcing = Make(Both(Clip(Bullet), Gun("arcingShot: true")));
        var arcingGun = Arm(arcing, arcing.Alien);
        var open = Score(arcing, arcingGun);
        Assert.True(open > 0);
        for (var y = 6; y <= 14; y++) arcing.Put(12, y, 0, BattleTilePart.ObjectPart, Wall);
        Assert.Equal(open, Score(arcing, arcingGun));
        for (var y = 6; y <= 14; y++)
            for (var z = 1; z < arcing.Map.Height; z++) arcing.Put(12, y, z, BattleTilePart.ObjectPart, Wall);
        Assert.Equal(0, Score(arcing, arcingGun));
    }

    [Fact]
    public void TheAisArcingShotAimsAtAUnitOnlyWhenTheUnitIsVisibleToXcomWhileAnAttackOfAHostileUnitAimsAtAnyUnit()
    {
        var arena = Make(Clip(Bullet));
        var gun = Arm(arena, arena.Alien);
        // Projectile::calculateThrow lets a hostile attacker aim at any unit on the tile; AIModule::validateArcingShot (visibleUnitsOnly) only at a unit that
        // is visible (getVisible), else at the object, wall or floor of the tile.
        var hidden = Add(arena, 60, 12, 10, BattleFaction.Hostile);
        Assert.False(hidden.IsVisibleToPlayer(arena.Rules));
        Assert.True(arena.Soldier.IsVisibleToPlayer(arena.Rules));

        BattlePosition Aim(BattleUnit target, bool visibleUnitsOnly)
        {
            var action = new BattleAimAction(arena.Alien, BattleActionType.AimedShot, gun, target.RequiredPosition);
            new BattleProjectile(arena.Battle, arena.Rules, action, arena.Alien.RequiredPosition, default)
                .FindArc(null, out _, out var targetVoxel, out _, out _, visibleUnitsOnly);
            return targetVoxel;
        }

        // A visible unit is aimed at in both forms, at its middle (or, when no arc reaches it, at its top: the same either way).
        var visibleUnitVoxel = Aim(arena.Soldier, visibleUnitsOnly: true);
        Assert.Equal(visibleUnitVoxel, Aim(arena.Soldier, visibleUnitsOnly: false));
        // The unseen one is aimed at in the projectile's form, at the same height of the unit; in the AI's it is passed over for the floor under it (lower).
        var anyUnitVoxel = Aim(hidden, visibleUnitsOnly: false);
        var floorVoxel = Aim(hidden, visibleUnitsOnly: true);
        Assert.Equal(visibleUnitVoxel.Z, anyUnitVoxel.Z);
        Assert.True(floorVoxel.Z < anyUnitVoxel.Z);
        Assert.Equal(1 - arena.Map.TerrainLevel(arena.Tile(12, 10)), floorVoxel.Z);
    }

    [Fact]
    public void ShouldAvoidMeleeRangeNeedsCloseQuartersAThreatAndAUnitThatCanBeOutreached()
    {
        bool Avoid(string soldierExtra = "", string alienExtra = "", bool closeQuarters = true, Action<Arena>? equip = null)
        {
            var arena = Make(closeQuarters ? Cqc : null, distance: 1, soldierExtra: soldierExtra, alienExtra: alienExtra);
            Strip(arena, arena.Alien);
            equip?.Invoke(arena);
            return arena.Battle.ShouldAvoidMeleeRange(arena.Alien, arena.Soldier);
        }

        // A unit with no weapon has nothing to reach with, and a firearm reaches past one tile.
        Assert.True(Avoid());
        Assert.True(Avoid(equip: arena => Arm(arena, arena.Alien)));
        // A melee weapon reaches one tile, as does a firearm of range 1: nothing to avoid.
        Assert.False(Avoid(equip: arena => Ram(arena, arena.Alien, "STR_RIGHT_HAND", special: false)));
        // The enemy's armor decides whether it is a threat (unset: yes), and the unit's own can ignore threats; with close quarters off nothing is.
        Assert.False(Avoid(soldierExtra: "    createsMeleeThreat: false\n"));
        Assert.False(Avoid(alienExtra: "    ignoresMeleeThreat: true\n"));
        Assert.False(Avoid(closeQuarters: false));
        // A big unit (an armor of another size) creates no threat unless it says so.
        var big = Make(Cqc, distance: 2, bigAlien: true);
        Strip(big, big.Soldier);
        Assert.False(big.Battle.ShouldAvoidMeleeRange(big.Soldier, big.Alien));
    }

    [Fact]
    public void TheAiTakesAMindControlledUnitForTheSideItBeganOnAndKnowsTheUnitsItIgnores()
    {
        var arena = Make();
        var (battle, alien, soldier) = (arena.Battle, arena.Alien, arena.Soldier);
        var target = soldier.RequiredPosition;
        Assert.Equal(1.0f, battle.BrutalExplosiveEfficacy(target, alien, 1, false));
        // An X-COM unit under alien control is still an enemy; an alien under X-COM control is neither an enemy nor an ally for the aliens' count.
        soldier.Faction = BattleFaction.Hostile;
        Assert.Equal(1.0f, battle.BrutalExplosiveEfficacy(target, alien, 1, false));
        soldier.Faction = BattleFaction.Player;
        var turned = Add(arena, 90, 20, 20, BattleFaction.Hostile);
        Assert.Equal(-1.0f, battle.BrutalExplosiveEfficacy(turned.RequiredPosition, alien, 1, false));
        turned.Faction = BattleFaction.Player;
        Assert.Equal(0.0f, battle.BrutalExplosiveEfficacy(turned.RequiredPosition, alien, 1, false));

        // A unit of a type the AI ignores is not counted.
        var ignoring = Make(unitExtra: "    ignoredByAI: true\n");
        var ignored = Add(ignoring, 91, 20, 20, BattleFaction.Player);
        Assert.Equal(0.0f, ignoring.Battle.BrutalExplosiveEfficacy(ignored.RequiredPosition, ignoring.Alien, 1, false));
        var counted = Make();
        var seen = Add(counted, 91, 20, 20, BattleFaction.Player);
        Assert.Equal(1.0f, counted.Battle.BrutalExplosiveEfficacy(seen.RequiredPosition, counted.Alien, 1, false));
    }

    [Fact]
    public void TheSideAUnitsAiAimsAtAndTheArmorsOwnWeightsChangeTheTargetWeight()
    {
        // The module's target faction decides which of the hostile weights a target has.
        var arena = Make(rules => rules + "\nai:\n  targetWeightAsHostileCivilians: 400\n");
        Assert.Equal(1, arena.Battle.ExplosiveEfficacy(arena.Soldier.RequiredPosition, arena.Alien, 3, 0));
        arena.Alien.Ai ??= new BattleAiState(arena.Alien.OriginalFaction);
        arena.Alien.Ai.TargetFaction = (int)BattleFaction.Neutral;
        Assert.Equal(4, arena.Battle.ExplosiveEfficacy(arena.Soldier.RequiredPosition, arena.Alien, 3, 0));

        // The armor's friend weight replaces the mod's -200: two X-COM units and the target (300) against an ally.
        int Friends(string alienExtra)
        {
            var group = Make(alienExtra: alienExtra);
            Add(group, 60, 10, 11, BattleFaction.Player).TurnsSinceSpotted[(int)BattleFaction.Hostile] = 0;
            Add(group, 61, 11, 10, BattleFaction.Player).TurnsSinceSpotted[(int)BattleFaction.Hostile] = 0;
            Add(group, 62, 9, 10, BattleFaction.Hostile);
            return group.Battle.ExplosiveEfficacy(group.Soldier.RequiredPosition, group.Alien, 3, 0);
        }

        Assert.Equal(1, Friends(""));
        Assert.Equal(2, Friends("    ai:\n      targetWeightAsFriendly: -100\n"));
        // And the armor's weight of a civilian replaces the mod's 50.
        var civilians = Make(alienExtra: "    ai:\n      targetWeightAsHostileCivilians: 400\n");
        Add(civilians, 80, 10, 11, BattleFaction.Neutral).TurnsSinceSpotted[(int)BattleFaction.Hostile] = 0;
        Assert.Equal(5, civilians.Battle.ExplosiveEfficacy(civilians.Soldier.RequiredPosition, civilians.Alien, 3, 0));
    }

    [Fact]
    public void ADangerZoneMarksTheTilesABlastReachesAndTheAiSkipsTheUnitsInIt()
    {
        var arena = Make(rules => Replaced(rules, "- type: GRENADE\n    battleType: 4\n    weight: 1",
            "- type: GRENADE\n    battleType: 4\n    weight: 1\n    power: 50\n    damageType: 2\n    damageAlter: {FixRadius: 2}"));
        var alien = arena.Alien;
        // A primed grenade dropped where the alien stands marks its tile and those the blast of 2 reaches (the loops stop short of the radius on the positive side),
        // less those behind a wall.
        arena.Put(14, 10, 0, BattleTilePart.ObjectPart, Wall);
        var grenade = new BattleItem(arena.Battle.TakeItemId(), "GRENADE") { FuseEnabled = true };
        arena.Battle.AddItem(grenade);
        arena.Battle.ItemDrop(alien.RequiredPosition, grenade, updateLight: false);
        var marked = Enumerable.Range(0, arena.Map.TileCount).Where(arena.Map.Dangerous).Select(tile => arena.Map.PositionOf(tile)).ToList();
        Assert.Contains(new BattlePosition(15, 10, 0), marked);
        Assert.Contains(new BattlePosition(16, 10, 0), marked);
        Assert.Contains(new BattlePosition(15, 8, 0), marked);
        Assert.Contains(new BattlePosition(15, 11, 0), marked);
        Assert.DoesNotContain(new BattlePosition(14, 10, 0), marked);
        Assert.DoesNotContain(new BattlePosition(13, 10, 0), marked);
        Assert.DoesNotContain(new BattlePosition(17, 10, 0), marked);
        Assert.DoesNotContain(new BattlePosition(15, 12, 0), marked);
        Assert.DoesNotContain(new BattlePosition(16, 8, 0), marked);

        // No unit on the tile, or a grenade with no fuse, makes none.
        var empty = Make(rules => Replaced(rules, "- type: GRENADE\n    battleType: 4\n    weight: 1",
            "- type: GRENADE\n    battleType: 4\n    weight: 1\n    power: 50\n    damageType: 2\n    damageAlter: {FixRadius: 2}"));
        var lit = new BattleItem(empty.Battle.TakeItemId(), "GRENADE") { FuseEnabled = true };
        empty.Battle.AddItem(lit);
        empty.Battle.ItemDrop(new BattlePosition(20, 20, 0), lit, updateLight: false);
        var cold = new BattleItem(empty.Battle.TakeItemId(), "GRENADE");
        empty.Battle.AddItem(cold);
        empty.Battle.ItemDrop(empty.Alien.RequiredPosition, cold, updateLight: false);
        Assert.DoesNotContain(true, Enumerable.Range(0, empty.Map.TileCount).Select(empty.Map.Dangerous));
    }

    [Fact]
    public void ATargetInADangerZoneIsNoGrenadeTargetAndHalvesTheScoreOfAReaction()
    {
        var arena = Make(Clip(Bullet));
        var (battle, alien, soldier) = (arena.Battle, arena.Alien, arena.Soldier);
        var gun = Arm(arena, alien);
        var normal = Score(arena, gun);
        var target = soldier.RequiredPosition;
        var other = Add(arena, 60, 10, 11, BattleFaction.Player);
        other.TurnsSinceSpotted[(int)BattleFaction.Hostile] = 0;
        Assert.Equal(2, battle.ExplosiveEfficacy(target, alien, 3, 0));
        var brutal = battle.BrutalExplosiveEfficacy(target, alien, 3, true);
        Assert.True(brutal > 1);
        // The unit beside the target is not counted when its tile is dangerous, nor the target itself, nor is a grenade thrown at it worth anything.
        arena.Map.SetDangerous(arena.Tile(10, 11), true);
        Assert.Equal(1, battle.ExplosiveEfficacy(target, alien, 3, 0));
        Assert.Equal(1.0f, battle.BrutalExplosiveEfficacy(target, alien, 3, true));
        arena.Map.SetDangerous(arena.Tile(10, 10), true);
        Assert.Equal(0, battle.ExplosiveEfficacy(target, alien, 3, 0));
        Assert.Equal(0.0f, battle.BrutalExplosiveEfficacy(target, alien, 3, true));
        Assert.True(battle.BrutalExplosiveEfficacy(target, alien, 3, false) > 0);
        // A shot at a target already in the blast is worth half.
        Near(normal / 2, Score(arena, gun));
    }

    private sealed class WeightHook(int weight) : IBattleHooks
    {
        public int AiCalculateTargetWeight(BattleUnit unit, BattleUnit target, int original) => weight;
    }
}

using Oxce.Gameplay.Battles;
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
    private static Arena Make(Func<string, string>? edit = null, int distance = 5, bool bigAlien = false)
    {
        string Edit(string rules)
        {
            rules = Replaced(rules, "stats: {tu: 50, firing: 61}", "stats: {tu: 50, firing: 61, reactions: 100}");
            rules = Replaced(rules, GunText, GunText + "\n    tuSnap: 20\n    tuAimed: 50\n    accuracySnap: 60");
            rules = Replaced(rules, "  - type: RAM\n    battleType: 3\n    clipSize: -1", "  - type: RAM\n    battleType: 3\n    clipSize: -1\n    tuMelee: 30\n    specialUseEmptyHand: true");
            return edit is null ? rules : edit(rules);
        }

        return Flat(distance: distance, edit: Edit, bigAlien: bigAlien);
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
}

using Oxce.Core.Random;
using Oxce.Gameplay.Battles;
using Oxce.Gameplay.Battles.Terrain;
using Oxce.Gameplay.Campaigns;
using Oxce.Mods.Rulesets.Runtime;
using Oxce.Savegames.Oxce;
using Oxce.TestSupport;
using Xunit;
using static Oxce.CompatibilityTests.BattleDamageTests;
using static Oxce.CompatibilityTests.BattleSightTests;
using static Oxce.CompatibilityTests.BattleTestSupport;

namespace Oxce.CompatibilityTests;

/// <summary>
/// Phase 7 branch 5 S4: casualties (<c>BattlescapeGame::checkForCasualties</c>, <c>UnitDieBState</c>, <c>SavedGame::killSoldier</c>, <c>BattleUnit::knockOut</c>,
/// <c>SavedBattleGame::convertUnit</c>, <c>reviveUnconsciousUnits</c> and <c>BattlescapeGame::convertInfected</c>) on the flat arena of the sight tests: the soldier at
/// (10, 10) and the alien five tiles east, each with 100 health.
/// </summary>
public sealed class BattleCasualtyTests
{
    private static BattlePosition At(int x, int y, int z = 0) => new(x, y, z);

    private static IReadOnlyList<BattleCasualty> Check(Arena arena, BattleAttack? attack = null, RuntimeItemDamageRule? type = null, bool hidden = false,
        bool terrain = false) => arena.Battle.CheckForCasualties(arena.Rules, type, attack ?? default, hidden, terrain);

    /// <summary>A unit of the alien rule on the arena, of any faction, standing at the tile.</summary>
    private static BattleUnit Extra(Arena arena, int id, BattleFaction faction, int x, int y, string armor = "", int size = 1)
    {
        var unit = new BattleUnit(id, faction, faction == BattleFaction.Player ? BattleController.Human(0) : BattleController.Ai)
        {
            Stats = arena.Alien.Stats,
            ArmorId = armor.Length == 0 ? arena.Alien.ArmorId : armor,
            ArmorSize = size,
            UnitType = arena.Alien.UnitType,
            Health = 100,
            MovementType = 0,
        };
        arena.Battle.AddUnit(unit);
        arena.Battle.SetTile(unit, At(x, y));
        unit.SetDirection(6);
        unit.Morale = 100;
        return unit;
    }

    /// <summary>A unit lying unconscious where it was (its body item on the tile): a unit that is out stands on no tile.</summary>
    private static BattleItem Lie(Arena arena, BattleUnit unit)
    {
        var tile = unit.Position!.Value;
        arena.Battle.SetTile(unit, At(0, 0));
        arena.Map.SetUnitAt(arena.Tile(0, 0), null);
        unit.Position = null;
        unit.SavedPosition = tile;
        unit.Status = BattleUnitStatus.Unconscious;
        unit.Health = 10;
        unit.StunLevel = 3;
        return arena.Battle.AddItemOnTile(new BattleItem(arena.Battle.TakeItemId(), "CORPSE") { CorpseUnitId = unit.Id }, tile);
    }

    private static BattleAttack Murder(Arena arena, BattleUnit victim, BattleUnit murderer)
    {
        victim.MurdererId = murderer.Id;
        victim.MurdererWeapon = "GUN";
        victim.MurdererWeaponAmmo = "CLIP";
        return default;
    }

    // ---- a death: the statistics, the murderer and the morale ----

    [Fact]
    public void ADeathCreditsTheMurdererWithTheKillAndMovesTheMorale()
    {
        var arena = Duel();
        var (soldier, alien) = (arena.Soldier, arena.Alien);
        soldier.Morale = 50;
        alien.Health = 0;
        alien.FatalShotSide = BattleUnit.SideRear;
        alien.FatalShotBodyPart = BattleUnit.BodyPartHead;
        Murder(arena, alien, soldier);
        var casualty = Assert.Single(Check(arena));
        Assert.Equal((alien.Id, BattleUnitStatus.Dead), (casualty.UnitId, casualty.Status));
        Assert.Equal(BattleUnitStatus.Dead, alien.Status);
        // The kill: who, how, with what, where and when (the turn counts three to a turn and the side).
        var kill = Assert.Single(soldier.Statistics.Kills);
        Assert.Equal(new BattleUnitKillSnapshot
        {
            Type = "ALIEN",
            Rank = "STR_LIVE_SOLDIER",
            Race = "STR_HOSTILE",
            Weapon = "GUN",
            WeaponAmmo = "CLIP",
            Status = (int)BattleUnitStatus.Dead,
            Faction = (int)BattleFaction.Hostile,
            Mission = 0,
            Turn = arena.Battle.Turn * 3,
            Side = BattleUnit.SideRear,
            BodyPart = BattleUnit.BodyPartHead,
            Id = alien.Id,
        }, kill);
        Assert.Equal((1, (int)BattleFaction.Player, soldier.Id), (soldier.Kills, alien.KilledBy, alien.MurdererId));
        // The murderer gains 20 for killing the other side and every unit still up on the winning squad 10; the losing squad's morale falls.
        Assert.Equal(80, soldier.Morale);
        Assert.True(alien.Morale < 100);
        // The aliens learn where the murderer is.
        Assert.Equal((0, arena.Tile(10, 10)), (soldier.TurnsSinceSpotted[(int)BattleFaction.Hostile], soldier.TileLastSpottedBy[(int)BattleFaction.Hostile]));
        // The same victim is not killed twice.
        Assert.Empty(Check(arena));
        Assert.Single(soldier.Statistics.Kills);
    }

    [Fact]
    public void FriendlyFireAndAKilledCivilianCostTheMurdererMorale()
    {
        var arena = Duel();
        var (soldier, friend) = (arena.Soldier, Extra(arena, 930, BattleFaction.Player, 12, 12));
        soldier.Morale = 60;
        friend.Health = 0;
        Murder(arena, friend, soldier);
        Check(arena);
        // 2000 * 100 / 100 / 100 = 20 for the friendly fire, then 14 for losing a team member (100 * 100 * 200 * 7 / 100 / 100 / 100).
        Assert.Equal(26, soldier.Morale);
        Assert.Equal(BattleUnitStatus.Dead, friend.Status);

        arena = Duel();
        var civilian = Extra(arena, 931, BattleFaction.Neutral, 12, 12);
        arena.Soldier.Morale = 60;
        civilian.Health = 0;
        Murder(arena, civilian, arena.Soldier);
        Check(arena);
        // A civilian killed by X-COM costs 1000 * 100 / 100 / 100 = 10; the neutral side has no squad to lose morale.
        Assert.Equal((50, 100), (arena.Soldier.Morale, arena.Alien.Morale));
        Assert.Equal(BattleUnitStatus.Dead, civilian.Status);

        // A civilian killed by the aliens is a small pleasure to them.
        arena = Duel();
        civilian = Extra(arena, 931, BattleFaction.Neutral, 12, 12);
        arena.Alien.Morale = 50;
        civilian.Health = 0;
        Murder(arena, civilian, arena.Alien);
        Check(arena);
        Assert.Equal(60, arena.Alien.Morale);
    }

    [Fact]
    public void ALeaderModifiesTheMoraleAMurdererGainsAndAnOfficersDeathHurtsMore()
    {
        var arena = Duel();
        arena.Soldier.Rank = 3; // a captain: 100 + 10 + 5
        arena.Soldier.Morale = 40;
        arena.Alien.Health = 0;
        Murder(arena, arena.Alien, arena.Soldier);
        Check(arena);
        // 20 * 115 / 100 = 23; the winners then gain 10 * 115 / 100 = 11 (the winning side's modifier is the leader's too).
        Assert.Equal(40 + 23 + 11, arena.Soldier.Morale);
    }

    [Fact]
    public void AStunCreditsTheStunAndMovesTheMoraleOnlyUnderStunningImprovesMorale()
    {
        foreach (var improves in new[] { false, true })
        {
            var arena = Duel(global: improves ? "stunningImprovesMorale: true" : "");
            arena.Soldier.Morale = 60;
            arena.Alien.StunLevel = arena.Alien.Health;
            Murder(arena, arena.Alien, arena.Soldier);
            var casualty = Assert.Single(Check(arena));
            Assert.Equal(BattleUnitStatus.Unconscious, casualty.Status);
            Assert.Equal(BattleUnitStatus.Unconscious, arena.Alien.Status);
            Assert.True(arena.Alien.Statistics.WasUnconscious);
            Assert.Equal((int)BattleUnitStatus.Unconscious, Assert.Single(arena.Soldier.Statistics.Kills).Status);
            // The first stun only: the murderer gets 20 and the winning squad 10.
            Assert.Equal(improves ? 90 : 60, arena.Soldier.Morale);
            // A unit that is stunned (not killed) is no kill to count, nor does it lose the squad morale.
            Assert.Equal((0, 100), (arena.Soldier.Kills, arena.Alien.Morale));
            // The unit lies as a body, on no tile, facing the way a collapsed unit does.
            Assert.Equal((3, null, arena.Tile(15, 10) >= 0), (arena.Alien.Direction, arena.Alien.Position, true));
            Assert.Equal(At(15, 10), arena.Alien.SavedPosition);
            Assert.Single(arena.Battle.Items, item => item.CorpseUnitId == arena.Alien.Id && item.Tile == At(15, 10));
        }
    }

    [Fact]
    public void AKillWithoutAnAttackerGoesToTheLastUnitToHitTheVictim()
    {
        // Bleeding and fire kill with no attacker: the murderer is the last unit to hit, with the weapon recorded then.
        var arena = Duel();
        arena.Alien.Health = 0;
        arena.Alien.MurdererId = arena.Soldier.Id;
        arena.Alien.MurdererWeapon = "FLAME";
        arena.Alien.MurdererWeaponAmmo = "TANK";
        Check(arena);
        var kill = Assert.Single(arena.Soldier.Statistics.Kills);
        Assert.Equal(("FLAME", "TANK"), (kill.Weapon, kill.WeaponAmmo));
        // A terrain explosion has no such murderer.
        arena = Duel();
        arena.Alien.Health = 0;
        arena.Alien.MurdererId = arena.Soldier.Id;
        Check(arena, terrain: true);
        Assert.Empty(arena.Soldier.Statistics.Kills);
    }

    [Fact]
    public void TheAttackNamesTheWeaponAndTheAmmunitionAndAButtIsAGunButt()
    {
        var arena = Duel();
        var (attack, gun, clip) = Shot(arena);
        arena.Alien.Health = 0;
        Check(arena, attack);
        var kill = Assert.Single(arena.Soldier.Statistics.Kills);
        Assert.Equal((gun.ItemType, clip.ItemType), (kill.Weapon, kill.WeaponAmmo));

        arena = Duel();
        (attack, _, _) = Shot(arena, type: BattleActionType.Hit);
        arena.Alien.Health = 0;
        Check(arena, attack);
        Assert.Equal("__GUNBUTT", Assert.Single(arena.Soldier.Statistics.Kills).WeaponAmmo);

        // A mind controlled unit's kill is credited to the soldier who controls it.
        arena = Duel();
        var slave = Extra(arena, 932, BattleFaction.Hostile, 12, 12);
        slave.Faction = BattleFaction.Player;
        slave.MindControllerId = arena.Soldier.Id;
        arena.Alien.Health = 0;
        Check(arena, new BattleAttack(BattleActionType.AimedShot, slave));
        Assert.Equal(1, Assert.Single(arena.Soldier.Statistics.Kills).Id == arena.Alien.Id ? arena.Soldier.Statistics.SlaveKills : -1);
        Assert.Empty(slave.Statistics.Kills);
        Assert.Equal(arena.Soldier.Id, arena.Alien.MurdererId);
    }

    [Fact]
    public void ACosmeticVictimLeavesNoKillInTheStatistics()
    {
        var arena = Duel(unitExtra: "    cosmetic: true\n");
        arena.Alien.Health = 0;
        Murder(arena, arena.Alien, arena.Soldier);
        Check(arena);
        Assert.Empty(arena.Soldier.Statistics.Kills);
        Assert.Equal(BattleUnitStatus.Dead, arena.Alien.Status);
    }

    // ---- the fall of a dying unit ----

    [Fact]
    public void ADyingUnitFacesDirectionThreeLeavesItsTileAndDropsItsThings()
    {
        var arena = Duel();
        var soldier = arena.Soldier;
        var gun = GunFor(arena.Battle, soldier);
        arena.Battle.SelectedUnitId = soldier.Id;
        arena.Battle.UndoUnitId = soldier.Id;
        soldier.Health = 0;
        soldier.Kneeled = false;
        Check(arena);
        Assert.Equal((BattleUnitStatus.Dead, 3, null, At(10, 10)), (soldier.Status, soldier.Direction, soldier.Position, soldier.SavedPosition));
        Assert.Null(arena.Map.UnitAt(arena.Tile(10, 10)));
        // The inventory lies on the tile (the ammunition stays in the gun), with the corpse; the selection and undo units are cleared.
        Assert.Equal((At(10, 10), "STR_GROUND", null), (gun.Tile, gun.Slot, gun.OwnerUnitId));
        Assert.Single(arena.Battle.Items, item => item.CorpseUnitId == soldier.Id && item.Tile == At(10, 10));
        Assert.Equal((null, null), (arena.Battle.SelectedUnitId, arena.Battle.UndoUnitId));
        // The sight and the spotting of the dead end, and its soldier's notice is recorded.
        Assert.Equal([255, 255, 255], soldier.TurnsSinceSpotted.Select(static value => (int)value));
        Assert.Equal([0, 0, 0], soldier.TurnsLeftSpottedForSnipers.Select(static value => (int)value));
        Assert.Equal([-1, -1, -1], soldier.TileLastSpottedBy);
        Assert.Empty(soldier.VisibleUnits);
        Assert.Equal(2, soldier.NotificationShown);
    }

    [Fact]
    public void ADeathByExplosionOrFallingSkipsThePirouetteAndAnUnconsciousUnitsNoticeIsOne()
    {
        // A type that is not direct (an explosion) lets the unit fall where it faces: it still ends facing direction 3.
        var arena = Duel();
        arena.Soldier.Health = 0;
        arena.Soldier.SetDirection(5);
        Check(arena, type: RuntimeItemDamageRule.Predefined(RuntimeItemDamageRule.DamageHighExplosive));
        Assert.Equal((BattleUnitStatus.Dead, 3), (arena.Soldier.Status, arena.Soldier.Direction));

        arena = Duel();
        arena.Soldier.StunLevel = arena.Soldier.Health;
        Check(arena);
        Assert.Equal((BattleUnitStatus.Unconscious, 1, 0), (arena.Soldier.Status, arena.Soldier.NotificationShown, arena.Soldier.TurnsSinceStunned));
    }

    [Fact]
    public void AnUnconsciousUnitThatCannotBeCapturedDiesAtOnceAndOneThatCanKeepsItsHealth()
    {
        var arena = Duel(unitExtra: "    capturable: false\n");
        arena.Alien.StunLevel = arena.Alien.Health;
        Check(arena);
        Assert.Equal((BattleUnitStatus.Dead, 0), (arena.Alien.Status, arena.Alien.Health));
        var captured = Duel();
        captured.Alien.StunLevel = captured.Alien.Health;
        Check(captured);
        Assert.Equal((BattleUnitStatus.Unconscious, 100), (captured.Alien.Status, captured.Alien.Health));
    }

    [Fact]
    public void ABigUnitLeavesACorpseOnEachTileItCoveredAndOverkillLeavesNone()
    {
        var arena = Flat(bigAlien: true);
        arena.Alien.Health = 0;
        Assert.Equal(At(15, 10), arena.Alien.Position);
        Check(arena);
        Assert.Equal(BattleUnitStatus.Dead, arena.Alien.Status);
        var corpses = arena.Battle.Items.Where(item => item.CorpseUnitId == arena.Alien.Id).ToArray();
        Assert.Equal(4, corpses.Length);
        Assert.Equal([At(15, 10), At(15, 11), At(16, 10), At(16, 11)], corpses.Select(static item => item.Tile!.Value).OrderBy(static tile => tile.X).ThenBy(static tile => tile.Y));
        foreach (var (x, y) in new[] { (15, 10), (15, 11), (16, 10), (16, 11) }) Assert.Null(arena.Map.UnitAt(arena.Tile(x, y)));

        // Overkill (the health far below zero) leaves nothing but gravity's work on the tile.
        arena = Duel();
        arena.Alien.Health = -1000;
        Check(arena);
        Assert.Equal(BattleUnitStatus.Dead, arena.Alien.Status);
        Assert.DoesNotContain(arena.Battle.Items, item => item.CorpseUnitId == arena.Alien.Id);
    }

    [Fact]
    public void ACorpseDroppedOverAHoleFallsToTheFloorBelow()
    {
        var arena = Flat(edit: rules => rules.Replace("    shade: 0\n    width: 30\n    length: 30\n    height: 2", "    shade: 0\n    width: 30\n    length: 30\n    height: 4", StringComparison.Ordinal));
        // The alien lies in the air over the hole the floor below leaves.
        arena.Put(15, 10, 2, BattleTilePart.Floor, Floor);
        arena.Battle.SetTile(arena.Alien, At(15, 10, 2));
        arena.Alien.Health = 0;
        Check(arena);
        var corpse = Assert.Single(arena.Battle.Items, item => item.CorpseUnitId == arena.Alien.Id);
        Assert.Equal(At(15, 10, 2), corpse.Tile);
        arena.Clear(15, 10, 2, BattleTilePart.Floor);
        arena.Battle.ApplyGravity(At(15, 10, 2));
        Assert.Equal((At(15, 10, 0), At(15, 10, 0)), (corpse.Tile, arena.Alien.SavedPosition));
    }

    [Fact]
    public void ACarriedUnitThatDiesKeepsItsBodyItemAndOverkillRemovesIt()
    {
        var arena = Duel();
        var carried = Extra(arena, 933, BattleFaction.Player, 12, 12);
        var body = Lie(arena, carried);
        // The soldier picks the body up: the item is the soldier's, in no tile, and the unit's position is none.
        body.Tile = null;
        body.OwnerUnitId = arena.Soldier.Id;
        body.Slot = "STR_BACK_PACK";
        carried.Health = 0;
        Assert.True(arena.Battle.IsCarried(carried));
        Assert.Equal(BattleUnitStatus.Dead, Assert.Single(Check(arena)).Status);
        // Its things stay with it and its body item stays in the carrier's inventory (a 1x1 body takes the corpse of its armor).
        Assert.Equal(arena.Soldier.Id, body.OwnerUnitId);
        Assert.Contains(body, arena.Battle.Items);

        var over = Duel();
        carried = Extra(over, 933, BattleFaction.Player, 12, 12);
        body = Lie(over, carried);
        body.Tile = null;
        body.OwnerUnitId = over.Soldier.Id;
        body.Slot = "STR_BACK_PACK";
        carried.Health = -1000;
        Check(over);
        Assert.DoesNotContain(body, over.Battle.Items);
    }

    [Fact]
    public void ADyingHostileMakesTheNodesAroundItDangerousAndTheyAreSaved()
    {
        var arena = Duel();
        var node = (int x, int y, int z) => new BattleNode(arena.Map.Nodes.Count, At(x, y, z), 0, 0, 0, 0, 0, 1, []);
        foreach (var (x, y, z) in new[] { (15, 11, 0), (14, 10, 0), (17, 10, 0), (15, 10, 1) }) arena.Map.AddNode(node(x, y, z));
        arena.Map.AddNode(BattleNode.Dummy());
        var (before, killer) = (arena.Map.Nodes.Count, Extra(arena, 934, BattleFaction.Hostile, 20, 20));
        var first = before - 5;
        arena.Alien.Health = 0;
        Check(arena);
        // The squared distance under 4 (a unit and a node 2 tiles apart is 4): the first two are dangerous, the others are not.
        Assert.Equal([true, true, false, true], arena.Map.Nodes.Skip(first).Take(4).Select(static node => (node.Type & BattleNode.TypeDangerous) != 0));
        Assert.DoesNotContain(arena.Map.Nodes.Take(first), static node => (node.Type & BattleNode.TypeDangerous) != 0);
        Assert.Equal(before, arena.Map.Nodes.Count);
        Assert.Equal(BattleUnitStatus.Standing, killer.Status);
        // The nodes of the saved battle carry the type; an X-COM unit's death marks nothing.
        var saved = arena.Battle.Capture().Nodes;
        Assert.Equal(arena.Map.Nodes.Select(static node => node.Type), saved.Select(static node => node.Type));
        var other = Duel();
        other.Map.AddNode(node(10, 11, 0));
        other.Soldier.Health = 0;
        Check(other);
        Assert.Equal(0, other.Map.Nodes[^1].Type & BattleNode.TypeDangerous);
    }

    // ---- the memorial ----

    [Fact]
    public void ASoldierKilledInBattleGoesToTheMemorialWithItsCauseAndKeepsItsArmor()
    {
        var arena = Duel();
        var soldier = arena.Soldier;
        var before = arena.Campaign.Capture();
        var armor = before.Bases.SelectMany(static owner => owner.Soldiers).Single(entry => entry.Id == soldier.SoldierId).Personal!.Armor;
        soldier.Health = 0;
        soldier.FatalShotSide = BattleUnit.SideLeft;
        soldier.FatalShotBodyPart = BattleUnit.BodyPartLeftLeg;
        Murder(arena, soldier, arena.Alien);
        Check(arena);
        var after = arena.Campaign.Capture();
        Assert.DoesNotContain(after.Bases.SelectMany(static owner => owner.Soldiers), entry => entry.Id == soldier.SoldierId);
        var dead = Assert.Single(after.DeadSoldiers);
        Assert.Equal(soldier.SoldierId, dead.Soldier.Id);
        Assert.Equal(armor, dead.Soldier.Personal!.Armor);
        // The cause is the murderer's kill record with the victim's id, side and body part.
        Assert.Equal(new BattleUnitKillSnapshot
        {
            Type = "ALIEN",
            Rank = "STR_LIVE_SOLDIER",
            Race = "STR_HOSTILE",
            Weapon = "GUN",
            WeaponAmmo = "CLIP",
            Status = (int)BattleUnitStatus.Dead,
            Faction = (int)BattleFaction.Hostile,
            Turn = arena.Battle.Turn * 3,
            Side = BattleUnit.SideLeft,
            BodyPart = BattleUnit.BodyPartLeftLeg,
            Id = soldier.Id,
        }, dead.Cause);
        // The kill the alien made names the soldier by its name and rank.
        var kill = Assert.Single(arena.Alien.Statistics.Kills);
        Assert.Equal((dead.Soldier.Personal.Name, "STR_ROOKIE", "", "STR_FRIENDLY"), (kill.Name, kill.Rank, kill.Type, kill.Race));
        // The battle still knows the soldier (a reload of the battle makes its unit from the memorial).
        Assert.NotNull(arena.Campaign.BattleSoldier(soldier.SoldierId!.Value));
    }

    [Fact]
    public void AKilledSoldiersCauseIsSavedAndLoadedAndTheBattleReloadsWithTheUnitDead()
    {
        var (campaign, _) = BattleStartTests.Requested(BattleStartTests.Rules, "UFO_SCOUT");
        BattleStartTests.Started(campaign);
        foreach (var command in new ICampaignCommand[] { new BeginBriefing("", ""), new ConfirmBriefing(), new StartFirstTurn() })
            Assert.DoesNotContain(campaign.Execute(command).Events, static item => item is CampaignActionBlocked);
        var battle = BattleStartTests.Battle(campaign);
        var soldier = battle.Units.First(static unit => unit.SoldierId is not null && unit.Faction == BattleFaction.Player);
        var alien = battle.Units.First(static unit => unit.OriginalFaction == BattleFaction.Hostile);
        soldier.Health = 0;
        soldier.MurdererId = alien.Id;
        soldier.MurdererWeapon = "STR_PLASMA_RIFLE";
        soldier.MurdererWeaponAmmo = "STR_PLASMA_RIFLE_CLIP";
        battle.CheckForCasualties(campaign.Content.RuntimeRules, null, default);
        var snapshot = campaign.Capture();
        var dead = Assert.Single(snapshot.DeadSoldiers);
        Assert.Equal(("STR_PLASMA_RIFLE", (int)BattleUnitStatus.Dead), (dead.Cause!.Weapon, dead.Cause.Status));
        var options = new OxceSaveLoadOptions(campaign.Identity.MasterId, campaign.Identity.ActiveMods.ToHashSet(StringComparer.Ordinal))
        {
            BattleTerrainFiles = BattleStartTests.TerrainFiles(),
        };
        var text = OxceSaveAdapter.EmitNewCampaign(snapshot);
        BattleSaveConformance.CheckSave(text);
        var loaded = OxceSaveAdapter.Load(text, "casualty.sav", campaign.Content, new SplitMix64RandomSource(5), options);
        Assert.Equivalent(snapshot.DeadSoldiers, loaded.Campaign.Capture().DeadSoldiers, strict: true);
        var again = BattleStartTests.Battle(loaded.Campaign).UnitById(soldier.Id)!;
        Assert.Equal((BattleUnitStatus.Dead, null), (again.Status, again.Position));
        // Saved again, the loaded death keeps its cause.
        var rewritten = OxceSaveAdapter.EmitLoadedCampaign(loaded.Campaign.Capture(), loaded.Source);
        Assert.Equivalent(snapshot.DeadSoldiers, OxceSaveAdapter.Load(rewritten, "again.sav", campaign.Content, new SplitMix64RandomSource(5), options).Campaign.Capture().DeadSoldiers, strict: true);
    }

    // ---- conversion ----

    [Fact]
    public void AnInfectedUnitThatDiesTurnsIntoItsSpawnUnitWhereItFell()
    {
        var arena = Duel();
        var alien = arena.Alien;
        alien.SpawnUnit = "ALIEN";
        alien.SpawnUnitFaction = (int)BattleFaction.Hostile;
        alien.Health = 0;
        var gun = GunFor(arena.Battle, alien);
        gun.Slot = "STR_RIGHT_HAND";
        arena.Battle.SelectedUnitId = null;
        var before = arena.Battle.Units.Count;
        var casualty = Assert.Single(Check(arena));
        Assert.Equal(alien.Id + 0, casualty.UnitId);
        var spawned = arena.Battle.Units[^1];
        Assert.Equal((before + 1, spawned.Id, casualty.ConvertedTo), (arena.Battle.Units.Count, arena.Battle.Units.Max(static unit => unit.Id), spawned.Id));
        // The old unit is dead for good and leaves no corpse; the new one stands where it fell, facing as it did, with no time units.
        Assert.Equal((BattleUnitStatus.Dead, true), (alien.Status, alien.AlreadyRespawned));
        Assert.DoesNotContain(arena.Battle.Items, item => item.CorpseUnitId == alien.Id);
        Assert.Equal((At(15, 10), BattleFaction.Hostile, 0, true), (spawned.Position, spawned.Faction, spawned.TimeUnits, spawned.DontReselect));
        Assert.Equal(arena.Alien.Direction, spawned.Direction == 6 ? 3 : spawned.Direction);
        Assert.Equal(spawned.Id, arena.Map.UnitAt(arena.Tile(15, 10)));
        // The old unit's inventory lies on the ground under the new one.
        Assert.Equal(At(15, 10), gun.Tile);

        // A unit that respawned already dies without leaving anything.
        var second = Duel();
        second.Alien.SpawnUnit = "ALIEN";
        second.Alien.AlreadyRespawned = true;
        second.Alien.Health = 0;
        var units = second.Battle.Units.Count;
        Check(second);
        Assert.Equal((units, BattleUnitStatus.Dead), (second.Battle.Units.Count, second.Alien.Status));
        Assert.DoesNotContain(second.Battle.Items, item => item.CorpseUnitId == second.Alien.Id);
    }

    [Fact]
    public void AFriendlyInfectedUnitSpawnsOnItsSideAndASpawnUnitTheModLacksIsNoConversion()
    {
        var arena = Duel();
        arena.Alien.SpawnUnit = "ALIEN";
        arena.Alien.SpawnUnitFaction = (int)BattleFaction.Player;
        arena.Alien.Health = 0;
        Check(arena);
        var spawned = arena.Battle.Units[^1];
        Assert.Equal((BattleFaction.Player, true, arena.Alien.SpawnUnitFaction), (spawned.Faction, spawned.SummonedPlayerUnit, spawned.SpawnUnitFaction));

        // A spawn unit that does not exist converts nothing; the unit dies as any other (the reference would dereference a missing rule).
        var missing = Duel();
        missing.Alien.SpawnUnit = "NO_SUCH_UNIT";
        missing.Alien.Health = 0;
        var count = missing.Battle.Units.Count;
        Check(missing);
        Assert.Equal((count, BattleUnitStatus.Dead), (missing.Battle.Units.Count, missing.Alien.Status));
        Assert.Single(missing.Battle.Items, item => item.CorpseUnitId == missing.Alien.Id);
    }

    [Fact]
    public void ConvertInfectedTurnsTheLiveInfectedAndKnockOutConvertsThenStuns()
    {
        var arena = Duel();
        arena.Alien.SpawnUnit = "ALIEN";
        arena.Alien.Respawn = true;
        Assert.True(arena.Battle.ConvertInfected(arena.Rules));
        Assert.False(arena.Alien.Respawn);
        Assert.Equal(BattleUnitStatus.Dead, arena.Alien.Status);
        var spawned = arena.Battle.Units[^1];
        Assert.Equal((At(15, 10), spawned.Stats.Health), (spawned.Position, spawned.Health));
        Assert.False(arena.Battle.ConvertInfected(arena.Rules));

        // knockOut: a unit with a spawn unit turns first and the new unit is stunned; without one the stun reaches the health.
        var knock = Duel();
        knock.Alien.SpawnUnit = "ALIEN";
        knock.Battle.KnockOut(knock.Rules, knock.Alien);
        var created = knock.Battle.Units[^1];
        Assert.Equal((BattleUnitStatus.Dead, "", true), (knock.Alien.Status, created.SpawnUnit, created.StunLevel >= created.Health));
        var plain = Duel();
        plain.Battle.KnockOut(plain.Rules, plain.Soldier);
        Assert.Equal(100, plain.Soldier.StunLevel);
        plain.Soldier.Health = 0;
        plain.Battle.KnockOut(plain.Rules, plain.Soldier);
        Assert.Equal(1, plain.Soldier.StunLevel);
    }

    // ---- revival ----

    [Fact]
    public void AnUnconsciousUnitWakesUpWhereItLiesAndLosesItsTimeUnits()
    {
        var arena = Duel();
        var alien = arena.Alien;
        var body = Lie(arena, alien);
        alien.Direction = 3;
        arena.Battle.ReviveUnconsciousUnits(arena.Rules, noTU: true);
        Assert.Equal((BattleUnitStatus.Standing, At(15, 10), 0, 0), (alien.Status, alien.Position, alien.TimeUnits, alien.NotificationShown));
        Assert.Equal(alien.Id, arena.Map.UnitAt(arena.Tile(15, 10)));
        Assert.DoesNotContain(body, arena.Battle.Items);
        Assert.False(alien.Kneeled);
    }

    [Fact]
    public void AWakingUnitRecoversTimeUnitsAsANewTurnDoesCutByTheWakeUpShare()
    {
        var arena = Duel(global: "tuRecoveryWakeUpNewTurn: 50");
        Lie(arena, arena.Alien);
        arena.Battle.ReviveUnconsciousUnits(arena.Rules, noTU: false);
        Assert.Equal((BattleUnitStatus.Standing, arena.Alien.Stats.TimeUnits * 50 / 100), (arena.Alien.Status, arena.Alien.TimeUnits));
        var whole = Duel();
        Lie(whole, whole.Alien);
        whole.Battle.ReviveUnconsciousUnits(whole.Rules, noTU: false);
        Assert.Equal(whole.Alien.Stats.TimeUnits, whole.Alien.TimeUnits);
    }

    [Fact]
    public void AUnitThatIsStillStunnedOrBigOrDeadStaysWhereItIs()
    {
        var arena = Duel();
        Lie(arena, arena.Alien);
        arena.Alien.StunLevel = 10;
        arena.Battle.ReviveUnconsciousUnits(arena.Rules, noTU: true);
        Assert.Equal(BattleUnitStatus.Unconscious, arena.Alien.Status);
        arena.Alien.StunLevel = 3;
        arena.Alien.Status = BattleUnitStatus.Dead;
        arena.Battle.ReviveUnconsciousUnits(arena.Rules, noTU: true);
        Assert.Equal(BattleUnitStatus.Dead, arena.Alien.Status);
    }

    [Fact]
    public void ARevivalNextToABigUnitLooksOneTileFurtherAndNoFreeTileLeavesTheUnitUnconscious()
    {
        var arena = Duel();
        var big = Extra(arena, 940, BattleFaction.Hostile, 15, 10, "VEHICLE_ARMOR", 2);
        Assert.Equal(arena.Map.UnitAt(arena.Tile(16, 11)), big.Id);
        // The unit lies under the big one (the tile is taken); the tile north of it is taken too, so the first free place is two tiles out east of north.
        var lying = Extra(arena, 941, BattleFaction.Hostile, 3, 3);
        var body = Lie(arena, lying);
        lying.SavedPosition = At(15, 10);
        body.Tile = At(15, 10);
        Extra(arena, 942, BattleFaction.Hostile, 15, 9);
        arena.Battle.ReviveUnconsciousUnits(arena.Rules, noTU: true);
        Assert.Equal((BattleUnitStatus.Standing, At(17, 9)), (lying.Status, lying.Position));

        // A place with no floor around it takes no walker: it stays unconscious.
        var stuck = Duel();
        var sleeper = Extra(stuck, 943, BattleFaction.Hostile, 3, 3);
        var spot = Lie(stuck, sleeper);
        sleeper.SavedPosition = At(20, 20);
        spot.Tile = At(20, 20);
        foreach (var (dx, dy) in new[] { (0, 0), (0, -1), (1, -1), (1, 0), (1, 1), (0, 1), (-1, 1), (-1, 0), (-1, -1) }) stuck.Clear(20 + dx, 20 + dy, 0, BattleTilePart.Floor);
        stuck.Battle.ReviveUnconsciousUnits(stuck.Rules, noTU: true);
        Assert.Equal((BattleUnitStatus.Unconscious, true), (sleeper.Status, stuck.Battle.Items.Contains(spot)));
    }

    // ---- when the check runs ----

    [Fact]
    public void BeforeTheGameAUnitBecomesItsCorpseAtOnce()
    {
        // The battlescape's constructor and the UFO's power sources: the state's constructor converts the unit, and nothing else of the state runs.
        var arena = Duel();
        arena.Battle.IsBeforeGame = true;
        arena.Alien.Health = 0;
        arena.Alien.TurnsSinceSpotted[0] = 3;
        arena.Battle.OpenBattlescape(arena.Rules);
        Assert.Equal((BattleUnitStatus.Dead, null, 3), (arena.Alien.Status, arena.Alien.Position, (int)arena.Alien.TurnsSinceSpotted[0]));
        Assert.Single(arena.Battle.Items, item => item.CorpseUnitId == arena.Alien.Id);
        Assert.Equal(3, arena.Alien.Direction);
    }

    [Fact]
    public void TheDeathsRunInTheOrderTheQueueGivesThem()
    {
        var arena = Duel();
        var second = Extra(arena, 950, BattleFaction.Hostile, 17, 12);
        var third = Extra(arena, 951, BattleFaction.Hostile, 18, 12);
        foreach (var unit in new[] { arena.Alien, second, third }) unit.Health = 0;
        // Into an empty queue the first state runs at once and the others go right behind the running one, the last pushed first; behind a state that
        // is running (an explosion, a fall) every push goes there.
        var empty = arena.Battle.QueueCasualties(arena.Rules, null, default);
        Assert.Equal([arena.Alien.Id, third.Id, second.Id], empty.Select(static death => death.Unit.Id));
        var again = Duel();
        var (b, c) = (Extra(again, 950, BattleFaction.Hostile, 17, 12), Extra(again, 951, BattleFaction.Hostile, 18, 12));
        foreach (var unit in new[] { again.Alien, b, c }) unit.Health = 0;
        Assert.Equal([c.Id, b.Id, again.Alien.Id], again.Battle.QueueCasualties(again.Rules, null, default, statesQueued: true).Select(static death => death.Unit.Id));
    }

    [Fact]
    public void ACasualtyCheckQueuesAUnitWithoutAFloorToFall()
    {
        var arena = Flat(edit: rules => rules.Replace("    shade: 0\n    width: 30\n    length: 30\n    height: 2", "    shade: 0\n    width: 30\n    length: 30\n    height: 4", StringComparison.Ordinal));
        arena.Put(15, 10, 2, BattleTilePart.Floor, Floor);
        arena.Battle.SetTile(arena.Alien, At(15, 10, 2));
        arena.Clear(15, 10, 2, BattleTilePart.Floor);
        arena.Battle.UpdateTileFloorState(arena.Alien);
        Assert.True(arena.Alien.HaveNoFloorBelow);
        Assert.Empty(arena.Battle.FallingUnits);
        Check(arena);
        Assert.Equal([arena.Alien.Id], arena.Battle.FallingUnits.Select(static unit => unit.Id));
    }

    [Fact]
    public void ALoadedBattleUnderWayTakesOutItsCasualtiesAndLetsItsFloorlessWalkersFall()
    {
        var arena = Duel();
        arena.Alien.Health = 0;
        arena.Soldier.StunLevel = 100;
        var restored = CampaignState.Restore(arena.Campaign.Capture(), arena.Campaign.Content, new SplitMix64RandomSource(3), BattleStartTests.TerrainFiles(Terrain));
        var battle = BattleStartTests.Battle(restored);
        Assert.Equal((BattleUnitStatus.Dead, BattleUnitStatus.Unconscious), (battle.UnitById(arena.Alien.Id)!.Status, battle.UnitById(arena.Soldier.Id)!.Status));
        Assert.Empty(battle.FallingUnits);
    }

    [Fact]
    public void NoCasualtyMeansNothingChanges()
    {
        var arena = Duel();
        var revision = arena.Battle.Revision;
        var states = arena.Battle.Units.Select(static unit => (unit.Status, unit.Health, unit.Morale)).ToArray();
        Assert.Empty(Check(arena));
        Assert.Equal(states, arena.Battle.Units.Select(static unit => (unit.Status, unit.Health, unit.Morale)));
        Assert.True(arena.Battle.Revision >= revision);
    }

    [Fact]
    public void AVictimOfAnExplodingUnitsDeathBlastIsCreditedToTheOneWhoKilledTheExploder()
    {
        var arena = Duel(unitExtra: "    specab: 1\n");
        var victim = Extra(arena, 960, BattleFaction.Hostile, 12, 12);
        arena.Alien.Health = 0;
        arena.Alien.Status = BattleUnitStatus.Dead;
        arena.Alien.MurdererId = arena.Soldier.Id;
        victim.Health = 0;
        Check(arena, new BattleAttack(BattleActionType.AimedShot, arena.Alien));
        Assert.Equal(victim.Id, Assert.Single(arena.Soldier.Statistics.Kills).Id);
        Assert.Empty(arena.Alien.Statistics.Kills);
    }

    [Fact]
    public void AConversionFindsNoRoomWhenAnotherUnitStandsOnTheBodyAndACarriedHostileDoesNotDieThroughTheState()
    {
        var arena = Duel();
        var sleeper = Extra(arena, 961, BattleFaction.Hostile, 12, 12);
        sleeper.SpawnUnit = "ALIEN";
        Lie(arena, sleeper);
        sleeper.SavedPosition = At(14, 14);
        Extra(arena, 962, BattleFaction.Hostile, 14, 14);
        var count = arena.Battle.Units.Count;
        Assert.Null(arena.Battle.ConvertUnit(arena.Rules, sleeper));
        Assert.Equal((count, BattleUnitStatus.Dead, true), (arena.Battle.Units.Count, sleeper.Status, sleeper.AlreadyRespawned));

        // A carried hostile that loses its health is dead, but the state skips a unit with no tile: its body item stays.
        var carried = Extra(arena, 963, BattleFaction.Hostile, 16, 16);
        var body = Lie(arena, carried);
        body.Tile = null;
        body.OwnerUnitId = arena.Soldier.Id;
        body.Slot = "STR_BACK_PACK";
        carried.Health = 0;
        Assert.Empty(Check(arena));
        Assert.Equal((BattleUnitStatus.Dead, arena.Soldier.Id), (carried.Status, body.OwnerUnitId));
    }

    [Fact]
    public void ACarriedUnitWakesNextToItsCarrier()
    {
        var arena = Duel();
        var carried = Extra(arena, 964, BattleFaction.Hostile, 16, 16);
        var body = Lie(arena, carried);
        body.Tile = null;
        body.OwnerUnitId = arena.Soldier.Id;
        body.Slot = "STR_BACK_PACK";
        arena.Battle.ReviveUnconsciousUnits(arena.Rules, noTU: true);
        // The carrier's own tile is taken, so the first free place around it (north) is where the unit wakes up.
        Assert.Equal((BattleUnitStatus.Standing, At(10, 9)), (carried.Status, carried.Position));
        Assert.DoesNotContain(body, arena.Battle.Items);
    }

    [Fact]
    public void ADryRunRefusesAHitOnlyWhereTheUnitItReachesBlowsUpWithItsDeath()
    {
        var voxel = new BattlePosition(15 * 16 + 6, 10 * 16 + 8, 6);
        var arena = Duel(alienExtra: "    selfDestructItem: GRENADE\n", unitExtra: "    specab: 1\n");
        var (attack, _, _) = Shot(arena);
        Assert.True(arena.Battle.HitWouldReachUnit(arena.Soldier, voxel, arena.Rules));
        Assert.Equal("battle.terrain-explosion-unsupported", arena.Battle.Hit(arena.Rules, attack, voxel, 30, BattleDamageTypes.Fire, false, dryRun: true)?.Code ?? "battle.terrain-explosion-unsupported");
        var plain = Duel();
        Assert.False(plain.Battle.HitWouldReachUnit(plain.Soldier, voxel, plain.Rules));
    }

    [Fact]
    public void AFloorlessWalkerOfALoadedBattleFallsWhenItOpens()
    {
        var arena = Flat(edit: rules => rules.Replace("    shade: 0\n    width: 30\n    length: 30\n    height: 2", "    shade: 0\n    width: 30\n    length: 30\n    height: 4", StringComparison.Ordinal));
        arena.Battle.SetTile(arena.Alien, At(15, 10, 2));
        arena.Battle.UpdateTileFloorState(arena.Alien);
        Assert.True(arena.Alien.HaveNoFloorBelow);
        var restored = CampaignState.Restore(arena.Campaign.Capture(), arena.Campaign.Content, new SplitMix64RandomSource(3), BattleStartTests.TerrainFiles(Terrain));
        var battle = BattleStartTests.Battle(restored);
        Assert.Equal(At(15, 10, 0), battle.UnitById(arena.Alien.Id)!.Position);
        Assert.Empty(battle.FallingUnits);
    }
}

using Oxce.Core.Random;
using Oxce.Gameplay.Battles;
using Oxce.Gameplay.Battles.Generation;
using Oxce.Gameplay.Battles.Terrain;
using Oxce.Gameplay.Campaigns;
using Oxce.Gameplay.Campaigns.World;
using Oxce.Mods.Rulesets.Runtime;
using Xunit;
using static Oxce.CompatibilityTests.BattleStartTests;

namespace Oxce.CompatibilityTests;

/// <summary>
/// Phase 7 branch 3 S3: the briefing and the first turn. <c>BriefingState</c> (the constructor's names and its button),
/// <c>BattlescapeGame::spawnFromPrimedItems</c> and <c>tallyUnits</c>, <c>NextTurnState</c>'s turn-0 work (bug hunt mode,
/// reinforcements), <c>SavedBattleGame::startFirstTurn</c> with <c>randomizeItemLocations</c>, <c>resetUnitTiles</c> and
/// <c>BattleUnit::prepareNewTurn(false)</c> (<c>src/Battlescape/BriefingState.cpp</c>, <c>NextTurnState.cpp</c>,
/// <c>BattlescapeGame.cpp</c> and <c>src/Savegame/SavedBattleGame.cpp</c> at 4df3a5e).
/// </summary>
public sealed class BattleFirstTurnTests
{
    private static (CampaignState Campaign, BattleState Battle) Begun(string? rules = null, string deployment = "UFO_SCOUT")
    {
        var (campaign, _) = Requested(rules ?? Rules, deployment);
        Started(campaign);
        return (campaign, Battle(campaign));
    }

    private static string Refusal(CampaignState campaign, ICampaignCommand command) =>
        Assert.IsType<CampaignActionBlocked>(Assert.Single(campaign.Execute(command).Events)).Code;

    private static T Event<T>(CampaignState campaign, ICampaignCommand command) where T : ICampaignEvent
    {
        var events = campaign.Execute(command).Events;
        Assert.Equal("", events.OfType<CampaignActionBlocked>().SingleOrDefault()?.Reason ?? "");
        return Assert.IsType<T>(Assert.Single(events));
    }

    private static CampaignState ToInventory(string? rules = null, string deployment = "UFO_SCOUT")
    {
        var (campaign, _) = Begun(rules, deployment);
        Event<BriefingOpened>(campaign, new BeginBriefing("UFO-1", "SHIP"));
        Event<BriefingConfirmed>(campaign, new ConfirmBriefing());
        return campaign;
    }

    private static BattleUnit Soldier(BattleState battle) => battle.Units.First(static unit => unit.SoldierId is not null);

    /// <summary>The ARMORY block's grenades primed to explode at the start, spawning an alien and a clip.</summary>
    private static string SpawnRules(int chance = 100, string fuse = "0", int faction = 1, string unit = "ALIEN") => Rules
        .Replace("fuseTimers: {GRENADE: [3, 3]}", $"fuseTimers: {{GRENADE: [{fuse}, {fuse}]}}", StringComparison.Ordinal)
        .Replace("- type: GRENADE\n    battleType: 4\n    weight: 1",
            $"- type: GRENADE\n    battleType: 4\n    weight: 1\n    spawnUnit: {unit}\n    spawnUnitFaction: {faction}\n    spawnUnitChance: {chance}\n    spawnItem: CLIP\n    spawnItemChance: {chance}",
            StringComparison.Ordinal);

    [Fact]
    public void TheBriefingNamesTheTargetAndTheCraftAndEachBattleIsBriefedOnce()
    {
        var (campaign, battle) = Begun();
        Assert.Equal(("", "", BattlePhase.Briefing), (battle.TargetName, battle.CraftOrBaseName, battle.Phase));
        var briefing = campaign.GetQuery<IBattleBriefingQuery>()!.QueryBriefing()!;
        Assert.Equal(("STR_UFO_GROUND_ASSAULT", "STR_UFO_GROUND_ASSAULT_BRIEFING", "", ""),
            (briefing.Title, briefing.Description, briefing.TargetName, briefing.CraftOrBaseName));

        // BriefingState's constructor: the destination's name and the craft line (the client composes both with its language).
        Assert.Equal(new BriefingOpened("UFO-1", "Craft SHIP-1"), Event<BriefingOpened>(campaign, new BeginBriefing("UFO-1", "Craft SHIP-1")));
        Assert.Equal(("UFO-1", "Craft SHIP-1"), (battle.TargetName, battle.CraftOrBaseName));
        Assert.Equal("battle.briefing-already-open", Refusal(campaign, new BeginBriefing("other", "other")));
        Assert.Equal("UFO-1", battle.TargetName);
        // A craft without a destination leaves the target as it was.
        var (second, other) = Begun();
        Event<BriefingOpened>(second, new BeginBriefing("", "Craft SHIP-1"));
        Assert.Equal(("", "Craft SHIP-1"), (other.TargetName, other.CraftOrBaseName));
        Assert.Equal(new BattleBriefing("STR_UFO_GROUND_ASSAULT", "STR_UFO_GROUND_ASSAULT_BRIEFING", "GMDEFEND", "BACK16.SCR", "", 0, 0, true, true, false,
            "", "Craft SHIP-1", BattlePhase.Briefing), second.GetQuery<IBattleBriefingQuery>()!.QueryBriefing());
    }

    [Fact]
    public void AModsOperationNamesReplaceTheTargetFromTheBattlesOwnStream()
    {
        var rules = Rules + "\noperationNamesFirst: [ALPHA, BETA, GAMMA]\noperationNamesLast: [ONE, TWO]\n";
        var (campaign, battle) = Begun(rules);
        var before = campaign.Random.State;
        Event<BriefingOpened>(campaign, new BeginBriefing("UFO-1", "SHIP"));
        // RNG::seedless: the game's stream is untouched.
        Assert.Equal(before, campaign.Random.State);
        Assert.Matches("^(ALPHA|BETA|GAMMA) (ONE|TWO)$", battle.TargetName);
        Assert.Equal("SHIP", battle.CraftOrBaseName);
        Assert.True(campaign.GetQuery<IBattleBriefingQuery>()!.QueryBriefing()!.OperationName);
        // The same campaign picks the same name.
        var (again, repeated) = Begun(rules);
        Event<BriefingOpened>(again, new BeginBriefing("UFO-1", "SHIP"));
        Assert.Equal(battle.TargetName, repeated.TargetName);
        // Without last names the first name stands alone.
        var (single, alone) = Begun(rules.Replace("operationNamesLast: [ONE, TWO]", "operationNamesLast: []", StringComparison.Ordinal));
        Event<BriefingOpened>(single, new BeginBriefing("UFO-1", "SHIP"));
        Assert.Matches("^(ALPHA|BETA|GAMMA)$", alone.TargetName);
    }

    [Fact]
    public void TheBriefingScreenReadsTheDeploymentsBriefingData()
    {
        var rules = Rules.Replace("""
              - type: UFO_ALIENS
                width: 20
            """.Replace("\r\n", "\n", StringComparison.Ordinal), """
              - type: UFO_ALIENS
                briefing: {palette: 3, textOffset: 5, title: STR_CUSTOM_TITLE, desc: STR_CUSTOM_DESC, music: GMCUSTOM, background: BACK99.SCR, cutscene: CUT, showCraft: false, showTarget: false}
                width: 20
            """.Replace("\r\n", "\n", StringComparison.Ordinal), StringComparison.Ordinal);
        Assert.NotEqual(Rules, rules);
        var (campaign, _) = Begun(rules, "UFO_ALIENS");
        Assert.Equal(new BattleBriefing("STR_CUSTOM_TITLE", "STR_CUSTOM_DESC", "GMCUSTOM", "BACK99.SCR", "CUT", 3, 5, false, false, false, "", "",
            BattlePhase.Briefing), campaign.GetQuery<IBattleBriefingQuery>()!.QueryBriefing());
        // A briefing with only some keys keeps the others' defaults.
        var partial = rules.Replace("title: STR_CUSTOM_TITLE, ", "", StringComparison.Ordinal).Replace("desc: STR_CUSTOM_DESC, ", "", StringComparison.Ordinal);
        var (second, _) = Begun(partial, "UFO_ALIENS");
        var data = second.GetQuery<IBattleBriefingQuery>()!.QueryBriefing()!;
        Assert.Equal(("STR_UFO_GROUND_ASSAULT", "STR_UFO_GROUND_ASSAULT_BRIEFING", "GMCUSTOM"), (data.Title, data.Description, data.Music));
    }

    [Fact]
    public void TheRulesCarryTheBugHuntBriefingReinforcementAndSpawnValues()
    {
        var rules = Rules.Replace("  - type: UFO_SCOUT\n    width", "  - type: UFO_SCOUT\n    briefing: {title: STR_T, desc: STR_D, music: M, background: B.SCR, cutscene: C, palette: 2, textOffset: 4, showCraft: false, showTarget: false}\n    reinforcements: [{type: WAVE, minDifficulty: 1, maxDifficulty: 3, objectiveDestroyed: true, turns: [2, 5], minTurn: 3, maxTurn: 9, executionOdds: 40, maxRuns: 2}, {type: DEFAULTS}]\n    width", StringComparison.Ordinal)
            .Replace("  - type: ALIEN\n    armor: SMALL_VEHICLE_ARMOR", "  - type: ALIEN\n    energyRecovery: 12\n    armor: SMALL_VEHICLE_ARMOR", StringComparison.Ordinal)
            + "\nbughuntMaxEnemies: 5\nbughuntRank: 2\nbughuntLowMorale: 10\nbughuntTimeUnitsLeft: 20\noperationNamesFirst: [A]\noperationNamesLast: [B]\n";
        Assert.NotEqual(Rules, rules);
        var (campaign, battle) = Begun(rules);
        var catalog = campaign.Content.RuntimeRules;
        Assert.Equal((5, 2, 10, 20, 999), (catalog.Campaign.BughuntMaxEnemies, catalog.Campaign.BughuntRank, catalog.Campaign.BughuntLowMorale,
            catalog.Campaign.BughuntTimeUnitsLeft, catalog.Campaign.BughuntMinTurn));
        Assert.Equal(["A"], catalog.Campaign.OperationNamesFirst);
        Assert.Equal(["B"], catalog.Campaign.OperationNamesLast);
        Assert.Equal(12, catalog.Battle.Equipment.Units["ALIEN"].EnergyRecovery);
        var deployment = catalog.AlienDeployments[catalog.AlienDeployments.GetRequired("UFO_SCOUT")].Value.Battle;
        Assert.Equal(new RuntimeBriefing(2, 4, "STR_T", "STR_D", "M", "B.SCR", "C", false, false), deployment.Briefing);
        Assert.Equal(2, deployment.Reinforcements.Count);
        Assert.Equal(("WAVE", 1, 3, true, 3, 9, 40, 2), (deployment.Reinforcements[0].Type, deployment.Reinforcements[0].MinimumDifficulty, deployment.Reinforcements[0].MaximumDifficulty,
            deployment.Reinforcements[0].ObjectiveDestroyed, deployment.Reinforcements[0].MinimumTurn, deployment.Reinforcements[0].MaximumTurn, deployment.Reinforcements[0].ExecutionOdds,
            deployment.Reinforcements[0].MaximumRuns));
        Assert.Equal([2, 5], deployment.Reinforcements[0].Turns);
        Assert.Equal(("DEFAULTS", 0, 4, false, 0, -1, 100, -1), (deployment.Reinforcements[1].Type, deployment.Reinforcements[1].MinimumDifficulty, deployment.Reinforcements[1].MaximumDifficulty,
            deployment.Reinforcements[1].ObjectiveDestroyed, deployment.Reinforcements[1].MinimumTurn, deployment.Reinforcements[1].MaximumTurn, deployment.Reinforcements[1].ExecutionOdds,
            deployment.Reinforcements[1].MaximumRuns));
        Assert.Empty(deployment.Reinforcements[1].Turns);
        // The unit rule's energy recovery is every unit's energy regeneration; a soldier's is a third of its initial time units.
        Assert.All(battle.Units.Where(static unit => unit.UnitType == "ALIEN"), static unit => Assert.Equal(12, unit.EnergyRegeneration));
        Assert.Equal(Soldier(battle).EnergyRegeneration, campaign.Capture().Bases[0].Soldiers.Single(state => state.Id == Soldier(battle).SoldierId).Personal!.InitialStats["tu"] / 3);
        // The item's spawn values: chances default to specialChance.
        var items = SpawnRules(chance: 30);
        var spawning = Begun(items, "UFO_ARMORY").Campaign.Content.RuntimeRules.Battle.Equipment.Items["GRENADE"];
        Assert.Equal(("ALIEN", "CLIP", 1, 30, 30, true), (spawning.SpawnUnit, spawning.SpawnItem, spawning.SpawnUnitFaction, spawning.SpawnUnitChance,
            spawning.SpawnItemChance, spawning.Spawns));
        var plain = Begun(Rules, "UFO_ARMORY").Campaign.Content.RuntimeRules.Battle.Equipment.Items["GRENADE"];
        Assert.Equal(("", "", -1, 100, 100, false), (plain.SpawnUnit, plain.SpawnItem, plain.SpawnUnitFaction, plain.SpawnUnitChance, plain.SpawnItemChance, plain.Spawns));
    }

    [Fact]
    public void ABriefingGoesThroughItsStepsInOrderAndAnythingElseIsRefused()
    {
        var (campaign, battle) = Begun();
        Assert.Equal("battle.not-before-first-turn", Refusal(campaign, new StartFirstTurn()));
        // The preview is a battle mode of its own.
        Assert.Equal("battle.in-progress", Refusal(campaign, new StartBattle()));
        Assert.Equal(BattlePhase.Briefing, campaign.GetQuery<IBattleQuery>()!.QueryBattle(BattleViewer.LocalPlayer)!.Phase);

        Assert.Equal(new BriefingConfirmed(0, 0), Event<BriefingConfirmed>(campaign, new ConfirmBriefing()));
        Assert.Equal(BattlePhase.Inventory, battle.Phase);
        Assert.Equal("battle.not-in-briefing", Refusal(campaign, new ConfirmBriefing()));
        Assert.Equal("battle.not-in-briefing", Refusal(campaign, new BeginBriefing("a", "b")));
        Assert.Equal(0, battle.Turn);

        Assert.Equal(new BattleFirstTurnStarted(battle.SelectedUnitId!.Value) { LayoutsSaved = 1 }, Event<BattleFirstTurnStarted>(campaign, new StartFirstTurn()));
        Assert.Equal((1, BattlePhase.InProgress), (battle.Turn, battle.Phase));
        // Played through: no briefing any more, and the first turn is not started twice.
        Assert.Null(campaign.GetQuery<IBattleBriefingQuery>()!.QueryBriefing());
        Assert.Equal("battle.not-before-first-turn", Refusal(campaign, new StartFirstTurn()));
        Assert.Equal("battle.not-in-briefing", Refusal(campaign, new ConfirmBriefing()));
    }

    [Fact]
    public void ThePreviewIsRefusedAtBattleStartAndNothingChanges()
    {
        var (campaign, _) = Requested(Rules);
        var before = campaign.Capture();
        Assert.Equal("battle.preview-unsupported", Refusal(campaign, new StartBattle(Preview: true)));
        Assert.Equivalent(before, campaign.Capture(), strict: true);
        Assert.NotNull(campaign.GetQuery<ICampaignLandingQuery>()!.QueryDeploymentRequest());
        Started(campaign);
    }

    [Fact]
    public void PrimedSpawnItemsSpawnTheirUnitAndItemAndLeaveTheGameWhateverTheChance()
    {
        var (campaign, battle) = Begun(SpawnRules(), "UFO_ARMORY");
        var primed = battle.Items.Where(static item => item.ItemType == "GRENADE" && item.Tile is not null).ToArray();
        Assert.Equal([0, 0], primed.Select(static item => item.FuseTimer));
        Assert.All(primed, static item => Assert.True(item.FuseEnabled));
        var aliens = battle.Units.Count(static unit => unit.Faction == BattleFaction.Hostile);
        var clips = battle.Items.Count(static item => item.ItemType == "CLIP" && item.Tile is not null);
        var nextItem = battle.NextItemId;
        var nextUnit = battle.Units[^1].Id + 1;
        var tiles = primed.Select(static item => item.Tile!.Value).ToArray();

        Assert.Equal(new BriefingConfirmed(2, 2), Event<BriefingConfirmed>(campaign, new ConfirmBriefing()));
        Assert.DoesNotContain(battle.Items, static item => item.ItemType == "GRENADE" && item.Tile is not null);
        Assert.Equal(aliens + 2, battle.Units.Count(static unit => unit.Faction == BattleFaction.Hostile));
        Assert.Equal(clips + 2, battle.Items.Count(static item => item.ItemType == "CLIP" && item.Tile is not null));
        // createTempUnit: the next unit ID after the last; the spawned aliens stand on or beside the grenade, hidden, with no time units.
        var spawned = battle.Units.Where(unit => unit.Id >= nextUnit).ToArray();
        Assert.Equal([nextUnit, nextUnit + 1], spawned.Select(static unit => unit.Id));
        Assert.All(spawned, unit =>
        {
            Assert.Equal((BattleFaction.Hostile, BattleFaction.Hostile, "ALIEN", 0), (unit.Faction, unit.OriginalFaction, unit.UnitType, unit.TimeUnits));
            Assert.False(unit.IsSpottedBy(BattleFaction.Player));
            Assert.NotNull(unit.Ai);
            Assert.Contains(tiles, tile => Math.Abs(tile.X - unit.Position!.Value.X) <= 1 && Math.Abs(tile.Y - unit.Position!.Value.Y) <= 1);
            Assert.Equal(unit.Position, battle.Map.PositionOf(battle.Map.TileIndex(unit.Position!.Value)));
            Assert.Equal(unit.Id, battle.Map.UnitAt(battle.Map.TileIndex(unit.Position!.Value)));
        });
        // The spawned clips lie on the grenades' tiles and remember no owner; the grenades' IDs stay used.
        var newClips = battle.Items.Where(item => item.Id >= nextItem && item.ItemType == "CLIP").ToArray();
        Assert.Equal(2, newClips.Length);
        Assert.Equal(tiles, newClips.Select(static item => item.Tile!.Value));
        Assert.All(newClips, static item => Assert.Null(item.PreviousOwnerUnitId));
        battle.Validate();
    }

    [Fact]
    public void ASpawnThatFailsItsChanceStillRemovesThePrimedItem()
    {
        var (campaign, battle) = Begun(SpawnRules(chance: 0), "UFO_ARMORY");
        var units = battle.Units.Count;
        var random = campaign.Random.State;
        Assert.Equal(new BriefingConfirmed(0, 0), Event<BriefingConfirmed>(campaign, new ConfirmBriefing()));
        Assert.Equal(units, battle.Units.Count);
        Assert.DoesNotContain(battle.Items, static item => item.ItemType == "GRENADE" && item.Tile is not null);
        // Each of the four chances (a unit and an item per grenade) drew once.
        var expected = new SplitMix64RandomSource(random);
        for (var draw = 0; draw < 4; draw++) expected.NextInclusive(0, 99);
        Assert.Equal(expected.State, campaign.Random.State);
    }

    [Fact]
    public void OnlyGrenadesPrimedForTheStartSpawn()
    {
        // A fuse still running (3) and an unprimed grenade (-1) spawn nothing and stay.
        foreach (var fuse in new[] { "3", "-1" })
        {
            var (campaign, battle) = Begun(SpawnRules(fuse: fuse), "UFO_ARMORY");
            var items = battle.Items.Count;
            Assert.Equal(new BriefingConfirmed(0, 0), Event<BriefingConfirmed>(campaign, new ConfirmBriefing()));
            Assert.Equal(items, battle.Items.Count);
        }
        // An X-COM grenade spawns nothing, and neither does one without a spawn rule.
        var (xcom, owned) = Begun(SpawnRules(), "UFO_ARMORY");
        foreach (var item in owned.Items.Where(static item => item.ItemType == "GRENADE")) item.XcomProperty = true;
        Assert.Equal(new BriefingConfirmed(0, 0), Event<BriefingConfirmed>(xcom, new ConfirmBriefing()));
        Assert.Equal(2, owned.Items.Count(static item => item.ItemType == "GRENADE"));
        var (plain, unruled) = Begun(Rules.Replace("fuseTimers: {GRENADE: [3, 3]}", "fuseTimers: {GRENADE: [0, 0]}", StringComparison.Ordinal), "UFO_ARMORY");
        Assert.Equal(new BriefingConfirmed(0, 0), Event<BriefingConfirmed>(plain, new ConfirmBriefing()));
        Assert.Equal(2, unruled.Items.Count(static item => item.ItemType == "GRENADE"));
    }

    [Fact]
    public void ASpawnOnATileWithoutAFloorIsRefusedBeforeAnyChange()
    {
        var (campaign, battle) = Begun(SpawnRules(), "UFO_ARMORY");
        var tile = battle.Items.First(static item => item.ItemType == "GRENADE" && item.Tile is not null).Tile!.Value;
        var index = battle.Map.TileIndex(tile);
        var (floorId, floorSet) = battle.Map.Part(index, BattleTilePart.Floor);
        battle.Map.SetPart(index, BattleTilePart.Floor, -1, -1);
        var before = (campaign.Capture(), campaign.Random.State);
        Assert.Equal("battle.spawn-gravity-unsupported", Refusal(campaign, new ConfirmBriefing()));
        Assert.Equal(before.Item2, campaign.Random.State);
        Assert.Equal(BattlePhase.Briefing, battle.Phase);
        battle.Map.SetPart(index, BattleTilePart.Floor, floorId, floorSet);
    }

    private static void KillTheAliens(BattleState battle)
    {
        foreach (var unit in battle.Units.Where(static unit => unit.OriginalFaction == BattleFaction.Hostile)) unit.Status = BattleUnitStatus.Dead;
    }

    [Fact]
    public void ABattleWhoseOnlyLiveAliensComeFromPrimedSpawnsIsPlayed()
    {
        // BriefingState::btnOkClick spawns from the primed items first and counts the aliens after.
        var (campaign, battle) = Begun(SpawnRules(), "UFO_ARMORY");
        KillTheAliens(battle);
        Assert.Equal(0, battle.Tally().LiveAliens);
        Assert.Equal(new BriefingConfirmed(2, 2), Event<BriefingConfirmed>(campaign, new ConfirmBriefing()));
        Assert.Equal(2, battle.Tally().LiveAliens);
        Assert.Equal(BattlePhase.Inventory, battle.Phase);
    }

    [Fact]
    public void ABattleWithoutLiveAliensAfterItsSpawnsIsRefusedBeforeAnyChange()
    {
        // No alien alive and no spawn that could add one: nothing spawns at all, or the spawn is X-COM's, or its chance is 0.
        foreach (var rules in new[] { Rules.Replace("fuseTimers: {GRENADE: [3, 3]}", "fuseTimers: {GRENADE: [0, 0]}", StringComparison.Ordinal),
                     SpawnRules(faction: 0), SpawnRules(faction: 2), SpawnRules(chance: 0) })
        {
            var (campaign, battle) = Begun(rules, "UFO_ARMORY");
            KillTheAliens(battle);
            var before = (campaign.Random.State, battle.Units.Count, battle.Items.Count);
            Assert.Equal("battle.no-live-aliens", Refusal(campaign, new ConfirmBriefing()));
            Assert.Equal(before, (campaign.Random.State, battle.Units.Count, battle.Items.Count));
            Assert.Equal(BattlePhase.Briefing, battle.Phase);
        }
        // A spawn that might add an alien but does not (one chance in a hundred): the trial spawn on a copy shows it, the battle is refused
        // and the stream is as it was; when the draw does succeed the battle is played.
        var refused = 0;
        for (ulong seed = 1; seed <= 12; seed++)
        {
            var (campaign, battle) = Begun(SpawnRules(chance: 1), "UFO_ARMORY");
            KillTheAliens(battle);
            campaign.Random.Restore(seed);
            var before = (campaign.Random.State, battle.Units.Count, battle.Items.Count(static item => item.ItemType == "GRENADE" && item.Tile is not null));
            var events = campaign.Execute(new ConfirmBriefing()).Events;
            if (events.OfType<CampaignActionBlocked>().SingleOrDefault() is { } blocked)
            {
                refused++;
                Assert.Equal("battle.no-live-aliens", blocked.Code);
                Assert.Equal(before, (campaign.Random.State, battle.Units.Count, battle.Items.Count(static item => item.ItemType == "GRENADE" && item.Tile is not null)));
                Assert.Equal(BattlePhase.Briefing, battle.Phase);
            }
            else
            {
                Assert.True(battle.Tally().LiveAliens > 0);
            }
        }
        Assert.True(refused > 0);
    }

    [Fact]
    public void AUnitPastTheOutThresholdIsACasualtyTheBattleDoesNotProcessYet()
    {
        // checkForCasualties takes out a unit that is not dead with no health, or not unconscious with the stun at its health,
        // whatever its spawn unit; an unconscious unit with no health left is still one.
        foreach (var make in new Action<BattleUnit>[]
        {
            static unit => unit.Health = 0,
            static unit => unit.StunLevel = unit.Health,
            static unit => { unit.Status = BattleUnitStatus.Unconscious; unit.Health = 0; },
            static unit => { unit.SpawnUnit = "ALIEN"; unit.Health = 0; },
        })
        {
            var (campaign, battle) = Begun();
            Assert.False(battle.HasUncheckedCasualty());
            make(battle.Units.First(static unit => unit.OriginalFaction == BattleFaction.Hostile));
            Assert.True(battle.HasUncheckedCasualty());
            Assert.Equal("battle.casualties-unsupported", Refusal(campaign, new ConfirmBriefing()));
        }
        // A unit that is out already (dead, or unconscious with the stun over its health) and a removed unit are not casualties.
        var (second, other) = Begun();
        var victim = other.Units.First(static unit => unit.OriginalFaction == BattleFaction.Hostile);
        victim.Health = 0;
        victim.Status = BattleUnitStatus.Dead;
        Assert.False(other.HasUncheckedCasualty());
        victim.Status = BattleUnitStatus.IgnoreMe;
        Assert.False(other.HasUncheckedCasualty());
        victim.Health = 10;
        victim.StunLevel = 12;
        victim.Status = BattleUnitStatus.Unconscious;
        Assert.False(other.HasUncheckedCasualty());
        Assert.IsType<BriefingConfirmed>(Assert.Single(second.Execute(new ConfirmBriefing()).Events));
    }

    [Fact]
    public void ARunningBattleOpenedWithACasualtyRefusesItsCommandsByName()
    {
        // The battlescape's constructor runs checkForCasualties for a loaded battle too; branch 6 ports it, so until then the
        // battle's own commands refuse by name and nothing changes.
        var campaign = ToInventory();
        Event<BattleFirstTurnStarted>(campaign, new StartFirstTurn());
        var battle = Battle(campaign);
        Assert.Null(campaign.Execute(new SelectNextUnit(battle.SelectedUnitId!.Value, false)).Events.OfType<CampaignActionBlocked>().FirstOrDefault());
        battle.Units.First(static unit => unit.OriginalFaction == BattleFaction.Hostile).Health = 0;
        var before = campaign.Random.State;
        Assert.Equal("battle.casualties-unsupported", Refusal(campaign, new SelectNextUnit(battle.SelectedUnitId!.Value, false)));
        Assert.Equal(before, campaign.Random.State);
    }

    [Fact]
    public void ASpawnBesideATileWithoutAFloorIsNotRefusedWhereGravityActsOnNothing()
    {
        // applyGravity acts on the tile the unit takes, not on its surroundings: the neighbours of a floored spawn may be open air.
        var (campaign, battle) = Begun(SpawnRules(), "UFO_ARMORY");
        var grenades = battle.Items.Where(static item => item.ItemType == "GRENADE" && item.Tile is not null).ToArray();
        var rules = campaign.Content.RuntimeRules;
        // One spawner only, so that no other spawn could take the neighbour's place.
        grenades[1].XcomProperty = true;
        Assert.False(battle.SpawnNeedsGravity(rules));
        var tile = grenades[0].Tile!.Value;
        var neighbour = tile with { X = tile.X + 2 };
        battle.Map.SetPart(battle.Map.TileIndex(neighbour), BattleTilePart.Floor, -1, -1);
        Assert.False(battle.SpawnNeedsGravity(rules));
        // Two spawners close together each take their own tile, so the floorless tile two away still counts for nothing.
        grenades[1].XcomProperty = false;
        grenades[1].Tile = tile with { X = tile.X + 1 };
        Assert.False(battle.SpawnNeedsGravity(rules));
        // Two on one tile: the second takes the first free position, the one to the north, which has no floor.
        grenades[1].Tile = tile;
        var north = tile with { Y = tile.Y - 1 };
        Assert.False(battle.SpawnNeedsGravity(rules));
        battle.Map.SetPart(battle.Map.TileIndex(north), BattleTilePart.Floor, -1, -1);
        Assert.True(battle.SpawnNeedsGravity(rules));
        grenades[1].XcomProperty = true;
        // The unit's own tile above level 0 without a floor drops its items, and the same tile with a floor does not.
        var upper = tile with { Z = 1 };
        if (battle.Map.Contains(upper))
        {
            grenades[0].Tile = upper;
            Assert.True(battle.SpawnNeedsGravity(rules) == battle.Map.HasNoFloor(upper));
        }
    }

    [Fact]
    public void ASpawnWithoutAlienItemLevelsIsRefusedAsBrokenContent()
    {
        var (campaign, battle) = Begun(SpawnRules(), "UFO_ARMORY");
        battle.AlienItemLevel = 5;
        Assert.Equal("battle.generation-failed", Refusal(campaign, new ConfirmBriefing()));
        Assert.Equal(2, battle.Items.Count(static item => item.ItemType == "GRENADE" && item.Tile is not null));
    }

    [Fact]
    public void AReinforcementWaveThatCouldDeployAtTheStartIsRefusedAndOthersAreNot()
    {
        string Waves(string wave) => Rules.Replace("""
              - type: UFO_SCOUT
                width: 20
            """.Replace("\r\n", "\n", StringComparison.Ordinal), $"""
              - type: UFO_SCOUT
                reinforcements: [{wave}]
                width: 20
            """.Replace("\r\n", "\n", StringComparison.Ordinal), StringComparison.Ordinal);

        // Waves that cannot run now: later turns, a turn list without 0, another difficulty, an objective not destroyed.
        foreach (var wave in new[]
        {
            "{type: LATER, minTurn: 3}", "{type: LIST, turns: [2, 4]}", "{type: HARD, minDifficulty: 4}", "{type: EASY, maxDifficulty: -1}",
            "{type: OBJECTIVE, objectiveDestroyed: true}",
            "{type: NEVER, maxRuns: 0}",
        })
        {
            var (campaign, _) = Begun(Waves(wave));
            Assert.IsType<BriefingConfirmed>(Assert.Single(campaign.Execute(new ConfirmBriefing()).Events));
        }
        // Waves that would pass at turn 0: no limits, a turn list with 0, a range holding 0, whatever their odds or runs.
        foreach (var wave in new[]
        {
            "{type: NOW}", "{type: LIST, turns: [0, 3]}", "{type: RANGE, minTurn: 0, maxTurn: 2, executionOdds: 10}", "{type: ONCE, maxRuns: 1}", "{type: ODDS, maxRuns: 0, executionOdds: 10}",
        })
        {
            var (campaign, battle) = Begun(Waves(wave));
            var state = campaign.Random.State;
            Assert.Equal("battle.reinforcements-unsupported", Refusal(campaign, new ConfirmBriefing()));
            Assert.Equal((state, BattlePhase.Briefing), (campaign.Random.State, battle.Phase));
        }
        // A destroyed objective releases an objective wave.
        var (objective, goal) = Begun(Waves("{type: OBJECTIVE, objectiveDestroyed: true}"));
        goal.ObjectiveCount = 1;
        goal.ObjectivesDestroyed = 1;
        Assert.Equal("battle.reinforcements-unsupported", Refusal(objective, new ConfirmBriefing()));
    }

    [Fact]
    public void BugHuntModeStartsWhenFewHiddenPowerlessAliensAreLeft()
    {
        // The deployment's minimum turn is 1; the aliens (UFO_ALIENS: two, with a gun and a clip) are 50 time units strong.
        var rules = Rules.Replace("  - type: UFO_ALIENS\n    width", "  - type: UFO_ALIENS\n    bughuntMinTurn: 1\n    width", StringComparison.Ordinal);
        bool Hunt(Action<BattleState>? adjust = null, string? ruleText = null)
        {
            var (campaign, battle) = Begun(ruleText ?? rules, "UFO_ALIENS");
            Assert.Equal((false, ruleText is null ? 1 : 999), (battle.BughuntMode, battle.BughuntMinTurn));
            foreach (var alien in battle.Units.Where(static unit => unit.OriginalFaction == BattleFaction.Hostile)) alien.Rank = 3;
            adjust?.Invoke(battle);
            Event<BriefingConfirmed>(campaign, new ConfirmBriefing());
            return battle.BughuntMode;
        }

        IEnumerable<BattleUnit> Aliens(BattleState battle) => battle.Units.Where(static unit => unit.OriginalFaction == BattleFaction.Hostile);

        // Armed, but with more than 60% of the rule's 50 time units: powerless; all hidden, no leaders, two enemies at most.
        Assert.True(Hunt(battle => Assert.All(Aliens(battle), alien => Assert.Contains(battle.InventoryOf(alien), static item => item.Slot == "STR_RIGHT_HAND"))));
        Assert.True(Hunt(battle => Assert.All(Aliens(battle), static alien => alien.Morale = 39)));
        Assert.True(Hunt(battle => Assert.All(Aliens(battle), static alien => alien.TimeUnits = 31)));
        // One fit alien (armed, morale 40 or more, 30 time units or fewer) keeps the hunt off, as does a visible one, a leader or a third enemy.
        Assert.False(Hunt(battle => Aliens(battle).First().TimeUnits = 30));
        Assert.False(Hunt(battle => Aliens(battle).First().SetSpottedBy(BattleFaction.Player, true)));
        Assert.False(Hunt(battle => Aliens(battle).First().Rank = 0));
        Assert.False(Hunt(battle =>
        {
            foreach (var alien in Aliens(battle).ToArray())
            {
                var copy = new BattleUnit(alien.Id + 500, BattleFaction.Hostile, BattleController.Ai) { Stats = alien.Stats, Rank = 3, Health = 10 };
                battle.AddUnit(copy);
            }
        }));
        // Out units are not counted, and an unarmed alien is powerless whatever its time units.
        Assert.True(Hunt(battle =>
        {
            foreach (var item in battle.Items.Where(static item => item.Slot is "STR_RIGHT_HAND" or "STR_LEFT_HAND")) item.Slot = "STR_BELT";
            Aliens(battle).First().TimeUnits = 1;
        }));
        // The mod's minimum turn (999 without the deployment's) is far away.
        Assert.False(Hunt(ruleText: Rules));
    }

    [Fact]
    public void TheFirstTurnStartsTheXcomSideAndRefreshesItsUnits()
    {
        var campaign = ToInventory();
        var battle = Battle(campaign);
        var soldier = Soldier(battle);
        var aliens = battle.Units.Where(static unit => unit.OriginalFaction == BattleFaction.Hostile).Select(static unit => (unit.Id, unit.TimeUnits)).ToArray();
        soldier.DontReselect = true;
        soldier.AiMedikitUsed = true;
        soldier.MotionPoints = 7;
        soldier.IsSurrendering = true;
        soldier.MeleeAttackedBy.Add(99);
        soldier.Kneeled = true;
        soldier.TurnsSinceStunned = 4;
        soldier.TimeUnits = 10;
        soldier.Energy = 1;
        var maximum = soldier.Stats;

        Event<BattleFirstTurnStarted>(campaign, new StartFirstTurn());
        Assert.Equal((1, BattleFaction.Player, BattlePhase.InProgress), (battle.Turn, battle.Side, battle.Phase));
        Assert.Equal((false, false, 0, false, false, 5), (soldier.DontReselect, soldier.AiMedikitUsed, soldier.MotionPoints, soldier.IsSurrendering,
            soldier.Kneeled, soldier.TurnsSinceStunned));
        Assert.Empty(soldier.MeleeAttackedBy);
        // The default recovery gives the unit's time units back (up to its maximum, less encumbrance) and its energy regeneration.
        var weight = new UnitEquipment(battle, campaign.Content.RuntimeRules, battle.Seedless).CarriedWeight(soldier);
        var encumbrance = maximum.Strength / (float)weight;
        var expectedTime = Math.Min(maximum.TimeUnits, 10 + maximum.TimeUnits);
        if (encumbrance < 1) expectedTime = (int)(encumbrance * expectedTime);
        Assert.Equal((expectedTime, Math.Min(maximum.Stamina, 1 + soldier.EnergyRegeneration)), (soldier.TimeUnits, soldier.Energy));
        // The aliens are not prepared by the first turn.
        Assert.Equal(aliens, battle.Units.Where(static unit => unit.OriginalFaction == BattleFaction.Hostile).Select(static unit => (unit.Id, unit.TimeUnits)));
        Assert.Equal(1, campaign.GetQuery<IBattleQuery>()!.QueryBattle(BattleViewer.LocalPlayer)!.Turn);
    }

    [Fact]
    public void EncumbranceAndLegWoundsCutTheRecoveredTimeUnits()
    {
        var campaign = ToInventory();
        var battle = Battle(campaign);
        var soldier = Soldier(battle);
        var weight = new UnitEquipment(battle, campaign.Content.RuntimeRules, battle.Seedless).CarriedWeight(soldier);
        Assert.True(weight > 0);
        // A unit that carries more than its strength lets: strength 1 against the weight.
        soldier.Stats = soldier.Stats with { Strength = 1 };
        soldier.TimeUnits = 0;
        soldier.FatalWounds[4] = 1;
        soldier.FatalWounds[5] = 1;
        var maximum = soldier.Stats.TimeUnits;
        var recovered = (int)(1f / weight * maximum);
        Event<BattleFirstTurnStarted>(campaign, new StartFirstTurn());
        // Two leg wounds: 20% off.
        Assert.Equal(recovered - recovered * 20 / 100, soldier.TimeUnits);
    }

    [Fact]
    public void UnitsThatAreOutOrIgnoredAndAControlledUnitSkipThePreparation()
    {
        var campaign = ToInventory();
        var battle = Battle(campaign);
        var template = Soldier(battle);
        BattleUnit Extra(int id, BattleUnitStatus status, BattleFaction faction = BattleFaction.Player, BattleFaction original = BattleFaction.Player)
        {
            var unit = new BattleUnit(id, faction, BattleController.Human(0)) { Stats = template.Stats, ArmorId = template.ArmorId, UnitType = template.UnitType };
            unit.OriginalFaction = original;
            unit.Status = status;
            unit.TimeUnits = 5;
            unit.Energy = 5;
            unit.DontReselect = true;
            battle.AddUnit(unit);
            return unit;
        }

        var dead = Extra(900, BattleUnitStatus.Dead);
        var unconscious = Extra(901, BattleUnitStatus.Unconscious);
        var ignored = Extra(902, BattleUnitStatus.IgnoreMe);
        // A mind-controlled soldier returns to its side and recovers nothing.
        var controlled = Extra(903, BattleUnitStatus.Standing, BattleFaction.Hostile);
        controlled.Ai = new BattleAiState(BattleFaction.Hostile);
        // Unconscious units over the out threshold neither wake nor skip: give it none to wake.
        unconscious.Health = 0;
        Event<BattleFirstTurnStarted>(campaign, new StartFirstTurn());
        Assert.All(new[] { dead, unconscious, ignored }, unit => Assert.Equal((5, 5, true), (unit.TimeUnits, unit.Energy, unit.DontReselect)));
        Assert.Equal((BattleFaction.Player, 5, 5, false, null), (controlled.Faction, controlled.TimeUnits, controlled.Energy, controlled.DontReselect,
            controlled.Ai));
    }

    [Fact]
    public void TheUnitStandingOnTheInventoryTileIsSelectedAfterTheTilesAreReset()
    {
        var campaign = ToInventory();
        var battle = Battle(campaign);
        var first = Soldier(battle);
        var template = first;
        var second = new BattleUnit(901, BattleFaction.Player, BattleController.Human(0)) { Stats = template.Stats, ArmorId = template.ArmorId, UnitType = template.UnitType };
        battle.AddUnit(second);
        // Two units claim one tile; resetUnitTiles gives it to the later one, and the selection follows.
        second.Position = first.Position;
        Assert.Equal(first.Id, battle.SelectedUnitId);
        Event<BattleFirstTurnStarted>(campaign, new StartFirstTurn());
        Assert.Equal(901, battle.SelectedUnitId);
        Assert.Equal(901, battle.Map.UnitAt(battle.Map.TileIndex(first.Position!.Value)));
    }

    [Fact]
    public void TheInventoryTilesGroundItemsGoToSeededStorageTilesAndTheTileLeavesTheStorageSpace()
    {
        var campaign = ToInventory();
        var battle = Battle(campaign);
        var pile = Soldier(battle).Position!.Value;
        battle.CraftInventoryTile = pile;
        var pileItems = battle.Items.Where(item => item.Tile == pile).ToArray();
        if (pileItems.Length == 0)
        {
            foreach (var type in new[] { "GUN", "CLIP", "GRENADE" }) new UnitEquipment(battle, campaign.Content.RuntimeRules, battle.Seedless).CreateItemForTile(type, pile);
            pileItems = [.. battle.Items.Where(item => item.Tile == pile)];
        }
        var storage = new[] { new BattlePosition(1, 1, 0), pile, new BattlePosition(2, 2, 0), new BattlePosition(3, 3, 0) };
        battle.StorageSpace = storage;
        var elsewhere = battle.Items.Where(item => item.Tile is not null && item.Tile != pile).Select(static item => (item.Id, item.Tile)).ToArray();

        var expected = new SplitMix64RandomSource(campaign.Random.State);
        var remaining = storage.Where(position => position != pile).ToArray();
        var destinations = pileItems.Select(_ => remaining[expected.NextInclusive(0, remaining.Length - 1)]).ToArray();

        Event<BattleFirstTurnStarted>(campaign, new StartFirstTurn());
        Assert.Equal(remaining, battle.StorageSpace);
        Assert.Equal(destinations, pileItems.Select(static item => item.Tile!.Value));
        Assert.Equal(expected.State, campaign.Random.State);
        Assert.Equal(elsewhere, battle.Items.Where(item => elsewhere.Any(other => other.Id == item.Id)).Select(static item => (item.Id, item.Tile)));
    }

    [Fact]
    public void WithAUnitOffThePileSelectedTheFirstTurnScattersThePileAndSelectsTheUnitOnIt()
    {
        var campaign = ToInventory();
        var battle = Battle(campaign);
        var first = Soldier(battle);
        var pile = first.Position!.Value;
        Assert.Equal((pile, pile), (battle.CraftInventoryTile, first.InventoryTile));
        // The player tabbed to a second soldier that stands beside the pile; its tile before the game is the pile too.
        var second = new BattleUnit(901, BattleFaction.Player, BattleController.Human(0))
        {
            Stats = first.Stats,
            ArmorId = first.ArmorId,
            UnitType = first.UnitType,
            Position = pile with { X = pile.X + 1 },
            InventoryTile = pile,
        };
        battle.AddUnit(second);
        battle.SelectedUnitId = second.Id;
        var equipment = new UnitEquipment(battle, campaign.Content.RuntimeRules, battle.Seedless);
        BattleItem[] pileItems = [equipment.CreateItemForTile("GUN", pile), equipment.CreateItemForTile("CLIP", pile)];
        var storage = new BattlePosition(1, 1, 0);
        battle.StorageSpace = [pile, storage];

        Assert.Equal(first.Id, Event<BattleFirstTurnStarted>(campaign, new StartFirstTurn()).SelectedUnitId);
        Assert.Equal([storage], battle.StorageSpace);
        Assert.All(pileItems, item => Assert.Equal(storage, item.Tile));
        Assert.Equal(((BattlePosition?)null, (BattlePosition?)null), (first.InventoryTile, second.InventoryTile));
        Assert.Equal(second.Id, battle.Map.UnitAt(battle.Map.TileIndex(second.Position!.Value)));
    }

    [Fact]
    public void ASpawnedUnitOrItemWithANewTurnScriptRefusesTheBriefingsConfirmationAndChangesNothing()
    {
        // Rules only the spawn uses: the battle had none of them before its primed grenades spawn.
        var armor = "\narmors:\n  - type: SPAWN_ARMOR\n    size: 1\n    allowInv: false\n    corpseBattle: [CORPSE]\n    corpseGeo: CORPSE\n    loftempsSet: [0]\n" +
            "    scripts:\n      newTurnUnit: |\n        return;\n";
        var unit = SpawnRules(unit: "SPAWNED").Replace("\narmors:\n", armor, StringComparison.Ordinal)
            .Replace("\nunits:\n", "\nunits:\n  - type: SPAWNED\n    armor: SPAWN_ARMOR\n    stats: {tu: 50}\n", StringComparison.Ordinal);
        var item = SpawnRules().Replace("spawnItem: CLIP", "spawnItem: SPAWN_CLIP", StringComparison.Ordinal)
            .Replace("\nitems:\n", "\nitems:\n  - type: SPAWN_CLIP\n    battleType: 2\n    scripts:\n      newTurnItem: |\n        return;\n", StringComparison.Ordinal);
        foreach (var rules in new[] { unit, item })
        {
            Assert.NotEqual(SpawnRules(), rules);
            var (campaign, battle) = Begun(rules, "UFO_ARMORY");
            Event<BriefingOpened>(campaign, new BeginBriefing("UFO-1", "SHIP"));
            var before = (campaign.Capture(), campaign.Random.State);
            Assert.Equal("battle.new-turn-script-unsupported", Refusal(campaign, new ConfirmBriefing()));
            Assert.Equal(before.Item2, campaign.Random.State);
            Assert.Equal(BattlePhase.Briefing, battle.Phase);
            Assert.Equivalent(before.Item1, campaign.Capture(), strict: true);
        }
        // The same spawn without the scripts confirms.
        var (plain, _) = Begun(SpawnRules(), "UFO_ARMORY");
        Event<BriefingConfirmed>(plain, new ConfirmBriefing());
    }

    [Fact]
    public void WithoutAStorageSpaceTheGroundItemsStayAndNoDrawIsSpent()
    {
        var campaign = ToInventory();
        var battle = Battle(campaign);
        var tiles = battle.Items.Select(static item => item.Tile).ToArray();
        var random = campaign.Random.State;
        Event<BattleFirstTurnStarted>(campaign, new StartFirstTurn());
        Assert.Equal(tiles, battle.Items.Select(static item => item.Tile));
        Assert.Equal(random, campaign.Random.State);
    }

    [Fact]
    public void ABattleWithoutLiveSoldiersDoesNotStart()
    {
        var campaign = ToInventory();
        var battle = Battle(campaign);
        Soldier(battle).Status = BattleUnitStatus.Dead;
        Assert.Equal("battle.no-live-soldiers", Refusal(campaign, new StartFirstTurn()));
        Assert.Equal((0, BattlePhase.Inventory), (battle.Turn, battle.Phase));
    }

    [Fact]
    public void ABoundNewTurnScriptRefusesTheBriefingsConfirmationAndChangesNothing()
    {
        foreach (var (section, script) in new[]
        {
            ("armors", "  - type: SMALL_VEHICLE_ARMOR\n    scripts:\n      newTurnUnit: |\n        return;"),
            ("items", "  - type: GUN\n    scripts:\n      newTurnItem: |\n        return;"),
        })
        {
            var rules = Rules.Replace($"\n{section}:\n", $"\n{section}:\n{script}\n", StringComparison.Ordinal);
            Assert.NotEqual(Rules, rules);
            var (campaign, battle) = Begun(rules, "UFO_ALIENS");
            var before = (campaign.Capture(), campaign.Random.State);
            Assert.Equal("battle.new-turn-script-unsupported", Refusal(campaign, new ConfirmBriefing()));
            Assert.Equal(before.Item2, campaign.Random.State);
            Assert.Equal(BattlePhase.Briefing, battle.Phase);
            Assert.Equivalent(before.Item1, campaign.Capture(), strict: true);
            // The first turn checks the same, should a script bind after the confirmation.
            battle.EnterInventory(campaign.Content.RuntimeRules);
            Assert.Equal("battle.new-turn-script-unsupported", Refusal(campaign, new StartFirstTurn()));
            Assert.Equal(0, battle.Turn);
        }
    }

    [Fact]
    public void ABoundNewTurnScriptOnAnUnusedRuleDoesNotStopTheBattle()
    {
        var rules = Rules.Replace("\narmors:\n", "\narmors:\n  - type: UNUSED_ARMOR\n    corpseBattle: [CORPSE]\n    corpseGeo: CORPSE\n    loftempsSet: [0]\n    scripts:\n      newTurnUnit: |\n        return;\n", StringComparison.Ordinal);
        Assert.NotEqual(Rules, rules);
        Event<BattleFirstTurnStarted>(ToInventory(rules), new StartFirstTurn());
    }

    [Fact]
    public void AScriptedRecoveryRefusesTheBriefingsConfirmation()
    {
        var (_, probed) = Begun();
        var armor = Soldier(probed).ArmorId;
        var rules = Rules.Replace("\narmors:\n", $"\narmors:\n  - type: {armor}\n    recovery: {{time: {{tu: 0.5}}}}\n", StringComparison.Ordinal);
        Assert.NotEqual(Rules, rules);
        var (campaign, battle) = Begun(rules);
        Assert.Equal("battle.turn-recovery-script-unsupported", Refusal(campaign, new ConfirmBriefing()));
        Assert.Equal(BattlePhase.Briefing, battle.Phase);
    }

    [Fact]
    public void AnUnconsciousUnitThatWouldWakeOrAnInfectedUnitRefusesTheFirstTurn()
    {
        var campaign = ToInventory();
        var battle = Battle(campaign);
        var alien = battle.Units.First(static unit => unit.OriginalFaction == BattleFaction.Hostile);
        alien.Respawn = true;
        Assert.Equal("battle.turn-one-revive-unsupported", Refusal(campaign, new StartFirstTurn()));
        alien.Respawn = false;
        alien.Status = BattleUnitStatus.Unconscious;
        alien.Health = 10;
        alien.StunLevel = 3;
        Assert.Equal("battle.turn-one-revive-unsupported", Refusal(campaign, new StartFirstTurn()));
        // Still stunned out: it stays unconscious and the turn starts.
        alien.StunLevel = 10;
        Event<BattleFirstTurnStarted>(campaign, new StartFirstTurn());
    }

    [Fact]
    public void ABattleSavedAfterItsFirstTurnReloadsIntoTheBattlescapeWithoutABriefing()
    {
        var campaign = ToInventory();
        Event<BattleFirstTurnStarted>(campaign, new StartFirstTurn());
        Assert.Null(campaign.QuerySaveRefusal());
        var snapshot = campaign.Capture();
        Assert.Equal(1, snapshot.Battle!.Turn);
        var restored = CampaignState.Restore(snapshot, campaign.Content, new SplitMix64RandomSource(3), TerrainFiles());
        var overview = restored.GetQuery<IBattleQuery>()!.QueryBattle(BattleViewer.LocalPlayer)!;
        Assert.Equal((1, BattlePhase.InProgress), (overview.Turn, overview.Phase));
        Assert.Null(restored.GetQuery<IBattleBriefingQuery>()!.QueryBriefing());
        Assert.Equal("battle.not-in-briefing", Refusal(restored, new ConfirmBriefing()));
        Assert.Equal("battle.not-before-first-turn", Refusal(restored, new StartFirstTurn()));
    }

    [Fact]
    public void ABattleSavedBeforeItsFirstTurnReloadsIntoTheBriefing()
    {
        // The reference cannot save there; the port can, and the battle is briefed again (ADR 0039).
        var (campaign, _) = Begun();
        Event<BriefingOpened>(campaign, new BeginBriefing("UFO-1", "SHIP"));
        Assert.Null(campaign.QuerySaveRefusal());
        var restored = CampaignState.Restore(campaign.Capture(), campaign.Content, new SplitMix64RandomSource(3), TerrainFiles());
        var battle = Battle(restored);
        Assert.Equal((0, BattlePhase.Briefing, "UFO-1", "SHIP"), (battle.Turn, battle.Phase, battle.TargetName, battle.CraftOrBaseName));
        Assert.Equal("STR_UFO_GROUND_ASSAULT_BRIEFING", restored.GetQuery<IBattleBriefingQuery>()!.QueryBriefing()!.Description);
        Event<BriefingConfirmed>(restored, new ConfirmBriefing());
        Event<BattleFirstTurnStarted>(restored, new StartFirstTurn());
    }

    [Fact]
    public void AReloadedBriefingKeepsItsLinesWhateverTheClientSendsAfterTheLoad()
    {
        // The briefing's constructor runs once (ADR 0039): a client that opens the loaded briefing sends its lines again (empty ones,
        // after a load) and neither blanks the saved craft line nor draws another operation name.
        var rules = Rules + "\noperationNamesFirst: [ALPHA, BETA, GAMMA]\noperationNamesLast: [ONE, TWO]\n";
        var (campaign, battle) = Begun(rules);
        Event<BriefingOpened>(campaign, new BeginBriefing("UFO-1", "Craft: SHIP-1"));
        var named = battle.TargetName;
        var restored = CampaignState.Restore(campaign.Capture(), campaign.Content, new SplitMix64RandomSource(3), TerrainFiles());
        var before = restored.Random.State;
        Assert.Equal("battle.briefing-already-open", Refusal(restored, new BeginBriefing("", "")));
        Assert.Equal("battle.briefing-already-open", Refusal(restored, new BeginBriefing("OTHER", "Craft: OTHER")));
        var again = Battle(restored);
        Assert.Equal((named, "Craft: SHIP-1"), (again.TargetName, again.CraftOrBaseName));
        Assert.Equal(before, restored.Random.State);
        var briefing = restored.GetQuery<IBattleBriefingQuery>()!.QueryBriefing()!;
        Assert.Equal((named, "Craft: SHIP-1"), (briefing.TargetName, briefing.CraftOrBaseName));

        // A save from before the briefing named anything is still named by the first client that opens it.
        var (unnamed, _) = Begun(rules);
        var loaded = CampaignState.Restore(unnamed.Capture(), unnamed.Content, new SplitMix64RandomSource(3), TerrainFiles());
        Event<BriefingOpened>(loaded, new BeginBriefing("UFO-1", "Craft: SHIP-1"));
        Assert.Equal("Craft: SHIP-1", Battle(loaded).CraftOrBaseName);
    }

    [Fact]
    public void ACommandOfTheBattleIsNotHeldBackByTheBattleButOthersAre()
    {
        var (campaign, _) = Begun();
        Assert.Equal("battle.in-progress", Refusal(campaign, new RecallCraft(1, "SHIP", 1)));
        Assert.IsType<BriefingOpened>(Assert.Single(campaign.Execute(new BeginBriefing("a", "b")).Events));
    }

    [Fact]
    public void ASaveFromBeforeTheFirstTurnKeepsItsCraftAndBaseWhateverThePhase()
    {
        foreach (var phase in new[] { "before", "named", "inventory" })
        {
            var (campaign, battle) = Begun();
            if (phase != "before") Event<BriefingOpened>(campaign, new BeginBriefing("UFO-1", "SHIP"));
            if (phase == "inventory") Event<BriefingConfirmed>(campaign, new ConfirmBriefing());
            var restored = CampaignState.Restore(campaign.Capture(), campaign.Content, new SplitMix64RandomSource(3), TerrainFiles());
            var again = Battle(restored);
            Assert.Equal((BattlePhase.Briefing, true, battle.BriefingBaseId), (again.Phase, again.BriefingFromCraft, again.BriefingBaseId));
            Assert.True(again.BriefingBaseId >= 0);
            // The briefing is as the first one: the craft's destination is the target again.
            var fresh = Battle(restored);
            fresh.TargetName = "";
            fresh.BriefingOpened = false;
            Event<BriefingOpened>(restored, new BeginBriefing("UFO-2", "SHIP"));
            Assert.Equal("UFO-2", fresh.TargetName);
            Event<BriefingConfirmed>(restored, new ConfirmBriefing());
        }
    }

    [Fact]
    public void AUnitPastTheOutThresholdButNotOutRefusesTheConfirmation()
    {
        var (campaign, battle) = Begun();
        var alien = battle.Units.First(static unit => unit.OriginalFaction == BattleFaction.Hostile);
        alien.Health = 0;
        Assert.Equal("battle.casualties-unsupported", Refusal(campaign, new ConfirmBriefing()));
        Assert.Equal(BattlePhase.Briefing, battle.Phase);
        alien.Status = BattleUnitStatus.Dead;
        Event<BriefingConfirmed>(campaign, new ConfirmBriefing());
    }

    [Fact]
    public void ALaterModsBriefingMergesKeyByKey()
    {
        var rules = Rules.Replace("  - type: UFO_ALIENS\n    width", "  - type: UFO_ALIENS\n    briefing: {music: M1, title: T1}\n  - type: UFO_ALIENS\n    briefing: {desc: D2, music: M2}\n    width", StringComparison.Ordinal);
        Assert.NotEqual(Rules, rules);
        var (campaign, _) = Begun(rules, "UFO_ALIENS");
        var briefing = campaign.GetQuery<IBattleBriefingQuery>()!.QueryBriefing()!;
        Assert.Equal(("T1", "D2", "M2"), (briefing.Title, briefing.Description, briefing.Music));
    }

    [Theory]
    [InlineData(0, BattleFaction.Player)]
    [InlineData(1, BattleFaction.Hostile)]
    [InlineData(2, BattleFaction.Neutral)]
    [InlineData(-1, BattleFaction.Player)]
    public void ASpawnedUnitTakesTheRulesFactionOrItsPreviousOwners(int rule, BattleFaction expected)
    {
        var (campaign, battle) = Begun(SpawnRules(faction: rule), "UFO_ARMORY");
        var owner = Soldier(battle);
        // Faction -1 takes the faction of the owner (the unit that carried the grenade before).
        foreach (var grenade in battle.Items.Where(static item => item.ItemType == "GRENADE")) grenade.PreviousOwnerUnitId = owner.Id;
        var next = battle.Units[^1].Id + 1;
        Event<BriefingConfirmed>(campaign, new ConfirmBriefing());
        var spawned = battle.Units.Where(unit => unit.Id >= next).ToArray();
        Assert.Equal(2, spawned.Length);
        Assert.All(spawned, unit =>
        {
            Assert.Equal((expected, owner.Id, expected == BattleFaction.Player), (unit.Faction, unit.PreviousOwnerId, unit.IsSpottedBy(BattleFaction.Player)));
            Assert.Equal(expected == BattleFaction.Player, unit.SummonedPlayerUnit);
            Assert.Equal(expected == BattleFaction.Player, unit.Ai is null);
        });
        Assert.All(battle.Items.Where(static item => item.ItemType == "CLIP" && item.Tile is not null && item.PreviousOwnerUnitId is not null), item => Assert.Equal(owner.Id, item.PreviousOwnerUnitId));
    }

    [Fact]
    public void TorsoWoundsCutTheRecoveredEnergyAndTheTallyCountsByOriginalFaction()
    {
        var campaign = ToInventory();
        var battle = Battle(campaign);
        var soldier = Soldier(battle);
        soldier.FatalWounds[1] = 2;
        soldier.Energy = 10;
        var expected = Math.Clamp(10 + soldier.EnergyRegeneration - 10 * 20 / 100, 0, soldier.Stats.Stamina);
        var tally = battle.Tally();
        // A mind-controlled soldier counts as an alien, a summoned one not at all, and a unit over the threshold with a spawn unit still counts.
        var controlled = new BattleUnit(910, BattleFaction.Hostile, BattleController.Ai) { Stats = soldier.Stats, Health = 10 };
        controlled.OriginalFaction = BattleFaction.Player;
        var summoned = new BattleUnit(911, BattleFaction.Player, BattleController.Human(0)) { Stats = soldier.Stats, SummonedPlayerUnit = true, Health = 10, ArmorId = soldier.ArmorId };
        var zombie = new BattleUnit(912, BattleFaction.Hostile, BattleController.Ai) { Stats = soldier.Stats, Health = 0, SpawnUnit = "ALIEN" };
        var gone = new BattleUnit(913, BattleFaction.Hostile, BattleController.Ai) { Stats = soldier.Stats, Health = 0 };
        foreach (var unit in new[] { controlled, summoned, zombie, gone }) battle.AddUnit(unit);
        Assert.Equal(new BattleTally(tally.LiveAliens + 2, tally.LiveSoldiers), battle.Tally());
        battle.RemoveItem(battle.Items[0]);
        Event<BattleFirstTurnStarted>(campaign, new StartFirstTurn());
        Assert.Equal(expected, soldier.Energy);
    }

    [Fact]
    public void RemovingAnItemTakesItsLoadedAmmoAndKeepsSpecialWeapons()
    {
        var campaign = ToInventory();
        var battle = Battle(campaign);
        var equipment = new UnitEquipment(battle, campaign.Content.RuntimeRules, battle.Seedless);
        var tile = Soldier(battle).Position!.Value;
        var gun = equipment.CreateItemForTile("GUN", tile);
        var clip = equipment.CreateItemForTile("CLIP", tile);
        gun.AmmoSlots[0] = clip.Id;
        clip.Tile = null;
        battle.RemoveItem(gun);
        Assert.DoesNotContain(battle.Items, item => item.Id == gun.Id || item.Id == clip.Id);
        var special = equipment.CreateItemForTile("CLIP", tile);
        special.IsSpecialWeapon = true;
        battle.RemoveItem(special);
        Assert.Contains(battle.Items, item => item.Id == special.Id);
    }

    private static string WithBriefing(string rules) => rules.Replace("  - type: UFO_ALIENS\n    width",
        "  - type: UFO_ALIENS\n    briefing: {title: STR_FAKE, desc: STR_FAKE_DESC}\n    width", StringComparison.Ordinal);

    private static CampaignState Reloaded(CampaignState campaign, Func<BattleSnapshot, BattleSnapshot> adjust)
    {
        var snapshot = campaign.Capture();
        return CampaignState.Restore(snapshot with { Battle = adjust(snapshot.Battle!) }, campaign.Content, new SplitMix64RandomSource(3), TerrainFiles());
    }

    [Fact]
    public void ABaseDefensesBriefingUsesTheCustomDeploymentWithADescriptionAndAFakeUfoItsCustomMission()
    {
        var rules = WithBriefing(Rules);
        Assert.NotEqual(Rules, rules);
        var (campaign, _) = Begun(rules);
        var defense = Reloaded(campaign, static battle => battle with { MissionType = "STR_BASE_DEFENSE", AlienCustomDeploy = "UFO_ALIENS" });
        var briefing = defense.GetQuery<IBattleBriefingQuery>()!.QueryBriefing()!;
        Assert.Equal(("STR_FAKE", "STR_FAKE_DESC"), (briefing.Title, briefing.Description));
        // A custom deployment without a description does not replace the mission's own.
        var plain = Reloaded(campaign, static battle => battle with { MissionType = "STR_BASE_DEFENSE", AlienCustomDeploy = "UFO_SCOUT" });
        Assert.Equal(("STR_BASE_DEFENSE", "STR_BASE_DEFENSE_BRIEFING"), (plain.GetQuery<IBattleBriefingQuery>()!.QueryBriefing()!.Title,
            plain.GetQuery<IBattleBriefingQuery>()!.QueryBriefing()!.Description));
        // A mission without a deployment briefs by the landing UFO's, or by the custom mission of a fake underwater UFO.
        var ufo = Reloaded(campaign, static battle => battle with { MissionType = "STR_NO_DEPLOYMENT" });
        Assert.Equal("STR_NO_DEPLOYMENT", ufo.GetQuery<IBattleBriefingQuery>()!.QueryBriefing()!.Title);
        var fake = Reloaded(campaign, static battle => battle with { MissionType = "STR_NO_DEPLOYMENT", AlienCustomMission = "UFO_ALIENS" });
        Assert.Equal(("STR_FAKE", "STR_FAKE_DESC"), (fake.GetQuery<IBattleBriefingQuery>()!.QueryBriefing()!.Title,
            fake.GetQuery<IBattleBriefingQuery>()!.QueryBriefing()!.Description));
    }

    [Fact]
    public void TheBriefingCommandEndsABaseDefensesRetaliationTarget()
    {
        var (campaign, _) = Requested(Rules, adjust: static snapshot => snapshot with
        {
            Bases = [snapshot.Bases[0] with { RetaliationTarget = true, RetaliationMissionId = 40 }, .. snapshot.Bases.Skip(1)],
            World = snapshot.World with { Missions = [.. snapshot.World.Missions, new AlienMissionSnapshot(40, "MISSION_RETALIATION", "REGION", "RACE_A", 1, 0, 1500, 1, -1)] },
        });
        Started(campaign);
        Assert.True(campaign.Capture().Bases[0].RetaliationTarget);
        // A craft's briefing leaves the base alone; a base defense's ends the target.
        var craft = Reloaded(campaign, static battle => battle);
        Event<BriefingOpened>(craft, new BeginBriefing("a", "b"));
        Assert.True(craft.Capture().Bases[0].RetaliationTarget);
        var defense = Reloaded(campaign, static battle => battle with { MissionType = "STR_BASE_DEFENSE" });
        Battle(defense).BriefingFromCraft = false;
        Event<BriefingOpened>(defense, new BeginBriefing("a", "b"));
        Assert.False(defense.Capture().Bases[0].RetaliationTarget);
    }

    [Fact]
    public void ATankSpawnedByAGrenadeIsArmedWithItsWeaponAndClip()
    {
        var (campaign, battle) = Begun(SpawnRules(unit: "VEHICLE"), "UFO_ARMORY");
        var next = battle.Units[^1].Id + 1;
        var clipSize = new UnitEquipment(battle, campaign.Content.RuntimeRules, battle.Seedless).VehicleClipSize("VEHICLE");
        Event<BriefingConfirmed>(campaign, new ConfirmBriefing());
        var tank = battle.Units.First(unit => unit.Id >= next);
        Assert.Equal(("VEHICLE", 2), (tank.UnitType, tank.TurretType));
        var weapon = Assert.Single(battle.InventoryOf(tank), static item => item.ItemType == "VEHICLE");
        // The clip is loaded into the weapon.
        var ammo = battle.Items.Single(item => item.Id == weapon.AmmoSlots[0]);
        Assert.Equal(("VEHICLE_AMMO", clipSize), (ammo.ItemType, ammo.AmmoQuantity));
    }

    [Fact]
    public void ASpawnWithNoValidPositionCreatesNoUnitAndDrawsNoDirection()
    {
        var (campaign, battle) = Begun(SpawnRules(), "UFO_ARMORY");
        foreach (var grenade in battle.Items.Where(static item => item.ItemType == "GRENADE" && item.Tile is not null).ToArray())
            for (var x = -2; x <= 2; x++)
                for (var y = -2; y <= 2; y++)
                {
                    var near = grenade.Tile!.Value with { X = grenade.Tile!.Value.X + x, Y = grenade.Tile!.Value.Y + y };
                    if (battle.Contains(near)) battle.Map.SetUnitAt(battle.Map.TileIndex(near), 9999);
                }
        var units = battle.Units.Count;
        var expected = new SplitMix64RandomSource(campaign.Random.State);
        for (var draw = 0; draw < 4; draw++) expected.NextInclusive(0, 99);
        Assert.Equal(new BriefingConfirmed(0, 2), Event<BriefingConfirmed>(campaign, new ConfirmBriefing()));
        Assert.Equal(units, battle.Units.Count);
        Assert.Equal(expected.State, campaign.Random.State);
    }

    [Fact]
    public void SoldierBonusAndGlobalEventRecoveryAndNewTurnScriptsRefuseTheConfirmation()
    {
        // A soldier bonus the soldier has, with a scripted time recovery.
        var rules = Rules + "\nsoldierBonuses:\n  - name: BONUS_TEST\n    recovery: {time: {tu: 0.5}}\n";
        var (campaign, battle) = Requested(rules, adjust: static snapshot => snapshot with
        {
            Bases = [snapshot.Bases[0] with { Soldiers = [.. snapshot.Bases[0].Soldiers.Select(static soldier => soldier with
            {
                Personal = soldier.Personal! with { TransformationBonuses = new Dictionary<string, int> { ["BONUS_TEST"] = 1 } },
            })] }, .. snapshot.Bases.Skip(1)],
        });
        Started(campaign);
        Assert.Equal("battle.turn-recovery-script-unsupported", Refusal(campaign, new ConfirmBriefing()));
        Assert.Equal(BattlePhase.Briefing, Battle(campaign).Phase);
        Assert.NotNull(battle);
        // A global event script for the new-turn units.
        var events = Rules + "\nextended:\n  scripts:\n    newTurnUnit:\n      - new: global-turn\n        offset: 1\n        code: |\n          return;\n";
        var (global, _) = Begun(events);
        Assert.Equal("battle.new-turn-script-unsupported", Refusal(global, new ConfirmBriefing()));
    }
}

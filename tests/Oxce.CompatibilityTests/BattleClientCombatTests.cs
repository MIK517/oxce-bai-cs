using Oxce.Engine;
using Oxce.Engine.Battlescape;
using Oxce.Engine.Input;
using Oxce.Gameplay.Battles;
using Oxce.Gameplay.Campaigns;
using Oxce.TestSupport;
using Xunit;
using static Oxce.CompatibilityTests.BattleClientMovementTests;
using static Oxce.CompatibilityTests.BattleSightTests;
using static Oxce.CompatibilityTests.BattleTestSupport;

namespace Oxce.CompatibilityTests;

/// <summary>
/// Phase 7 branch 5 S10 (the first part): the battlescape client's combat. The hand buttons open the action menu (<c>ActionMenuState</c>) with the accuracy and
/// time units of each line, a line starts aiming (the cursor's accuracy from the rules, Ctrl for force fire, the waypoints and launch button of a launcher) or acts at once
/// (a hit, priming), a command's shots, hits, blasts, deaths and a reaction's shots are replayed, and the reserve buttons are commands. The shots draw from a scripted source,
/// so the client's commands run through a battle runtime that holds it.
/// </summary>
public sealed class BattleClientCombatTests
{
    private sealed class RuntimeTarget(BattleRuntime runtime, List<ICampaignCommand>? sent) : ICampaignCommandTarget
    {
        public CampaignCommandResult Execute(ICampaignCommand command)
        {
            sent?.Add(command);
            return command is IBattleCommand battleCommand ? runtime.Execute(battleCommand) : throw new InvalidOperationException(command.GetType().Name);
        }
    }

    private const string Nl = "\n";

    private const string Rifle = "costAimed: {time: 30}\ncostSnap: {time: 20}\ncostAuto: {time: 40}\naccuracyAimed: 100\naccuracySnap: 100\naccuracyAuto: 100";

    private static (BattleUiContext Context, BattlescapeClient Screen, BattleViewModel Model) ClientOf(Arena arena, List<ICampaignCommand>? sent = null)
    {
        var campaign = arena.Campaign;
        var runtime = new BattleRuntime(arena.Battle, arena.Rules, new ScriptedDraws(0), null);
        var context = new BattleUiContext(new RuntimeTarget(runtime, sent), campaign.GetQuery<IBattleQuery>()!, campaign.GetQuery<IBattleViewQuery>()!,
            campaign.GetQuery<IBattleInventoryQuery>()!, campaign.GetQuery<IBattleBriefingQuery>()!, BattleViewer.LocalPlayer, new ScreenNavigator());
        Assert.True(context.Open());
        var screen = Assert.IsType<BattlescapeClient>(context.Screens.Top);
        Assert.True(screen.Model.Select(arena.Soldier.Id));
        screen.Model.Camera.SetViewLevel(0);
        screen.Model.Refresh();
        return (context, screen, screen.Model);
    }

    private static string[] Lines(BattlescapeClient screen) => [.. screen.Replay.Select(static frame => frame.Text)];

    private static void Press(BattleUiContext context, char key, InputKeyModifiers modifiers = InputKeyModifiers.None) => context.Screens.HandleInput(Key(key, modifiers));

    /// <summary>Opens the right hand's menu and picks the line of the action by its number.</summary>
    private static void Pick(BattleUiContext context, BattleViewModel model, BattleAimKind kind)
    {
        Press(context, BattlescapeClient.RightHandKey);
        var menu = Assert.IsType<BattleAimMenuView>(model.Menu);
        Press(context, (char)('1' + menu.Items.ToList().FindIndex(item => item.Kind == kind)));
    }

    /// <summary>A press on the panel at a row (the hand lines are at 70 and 82, the menu's lines from 110).</summary>
    private static GameInputEvent Panel(int y, byte button = 1) =>
        GameInputEvent.PointerButtonChange(GameInputEventKind.PointerPressed, 0, 0, BattlescapeClient.MapWidth + 20, y, button, 1);

    // ---- the menu ----

    [Fact]
    public void TheHandButtonOpensTheMenuWithEachLinesAccuracyAndTimeUnitsAndEscapeClosesIt()
    {
        var (arena, _, _) = BattleShootingTests.Load();
        var (context, screen, model) = ClientOf(arena);
        // The hand buttons set the active hand first; an empty hand with no special weapon that uses it opens no menu and says nothing (handleItemClick of no item).
        Press(context, BattlescapeClient.LeftHandKey);
        Assert.Null(model.Menu);
        Assert.Equal("", screen.Message);
        Assert.Equal("STR_LEFT_HAND", arena.Soldier.ActiveHand);
        Press(context, BattlescapeClient.RightHandKey);
        Assert.Equal("STR_RIGHT_HAND", arena.Soldier.ActiveHand);
        var menu = Assert.IsType<BattleAimMenuView>(model.Menu);
        Assert.Equal([BattleAimKind.Throw, BattleAimKind.AutoShot, BattleAimKind.SnapShot, BattleAimKind.AimedShot], menu.Items.Select(static item => item.Kind));
        Assert.Equal([40, 20, 30], menu.Items.Skip(1).Select(static item => item.TimeUnits));
        Assert.All(menu.Items, static item => Assert.True(item.Accuracy > 0));
        screen.Tick(TimeSpan.FromMilliseconds(16));
        // The menu is modal: a key that is no line leaves it open (the kneel key does not kneel), and Escape closes it.
        Press(context, BattlescapeClient.KneelKey);
        Assert.NotNull(model.Menu);
        Assert.False(arena.Soldier.Kneeled);
        Press(context, '\u001b');
        Assert.Null(model.Menu);
        Assert.Null(model.Aiming);
        // A click on the third line picks it (the lines start at the panel's menu row).
        Press(context, BattlescapeClient.RightHandKey);
        context.Screens.HandleInput(GameInputEvent.PointerButtonChange(GameInputEventKind.PointerPressed, 0, 0, BattlescapeClient.MapWidth + 20, 110 + 24 + 3, 1, 1));
        Assert.Equal((arena.Soldier.Id, BattleAimKind.SnapShot), (model.SelectedUnitId!.Value, model.Aiming!.Value.Kind));
        // The hand key while aiming cancels the aim and opens no menu (the touch concession).
        Press(context, BattlescapeClient.RightHandKey);
        Assert.Null(model.Aiming);
        Assert.Null(model.Menu);
        // A click on the hand line opens the menu (the right hand's line is at row 70); a click off any line closes it.
        context.Screens.HandleInput(Panel(73));
        Assert.NotNull(model.Menu);
        context.Screens.HandleInput(Panel(300));
        Assert.Null(model.Menu);
    }

    private const string Fist = "- type: FIST\n    battleType: 3\n    clipSize: 1\n    weight: 1\n    specialIconSprite: 7\n    specialUseEmptyHand: true\n    costThrow: {time: 25}\n    costMelee: {time: 20}\n    accuracyMelee: 100\n    power: 5\n    damageType: 1\n  ";

    /// <summary>The shooting arena with a built-in FIST (a melee special weapon with an icon that uses an empty hand) on the soldier.</summary>
    private static (Arena Arena, BattleItem Fist) WithFist(bool icon = true, bool emptyHand = true, int battleType = 3, bool show = false, string skills = "")
    {
        var (arena, _, _) = BattleShootingTests.Load(more: FistRules(icon, emptyHand, battleType, show, skills));
        return (arena, AddFist(arena));
    }

    private static Func<string, string> FistRules(bool icon = true, bool emptyHand = true, int battleType = 3, bool show = false, string skills = "", string extraItems = "")
    {
        var text = Fist.Replace("battleType: 3", $"battleType: {battleType}", StringComparison.Ordinal);
        if (!icon) text = text.Replace("    specialIconSprite: 7\n", "", StringComparison.Ordinal);
        if (!emptyHand) text = text.Replace("    specialUseEmptyHand: true\n", "", StringComparison.Ordinal);
        if (show) text = text.Replace("    specialUseEmptyHand: true\n", "    specialUseEmptyHand: true\n    specialUseEmptyHandShow: true\n", StringComparison.Ordinal);
        return rules => BattleTestSupport.Replaced(rules, "- type: GRENADE" + Nl + "    battleType: 4", text + extraItems + "- type: GRENADE" + Nl + "    battleType: 4") + skills;
    }

    private static BattleItem AddFist(Arena arena, string type = "FIST")
    {
        var fist = new BattleItem(arena.Battle.TakeItemId(), type) { OwnerUnitId = arena.Soldier.Id, Slot = "", IsSpecialWeapon = true };
        arena.Battle.AddItem(fist);
        arena.Soldier.SpecialWeaponIds.Add(fist.Id);
        return fist;
    }

    [Fact]
    public void TheSpecialWeaponButtonOpensTheMenuOfTheIconWeaponAndAnEmptyHandOpensTheOneThatUsesIt()
    {
        var (arena, fist) = WithFist();
        var (context, screen, model) = ClientOf(arena);
        Assert.Equal((fist.Id, fist.Id), (model.SelectedUnit!.SpecialIconItem!.Value.ItemId, model.SelectedUnit.EmptyHandItem!.Value.ItemId));
        // The button (key Y) opens the menu of the item without setting a hand; the menu lists the throw and the hit.
        arena.Soldier.ActiveHand = "STR_RIGHT_HAND";
        Press(context, BattlescapeClient.SpecialKey);
        var menu = Assert.IsType<BattleAimMenuView>(model.Menu);
        Assert.Equal(fist.Id, menu.ItemId);
        Assert.Equal([BattleAimKind.Throw, BattleAimKind.Hit], menu.Items.Select(static item => item.Kind));
        Assert.Equal([25, 20], menu.Items.Select(static item => item.TimeUnits));
        Assert.Equal("", screen.Message);
        // The menu is the item's: a line starts its aim, which the refresh keeps (the item is no hand's, it is the unit's special weapon).
        Press(context, '1');
        Assert.Equal((fist.Id, BattleAimKind.Throw), (model.Aiming!.Value.ItemId, model.Aiming.Value.Kind));
        model.Refresh();
        Assert.NotNull(model.Aiming);
        // The button while aiming cancels the aim and opens no menu (the touch concession).
        Press(context, BattlescapeClient.SpecialKey);
        Assert.Null(model.Aiming);
        Assert.Null(model.Menu);
        // The middle click on the button is the Ufopaedia page, refused by owner; the left click on it opens the menu again.
        var button = BattlescapeClient.MapWidth + 8 + 5 * 22 + 4;
        context.Screens.HandleInput(GameInputEvent.PointerButtonChange(GameInputEventKind.PointerPressed, 0, 0, button, 288, 2, 1));
        Assert.Equal((BattleViewModel.UfopaediaCode, true), (screen.Message, model.Menu is null));
        context.Screens.HandleInput(GameInputEvent.PointerButtonChange(GameInputEventKind.PointerPressed, 0, 0, button, 288, 1, 1));
        Assert.Equal(fist.Id, model.Menu!.ItemId);
        Press(context, '\u001b');
        // The empty left hand (the right one holds the gun) opens the same menu and becomes the active hand; the right hand keeps the gun's.
        Press(context, BattlescapeClient.LeftHandKey);
        Assert.Equal((fist.Id, "STR_LEFT_HAND"), (model.Menu!.ItemId, arena.Soldier.ActiveHand));
        Press(context, '\u001b');
        Press(context, BattlescapeClient.RightHandKey);
        Assert.NotEqual(fist.Id, model.Menu!.ItemId);
    }

    [Fact]
    public void TheSpecialWeaponButtonDoesNothingWithoutAnIconAndAnEmptyHandNeedsAWeaponThatUsesIt()
    {
        // An item with no icon on a soldier whose rules list no skill: no button, the key does nothing, but an empty hand still opens the item that uses it.
        var (arena, fist) = WithFist(icon: false);
        var (context, screen, model) = ClientOf(arena);
        Assert.NotNull(arena.Soldier.SoldierId);
        Assert.Null(model.SelectedUnit!.SpecialIconItem);
        Assert.False(model.SelectedUnit.SpecialButtonShown);
        Press(context, BattlescapeClient.SpecialKey);
        Assert.Equal(("", true), (screen.Message, model.Menu is null));
        Press(context, BattlescapeClient.LeftHandKey);
        Assert.Equal(fist.Id, model.Menu!.ItemId);
        // An item that does not use an empty hand: the empty hand opens nothing (the icon button still does).
        var (second, other) = WithFist(emptyHand: false);
        var (secondContext, _, secondModel) = ClientOf(second);
        Assert.Null(secondModel.SelectedUnit!.EmptyHandItem);
        Press(secondContext, BattlescapeClient.LeftHandKey);
        Assert.Null(secondModel.Menu);
        Press(secondContext, BattlescapeClient.SpecialKey);
        Assert.Equal(other.Id, secondModel.Menu!.ItemId);
    }

    [Fact]
    public void ASoldierWhoseRulesListASkillShowsTheButtonThatFallsBackOnTheSkillsMenuRefusedByName()
    {
        // The reference's skills button shares the key; the port's fallback is refused by name (branch 6) for a soldier whose rules list a skill, and only for one.
        var (arena, _) = WithFist(icon: false, skills: "\nsoldiers:\n  - type: RECRUIT\n    skills: [SKILL_TEST]\nskills:\n  - type: SKILL_TEST\n");
        var (context, screen, model) = ClientOf(arena);
        Assert.Equal((true, true, true), (model.SelectedUnit!.HasSkills, model.SelectedUnit.SpecialButtonShown, model.SelectedUnit.SpecialIconItem is null));
        Press(context, BattlescapeClient.SpecialKey);
        Assert.Equal((BattleViewModel.SkillsCode, true), (screen.Message, model.Menu is null));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(10)]
    [InlineData(11)]
    public void AnIconWeaponOfATypeTheButtonDoesNotShowHasNoButtonAndTheKeyAndThePointerDoNothing(int battleType)
    {
        // updateUiButton hides the button for none, ammo, grenade, proximity grenade, flare and corpse; an invisible button handles no input.
        var (arena, _) = WithFist(battleType: battleType);
        var (context, screen, model) = ClientOf(arena);
        Assert.Null(model.SelectedUnit!.SpecialIconItem);
        Assert.False(model.SelectedUnit.SpecialButtonShown);
        Press(context, BattlescapeClient.SpecialKey);
        Assert.Equal(("", true), (screen.Message, model.Menu is null));
        var button = BattlescapeClient.MapWidth + 8 + 5 * 22 + 4;
        context.Screens.HandleInput(GameInputEvent.PointerButtonChange(GameInputEventKind.PointerPressed, 0, 0, button, 288, 1, 1));
        Assert.Null(model.Menu);
    }

    [Fact]
    public void TheSpecialButtonWhileALaunchIsAimedTakesBackOnlyTheLastWaypointAndWithAMenuOpenItClosesTheMenuFirst()
    {
        var arena = BattleDamageTests.Duel(gun: "costAimed: {time: 30}\naccuracyAimed: 100\nwaypoints: 3", clip: "power: 40\ndamageType: 2\ndamageAlter: {RandomType: 3}", more: FistRules());
        arena.Stand(arena.Alien, 15, 10, 6);
        arena.Soldier.Stats = arena.Soldier.Stats with { TimeUnits = 100, Firing = 100 };
        arena.Soldier.TimeUnits = 100;
        BattleDamageTests.Shot(arena).Clip.AmmoQuantity = 3;
        var fist = AddFist(arena);
        arena.See();
        arena.Battle.Random = new ScriptedDraws(0);
        var (context, screen, model) = ClientOf(arena);
        Pick(context, model, BattleAimKind.Launch);
        context.Screens.HandleInput(ClickTile(screen, 12, 12));
        context.Screens.HandleInput(ClickTile(screen, 15, 10));
        Assert.Equal(2, model.Waypoints.Count);
        // The button is btnSpecialClick's cancelCurrentAction: the last waypoint goes and the aim stays.
        Press(context, BattlescapeClient.SpecialKey);
        Assert.Equal([new BattlePosition(12, 12, 0)], model.Waypoints);
        Assert.NotNull(model.Aiming);
        Press(context, BattlescapeClient.SpecialKey);
        Assert.Empty(model.Waypoints);
        // With a menu open (no aim) the button closes it and opens the special weapon's menu, as a hand button does (one rule for both).
        Press(context, '\u001b');
        Press(context, BattlescapeClient.RightHandKey);
        Assert.NotEqual(fist.Id, model.Menu!.ItemId);
        // (The client's menu is modal for keys and clicks, so this is the model's rule, the same as the hand buttons'.)
        Assert.True(model.OpenSpecialMenu());
        Assert.Equal(fist.Id, model.Menu!.ItemId);
    }

    [Fact]
    public void AnEmptyHandsItemIsDrawnOnlyWithItsShowFlagWhileTheClickNeedsOnlyTheEmptyHandFlag()
    {
        var (arena, fist) = WithFist();
        var (_, _, model) = ClientOf(arena);
        Assert.Equal((fist.Id, true, "L -"), (model.SelectedUnit!.EmptyHandItem!.Value.ItemId, model.SelectedUnit.ShownEmptyHandItem is null, BattlescapeClient.HandLines(model.SelectedUnit).Left));
        var (shown, shownFist) = WithFist(show: true);
        var (_, _, shownModel) = ClientOf(shown);
        Assert.Equal((shownFist.Id, "L FIST"), (shownModel.SelectedUnit!.ShownEmptyHandItem!.Value.ItemId, BattlescapeClient.HandLines(shownModel.SelectedUnit).Left));
    }

    [Fact]
    public void TheEmptyHandsItemFollowsTheTypeOrderAndTheHandLinesPlaceItAndCarryTheReactionMarkers()
    {
        var psi = "- type: PSI\n    battleType: 9\n    clipSize: 1\n    weight: 1\n    specialUseEmptyHand: true\n    specialUseEmptyHandShow: true\n  ";
        var (arena, _, _) = BattleShootingTests.Load(more: FistRules(show: true, extraItems: psi));
        // The psi amp is added first, but the melee fist comes before it in the type order.
        var psiAmp = AddFist(arena, "PSI");
        var fist = AddFist(arena);
        var (context, _, model) = ClientOf(arena);
        Assert.Equal((fist.Id, fist.Id), (model.SelectedUnit!.EmptyHandItem!.Value.ItemId, model.SelectedUnit.ShownEmptyHandItem!.Value.ItemId));
        // The right hand holds the gun, so the empty left hand shows it; the right line shows the gun.
        var (right, left) = BattlescapeClient.HandLines(model.SelectedUnit);
        Assert.Equal(("L FIST", false), (left, right.Contains("FIST", StringComparison.Ordinal)));
        // A preferred left hand is starred and a disabled right hand has an exclamation mark.
        context.Screens.HandleInput(Panel(85, 3));
        context.Screens.HandleInput(Panel(73, 3) with { Modifiers = InputKeyModifiers.LeftControl });
        (right, left) = BattlescapeClient.HandLines(model.SelectedUnit!);
        Assert.Equal(("L* FIST", true), (left, right.StartsWith("R!", StringComparison.Ordinal)));
        // Without the melee fist the psi amp takes its place.
        arena.Soldier.SpecialWeaponIds.Remove(fist.Id);
        arena.Battle.MarkChanged();
        model.Refresh();
        Assert.Equal((psiAmp.Id, "L* PSI"), (model.SelectedUnit!.ShownEmptyHandItem!.Value.ItemId, BattlescapeClient.HandLines(model.SelectedUnit).Left));
    }

    [Fact]
    public void TheRightClickOnAHandTogglesItForReactionsAndTheMiddleClickIsRefusedByName()
    {
        var (arena, _, _) = BattleShootingTests.Load();
        var (context, screen, model) = ClientOf(arena);
        arena.Soldier.ActiveHand = "STR_LEFT_HAND";
        context.Screens.HandleInput(Panel(73, 3));
        Assert.Equal(("", "STR_RIGHT_HAND", "STR_RIGHT_HAND"), (screen.Message, arena.Soldier.ActiveHand, arena.Soldier.PreferredHandForReactions));
        Assert.Equal("STR_RIGHT_HAND", model.SelectedUnit!.PreferredHandForReactions);
        Assert.Null(model.Menu);
        // Again: the preferred hand is dropped. The left hand with Ctrl: its reactions are disabled, and again: enabled; the preferred flag is dropped only by its own hand.
        context.Screens.HandleInput(Panel(73, 3));
        Assert.Equal("", arena.Soldier.PreferredHandForReactions);
        context.Screens.HandleInput(Panel(73, 3));
        context.Screens.HandleInput(Panel(85, 3) with { Modifiers = InputKeyModifiers.LeftControl });
        Assert.Equal(("STR_RIGHT_HAND", true, "STR_LEFT_HAND"), (arena.Soldier.PreferredHandForReactions, arena.Soldier.ReactionsDisabledForLeftHand, arena.Soldier.ActiveHand));
        Assert.True(model.SelectedUnit!.LeftHandDisabledForReactions);
        context.Screens.HandleInput(Panel(73, 3) with { Modifiers = InputKeyModifiers.LeftControl });
        Assert.Equal(("", true, true), (arena.Soldier.PreferredHandForReactions, arena.Soldier.ReactionsDisabledForLeftHand, arena.Soldier.ReactionsDisabledForRightHand));
        context.Screens.HandleInput(Panel(85, 3) with { Modifiers = InputKeyModifiers.LeftControl });
        // A plain right click on a disabled hand enables it again and prefers it.
        context.Screens.HandleInput(Panel(73, 3));
        Assert.Equal(("STR_RIGHT_HAND", false, false), (arena.Soldier.PreferredHandForReactions, arena.Soldier.ReactionsDisabledForLeftHand, arena.Soldier.ReactionsDisabledForRightHand));
        // The command refuses a unit that is not the side's and the middle click opens no menu.
        Assert.Equal("battle.selection-unit-unknown", Refusal(arena.Campaign.Execute(new ToggleHandForReactions(arena.Alien.Id, true, false))));
        context.Screens.HandleInput(Panel(73, 2));
        Assert.Equal((BattleViewModel.UfopaediaCode, true), (screen.Message, model.Menu is null));
        // The event carries the unit's flags in the order the fields name them: the left hand disabled by Ctrl, the preferred right hand kept.
        var runtime = new BattleRuntime(arena.Battle, arena.Rules, new ScriptedDraws(0), null);
        var toggled = Assert.IsType<HandForReactionsChanged>(Assert.Single(runtime.Execute(new ToggleHandForReactions(arena.Soldier.Id, false, true)).Events));
        Assert.Equal((arena.Soldier.Id, "STR_RIGHT_HAND", true, false), (toggled.UnitId, toggled.PreferredHand, toggled.LeftDisabled, toggled.RightDisabled));
    }

    [Fact]
    public void AWalkThatDrawsAReactionIsReplayedWithItsStopBeforeTheReactorsShot()
    {
        var arena = BattleWalkTests.Make();
        BattleTestSupport.GunFor(arena.Battle, arena.Alien);
        arena.Soldier.Stats = arena.Soldier.Stats with { Health = 1000 };
        arena.Soldier.Health = 1000;
        var (context, screen, _) = ClientOf(arena);
        context.Screens.HandleInput(ClickTile(screen, 12, 10));
        var lines = Lines(screen);
        var stop = Array.FindIndex(lines, static line => line.StartsWith("Walk ends: ReactionFire after 1 steps", StringComparison.Ordinal));
        var shot = Array.FindIndex(lines, line => line.StartsWith($"Unit {arena.Alien.Id} fires", StringComparison.Ordinal));
        Assert.True(stop >= 0 && shot > stop, string.Join(" | ", lines));
        Play(screen);
        Assert.Equal(BattleWalkTests.At(11, 10, 0), arena.Soldier.Position);
    }

    [Fact]
    public void AnAimEndsWhenItsWeaponLeavesTheHand()
    {
        var (arena, gun, _) = BattleShootingTests.Load();
        var (context, _, model) = ClientOf(arena);
        Pick(context, model, BattleAimKind.AimedShot);
        Assert.NotNull(model.Aiming);
        gun.Slot = "STR_BACK_PACK";
        arena.Battle.MarkChanged();
        model.Refresh();
        Assert.Null(model.Aiming);
        Assert.Null(model.Menu);
    }

    [Fact]
    public void AMedikitAndAPsiAmpOpenTheirMenuWithTheNamesOfTheRefusalsThatBelongToBranch6()
    {
        foreach (var (type, code) in new[] { (6, "battle.item-use-unsupported"), (7, "battle.item-use-unsupported"), (8, "battle.item-use-unsupported"), (9, "battle.psi-unsupported") })
        {
            var (arena, _) = BattleThrowingTests.Load(more: rules => BattleTestSupport.Replaced(rules, "- type: GRENADE" + Nl + "    battleType: 4" + Nl, $"- type: GRENADE{Nl}    battleType: {type}{Nl}"));
            var (context, screen, model) = ClientOf(arena);
            Press(context, BattlescapeClient.RightHandKey);
            Assert.Equal(code, model.Menu?.Unsupported);
            Assert.Equal(code, screen.Message);
        }
    }

    // ---- shots ----

    [Fact]
    public void AnAimedShotShowsTheCursorsAccuracyThenFiresAndItsShotAndHitAreReplayed()
    {
        var (arena, gun, clip) = BattleShootingTests.Load();
        var (context, screen, model) = ClientOf(arena);
        Pick(context, model, BattleAimKind.AimedShot);
        Assert.Equal(BattleAimKind.AimedShot, model.Aiming!.Value.Kind);
        context.Screens.HandleInput(Over(screen, 15, 10));
        var aim = Assert.IsType<BattleAimView>(model.Aim);
        Assert.Equal((true, true, true, true, 30), (aim.Usable, aim.CanPay, aim.InRange, aim.HasLineOfFire, aim.TimeUnits));
        Assert.True(aim.Accuracy > 0 && aim.HitChance > 0);
        // Off the map the cursor shows nothing.
        context.Screens.HandleInput(GameInputEvent.PointerMotion(0, 0, BattlescapeClient.MapWidth + 10, 10, 0, 0, 0));
        Assert.Null(model.Aim);
        context.Screens.HandleInput(ClickTile(screen, 15, 10));
        // The aim stays after a shot (popState: the cursor stays in targeting mode), so the player can shoot again in the same mode.
        Assert.Equal(BattleAimKind.AimedShot, model.Aiming!.Value.Kind);
        Assert.Equal((70, 11, true), (arena.Soldier.TimeUnits, clip.AmmoQuantity, arena.Alien.Health < 100));
        Assert.True(screen.Replaying);
        Play(screen);
        var lines = Lines(screen);
        Assert.Contains(lines, static line => line.StartsWith("Unit ", StringComparison.Ordinal) && line.Contains("fires AimedShot", StringComparison.Ordinal) && line.Contains("unit", StringComparison.Ordinal));
        Assert.Contains(lines, line => line.StartsWith($"Unit {arena.Alien.Id} takes ", StringComparison.Ordinal));
        Assert.Contains(screen.Replay, static frame => frame.Marker == new BattlePosition(15, 10, 0));
        Assert.Equal(11, gun.AmmoSlots[0] is { } ? clip.AmmoQuantity : -1);
        Play(screen);
        context.Screens.HandleInput(ClickTile(screen, 15, 10));
        Assert.Equal((40, 10), (arena.Soldier.TimeUnits, clip.AmmoQuantity));
    }

    [Fact]
    public void ARefusedShotShowsItsReasonAndTheCursorSaysWhyBeforeAnythingIsSent()
    {
        var (arena, _, clip) = BattleShootingTests.Load();
        arena.Soldier.TimeUnits = 5;
        var (context, screen, model) = ClientOf(arena);
        Pick(context, model, BattleAimKind.AimedShot);
        context.Screens.HandleInput(Over(screen, 15, 10));
        Assert.Equal((false, "STR_NOT_ENOUGH_TIME_UNITS"), (model.Aim!.CanPay, model.Aim.ShortfallReason));
        var revision = arena.Battle.Revision;
        context.Screens.HandleInput(ClickTile(screen, 15, 10));
        // The rules refused: the message is their code, nothing was spent, nothing is replayed, and the aim stays.
        Assert.StartsWith("battle.", screen.Message, StringComparison.Ordinal);
        Assert.Equal((5, 12, revision), (arena.Soldier.TimeUnits, clip.AmmoQuantity, arena.Battle.Revision));
        Assert.False(screen.Replaying);
        Assert.Equal(BattleAimKind.AimedShot, model.Aiming!.Value.Kind);
    }

    [Fact]
    public void CtrlFiresAtATileNoUnitIsOnAndAnAutoShotIsReplayedShotByShot()
    {
        var (arena, _, clip) = BattleShootingTests.Load();
        var sent = new List<ICampaignCommand>();
        var (context, screen, model) = ClientOf(arena, sent);
        Pick(context, model, BattleAimKind.AutoShot);
        context.Screens.HandleInput(ClickTile(screen, 14, 10, modifiers: InputKeyModifiers.LeftControl));
        Assert.Equal((60, 9), (arena.Soldier.TimeUnits, clip.AmmoQuantity));
        Play(screen);
        Assert.Equal(3, Lines(screen).Count(static line => line.Contains("fires AutoShot", StringComparison.Ordinal)));
        // The force fire is the Ctrl key at the click: the command carries it, and a click without Ctrl does not.
        var forced = model.LastEvents.OfType<UnitFired>().First();
        Assert.Equal(BattleAimKind.AutoShot, forced.Kind);
        Assert.True(Assert.IsType<Attack>(sent[^1]).ForceFire);
        Assert.Equal(BattleAimKind.AutoShot, model.Aiming!.Value.Kind);
        Play(screen);
        context.Screens.HandleInput(ClickTile(screen, 14, 10));
        Assert.False(Assert.IsType<Attack>(sent[^1]).ForceFire);
        Play(screen);
        // The right click cancels an aim and the first Escape too; the second is the deselect refusal of branch 6.
        context.Screens.HandleInput(ClickTile(screen, 12, 12, button: 3));
        Assert.Null(model.Aiming);
        Pick(context, model, BattleAimKind.SnapShot);
        context.Screens.HandleInput(ClickTile(screen, 12, 12, button: 3));
        Assert.Null(model.Aiming);
        Pick(context, model, BattleAimKind.SnapShot);
        Press(context, '\u001b');
        Assert.Null(model.Aiming);
        Press(context, '\u001b');
        Assert.Equal(BattlescapeClient.DeselectCode, screen.Message);
    }

    // ---- throwing, priming, melee, launching ----

    [Fact]
    public void AThrowIsAimedFromTheMenuAndReplayedAndAPrimingLineActsAtOnce()
    {
        var (arena, grenade) = BattleThrowingTests.Load();
        var (context, screen, model) = ClientOf(arena);
        Press(context, BattlescapeClient.RightHandKey);
        var kinds = model.Menu!.Items.Select(static item => item.Kind).ToList();
        Assert.Contains(BattleAimKind.Throw, kinds);
        Press(context, '\u001b');
        // The priming line acts at once, with the fuse the plus and minus keys set.
        Assert.Contains(BattleAimKind.Prime, kinds);
        Press(context, BattlescapeClient.RightHandKey);
        Press(context, '+');
        Press(context, '+');
        Assert.Equal(2, model.PrimeTurns);
        Press(context, (char)('1' + kinds.IndexOf(BattleAimKind.Prime)));
        Assert.Equal(90, arena.Soldier.TimeUnits);
        Play(screen);
        Assert.Contains("Primed: 2 turns", Lines(screen).Concat(new[] { screen.Message }));
        Pick(context, model, BattleAimKind.Throw);
        context.Screens.HandleInput(ClickTile(screen, 15, 10));
        // A throw that was done ends the aim (popState cancels it).
        Assert.Null(model.Aiming);
        Play(screen);
        Assert.Contains(Lines(screen), static line => line.Contains("fires Throw", StringComparison.Ordinal));
        Assert.Null(grenade.OwnerUnitId);
    }

    [Fact]
    public void AHitFromTheMenuStrikesAtOnceAndItsMeleeEventIsReplayed()
    {
        var arena = BattleMeleeTests.Arm();
        var (context, screen, model) = ClientOf(arena);
        Pick(context, model, BattleAimKind.Hit);
        Assert.Null(model.Aiming);
        Assert.Equal(80, arena.Soldier.TimeUnits);
        Play(screen);
        Assert.Contains(Lines(screen), static line => line.Contains("strikes and hits", StringComparison.Ordinal));
        Assert.Contains(Lines(screen), line => line.StartsWith($"Unit {arena.Alien.Id} takes ", StringComparison.Ordinal));
    }

    [Fact]
    public void AHitChosenWithAltCarriesTheModifier()
    {
        var arena = BattleMeleeTests.Arm();
        var sent = new List<ICampaignCommand>();
        var (context, screen, model) = ClientOf(arena, sent);
        Press(context, BattlescapeClient.RightHandKey);
        var line = model.Menu!.Items.ToList().FindIndex(static item => item.Kind == BattleAimKind.Hit);
        Press(context, (char)('1' + line), InputKeyModifiers.LeftAlt);
        Assert.True(Assert.IsType<Attack>(sent[^1]).AltPressed);
        Play(screen);
        Press(context, BattlescapeClient.RightHandKey);
        Press(context, (char)('1' + line));
        Assert.False(Assert.IsType<Attack>(sent[^1]).AltPressed);
    }

    [Fact]
    public void ALauncherTakesWaypointsOnClicksTheRightClickRemovesTheLastAndTheLaunchKeyFiresThemWithTheBlastReplayed()
    {
        var (arena, _, clip) = BattleLaunchTests.Load();
        var (context, screen, model) = ClientOf(arena);
        Pick(context, model, BattleAimKind.Launch);
        context.Screens.HandleInput(ClickTile(screen, 12, 12));
        context.Screens.HandleInput(ClickTile(screen, 15, 10));
        Assert.Equal(2, model.Waypoints.Count);
        context.Screens.HandleInput(ClickTile(screen, 12, 12, button: 3));
        Assert.Equal([new BattlePosition(12, 12, 0)], model.Waypoints);
        // The launch button with nothing set does nothing; with a waypoint the missile flies.
        context.Screens.HandleInput(ClickTile(screen, 15, 10));
        Press(context, BattlescapeClient.LaunchKey);
        Assert.Null(model.Aiming);
        Assert.Equal(2, clip.AmmoQuantity);
        Play(screen);
        Assert.Contains(Lines(screen), static line => line.Contains("fires Launch", StringComparison.Ordinal));
        Assert.Contains(Lines(screen), static line => line.StartsWith("Explosion power 40", StringComparison.Ordinal));
        Assert.Contains(screen.Replay, static frame => frame.Marker is not null);
        Press(context, BattlescapeClient.LaunchKey);
        Assert.False(screen.Replaying);
    }

    [Fact]
    public void ALaunchersWaypointsStopAtItsLimitAndARefusedLaunchKeepsThem()
    {
        var (arena, _, clip) = BattleLaunchTests.Load();
        var (context, screen, model) = ClientOf(arena);
        Pick(context, model, BattleAimKind.Launch);
        // The launcher takes three waypoints: a click beyond them is ignored, and the launch flies with the three placed.
        foreach (var x in new[] { 11, 12, 13, 14 }) context.Screens.HandleInput(ClickTile(screen, x, 12));
        Assert.Equal([new BattlePosition(11, 12, 0), new BattlePosition(12, 12, 0), new BattlePosition(13, 12, 0)], model.Waypoints);
        // The rules refuse it (no time units): the aim and its waypoints stay.
        arena.Soldier.TimeUnits = 0;
        arena.Battle.MarkChanged();
        Press(context, BattlescapeClient.LaunchKey);
        Assert.StartsWith("battle.", screen.Message, StringComparison.Ordinal);
        Assert.Equal((BattleAimKind.Launch, 3), (model.Aiming!.Value.Kind, model.Waypoints.Count));
        arena.Soldier.TimeUnits = 100;
        arena.Battle.MarkChanged();
        Press(context, BattlescapeClient.LaunchKey);
        Assert.Null(model.Aiming);
        Assert.Equal(2, clip.AmmoQuantity);
    }

    [Fact]
    public void TheLaunchLineChecksTheTimeUnitsBeforeAnyWaypointIsPlaced()
    {
        var (arena, _, clip) = BattleLaunchTests.Load();
        arena.Soldier.TimeUnits = 5;
        var (context, screen, model) = ClientOf(arena);
        Pick(context, model, BattleAimKind.Launch);
        Assert.Null(model.Aiming);
        Assert.StartsWith("battle.", screen.Message, StringComparison.Ordinal);
        Assert.Equal(3, clip.AmmoQuantity);
        // With the units back the same line aims.
        arena.Soldier.TimeUnits = 100;
        arena.Battle.MarkChanged();
        model.Refresh();
        Pick(context, model, BattleAimKind.Launch);
        Assert.Equal(BattleAimKind.Launch, model.Aiming!.Value.Kind);
    }

    [Fact]
    public void CtrlAndShiftStartTheSprayAutoShotWhoseClicksCollectWaypointsAndAnEarlyShiftCtrlFires()
    {
        var (arena, _, clip) = BattleShootingTests.Load(gun: Rifle + "\nsprayWaypoints: 3", distance: 8);
        var sent = new List<ICampaignCommand>();
        var (context, screen, model) = ClientOf(arena, sent);
        Pick(context, model, BattleAimKind.AutoShot);
        const InputKeyModifiers both = InputKeyModifiers.LeftControl | InputKeyModifiers.LeftShift;
        var before = sent.Count;
        context.Screens.HandleInput(ClickTile(screen, 13, 12, modifiers: both));
        Assert.Equal((before, 1), (sent.Count, model.Waypoints.Count));
        // A right click takes the last waypoint, and the spray with it; starting again, the second click with Ctrl and Shift fires early.
        context.Screens.HandleInput(ClickTile(screen, 13, 12, button: 3));
        Assert.Empty(model.Waypoints);
        Assert.NotNull(model.Aiming);
        context.Screens.HandleInput(ClickTile(screen, 13, 12, modifiers: both));
        context.Screens.HandleInput(ClickTile(screen, 15, 12, modifiers: both));
        var attack = Assert.IsType<Attack>(sent[^1]);
        Assert.Equal([new BattlePosition(13, 12, 0), new BattlePosition(15, 12, 0)], attack.SprayWaypoints);
        Assert.Equal(9, clip.AmmoQuantity);
        Play(screen);
        Assert.Equal(3, Lines(screen).Count(static line => line.Contains("fires AutoShot", StringComparison.Ordinal)));
        Assert.Empty(model.Waypoints);
        // The third click of a spray with no Shift is a plain forced shot.
        context.Screens.HandleInput(ClickTile(screen, 15, 12, modifiers: InputKeyModifiers.LeftControl));
        Assert.Null(Assert.IsType<Attack>(sent[^1]).SprayWaypoints);
    }

    // ---- reaction fire, reserve, wounds, carried units ----

    [Fact]
    public void AReactionToAKneelIsReplayedWithTheReactorsShotThoughItIsNotOnTurn()
    {
        var arena = BattleReactionFireTests.Make();
        var (context, screen, _) = ClientOf(arena);
        Press(context, BattlescapeClient.KneelKey);
        Play(screen);
        Assert.Contains(Lines(screen), line => line.StartsWith($"Unit {arena.Alien.Id} fires", StringComparison.Ordinal));
    }

    [Fact]
    public void TheReserveButtonsAreCommandsThatSaveTheReserveAndTheButtonInForceIsMarked()
    {
        var (arena, _, _) = BattleShootingTests.Load();
        var (context, screen, model) = ClientOf(arena);
        context.Screens.HandleInput(Key(0x4000003au + 2));
        Assert.Equal((int)BattleActionType.AimedShot, arena.Battle.TimeUnitsReserved);
        Assert.Equal(BattleReserveKind.AimedShot, model.Selection!.TimeUnitReserve);
        Press(context, BattlescapeClient.ReserveKneelKey);
        Assert.True(arena.Battle.KneelReserved);
        Assert.True(model.Selection!.KneelReserved);
        Tick(screen);
        var x = BattlescapeClient.MapWidth + 8;
        Assert.Equal(BattleUiContext.Color.Xcom, screen.Frame.GetPixel(x + 2 * 22 + 1, 285));
        Assert.Equal(BattleUiContext.Color.Frame, screen.Frame.GetPixel(x + 22 + 1, 285));
        // The cycle key steps none, snap, aimed, auto; a click on the none button clears it; the kneel button flips again.
        Press(context, BattlescapeClient.ReserveKey);
        Assert.Equal((int)BattleActionType.AutoShot, arena.Battle.TimeUnitsReserved);
        Press(context, BattlescapeClient.ReserveKey);
        Assert.Equal((int)BattleActionType.None, arena.Battle.TimeUnitsReserved);
        context.Screens.HandleInput(GameInputEvent.PointerButtonChange(GameInputEventKind.PointerPressed, 0, 0, x + 4 * 22 + 2, 288, 1, 1));
        Assert.False(arena.Battle.KneelReserved);
        // The rules refuse a reserve for a unit that is not the side's.
        var refused = arena.Campaign.Execute(new SetTimeUnitReserve(arena.Alien.Id, BattleReserveKind.SnapShot));
        Assert.Equal("battle.selection-unit-unknown", Refusal(refused));
        Assert.Equal((int)BattleActionType.None, arena.Battle.TimeUnitsReserved);
    }

    [Fact]
    public void TheSelectionButtonsAndTheInventoryEndAnAimAndAnAimHidesThePathPreview()
    {
        var (arena, _, _) = BattleShootingTests.Load();
        var (context, _, model) = ClientOf(arena);
        model.Preview(12, 12);
        Assert.NotNull(model.Path);
        // btnActionMenuItemClick removes the path preview; targeting draws none.
        Pick(context, model, BattleAimKind.AimedShot);
        Assert.Null(model.Path);
        // selectNextPlayerUnit's cancelAllActions: the aim ends and the preview comes back.
        Assert.True(model.Next());
        Assert.Null(model.Aiming);
        model.Refresh();
        Assert.NotNull(model.Path);
        Pick(context, model, BattleAimKind.AimedShot);
        Assert.True(model.Skip());
        Assert.Null(model.Aiming);
        // btnInventoryClick's cancelAllActions (the skip left the only soldier unselected).
        Assert.True(model.Select(arena.Soldier.Id));
        Pick(context, model, BattleAimKind.AimedShot);
        Press(context, 'i');
        Assert.Null(model.Aiming);
    }

    [Fact]
    public void ALaunchThatStopsKeepsTheAimAndItsWaypointsAndOneThatFlewEndsIt()
    {
        // popState's actionFailed: a launch whose result is set (a later waypoint out of range) leaves the targeting and the waypoints as they were.
        var (arena, _, _) = BattleLaunchTests.Load(gun: "costAimed: {time: 30}\naccuracyAimed: 100\nwaypoints: 3\nmaxRange: 8");
        var (context, _, model) = ClientOf(arena);
        Pick(context, model, BattleAimKind.Launch);
        Assert.True(model.Target(12, 10, force: false));
        Assert.True(model.Target(25, 10, force: false));
        Assert.True(model.Launch());
        Assert.Contains(model.LastEvents, static @event => @event is UnitAttackStopped { Reason: "STR_OUT_OF_RANGE" });
        Assert.Equal(BattleAimKind.Launch, model.Aiming!.Value.Kind);
        Assert.Equal([new BattlePosition(12, 10, 0), new BattlePosition(25, 10, 0)], model.Waypoints);
        // Within range the missile flies and the launch ends the aim.
        Assert.True(model.CancelLast());
        Assert.True(model.Launch());
        Assert.DoesNotContain(model.LastEvents, static @event => @event is UnitAttackStopped);
        Assert.Null(model.Aiming);
        Assert.Empty(model.Waypoints);
    }

    [Fact]
    public void TheReserveButtonsKeepAnAimInProgressAndALaunchsWaypoints()
    {
        // btnReserveClick only sets the reserve (and refreshes a previewed path): the targeting and its waypoints stay.
        var arena = BattleDamageTests.Duel(gun: "costAimed: {time: 30}\naccuracyAimed: 100\nwaypoints: 3", clip: "power: 40\ndamageType: 2\ndamageAlter: {RandomType: 3}");
        arena.Stand(arena.Alien, 15, 10, 6);
        arena.Soldier.Stats = arena.Soldier.Stats with { TimeUnits = 100, Firing = 100 };
        arena.Soldier.TimeUnits = 100;
        arena.See();
        var (context, screen, model) = ClientOf(arena);
        Pick(context, model, BattleAimKind.Launch);
        context.Screens.HandleInput(ClickTile(screen, 12, 12));
        Press(context, BattlescapeClient.ReserveKey);
        Assert.Equal((int)BattleActionType.SnapShot, arena.Battle.TimeUnitsReserved);
        Assert.Equal(BattleAimKind.Launch, model.Aiming!.Value.Kind);
        Assert.Equal([new BattlePosition(12, 12, 0)], model.Waypoints);
        Press(context, BattlescapeClient.ReserveKneelKey);
        Assert.True(arena.Battle.KneelReserved);
        Assert.Equal(BattleAimKind.Launch, model.Aiming!.Value.Kind);
        Assert.Single(model.Waypoints);
    }

    [Fact]
    public void AReserveFromAUnitThatIsNotTheWritingSlotChangesNothingAndTheKneelReserveReportsItsState()
    {
        var (arena, _, _) = BattleShootingTests.Load();
        var runtime = new BattleRuntime(arena.Battle, arena.Rules, new ScriptedDraws(0), null);
        arena.Battle.AddUnit(new BattleUnit(912, BattleFaction.Player, BattleController.Human(1)) { Stats = arena.Soldier.Stats, Health = 10, ArmorId = arena.Soldier.ArmorId });
        var revision = arena.Battle.Revision;
        var other = Assert.IsType<TimeUnitReserveChanged>(Assert.Single(runtime.Execute(new SetTimeUnitReserve(912, BattleReserveKind.SnapShot)).Events));
        // The command succeeds and reports it wrote nothing (ADR 0039): the saved battle is as it was.
        Assert.Equal((false, (int)BattleActionType.None), (other.Saved, arena.Battle.TimeUnitsReserved));
        Assert.False(Assert.IsType<TimeUnitReserveChanged>(Assert.Single(runtime.Execute(new ToggleKneelReserve(912)).Events)).Saved);
        Assert.False(arena.Battle.KneelReserved);
        Assert.Equal(revision, arena.Battle.Revision);
        var kneel = Assert.IsType<TimeUnitReserveChanged>(Assert.Single(runtime.Execute(new ToggleKneelReserve(arena.Soldier.Id)).Events));
        Assert.Equal((true, true, BattleReserveKind.None), (kneel.Saved, kneel.KneelReserved, kneel.Kind));
    }

    [Fact]
    public void AReserveNeedsNoLiveSelectedUnitOnlyAUnitOfTheSendingSide()
    {
        var (arena, _, _) = BattleShootingTests.Load();
        var (context, screen, model) = ClientOf(arena);
        // The selected unit was knocked out by a reaction: the reference still sets the reserve (allowButtons asks for the side only).
        arena.Soldier.Status = BattleUnitStatus.Unconscious;
        arena.Battle.MarkChanged();
        context.Screens.HandleInput(Key(0x4000003au + 1));
        Assert.Equal((int)BattleActionType.SnapShot, arena.Battle.TimeUnitsReserved);
        Assert.Equal(string.Empty, screen.Message);
        Assert.True(model.ToggleKneelReserve());
        Assert.True(arena.Battle.KneelReserved);
        // A unit of another side is still refused.
        Assert.Equal("battle.selection-unit-unknown", Refusal(arena.Campaign.Execute(new SetTimeUnitReserve(arena.Alien.Id, BattleReserveKind.None))));
    }

    [Fact]
    public void TheCommandReportsWhetherItWroteTheSavedReserveAndThePanelShowsTheWounds()
    {
        var (arena, _, _) = BattleShootingTests.Load();
        var result = arena.Campaign.Execute(new SetTimeUnitReserve(arena.Soldier.Id, BattleReserveKind.SnapShot));
        var changed = Assert.IsType<TimeUnitReserveChanged>(Assert.Single(result.Events));
        Assert.Equal((BattleReserveKind.SnapShot, false, true), (changed.Kind, changed.KneelReserved, changed.Saved));
        arena.Soldier.FatalWounds[0] = 2;
        var (_, screen, model) = ClientOf(arena);
        Assert.Equal(2, model.SelectedUnit!.FatalWounds);
        Tick(screen);
        var wounded = Ink(screen);
        arena.Soldier.FatalWounds[0] = 0;
        arena.Battle.MarkChanged();
        Tick(screen);
        // The health line carries the wound count, so it is not the line of an unwounded unit (its colour is a font group the test font does not show).
        var healthy = Ink(screen);
        Assert.NotEqual(healthy, wounded);
        Assert.Contains(wounded, static pixel => pixel != BattleUiContext.Color.Panel);
    }

    /// <summary>The pixels of the panel's health line (the third line of the selected unit's block).</summary>
    private static byte[] Ink(BattlescapeClient screen)
    {
        var pixels = new List<byte>();
        for (var y = 58; y < 70; y++)
            for (var x = BattlescapeClient.MapWidth + 8; x < 630; x++) pixels.Add(screen.Frame.GetPixel(x, y));
        return [.. pixels];
    }
    [Fact]
    public void ACarriedUnitsVisibleUnitButtonCentersOnItsCarrierAndALyingOneOnWhereItLies()
    {
        var (arena, _, _) = BattleShootingTests.Load();
        var (_, _, model) = ClientOf(arena);
        var lying = arena.Alien.Position!.Value;
        arena.Battle.SetTile(arena.Alien, new BattlePosition(0, 0, 0));
        arena.Map.SetUnitAt(arena.Tile(0, 0), null);
        arena.Alien.Position = null;
        arena.Alien.SavedPosition = lying;
        arena.Alien.Status = BattleUnitStatus.Unconscious;
        var body = arena.Battle.AddItemOnTile(new BattleItem(arena.Battle.TakeItemId(), "CORPSE") { CorpseUnitId = arena.Alien.Id }, lying);
        arena.Soldier.Stats = arena.Soldier.Stats with { Health = 100 };
        arena.Battle.MarkChanged();
        model.Refresh();
        Assert.True(model.CenterOnUnit(arena.Alien.Id));
        Assert.Equal(lying, model.Camera.Center);
        // Picked up, it is carried: the camera goes to the carrier.
        body.Tile = null;
        body.OwnerUnitId = arena.Soldier.Id;
        body.Slot = "STR_BACK_PACK";
        arena.Battle.MarkChanged();
        model.Refresh();
        Assert.True(model.CenterOnUnit(arena.Alien.Id));
        Assert.Equal(arena.Soldier.Position, model.Camera.Center);
        // A viewer is told the carrier's tile only when it sees the carrier: the alien's side has not spotted the soldier that carries its unit.
        var hostile = new BattleViewer(BattleFaction.Hostile);
        arena.Battle.SetSpotted(arena.Soldier, BattleFaction.Hostile, false);
        Assert.Null(arena.Battle.Overview(hostile).Units.First(unit => unit.Id == arena.Alien.Id).Whereabouts);
        arena.Battle.SetSpotted(arena.Soldier, BattleFaction.Hostile, true);
        Assert.Equal(arena.Soldier.Position, arena.Battle.Overview(hostile).Units.First(unit => unit.Id == arena.Alien.Id).Whereabouts);
    }

    // ---- the replay of events ----

    [Fact]
    public void EveryEventOfAnAttackHasItsReplayLine()
    {
        var at = new BattlePosition(16 * 3 + 5, 16 * 4 + 5, 24 * 2 + 3);
        ICampaignEvent[] events =
        [
            new UnitDied(5, 1), new UnitKnockedOut(6, 1), new UnitConverted(7, 70), new UnitIgnited(8, 3), new ItemUnprimed(1, 2),
            new UnitAttackStopped(1, 2, BattleAimKind.AimedShot, ""), new UnitAttackStopped(1, 2, BattleAimKind.AimedShot, "STR_NO_LINE_OF_FIRE"),
            new UnitCloseQuartersFailed(1, 2, new BattlePosition(1, 2, 0), 3, 4, 5), new UnitMeleeAttacked(1, 2, 3, at, 0, false),
            new UnitFired(1, 2, 3, BattleAimKind.Throw, [at], 3), new UnitFired(1, 2, 3, BattleAimKind.SnapShot, [], 5), new UnitFired(1, 2, 3, BattleAimKind.SnapShot, [], 9),
        ];
        var frames = BattleReplay.From(events);
        Assert.Equal(
        [
            "Unit 5 dies", "Unit 6 is knocked out", "Unit 7 turns into unit 70", "Unit 8 burns", "Unprimed", "AimedShot stops", "AimedShot stops: STR_NO_LINE_OF_FIRE",
            "Close quarters: unit 2 strikes first", "Unit 1 strikes and misses", "Unit 1 fires Throw (object)", "Unit 1 fires SnapShot (out of the map)", "Unit 1 fires SnapShot (nothing)",
        ], frames.Select(static frame => frame.Text));
        Assert.Equal(new BattlePosition(3, 4, 2), frames[8].Marker);
        Assert.Equal(new BattlePosition(3, 4, 2), frames[9].Marker);
        Assert.Null(frames[10].Marker);
        Assert.Equal(new BattlePosition(1, 2, 0), frames[7].Marker);
    }
}

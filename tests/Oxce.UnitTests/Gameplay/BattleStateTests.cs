using Oxce.Gameplay.Battles;
using Xunit;

namespace Oxce.UnitTests.Gameplay;

/// <summary>ADR 0038: viewer-scoped queries, per-faction spotting, revisions and battle invariants.</summary>
public sealed class BattleStateTests
{
    private static BattleState Create() => new("STR_UFO_CRASH_RECOVERY", "STR_SMALL_SCOUT", 40, 40, 4, 0, 0, 20);

    [Fact]
    public void AViewerSeesItsOwnFactionAndTheUnitsItHasSpotted()
    {
        var battle = Create();
        var soldier = new BattleUnit(7, BattleFaction.Player, BattleController.Human(0)) { Position = new(1, 2, 0) };
        var alien = new BattleUnit(BattleState.MaximumSoldierId, BattleFaction.Hostile, BattleController.Ai) { Position = new(30, 30, 1) };
        var civilian = new BattleUnit(BattleState.MaximumSoldierId + 1, BattleFaction.Neutral, BattleController.Ai);
        battle.AddUnit(soldier);
        battle.AddUnit(alien);
        battle.AddUnit(civilian);

        Assert.Equal([7], battle.Overview(BattleViewer.LocalPlayer).Units.Select(static unit => unit.Id));
        Assert.Equal([BattleState.MaximumSoldierId], battle.Overview(new BattleViewer(BattleFaction.Hostile)).Units.Select(static unit => unit.Id));

        var revision = battle.Revision;
        battle.SetSpotted(alien, BattleFaction.Player, true);
        Assert.Equal(revision + 1, battle.Revision);
        battle.SetSpotted(alien, BattleFaction.Player, true);
        Assert.Equal(revision + 1, battle.Revision);
        Assert.Equal([new BattleUnitView(7, BattleFaction.Player, new(1, 2, 0)),
            new BattleUnitView(BattleState.MaximumSoldierId, BattleFaction.Hostile, new(30, 30, 1))],
            battle.Overview(BattleViewer.LocalPlayer).Units);
        // Spotting is per faction: the neutral side has seen nobody.
        Assert.Equal([civilian.Id], battle.Overview(new BattleViewer(BattleFaction.Neutral)).Units.Select(static unit => unit.Id));
    }

    [Fact]
    public void TileIndicesFollowSavedBattleGameAndUnitsStayInsideTheMap()
    {
        var battle = Create();
        Assert.Equal(3 + 5 * 40 + 2 * 40 * 40, battle.TileIndex(new BattlePosition(3, 5, 2)));
        Assert.False(battle.Contains(new BattlePosition(40, 0, 0)));
        Assert.False(battle.Contains(new BattlePosition(0, 0, -1)));
        Assert.Throws<InvalidOperationException>(() =>
            battle.AddUnit(new BattleUnit(1, BattleFaction.Player, BattleController.Human(0)) { Position = new(0, 40, 0) }));
        battle.AddUnit(new BattleUnit(1, BattleFaction.Player, BattleController.Human(0)));
        Assert.Throws<InvalidOperationException>(() => battle.AddUnit(new BattleUnit(1, BattleFaction.Hostile, BattleController.Ai)));
        battle.Validate();
    }

    [Fact]
    public void MapSizesOutsideThePortsBoundsAreRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new BattleState("M", "D", BattleState.MaximumMapSide + 1, 10, 1, 0, 0, 20));
        Assert.Throws<ArgumentOutOfRangeException>(() => new BattleState("M", "D", 10, 10, BattleState.MaximumMapHeight + 1, 0, 0, 20));
        Assert.Throws<ArgumentOutOfRangeException>(() => new BattleState("M", "D", -10, 10, 1, 0, 0, 20));
        Assert.Throws<ArgumentOutOfRangeException>(() => BattleController.Human(-1));
    }
}

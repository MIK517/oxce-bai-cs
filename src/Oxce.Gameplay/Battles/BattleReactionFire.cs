using Oxce.Gameplay.Campaigns;

namespace Oxce.Gameplay.Battles;

/// <summary>
/// The action that drew a reaction (the <c>originalAction</c> of <c>checkReactionFire</c>): its type, the weapon, the target tile (null for a move) and how the unit moved (a step of
/// a walk, a kneel or a landing is the type <see cref="BattleActionType.Walk"/> or <see cref="BattleActionType.Kneel"/>).
/// </summary>
internal readonly record struct BattleReactionOrigin(BattleActionType Type, BattleItem? Weapon = null, BattlePosition? Target = null,
    BattleActionMove Move = BattleActionMove.Normal);

/// <summary>
/// Phase 7 branch 5 S9, third part: <c>TileEngine::checkReactionFire</c> and <c>tryReaction</c>. The units that spotted the actor react in the order of their reaction scores, each
/// one's snap shot or hit pushed to the back of the list of states (<c>statePushBack</c>) as a <see cref="ProjectileFlyState"/> or a <see cref="MeleeAttackState"/> that
/// <c>init</c> treats as a reaction because its unit is not of the side on turn.
/// </summary>
internal sealed partial class BattleState
{
    /// <summary>The most times a unit reacts to one action (the reference's <c>count > 10</c>).</summary>
    private const int MaximumReactions = 10;

    /// <summary>
    /// <c>BattlescapeGame::statePushBack</c>: the state goes to the end of the list, and into an empty list it starts at once (the kneel button's reactions, whose first one pays and
    /// turns its unit before the other reactors are asked).
    /// </summary>
    private void PushBack(QueuedState state)
    {
        // The walk and the fall are not states of the port's list: what they push to the back waits until they end (the reference's walk state is in front of the list).
        if (_deferredBack is not null)
        {
            _deferredBack.Add(state);
            return;
        }
        _states.Add(state);
        if (_states.Count == 1) state.Init();
    }

    /// <summary>What the running walk or fall pushed to the back of the list, in order, or null while none runs.</summary>
    private List<QueuedState>? _deferredBack;

    /// <summary>
    /// Whether the walk or fall that runs holds what it pushes (it is the outermost: a fall inside a walk leaves the pushes to the walk). The reference's fall state aborts the walk's
    /// path when a unit that landed drew a reaction: <see cref="_reactionDuringFall"/> tells the walk.
    /// </summary>
    private bool HoldBackPushes()
    {
        if (_deferredBack is not null) return false;
        _deferredBack = [];
        return true;
    }

    /// <summary>The held pushes go to the back of the list (<c>statePushBack</c>), after the explosions the walk or fall pushed behind itself.</summary>
    private void ReleaseBackPushes()
    {
        var held = _deferredBack;
        _deferredBack = null;
        if (held is null) return;
        foreach (var state in held) PushBack(state);
    }

    /// <summary>The unit the running command acts for (the reference's selected unit while it runs): set by <see cref="SelectionUnit"/>, cleared when the command ends.</summary>
    private BattleUnit? _commandActor;

    /// <summary>A unit that landed drew a reaction (<c>UnitFallBState</c> then aborts the path of the walk): set by <see cref="Land"/>, taken by the walk.</summary>
    private bool _reactionDuringFall;

    /// <summary>
    /// <c>TileEngine::checkReactionFire</c>: the units that see the actor (<see cref="SpottingUnits"/>, which also marks it spotted) react while the best of them has at least the
    /// actor's reaction score; each reaction lowers that unit's score by its reduction, and a unit reacts at most 10 times. A mind controlled hostile unit draws none. The reactions are
    /// states pushed to the back of the list; the result tells whether any was pushed.
    /// </summary>
    internal bool CheckReactionFire(BattleUnit unit, in BattleReactionOrigin original)
    {
        // reaction fire only triggered when the actioning unit is of the currently playing side, and is still on the map (alive)
        if (unit.Faction != Side || unit.Position is null) return false;
        var spotters = SpottingUnits(Sight(), unit);
        var result = false;
        // not mind controlled, or controlled by the player
        if (unit.Faction != unit.OriginalFaction && unit.Faction == BattleFaction.Hostile) return false;
        // get the first man up to bat.
        var reactor = GetReactor(spotters, unit);
        // start iterating through the possible reactors until the current unit is the one with the highest score.
        while (reactor is not null)
        {
            if (reactor.Count > MaximumReactions || !TryReaction(reactor, unit, original))
            {
                // can't make a reaction snapshot for whatever reason, boot this guy from the vector; one unit that can't react does not stop the rest
                spotters.Remove(reactor);
            }
            else
            {
                // nice shot, kid. don't get cocky.
                result = true;
                reactor.Score -= reactor.Reduction;
                reactor.Count += 1;
            }
            reactor = GetReactor(spotters, unit);
        }
        return result;
    }

    /// <summary>
    /// <c>TileEngine::tryReaction</c>: whether the reactor's weapon is usable and paid for, a hostile reactor holding an explosive shot only when the AI would set it off there
    /// (<see cref="ExplosiveEfficacy"/>), the three reaction scripts, the AI's score of the shot above 0 and the roll against the chance (always 100 for what is not a hit; a hit
    /// draws a reaction only with the extended melee reactions). The reaction state is pushed to the back of the list.
    /// </summary>
    private bool TryReaction(BattleReaction reaction, BattleUnit target, in BattleReactionOrigin original)
    {
        var unit = reaction.Unit;
        var weapon = reaction.Weapon!;
        var type = reaction.AttackType;
        if (!CanUseWeapon(ReactionResearch, weapon, unit, false, type).Usable) return false;

        var action = new BattleAimAction(unit, type, weapon, target.RequiredPosition);
        var cost = BattleActionCost.For(RequiredRules, type, unit, weapon);
        if (AmmoForAction(weapon, ActionIndex(type)!.Value) is not { } ammo || !cost.HaveTimeUnits()) return false;

        // hostile units will go into an "aggro" state when they react.
        if (unit.Faction == BattleFaction.Hostile)
        {
            unit.Ai ??= new BattleAiState(unit.OriginalFaction);
            var radius = ExplosionRadius(ammo, new BattleAttack(type, unit, weapon, ammo));
            if (type != BattleActionType.Hit && radius > 0 && ExplosiveEfficacy(action.Target, unit, radius) == 0) return false;
        }

        var meleeReactionChance = RequiredRules.Campaign.ExtendedMeleeReactions > 0 ? 100 : 0;
        var reactionChance = original.Type != BattleActionType.Hit ? 100 : meleeReactionChance;
        var from = CentreVoxel(unit);
        var to = CentreVoxel(target);
        var dist = (int)Math.Ceiling(Math.Sqrt((from.X - to.X) * (from.X - to.X) + (from.Y - to.Y) * (from.Y - to.Y)));
        var arc = ArcDirection(BattleGeometry.DirectionTo(from, to), unit.Direction);
        var originalTarget = original.Target is { } at && Contains(at) ? UnitOnTile(Map.TileIndex(at)) : null;
        var context = new BattleReactionHookContext(target, unit, weapon, type, reaction.Count, original.Weapon, original.Type, originalTarget, (int)original.Move, arc);
        var values = new BattleReactionHookValues(reactionChance, dist);
        if (original.Weapon is not null) values = Hooks.ReactionWeaponAction(context, values);

        // Use the AI to check whether our shot should actually hit (for some reason the unit may have no AI routine assigned).
        unit.Ai ??= new BattleAiState(unit.OriginalFaction);
        if (ReactionScore(action, target) <= 0) return false;

        values = Hooks.ReactionUnitAction(context, values);
        values = Hooks.ReactionUnitReaction(context, values);

        if (!RandomChance.Percent(Random, values.Chance)) return false;
        // (HITLOG_REACTION_FIRE is the client's hit log, not the battle's.)
        if (type == BattleActionType.Hit) PushBack(new MeleeAttackState(this, unit, weapon, action.Target, 0, cost));
        else PushBack(new ProjectileFlyState(this, action, cost));
        return true;
    }

    /// <summary>
    /// The start of a reaction state (<c>reactionShoot</c> of <c>init</c>): the unit it reacts to must still stand on the target tile, in play, and be the selected unit (the
    /// command's unit, else the battle's selection): a unit that landed without being the selected one draws reactions that cancel here. The reactor turns to it for free.
    /// Returns false when the reaction is cancelled.
    /// </summary>
    private bool StartReaction(BattleUnit unit, BattlePosition target)
    {
        var victim = UnitOnTile(Map.TileIndex(target));
        var selected = _commandActor ?? (SelectedUnitId is { } id ? UnitById(id) : null);
        // target is dead: cancel the shot.
        if (victim is null || victim.IsOut || victim.IsOutThresholdExceeded || victim != selected) return false;
        unit.LookAt(target, unit.TurretType != -1);
        while (unit.Status == BattleUnitStatus.Turning) unit.Turn(unit.TurretType != -1);
        return true;
    }
}

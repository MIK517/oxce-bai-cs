using Oxce.Mods.Rulesets.Runtime;

namespace Oxce.Gameplay.Battles;

/// <summary>
/// A unit's sight (<c>BattleUnit::_maxViewDistanceAtDark</c>, <c>_maxViewDistanceAtDay</c>, <c>_psiVision</c>, <c>_visibilityThroughSmoke</c> and
/// <c>_visibilityThroughFire</c>): set where the BattleUnit constructors set them, from the armor and, for a soldier, its bonuses. Runtime state,
/// never saved: a loaded battle's units get them from the same constructors.
/// </summary>
internal readonly record struct BattleUnitVision(int MaxViewDistanceAtDark, int MaxViewDistanceAtDay, int PsiVision, int VisibilityThroughSmoke,
    int VisibilityThroughFire)
{
    /// <summary>The default of a unit built outside the constructors: nine tiles in the dark, twenty by day, no psi or heat vision.</summary>
    public static BattleUnitVision Default { get; } = new(9, 20, 0, 0, 100);

    /// <summary>BattleUnit(Unit*): hostile units see as far in the dark as by day unless the armor says otherwise, the rest nine tiles.</summary>
    public static BattleUnitVision ForUnit(RuntimeRuleCatalog rules, RuntimeBattleArmor armor, BattleFaction originalFaction)
    {
        var maximum = rules.Campaign.MaximumViewDistance;
        return new BattleUnitVision(
            armor.VisibilityAtDark != 0 ? armor.VisibilityAtDark : originalFaction == BattleFaction.Hostile ? maximum : 9,
            armor.VisibilityAtDay != 0 ? armor.VisibilityAtDay : maximum,
            armor.PsiVision, armor.VisibilityThroughSmoke, armor.VisibilityThroughFire);
    }

    /// <summary>BattleUnit(Soldier*): the armor's values plus the soldier's bonuses', clamped between 1 tile and the mod's maximum.</summary>
    public static BattleUnitVision ForSoldier(RuntimeRuleCatalog rules, RuntimeBattleArmor armor, IEnumerable<RuntimeSoldierBonusRule> bonuses)
    {
        var maximum = rules.Campaign.MaximumViewDistance;
        int dark = 0, day = 0, psi = 0, smoke = 0, fire = 0;
        foreach (var bonus in bonuses)
        {
            dark += bonus.VisibilityAtDark;
            day += bonus.VisibilityAtDay;
            psi += bonus.PsiVision;
            smoke += bonus.VisibilityThroughSmoke;
            fire += bonus.VisibilityThroughFire;
        }
        return new BattleUnitVision(
            BattleSight.Clamp((armor.VisibilityAtDark != 0 ? armor.VisibilityAtDark : 9) + dark, 1, maximum),
            BattleSight.Clamp((armor.VisibilityAtDay != 0 ? armor.VisibilityAtDay : maximum) + day, 1, maximum),
            armor.PsiVision + psi, armor.VisibilityThroughSmoke + smoke, armor.VisibilityThroughFire + fire);
    }
}

/// <summary>
/// The unit's field of view lists (Phase 7 branch 4 S4): the units it sees (<c>_visibleUnits</c>), the tiles it sees (<c>_visibleTiles</c>) and the
/// tiles it has a line of fire to (<c>_lofTiles</c>). Runtime state: the field of view recomputes them on load and at the first turn.
/// </summary>
internal sealed partial class BattleUnit
{
    private readonly List<int> _visibleUnits = [];
    private readonly List<int> _visibleTiles = [];
    private readonly HashSet<int> _visibleTilesLookup = [];
    private readonly List<int> _lofTiles = [];
    private readonly HashSet<int> _lofTilesLookup = [];

    public BattleUnitVision Vision { get; set; } = BattleUnitVision.Default;

    /// <summary>BattleUnit::getVisibleUnits: the IDs of the opposing units this unit sees, in the order the reference's list holds them.</summary>
    public IReadOnlyList<int> VisibleUnits => _visibleUnits;

    /// <summary>BattleUnit::getVisibleTiles: tile indexes in the order they were added.</summary>
    public IReadOnlyList<int> VisibleTiles => _visibleTiles;

    /// <summary>BattleUnit::getLofTiles.</summary>
    public IReadOnlyList<int> LofTiles => _lofTiles;

    /// <summary>
    /// BattleUnit::addToVisibleUnits: the unit joins the units spotted this turn (once) and the visible units. True if it is new to the
    /// visible units.
    /// </summary>
    public bool AddToVisibleUnits(int unitId)
    {
        if (!UnitsSpottedThisTurn.Contains(unitId)) UnitsSpottedThisTurn.Add(unitId);
        if (_visibleUnits.Contains(unitId)) return false;
        _visibleUnits.Add(unitId);
        return true;
    }

    /// <summary>BattleUnit::removeFromVisibleUnits: the last unit takes its place in the list.</summary>
    public bool RemoveFromVisibleUnits(int unitId)
    {
        var index = _visibleUnits.IndexOf(unitId);
        if (index < 0) return false;
        _visibleUnits[index] = _visibleUnits[^1];
        _visibleUnits.RemoveAt(_visibleUnits.Count - 1);
        return true;
    }

    /// <summary>BattleUnit::hasVisibleUnit: a unit of the same faction always counts, others when the list holds them.</summary>
    public bool HasVisibleUnit(BattleUnit other) => Faction == other.Faction || _visibleUnits.Contains(other.Id);

    public void ClearVisibleUnits() => _visibleUnits.Clear();

    public bool HasVisibleTile(int tile) => _visibleTilesLookup.Contains(tile);

    internal bool AddVisibleTile(int tile)
    {
        if (!_visibleTilesLookup.Add(tile)) return false;
        _visibleTiles.Add(tile);
        return true;
    }

    internal bool AddLofTile(int tile)
    {
        if (!_lofTilesLookup.Add(tile)) return false;
        _lofTiles.Add(tile);
        return true;
    }

    /// <summary>BattleUnit::clearVisibleTiles: every tile the unit saw loses one from its visibility count (the map's), and the line of fire tiles go.</summary>
    internal void ClearVisibleTiles(BattleMap map)
    {
        foreach (var tile in _visibleTiles) map.SetVisibility(tile, map.Visibility(tile) - 1);
        _visibleTilesLookup.Clear();
        _visibleTiles.Clear();
        _lofTilesLookup.Clear();
        _lofTiles.Clear();
    }

    /// <summary>
    /// BattleUnit::getFloatHeight, getHeight: the voxels a unit stands above its tile's floor, and its height standing or kneeling
    /// (<see cref="BattleState.HeightsOf"/>).
    /// </summary>
    internal int HeightOf(RuntimeRuleCatalog rules)
    {
        var heights = BattleState.HeightsOf(rules, this);
        return Kneeled ? heights.Kneel : heights.Stand;
    }

    internal int FloatHeightOf(RuntimeRuleCatalog rules) => BattleState.HeightsOf(rules, this).Floating;

    /// <summary>BattleUnit::getLoftemps: the unit's line of fire template of one of its tiles.</summary>
    internal int LoftempsOf(RuntimeRuleCatalog rules, int entry = 0)
    {
        var loftemps = BattleState.ArmorOf(rules, this).Loftemps;
        return (uint)entry < (uint)loftemps.Count ? loftemps[entry] : throw new InvalidOperationException($"Battle unit {Id} has no line of fire template {entry}.");
    }

    /// <summary>BattleUnit::SMALL_MAX_RADIUS and BIG_MAX_RADIUS.</summary>
    private const int SmallMaximumRadius = 5;
    private const int BigMaximumRadius = 15;

    /// <summary>BattleUnit::getRadiusVoxels: the first template's number for a small unit (at most 5), 15 for a big one.</summary>
    internal int RadiusVoxels(RuntimeRuleCatalog rules)
    {
        var radius = LoftempsOf(rules);
        return ArmorSize switch
        {
            1 => Math.Min(radius, SmallMaximumRadius),
            2 => BigMaximumRadius,
            _ => throw new InvalidOperationException($"Battle unit {Id} is of armor size {ArmorSize}; the reference supports sizes 1 and 2."),
        };
    }
}

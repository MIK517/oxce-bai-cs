using Oxce.Core.Random;
using Oxce.Gameplay.Battles.Generation;
using Oxce.Gameplay.Campaigns;
using Oxce.Mods.Rulesets.Content;
using Oxce.Mods.Rulesets.Runtime;

namespace Oxce.Gameplay.Battles;

/// <summary>The reasons the briefing and the first turn refuse; each names the branch that owns the missing work.</summary>
internal static class BattleFlowReasons
{
    internal static readonly CampaignReason NotInBriefing = new("battle.not-in-briefing",
        "The battle is past its briefing.");

    internal static readonly CampaignReason BriefingAlreadyOpen = new("battle.briefing-already-open",
        "The briefing has already named the mission.");

    internal static readonly CampaignReason NotBeforeFirstTurn = new("battle.not-before-first-turn",
        "The first turn starts when the pre-battle inventory closes after the briefing.");

    internal static readonly CampaignReason PreviewUnsupported = new("battle.preview-unsupported",
        "The craft deployment preview needs its own battle mode, which arrives with the craft screens (Phase 8).");

    internal static readonly CampaignReason NoLiveAliens = new("battle.no-live-aliens",
        "The battle has no live aliens once its primed items have spawned; the reference then ends in the aliens' crash screen, and the battle outcome arrives with Phase 7 branch 7.");

    internal static readonly CampaignReason CasualtiesUnsupported = new("battle.casualties-unsupported",
        "A unit is past the out threshold but not yet out; the casualty check the battlescape runs when it opens arrives with turn processing (Phase 7 branch 6).");

    internal static readonly CampaignReason NoLiveSoldiers = new("battle.no-live-soldiers",
        "A battle with no live X-COM soldiers ends the battle, which arrives with the battle outcome (Phase 7 branch 7).");

    internal static readonly CampaignReason ReinforcementsUnsupported = new("battle.reinforcements-unsupported",
        "A reinforcement wave of the deployment could deploy at the start; deploying reinforcements arrives with turn processing (Phase 7 branch 6).");

    internal static readonly CampaignReason FlareBonusUnsupported = new("battle.flare-power-bonus-unsupported",
        "A flare's glow range is its power plus the damage bonus of its rule; evaluating a damage bonus script arrives with Phase 7 branch 8.");

    internal static readonly CampaignReason SpawnContentInvalid = new("battle.generation-failed",
        "A primed spawn item spawns a unit, but the mod lists no ten alien item levels for the battle's month.");

    internal static readonly CampaignReason VisibilityScriptUnsupported = new("battle.visibility-script-unsupported",
        "The battle's rules bind a visibility script (visibilityUnit), which changes whether a unit is seen; script bindings in battle arrive with Phase 7 branch 8.");

    internal static readonly CampaignReason LineOfFireDataInvalid = new("battle.line-of-fire-data-invalid",
        "A unit's armor or a map object refers to a line of fire template that LOFTEMPS.DAT lacks, where the reference's sight would throw.");

    internal static readonly CampaignReason NewTurnScriptUnsupported = new("battle.new-turn-script-unsupported",
        "The battle's rules bind a new-turn script (newTurnUnit or newTurnItem); script bindings in battle arrive with Phase 7 branch 8.");

    internal static readonly CampaignReason TurnRecoveryScriptUnsupported = new("battle.turn-recovery-script-unsupported",
        "A unit's time unit or energy recovery is scripted or comes from soldier bonuses; recovery scripts arrive with Phase 7 branch 8.");

    internal static readonly CampaignReason TurnOneReviveUnsupported = new("battle.turn-one-revive-unsupported",
        "An unconscious unit that would wake up or an infected unit that would turn at the first turn needs turn processing (Phase 7 branch 6).");
}

/// <summary>BattlescapeGame::tallyUnits' live counts: aliens (units of the aliens' side) and X-COM soldiers.</summary>
internal readonly record struct BattleTally(int LiveAliens, int LiveSoldiers);

/// <summary>
/// The briefing and the first turn (Phase 7 branch 3 S3): <c>BriefingState</c>'s naming, <c>spawnFromPrimedItems</c>,
/// <c>tallyUnits</c>, <c>NextTurnState</c>'s turn-0 work, <c>SavedBattleGame::startFirstTurn</c> with
/// <c>randomizeItemLocations</c>, <c>resetUnitTiles</c> and <c>BattleUnit::prepareNewTurn(false)</c>.
/// </summary>
internal sealed partial class BattleState
{
    private const int GrenadeBattleType = 4;
    private const int RightLeg = 4;
    private const int LeftLeg = 5;
    private const int Torso = 1;

    /// <summary>Where the battle stands: the briefing, the pre-battle inventory, or in progress.</summary>
    public BattlePhase Phase { get; internal set; } = BattlePhase.Briefing;

    /// <summary>Whether the briefing's craft is a craft (else a base), and the base of a base defense (-1 for none).</summary>
    public bool BriefingFromCraft { get; internal set; }

    public int BriefingBaseId { get; internal set; } = -1;

    /// <summary>Whether the briefing has named the mission (BriefingState's constructor runs once).</summary>
    public bool BriefingOpened { get; internal set; }
    // (Not saved: CampaignBattles derives the craft or base from the campaign's inBattlescape flags when it restores a battle before turn 1.)

    /// <summary>
    /// BriefingState's constructor: the target is the craft's destination (when it has one), the craft or base line is set,
    /// and the mod's operation names replace the target with a random name from <see cref="Seedless"/>.
    /// </summary>
    internal void NameMission(RuntimeRuleCatalog rules, string destination, string craftOrBase)
    {
        if (BriefingFromCraft && destination.Length != 0) TargetName = destination;
        CraftOrBaseName = craftOrBase;
        var first = rules.Campaign.OperationNamesFirst;
        if (first.Count != 0)
        {
            var name = first[Seedless.NextInclusive(0, first.Count - 1)];
            var last = rules.Campaign.OperationNamesLast;
            if (last.Count != 0) name += " " + last[Seedless.NextInclusive(0, last.Count - 1)];
            TargetName = name;
        }
        BriefingOpened = true;
        Revision++;
    }

    /// <summary>
    /// <c>BattlescapeGame::checkForCasualties</c>'s victims, which the battlescape's constructor runs for every battle it opens
    /// (<c>hiddenExplosion</c>): a unit that is not dead and has no health left, or that is not unconscious and whose stun reaches
    /// its health, whatever its spawn unit. The reference kills, stuns or converts them; turn processing (Phase 7 branch 6) does,
    /// so the briefing and the running battle refuse by name while one exists.
    /// </summary>
    internal bool HasUncheckedCasualty() =>
        _units.Any(static unit => unit.Status != BattleUnitStatus.IgnoreMe && unit.Status != BattleUnitStatus.Dead &&
            (unit.Health <= 0 || unit.StunLevel >= unit.Health && unit.Status != BattleUnitStatus.Unconscious));

    /// <summary>BattlescapeGame::tallyUnits for the counts the briefing and the first turn read: units that are not out (or are over
    /// the out threshold but turn into another unit), by original faction. Surrender mode, captured units and the exit areas
    /// are branch 7's. A surrendering alien still counts (surrender mode is branch 7's).</summary>
    internal BattleTally Tally()
    {
        var aliens = 0;
        var soldiers = 0;
        foreach (var unit in _units)
        {
            if (unit.IsOut || unit.IsOutThresholdExceeded && unit.SpawnUnit.Length == 0) continue;
            if (unit.OriginalFaction == BattleFaction.Hostile)
            {
                aliens++;
            }
            else if (unit.OriginalFaction == BattleFaction.Player && !unit.SummonedPlayerUnit)
            {
                if (unit.Faction == BattleFaction.Player) soldiers++;
                else aliens++;
            }
        }
        return new(aliens, soldiers);
    }

    /// <summary>
    /// BattlescapeGame::spawnFromPrimedItems' list: items on a tile that spawn a unit or an item, are not X-COM's or a unit's
    /// special weapon, and are grenades primed with a fuse that ran out (timer 0, enabled).
    /// </summary>
    internal List<BattleItem> PrimedSpawnItems(RuntimeRuleCatalog rules)
    {
        var result = new List<BattleItem>();
        foreach (var item in _items)
        {
            if (item.Tile is null || item.XcomProperty || item.IsSpecialWeapon) continue;
            if (!rules.Battle.Equipment.Items.TryGetValue(item.ItemType, out var battleRule) || !battleRule.Spawns) continue;
            if (rules.Items.GetRequiredValue(item.ItemType).BattleType == GrenadeBattleType && item.FuseTimer == 0 && item.FuseEnabled)
                result.Add(item);
        }
        return result;
    }

    /// <summary>Whether a primed item spawns a unit while the mod lists no ten alien item levels for the month (the reference throws).</summary>
    internal bool SpawnLacksItemLevels(RuntimeRuleCatalog rules)
    {
        var levels = rules.Campaign.AlienItemLevels;
        var missing = AlienItemLevel < 0 || AlienItemLevel >= levels.Count || levels[AlienItemLevel].Count < 10;
        return missing && PrimedSpawnItems(rules).Any(item => rules.Battle.Equipment.Items[item.ItemType].SpawnUnit.Length != 0);
    }

    /// <summary>The briefing is confirmed: the pre-battle inventory follows.</summary>
    internal void EnterInventory(RuntimeRuleCatalog rules)
    {
        Phase = BattlePhase.Inventory;
        // InventoryState::init: a selected unit without an inventory gives way to the next unit that has one. When none has (only vehicles),
        // the reference closes the inventory at once; the selection then stays and the client sends StartFirstTurn.
        if (SelectedUnitId is { } id && UnitById(id) is { } selected && !HasInventory(rules, selected))
            SelectedUnitId = SelectPlayerUnit(rules, selected, 1, checkReselect: false, setReselect: false, checkInventory: true)?.Id;
        Revision++;
    }

    /// <summary>
    /// BattlescapeGame::spawnFromPrimedItems: for each primed item, spawnNewUnit, spawnNewItem, then the item leaves the game
    /// (removeItem), whether or not anything spawned.
    /// </summary>
    internal (int Units, int Items) SpawnFromPrimedItems(RuntimeContent content, int difficulty, IRandomSource random)
    {
        var rules = content.RuntimeRules;
        var equipment = new UnitEquipment(this, rules, random);
        var units = 0;
        var items = 0;
        foreach (var item in PrimedSpawnItems(rules))
        {
            var rule = rules.Battle.Equipment.Items[item.ItemType];
            if (SpawnNewUnit(item, rule, rules, equipment, difficulty, random)) units++;
            if (SpawnNewItem(item, rule, equipment, random)) items++;
            RemoveItem(item);
        }
        return (units, items);
    }

    private BattleUnit? OwnerOf(BattleItem item)
    {
        var id = item.OwnerUnitId ?? item.PreviousOwnerUnitId;
        return id is { } owner ? _units.Find(unit => unit.Id == owner) : null;
    }

    /// <summary>spawnNewUnit's faction: the rule's, or the item owner's for -1 (any other value, or no owner, is the aliens').</summary>
    private BattleFaction SpawnFaction(BattleItem item, RuntimeBattleItem rule) =>
        rule.SpawnUnitFaction == -1 && OwnerOf(item) is { } owner ? owner.Faction : rule.SpawnUnitFaction switch
        {
            0 => BattleFaction.Player,
            1 => BattleFaction.Hostile,
            2 => BattleFaction.Neutral,
            _ => BattleFaction.Hostile,
        };

    /// <summary>Whether a primed item could spawn a live alien (a unit on the aliens' side, drawn with a chance over 0).</summary>
    internal bool PrimedSpawnsMayAddAliens(RuntimeRuleCatalog rules) =>
        PrimedSpawnItems(rules).Any(item =>
        {
            var rule = rules.Battle.Equipment.Items[item.ItemType];
            return rule.SpawnUnit.Length != 0 && rule.SpawnUnitChance > 0 && SpawnFaction(item, rule) == BattleFaction.Hostile;
        });

    private bool SpawnNewUnit(BattleItem item, RuntimeBattleItem rule, RuntimeRuleCatalog rules, UnitEquipment equipment, int difficulty,
        IRandomSource random)
    {
        if (rule.SpawnUnit.Length == 0 || !RandomChance.Percent(random, rule.SpawnUnitChance)) return false;
        var owner = OwnerOf(item);
        var faction = SpawnFaction(item, rule);
        var (unitRule, armor) = UnitRule(rules, rule.SpawnUnit);
        // createTempUnit: the next ID after the last unit's; a player unit made this way is a summoned one.
        var unit = TempUnit(rules, (_units.Count == 0 ? -1 : _units[^1].Id) + 1, faction,
            faction == BattleFaction.Player ? BattleController.Human(0) : BattleController.Ai, rule.SpawnUnit, unitRule, armor, difficulty,
            summoned: faction == BattleFaction.Player);
        if (BattlePlacement.ValidForUnit(Map, unit, item.Tile!.Value, surrounding: true, startDirection: 0) is not { } position) return false;
        var direction = random.NextInclusive(0, 7);
        // A tank is armed with its weapon (the item named like the unit) and that weapon's clip.
        if (rules.Battle.Equipment.Items.TryGetValue(unit.UnitType, out var weaponRule) && weaponRule.Fixed)
        {
            equipment.CreateItemForUnit(unit.UnitType, unit, fixedWeapon: true);
            if (equipment.VehicleClipAmmo(unit.UnitType) is { } clip && equipment.CreateItemForUnit(clip, unit) is { } ammo)
                ammo.AmmoQuantity = equipment.VehicleClipSize(unit.UnitType);
            unit.TurretType = weaponRule.TurretType;
        }
        var itemLevel = equipment.DrawItemLevel();
        unit.Position = position;
        unit.SetDirection(direction);
        unit.TimeUnits = 0;
        unit.PreviousOwnerId = owner?.Id;
        AddUnit(unit);
        SetSpotted(unit, BattleFaction.Player, faction == BattleFaction.Player);
        equipment.InitUnit(unit, unitRule, "", itemLevel);
        // applyGravity on the unit's tile: it falls with the first turn's falls, or its tile's items drop.
        ApplyGravity(unit.Position);
        return true;
    }

    private bool SpawnNewItem(BattleItem item, RuntimeBattleItem rule, UnitEquipment equipment, IRandomSource random)
    {
        if (rule.SpawnItem.Length == 0 || !RandomChance.Percent(random, rule.SpawnItemChance)) return false;
        var owner = OwnerOf(item);
        var created = equipment.CreateItemForTile(rule.SpawnItem, item.Tile);
        created.PreviousOwnerUnitId = owner?.Id;
        ApplyGravity(created.Tile);
        return true;
    }

    /// <summary>SavedBattleGame::removeItem: a special weapon stays; the item and the ammo loaded in it leave the battle's items.</summary>
    internal void RemoveItem(BattleItem item)
    {
        if (item.IsSpecialWeapon || !_items.Remove(item)) return;
        MoveToOwner(item, null);
        foreach (var slot in item.AmmoSlots)
            if (slot is { } id && id != item.Id) _items.RemoveAll(ammo => ammo.Id == id);
        Revision++;
    }

    /// <summary>
    /// NextTurnState::checkBugHuntMode on the first turn: bug hunt starts when the minimum turn came and the aliens left are
    /// few, hidden, no leaders and powerless (no weapon in hand, low morale or too many time units left).
    /// </summary>
    internal void CheckBugHuntMode(RuntimeRuleCatalog rules)
    {
        var campaign = rules.Campaign;
        if (Math.Max(Turn, 1) < BughuntMinTurn || BughuntMode) return;
        var count = 0;
        foreach (var unit in _units)
        {
            if (unit.IsOut || unit.OriginalFaction != BattleFaction.Hostile) continue;
            if (unit.IsSpottedBy(BattleFaction.Player) || unit.Rank <= campaign.BughuntRank) return;
            if (++count > campaign.BughuntMaxEnemies) return;
            var hasWeapon = InventoryOf(unit).Any(static item => item.Slot is "STR_LEFT_HAND" or "STR_RIGHT_HAND");
            var ruleTime = rules.Battle.Equipment.Units.TryGetValue(unit.UnitType, out var unitRule) ? unitRule.Stats.GetValueOrDefault("tu") : 0;
            if (hasWeapon && unit.Morale >= campaign.BughuntLowMorale && unit.TimeUnits <= ruleTime * campaign.BughuntTimeUnitsLeft / 100) return;
        }
        BughuntMode = true;
    }

    /// <summary>
    /// What stops <see cref="StartFirstTurn"/> before it changes anything: a new-turn or recovery script the rules bind (the
    /// reference runs them; script bindings in battle are branch 8's), and an unconscious unit that would wake up or an
    /// infected unit that would turn (branch 6's turn processing).
    /// </summary>
    internal CampaignReason? FirstTurnRefusal(RuntimeContent content, BattleRestoreCampaign campaign)
    {
        if (HasEventPlan(content, "newTurnUnit") || HasEventPlan(content, "newTurnItem")) return BattleFlowReasons.NewTurnScriptUnsupported;
        foreach (var unit in _units)
        {
            if (unit.Status == BattleUnitStatus.IgnoreMe) continue;
            if (HasRuleScript(content, "armors", unit.ArmorId, "newTurnUnit")) return BattleFlowReasons.NewTurnScriptUnsupported;
        }
        foreach (var item in _items)
        {
            var owner = item.OwnerUnitId is { } id ? _units.Find(unit => unit.Id == id) : null;
            if (owner?.Status == BattleUnitStatus.IgnoreMe) continue;
            if (HasRuleScript(content, "items", item.ItemType, "newTurnItem")) return BattleFlowReasons.NewTurnScriptUnsupported;
        }
        if (VisibilityScriptRefusal(content) is { } visibility) return visibility;
        if (VoxelDataFault(content.RuntimeRules) is not null) return BattleFlowReasons.LineOfFireDataInvalid;
        foreach (var item in _items)
            if (item.ItemType is var type && content.RuntimeRules.Items.TryGet(type, out var handle) && content.RuntimeRules.Items[handle].Value.BattleType == FlareBattleType &&
                (content.RuntimeRules.Battle.Equipment.Items[type].StrengthApplied || HasRuleScript(content, "items", type, "damageBonusBonusStats") || HasEventPlan(content, "damageBonusBonusStats")))
                return BattleFlowReasons.FlareBonusUnsupported;
        var rules = content.RuntimeRules;
        foreach (var unit in _units)
        {
            if (unit.OriginalFaction != BattleFaction.Player || unit.IsOut) continue;
            if (HasEventPlan(content, "timeRecoveryBonusStats") || HasEventPlan(content, "energyRecoveryBonusStats") ||
                HasRuleScript(content, "armors", unit.ArmorId, "timeRecoveryBonusStats") ||
                HasRuleScript(content, "armors", unit.ArmorId, "energyRecoveryBonusStats"))
                return BattleFlowReasons.TurnRecoveryScriptUnsupported;
            if (unit.SoldierId is { } soldierId && campaign.Soldiers(soldierId) is { } soldier)
                foreach (var bonus in CampaignState.SoldierBonuses(soldier.Personal, rules))
                {
                    var id = rules.SoldierBonuses.GetExternalId(bonus);
                    if (HasRuleScript(content, "soldierBonuses", id, "timeSoldierRecoveryBonusStats") ||
                        HasRuleScript(content, "soldierBonuses", id, "energySoldierRecoveryBonusStats"))
                        return BattleFlowReasons.TurnRecoveryScriptUnsupported;
                }
        }
        // reviveUnconsciousUnits(false) wakes a small unconscious unit that is over the out threshold no more; convertInfected
        // turns a unit with the respawn flag.
        foreach (var unit in _units)
            if (unit.ArmorSize == 1 && unit.Status == BattleUnitStatus.Unconscious && !unit.IsOutThresholdExceeded ||
                !unit.IsOutThresholdExceeded && unit.Respawn)
                return BattleFlowReasons.TurnOneReviveUnsupported;
        return null;
    }

    /// <summary>
    /// The armor's <c>visibilityUnit</c> script (or a global event of that name) changes whether a unit is seen, in <c>TileEngine::visible</c>;
    /// battle scripts are branch 8's, so a battle whose units wear such an armor, or whose mods bind the event, stops.
    /// </summary>
    internal CampaignReason? VisibilityScriptRefusal(RuntimeContent content)
    {
        if (HasEventPlan(content, "visibilityUnit")) return BattleFlowReasons.VisibilityScriptUnsupported;
        foreach (var unit in _units)
            if (HasRuleScript(content, "armors", unit.ArmorId, "visibilityUnit")) return BattleFlowReasons.VisibilityScriptUnsupported;
        return null;
    }

    private static bool HasRuleScript(RuntimeContent content, string section, string id, string parser) =>
        content.Scripts.Any(artifact => artifact.OwnerSection == section && artifact.OwnerId == id && artifact.ParserName == parser);

    private static bool HasEventPlan(RuntimeContent content, string parser) =>
        content.EventPlans.Any(plan => plan.ParserName == parser && plan.Plan.Count != 0);

    /// <summary>
    /// SavedBattleGame::startFirstTurn: the ground items of the selected unit's tile (the craft inventory tile, <see cref="TileOf"/>)
    /// go to random storage tiles (a base defense), the units are put back on their tiles, the unit standing on that tile is selected, every live X-COM
    /// unit prepares its first turn, and the turn is 1. The new-turn scripts, reviving and converting are refused beforehand
    /// (<see cref="FirstTurnRefusal"/>). Without a selected unit on the map (a null dereference in the reference) the
    /// inventory-tile steps are skipped (ADR 0039).
    /// </summary>
    internal CampaignReason? StartFirstTurn(RuntimeContent content, IRandomSource random)
    {
        var equipment = new UnitEquipment(this, content.RuntimeRules, Seedless);
        var selected = SelectedUnitId is { } id ? _units.Find(unit => unit.Id == id) : null;
        var inventoryTile = selected is null ? null : TileOf(selected);
        if (inventoryTile is { } tile) RandomizeItemLocations(tile, random);
        ResetUnitTiles();
        if (inventoryTile is { } pile && Map.UnitAt(Map.TileIndex(pile)) is { } standing) SelectedUnitId = standing;
        foreach (var unit in _units)
            if (unit.OriginalFaction == BattleFaction.Player && !unit.IsOut) PrepareNewTurn(unit, equipment);
        Turn = 1;
        Phase = BattlePhase.InProgress;
        // InventoryState's destructor, as the inventory closes: gravity on the selected unit's tile, flares dropped or picked up, then
        // everyone's field of view. The falls the spawns and that gravity started run when the battlescape first thinks.
        var rules = content.RuntimeRules;
        RequeueFallingUnits();
        ApplyGravity(SelectedUnitId is { } chosen ? UnitById(chosen)?.Position : null);
        CalculateLighting(rules, BattleLightLayer.Items);
        Sight().RecalculateFov();
        Revision++;
        return ResolveFalls(rules);
    }

    /// <summary>
    /// SavedBattleGame::randomizeItemLocations: the inventory tile leaves the storage space for good (an endless loop
    /// otherwise), and each ground item on it goes to a storage tile drawn at random.
    /// </summary>
    private void RandomizeItemLocations(BattlePosition tile, IRandomSource random)
    {
        StorageSpace = [.. StorageSpace.Where(position => position != tile)];
        if (StorageSpace.Count == 0) return;
        foreach (var item in ItemsOnTile(tile).ToArray())
        {
            AddToTile(item, StorageSpace[random.NextInclusive(0, StorageSpace.Count - 1)]);
        }
    }

    /// <summary>SavedBattleGame::resetUnitTiles: a unit that is not out stands on its tiles; X-COM's units are visible.</summary>
    private void ResetUnitTiles()
    {
        foreach (var unit in _units)
        {
            unit.InventoryTile = null;
            if (!unit.IsOut && unit.Position is { } position)
            {
                foreach (var covered in BattleMap.Footprint(position, unit.ArmorSize)) Map.SetUnitAt(Map.TileIndex(covered), unit.Id);
                // setTile ends with updateTileFloorState.
                UpdateTileFloorState(unit);
            }
            if (unit.Faction == BattleFaction.Player) unit.SetSpottedBy(BattleFaction.Player, true);
        }
        IsBeforeGame = false;
    }

    /// <summary>
    /// BattleUnit::prepareNewTurn(false), the stage transition: flags reset, the stun counter up, a unit another side controlled
    /// returns to its own (and recovers nothing), else the time units and energy recover with the default recovery (the
    /// unit's time units; <see cref="BattleUnit.EnergyRegeneration"/>) and a kneeling unit stands up.
    /// </summary>
    private static void PrepareNewTurn(BattleUnit unit, UnitEquipment equipment)
    {
        if (unit.Status == BattleUnitStatus.IgnoreMe) return;
        unit.IsSurrendering = false;
        unit.UnitsSpottedThisTurn.Clear();
        unit.MeleeAttackedBy.Clear();
        unit.DontReselect = false;
        unit.AiMedikitUsed = false;
        unit.MotionPoints = 0;
        if (!unit.IsOut && unit.TurnsSinceStunned < byte.MaxValue) unit.TurnsSinceStunned++;
        if (unit.Faction != unit.OriginalFaction)
        {
            unit.Faction = unit.OriginalFaction;
            if (unit.Faction == BattleFaction.Player) unit.Ai = null;
            return;
        }
        PrepareTimeUnits(unit, unit.Stats.TimeUnits, equipment);
        PrepareEnergy(unit, unit.EnergyRegeneration);
        unit.Kneeled = false;
    }

    /// <summary>
    /// BattleUnit::prepareTimeUnits: the recovery added (up to the maximum), cut by encumbrance when the carried weight
    /// exceeds the strength, and by 10% per fatal wound of a leg.
    /// </summary>
    private static void PrepareTimeUnits(BattleUnit unit, int recovery, UnitEquipment equipment)
    {
        if (unit.IsOut) return;
        var maximum = unit.Stats.TimeUnits;
        unit.TimeUnits = Math.Clamp(unit.TimeUnits + recovery, 0, maximum);
        var encumbrance = unit.Stats.Strength / (float)equipment.CarriedWeight(unit);
        if (encumbrance < 1) unit.TimeUnits = (int)(encumbrance * unit.TimeUnits);
        unit.TimeUnits -= unit.TimeUnits * ((unit.FatalWounds[LeftLeg] + unit.FatalWounds[RightLeg]) * 10) / 100;
        unit.TimeUnits = Math.Clamp(unit.TimeUnits, 0, maximum);
    }

    /// <summary>BattleUnit::prepareEnergy: the recovery less 10% of the energy per fatal wound of the torso, up to the stamina.</summary>
    private static void PrepareEnergy(BattleUnit unit, int energy)
    {
        if (unit.IsOut) return;
        energy -= unit.Energy * (unit.FatalWounds[Torso] * 10) / 100;
        unit.Energy = Math.Clamp(unit.Energy + energy, 0, unit.Stats.Stamina);
    }
}

internal sealed partial class BattleUnit
{
    /// <summary>BattleUnit::isOutThresholdExceed: no health left, or no more than the stun.</summary>
    public bool IsOutThresholdExceeded => Health <= 0 || Health <= StunLevel;
}

using System.Collections.ObjectModel;
using Oxce.Core.Random;
using Oxce.Gameplay.Campaigns;
using Oxce.Mods.Rulesets.Content;
using Oxce.Mods.Rulesets.Runtime;
using Oxce.Scripting.Globals;

namespace Oxce.Gameplay.Battles;

/// <summary>A geoscape soldier as a battle unit is made from it (SavedGame::getSoldier): its type and personal state.</summary>
internal sealed record BattleSoldier(string SoldierType, SoldierPersonalState Personal);

/// <summary>
/// The campaign side of a battle restore: the geoscape soldiers the soldier units are made from (SavedGame::getSoldier,
/// null for an ID no soldier has) and the difficulty a hostile unit's constructor adjusts its statistics with.
/// </summary>
internal sealed record BattleRestoreCampaign(Func<int, BattleSoldier?> Soldiers, int Difficulty)
{
    public static BattleRestoreCampaign From(CampaignState campaign)
    {
        ArgumentNullException.ThrowIfNull(campaign);
        return new(campaign.BattleSoldier, (int)campaign.Difficulty);
    }
}

/// <summary>
/// The BattleUnit constructors (the soldier's and the unit rule's) and the units' capture and restore (Phase 7 branch 2
/// S3: <c>SavedBattleGame::load</c>'s unit passes, <c>BattleUnit::load</c> and <c>save</c>, <c>resetUnitTiles</c>).
/// </summary>
internal sealed partial class BattleState
{
    /// <summary>
    /// The battle's <c>RNG::seedless</c>: a stream of its own for presentation draws (the ambience delay, a generated unit's
    /// recolor and a civilian's unified rank), so the game's stream is unchanged (ADR 0039). A generated battle seeds it
    /// from the campaign stream's state; a restored one from a constant.
    /// </summary>
    internal IRandomSource Seedless { get; set; } = new SplitMix64RandomSource(RestoredSeedlessSeed);

    private const ulong RestoredSeedlessSeed = 0x5EED_1E55_0000_0001UL;

    /// <summary>BattleUnit::deriveHostileRank: the alien ranks whose index is a hostile unit's unified rank.</summary>
    private static readonly string[] HostileRanks =
    [
        "STR_LIVE_SOLDIER", "STR_LIVE_ENGINEER", "STR_LIVE_MEDIC", "STR_LIVE_NAVIGATOR", "STR_LIVE_LEADER", "STR_LIVE_COMMANDER",
        "STR_LIVE_TERRORIST",
    ];

    /// <summary>
    /// SavedBattleGame::createTempUnit (the BattleUnit(Unit*) constructor): the unit rule's and the armor's statistics with
    /// a hostile unit's difficulty adjustment, full time units, energy, health and mana, the armor's sides, move costs and
    /// movement type, the unit rule's aggression, spawn unit, VIP flag and weapon pick-up, a unified rank, a recolor drawn
    /// from <see cref="Seedless"/>, and an AI module for a hostile or neutral unit (createTempUnit's).
    /// </summary>
    internal BattleUnit TempUnit(RuntimeRuleCatalog rules, int id, BattleFaction faction, BattleController controller, string type,
        RuntimeBattleUnitRule rule, RuntimeArmorRule armor, int difficulty = 0, bool summoned = false)
    {
        var unit = NewUnit(rules, id, faction, controller, type, rule, armor, difficulty);
        unit.SummonedPlayerUnit = summoned;
        unit.BannedInNextStage = BannedInNextStage(rules, unit.ArmorId);
        if (faction != BattleFaction.Player) unit.Ai = new BattleAiState(faction);
        return unit;
    }

    // The BattleUnit(Unit*) constructor itself, which the load also calls (with the save's armor, no starting condition and
    // no AI module).
    private BattleUnit NewUnit(RuntimeRuleCatalog rules, int id, BattleFaction faction, BattleController controller, string type,
        RuntimeBattleUnitRule rule, RuntimeArmorRule armor, int difficulty)
    {
        var battleArmor = BattleArmor(rules, armor.Type);
        var stats = (BattleUnitStats.From(rule.Stats) + BattleUnitStats.From(armor.Stats)).ObeyFixedMinimum();
        var sides = battleArmor.Sides.ToArray();
        if (faction == BattleFaction.Hostile)
        {
            // adjustStats: the statistics and the five side armor values (the diagonal sides stay 0, ADR 0039).
            stats = AdjustHostileStats(rules, difficulty, stats);
            for (var side = 0; side < sides.Length; side++) sides[side] = AdjustHostileArmor(rules, difficulty, sides[side]);
        }
        var movement = MovementType(armor);
        var unit = new BattleUnit(id, faction, controller)
        {
            UnitType = type,
            ArmorId = armor.Type,
            ArmorSize = armor.Size,
            MovementType = movement,
            Stats = stats,
            TimeUnits = stats.TimeUnits,
            Energy = stats.Stamina,
            Health = stats.Health,
            Mana = stats.Mana,
        };
        unit.OriginalFaction = faction;
        unit.KilledBy = (int)faction;
        unit.OriginalMovementType = movement;
        unit.MoveCosts = unit.ArmorMoveCosts = battleArmor.MoveCosts;
        sides.CopyTo(unit.CurrentArmor, 0);
        unit.Aggression = rule.Aggression;
        unit.SpawnUnit = rule.SpawnUnit;
        unit.IsVip = rule.IsVip;
        // Options::autoCombatDefaultHWP, autoCombatDefaultMindControl and autoCombatDefaultRemain, all on by default.
        unit.AllowAutoCombat = true;
        unit.PickUpWeaponsMoreActively = rule.PickUpWeaponsMoreActively != -1 ? rule.PickUpWeaponsMoreActively != 0
            : faction == BattleFaction.Hostile ? rules.Campaign.AiPickUpWeaponsMoreActively : rules.Campaign.AiPickUpWeaponsMoreActivelyCivilians;
        // deriveHostileRank and deriveNeutralRank, before updateArmorFromNonSoldier's recolor.
        if (faction == BattleFaction.Hostile) unit.RankIntUnified = Math.Max(Array.IndexOf(HostileRanks, rule.Rank), 0);
        else if (faction == BattleFaction.Neutral) unit.RankIntUnified = Seedless.NextInclusive(0, 7);
        var basic = Seedless.NextInclusive(0, 127);
        var utile = Seedless.NextInclusive(0, 127);
        SetRecolor(unit, battleArmor, basic, utile, unit.RankIntUnified);
        return unit;
    }

    /// <summary>
    /// The BattleUnit(Soldier*) constructor: the soldier's statistics with its bonuses and the armor's, its missing health
    /// and mana (a wounded soldier at 75 morale with its wound recovery taken off), the armor's sides plus the soldier
    /// bonuses' (at least 0), the soldier's rank, auto-combat flag and look's recolor. <paramref name="originalArmorId"/>
    /// is the soldier's own armor when the battle armor replaces it ("" otherwise). The generator passes the battle's
    /// starting condition (<paramref name="startingCondition"/>), which bans the armors it forbids in the next stage; the
    /// load passes none.
    /// </summary>
    internal BattleUnit SoldierUnit(RuntimeRuleCatalog rules, int id, BattleSoldier soldier, string armorId, RuntimeArmorRule armor,
        string originalArmorId, bool startingCondition = true)
    {
        ArgumentNullException.ThrowIfNull(soldier);
        var personal = soldier.Personal;
        var battleArmor = BattleArmor(rules, armorId);
        // Soldier::prepareStatsWithBonuses: current statistics, soldier bonuses and the battle armor's statistics.
        var stats = (BattleUnitStats.From(CampaignState.StatsWithBonuses(personal, rules)) + BattleUnitStats.From(armor.Stats))
            .ObeyFixedMinimum();
        var health = Math.Max(1, stats.Health - personal.HealthMissing);
        var morale = 100;
        // Wounded soldiers (defending the base) start with lowered morale.
        if (SoldierReadiness.IsWounded(personal, rules.Campaign.ManaWoundThreshold, rules.Campaign.HealthWoundThreshold))
        {
            morale = 75;
            health = Math.Max(1, health - (int)Math.Ceiling(personal.Recovery));
        }
        var movement = MovementType(armor);
        // deriveSoldierRank: rookie (0) to commander (5), any other rank 0.
        var rank = personal.Rank is >= 0 and <= 5 ? personal.Rank : 0;
        var unit = new BattleUnit(id, BattleFaction.Player, BattleController.Human(0))
        {
            UnitType = soldier.SoldierType,
            SoldierId = id,
            ArmorId = armorId,
            OriginalArmorId = originalArmorId,
            ArmorSize = armor.Size,
            MovementType = movement,
            Stats = stats,
            TimeUnits = stats.TimeUnits,
            Energy = stats.Stamina,
            Health = health,
            Mana = Math.Max(0, stats.Mana - personal.ManaMissing),
            Morale = morale,
            Rank = rank,
        };
        unit.OriginalFaction = BattleFaction.Player;
        unit.KilledBy = (int)BattleFaction.Player;
        unit.RankIntUnified = rank;
        unit.OriginalMovementType = movement;
        unit.MoveCosts = unit.ArmorMoveCosts = battleArmor.MoveCosts;
        // The aggression is left uninitialized for a soldier (ADR 0039: 0).
        unit.AllowAutoCombat = personal.AllowAutoCombat;
        var bonuses = CampaignState.SoldierBonuses(personal, rules);
        for (var side = 0; side < battleArmor.Sides.Count; side++)
        {
            var value = battleArmor.Sides[side];
            foreach (var bonus in bonuses) value = unchecked(value + rules.SoldierBonuses[bonus].Value.Sides[side]);
            unit.CurrentArmor[side] = Math.Max(0, value);
        }
        var look = personal.Gender + 2 * personal.Look + 8 * personal.LookVariant;
        SetRecolor(unit, battleArmor, look, look, unit.RankIntUnified);
        if (startingCondition) unit.BannedInNextStage = BannedInNextStage(rules, armorId);
        return unit;
    }

    /// <summary>
    /// BattleUnit::prepareBannedFlag with the battle's starting condition: whether its <c>forbiddenArmorsInNextStage</c>
    /// holds the armor.
    /// </summary>
    private bool BannedInNextStage(RuntimeRuleCatalog rules, string armorId) =>
        StartingConditionId.Length != 0 && rules.StartingConditions.TryGet(StartingConditionId, out var handle) &&
        rules.StartingConditions[handle].Value.ForbiddenArmorsInNextStage.Contains(armorId, StringComparer.Ordinal);

    private static RuntimeBattleArmor BattleArmor(RuntimeRuleCatalog rules, string armorId) =>
        rules.Battle.Equipment.Armors.TryGetValue(armorId, out var armor) ? armor
            : throw new Generation.MapGenerationException($"Armor {armorId} does not exist.");

    /// <summary>
    /// BattleUnit::setRecolor: the face, hair, utile and rank color groups (each shifted left by 4) with the armor's color
    /// for the look (findWithFallback), each only when both are non-zero, as Uint8 pairs.
    /// </summary>
    private static void SetRecolor(BattleUnit unit, RuntimeBattleArmor armor, int basicLook, int utileLook, int rankLook)
    {
        unit.Recolor.Clear();
        (int Group, int Color)[] colors =
        [
            (armor.FaceColorGroup, FindWithFallback(armor.FaceColors, basicLook)),
            (armor.HairColorGroup, FindWithFallback(armor.HairColors, basicLook)),
            (armor.UtileColorGroup, FindWithFallback(armor.UtileColors, utileLook)),
            (armor.RankColorGroup, FindWithFallback(armor.RankColors, rankLook)),
        ];
        foreach (var (group, color) in colors)
            if (group > 0 && color > 0) unit.Recolor.Add(new BattleRecolor((byte)(group << 4), (byte)color));
    }

    /// <summary>
    /// Armor's findWithFallback: the look's entry, else the entries of its lower bits (RuleSoldier::LookTotalMask shifted
    /// right up to LookVariantBits times), else 0.
    /// </summary>
    private static int FindWithFallback(IReadOnlyList<int> colors, int look)
    {
        const uint lookTotalMask = (1u << 9) - 1;
        for (var shift = 0; shift <= 6; shift++)
        {
            var index = (uint)look & (lookTotalMask >> shift);
            if (index < (uint)colors.Count) return colors[(int)index];
        }
        return 0;
    }

    /// <summary>
    /// BattleUnit::adjustStats for a hostile unit: percentage growth times the difficulty coefficient, the firing multiplier
    /// (truncated to the 16-bit field), absolute growth. Mod::getStatAdjustment takes the last adjustment past it.
    /// </summary>
    internal static BattleUnitStats AdjustHostileStats(RuntimeRuleCatalog rules, int difficulty, BattleUnitStats stats)
    {
        var adjustments = rules.Campaign.StatAdjustments;
        if (adjustments.Count == 0) return stats;
        var adjustment = adjustments[Math.Min(difficulty, adjustments.Count - 1)];
        var multiplier = rules.Campaign.Interception.DifficultyCoefficient(difficulty);
        var growth = BattleUnitStats.From(adjustment.StatGrowth);
        stats += Percent(stats, growth, multiplier);
        stats = stats with { Firing = unchecked((short)(int)(stats.Firing * adjustment.AimMultiplier)) };
        return stats + BattleUnitStats.From(adjustment.StatGrowthAbsolute);
    }

    /// <summary>adjustStats' armor: each side times the armor multiplier (truncated to an int), plus the absolute bonus.</summary>
    internal static int AdjustHostileArmor(RuntimeRuleCatalog rules, int difficulty, int side)
    {
        var adjustments = rules.Campaign.StatAdjustments;
        if (adjustments.Count == 0) return side;
        var adjustment = adjustments[Math.Min(difficulty, adjustments.Count - 1)];
        return unchecked((int)(side * adjustment.ArmorMultiplier) + adjustment.ArmorMultiplierAbsolute);
    }

    /// <summary>UnitStats::percent: each statistic times its percentage times the multiplier, over 100.</summary>
    private static BattleUnitStats Percent(BattleUnitStats value, BattleUnitStats percent, int multiplier)
    {
        static short Part(short base_, short pct, int multiplier) => unchecked((short)(base_ * pct * multiplier / 100));
        return new(Part(value.TimeUnits, percent.TimeUnits, multiplier), Part(value.Stamina, percent.Stamina, multiplier),
            Part(value.Health, percent.Health, multiplier), Part(value.Bravery, percent.Bravery, multiplier),
            Part(value.Reactions, percent.Reactions, multiplier), Part(value.Firing, percent.Firing, multiplier),
            Part(value.Throwing, percent.Throwing, multiplier), Part(value.Strength, percent.Strength, multiplier),
            Part(value.PsiStrength, percent.PsiStrength, multiplier), Part(value.PsiSkill, percent.PsiSkill, multiplier),
            Part(value.Melee, percent.Melee, multiplier), Part(value.Mana, percent.Mana, multiplier));
    }

    private int NodeId(int index) => index >= 0 && index < Map.Nodes.Count ? Map.Nodes[index].Id : -1;

    /// <summary>BattleUnit::save's values for one unit; a soldier's type is <c>SOLDIER</c>.</summary>
    private BattleUnitSnapshot CaptureUnit(BattleUnit unit) => new(unit.Id,
        unit.SoldierId is null ? unit.UnitType : BattleUnitSnapshot.SoldierType, unit.ArmorId, unit.Faction)
    {
        OriginalFaction = unit.OriginalFaction,
        Status = unit.Status,
        WantsToSurrender = unit.WantsToSurrender,
        IsSurrendering = unit.IsSurrendering,
        Position = unit.Position ?? unit.SavedPosition,
        Direction = unit.Direction,
        DirectionTurret = unit.DirectionTurret,
        TimeUnits = unit.TimeUnits,
        Health = unit.Health,
        Mana = unit.Mana,
        StunLevel = unit.StunLevel,
        Energy = unit.Energy,
        Morale = unit.Morale,
        Kneeled = unit.Kneeled,
        Floating = unit.Floating,
        Armor = Array.AsReadOnly(unit.CurrentArmor.ToArray()),
        FatalWounds = Array.AsReadOnly(unit.FatalWounds.ToArray()),
        Fire = unit.Fire,
        Experience = unit.Experience,
        CurrentStats = new ReadOnlyDictionary<string, short>(BattleUnitStats.Keys.ToDictionary(static key => key, unit.Stats.Get, StringComparer.Ordinal)),
        TurretType = unit.TurretType,
        Visible = unit.IsSpottedBy(BattleFaction.Player),
        TurnsSinceSpotted = Array.AsReadOnly(unit.TurnsSinceSpotted.Select(static value => (int)value).ToArray()),
        TurnsLeftSpottedForSnipers = Array.AsReadOnly(unit.TurnsLeftSpottedForSnipers.Select(static value => (int)value).ToArray()),
        TurnsSinceStunned = unit.TurnsSinceStunned,
        TurnsSinceSeenBy = Array.AsReadOnly(unit.TurnsSinceSeenBy.ToArray()),
        TileLastSpottedBy = Array.AsReadOnly(unit.TileLastSpottedBy.ToArray()),
        TileLastSpottedForBlindShotBy = Array.AsReadOnly(unit.TileLastSpottedForBlindShotBy.ToArray()),
        RankInt = unit.Rank,
        RankIntUnified = unit.RankIntUnified,
        MoraleRestored = unit.MoraleRestored,
        NotificationShown = unit.NotificationShown,
        KilledBy = unit.KilledBy,
        Kills = unit.Kills,
        DontReselect = unit.DontReselect,
        AiMedikitUsed = unit.AiMedikitUsed,
        IsBrutal = unit.IsBrutal,
        IsNotBrutal = unit.IsNotBrutal,
        IsCheatOnMovement = unit.IsCheatOnMovement,
        SpawnUnit = unit.SpawnUnit,
        Respawn = unit.Respawn,
        SpawnUnitFaction = unit.SpawnUnitFaction,
        MotionPoints = unit.MotionPoints,
        CustomMarker = unit.CustomMarker,
        AlreadyRespawned = unit.AlreadyRespawned,
        ActiveHand = unit.ActiveHand,
        PreferredHandForReactions = unit.PreferredHandForReactions,
        ReactionsDisabledForLeftHand = unit.ReactionsDisabledForLeftHand,
        ReactionsDisabledForRightHand = unit.ReactionsDisabledForRightHand,
        Statistics = unit.Statistics,
        MurdererId = unit.MurdererId,
        FatalShotSide = unit.FatalShotSide,
        FatalShotBodyPart = unit.FatalShotBodyPart,
        MurdererWeapon = unit.MurdererWeapon,
        MurdererWeaponAmmo = unit.MurdererWeaponAmmo,
        Recolor = Array.AsReadOnly(unit.Recolor.ToArray()),
        MindControllerId = unit.MindControllerId,
        SummonedPlayerUnit = unit.SummonedPlayerUnit,
        ResummonedFakeCivilian = unit.ResummonedFakeCivilian,
        PickUpWeaponsMoreActively = unit.PickUpWeaponsMoreActively,
        DisableIndicators = unit.DisableIndicators,
        MovementType = unit.MovementType,
        OriginalMovementType = unit.OriginalMovementType,
        MoveCosts = MoveCosts(unit.MoveCosts),
        ArmorMoveCosts = MoveCosts(unit.ArmorMoveCosts),
        Vip = unit.IsVip,
        BannedInNextStage = unit.BannedInNextStage,
        MeleeAttackedBy = Array.AsReadOnly(unit.MeleeAttackedBy.ToArray()),
        AllowAutoCombat = unit.AllowAutoCombat,
        Aggression = unit.Aggression,
        HasPanickedLastTurn = unit.HasPanickedLastTurn,
        // AIModule::save writes the nodes' getID(), which AIModule::load reads back as an index (they differ only for a
        // dummy node, whose Node() ID is 0, so an AI at a dummy rereads as at node 0, as in the reference).
        Ai = unit.Ai is { } ai ? new BattleAiSnapshot(NodeId(ai.FromNode), NodeId(ai.ToNode), ai.Mode, Array.AsReadOnly(ai.WasHitBy.ToArray()), ai.WeaponPickedUp)
        {
            TargetFaction = ai.TargetFaction,
        } : null,
        PreviousOwnerId = unit.PreviousOwnerId ?? -1,
        ScriptValues = Array.AsReadOnly((unit.ScriptValues?.Capture() ?? []).ToArray()),
        Controller = unit.Controller,
        SpottedByHostile = unit.IsSpottedBy(BattleFaction.Hostile),
        SpottedByNeutral = unit.IsSpottedBy(BattleFaction.Neutral),
    };

    private static BattleMoveCosts MoveCosts(RuntimeArmorMoveCosts costs) => new(costs.Base, costs.Fly, costs.Climb, costs.Normal);

    /// <summary>
    /// SavedBattleGame::load's unit passes and loadMapResources' resetUnitTiles: each saved unit is made by its constructor
    /// (a soldier from the campaign's soldier of its ID and that soldier's armor, any other unit from its unit rule and
    /// <c>genUnitArmor</c>), then BattleUnit::load sets what the save holds; the AI module is kept for a unit that is not
    /// the player's, dead or ignored; the selected and undo units are resolved among the player's units; previous owners
    /// name a loaded unit or none; a unit that is not out stands on its tile and the player's units are visible. A soldier
    /// the campaign lacks is a corrupted save (the reference dereferences a null soldier).
    /// </summary>
    private void RestoreUnits(BattleSnapshot snapshot, RuntimeContent content, BattleRestoreCampaign? campaign)
    {
        var rules = content.RuntimeRules;
        var restored = new List<(BattleUnit Unit, BattleUnitSnapshot Saved)>();
        foreach (var saved in snapshot.Units)
        {
            var unit = saved.Id < MaximumSoldierId ? RestoreSoldier(rules, saved, campaign) : RestoreGenerated(rules, saved, campaign);
            Load(unit, saved, content);
            restored.Add((unit, saved));
        }
        var ids = restored.Select(static pair => pair.Unit.Id).ToHashSet();
        int? selected = null;
        int? undo = null;
        foreach (var (unit, saved) in restored)
        {
            if (unit.Faction == BattleFaction.Player)
            {
                // if ((id == selectedUnitId) || (_selectedUnit == 0 && !isOut())): the saved unit, else the first standing.
                if (unit.Id == snapshot.SelectedUnitId || selected is null && !unit.IsOut) selected = unit.Id;
                if (unit.Id == snapshot.UndoUnitId) undo = unit.Id;
            }
            else if (unit.Status is not (BattleUnitStatus.Dead or BattleUnitStatus.IgnoreMe) && saved.Ai is { } ai)
            {
                // AIModule::load: a node ID outside the battle's nodes is none.
                int Node(int id) => id >= 0 && id < Map.Nodes.Count ? id : -1;
                unit.Ai = new BattleAiState(unit.OriginalFaction, Node(ai.FromNode))
                {
                    ToNode = Node(ai.ToNode),
                    Mode = ai.Mode,
                    WeaponPickedUp = ai.WeaponPickedUp,
                };
                unit.Ai.WasHitBy.AddRange(ai.WasHitBy);
                if (ai.TargetFaction is { } target) unit.Ai.TargetFaction = target;
            }
            // The units' second pass: findUnitById(previousOwner).
            unit.PreviousOwnerId = saved.PreviousOwnerId != -1 && ids.Contains(saved.PreviousOwnerId) ? saved.PreviousOwnerId : null;
            // resetUnitTiles: a unit that is not out stands on the tile of its position (none outside the map).
            unit.Position = !unit.IsOut && Contains(unit.SavedPosition) ? unit.SavedPosition : null;
            AddUnit(unit);
            if (unit.Faction == BattleFaction.Player) unit.SetSpottedBy(BattleFaction.Player, true);
        }
        SelectedUnitId = selected;
        UndoUnitId = undo;
    }

    private BattleUnit RestoreSoldier(RuntimeRuleCatalog rules, BattleUnitSnapshot saved, BattleRestoreCampaign? campaign)
    {
        var soldier = campaign?.Soldiers(saved.Id)
            ?? throw new InvalidDataException($"Battle save unit {saved.Id} is soldier {saved.Id}, which the campaign does not have.");
        var personal = soldier.Personal;
        // new BattleUnit(mod, savedGame->getSoldier(id), _depth, nullptr): the soldier's armor, which StartBattle set to the
        // battle armor (deployXCOM); genUnitArmor is not read, so a unit saved in other armor takes the soldier's.
        if (!rules.Armors.TryGet(personal.Armor, out var handle) || !rules.Battle.Equipment.Armors.ContainsKey(personal.Armor))
            throw new InvalidDataException($"Battle save unit {saved.Id}: soldier {saved.Id} wears unknown armor '{personal.Armor}'.");
        var original = personal.TransformedArmor.Length != 0 ? personal.TransformedArmor : personal.ReplacedArmor;
        var unit = SoldierUnit(rules, saved.Id, soldier, personal.Armor, rules.Armors[handle].Value, original, startingCondition: false);
        unit.Controller = saved.Controller;
        return unit;
    }

    private BattleUnit RestoreGenerated(RuntimeRuleCatalog rules, BattleUnitSnapshot saved, BattleRestoreCampaign? campaign)
    {
        // OxceBattleSave.Read leaves out a unit whose rule or armor is gone (the load's continue).
        if (!rules.Battle.Equipment.Units.TryGetValue(saved.UnitType, out var rule) || !rules.Armors.TryGet(saved.ArmorId, out var armor) ||
            !rules.Battle.Equipment.Armors.ContainsKey(saved.ArmorId))
            throw new InvalidDataException($"Battle save unit {saved.Id} names unknown unit '{saved.UnitType}' or armor '{saved.ArmorId}'.");
        // The BattleUnit(Unit*) constructor with the save's original faction and armor, no enviro effects and the
        // campaign's difficulty; the load creates no AI module unless the save has one.
        return NewUnit(rules, saved.Id, saved.OriginalFaction, saved.Controller, saved.UnitType, rule, rules.Armors[armor].Value,
            campaign?.Difficulty ?? 0);
    }

    /// <summary>BattleUnit::load over the constructor's unit: every saved value, the constructor's for a key the save leaves out.</summary>
    private static void Load(BattleUnit unit, BattleUnitSnapshot saved, RuntimeContent content)
    {
        unit.Faction = saved.Faction;
        unit.Status = saved.Status;
        unit.WantsToSurrender = saved.WantsToSurrender;
        unit.IsSurrendering = saved.IsSurrendering;
        unit.SavedPosition = saved.Position;
        unit.Direction = saved.Direction;
        unit.DirectionTurret = saved.DirectionTurret;
        unit.TimeUnits = saved.TimeUnits ?? unit.TimeUnits;
        unit.Health = saved.Health ?? unit.Health;
        unit.Mana = saved.Mana ?? unit.Mana;
        unit.StunLevel = saved.StunLevel;
        unit.Energy = saved.Energy ?? unit.Energy;
        unit.Morale = saved.Morale ?? unit.Morale;
        unit.Kneeled = saved.Kneeled;
        unit.Floating = saved.Floating;
        for (var side = 0; side < Math.Min(saved.Armor.Count, BattleUnit.ArmorSides); side++) unit.CurrentArmor[side] = saved.Armor[side];
        for (var part = 0; part < Math.Min(saved.FatalWounds.Count, BattleUnit.BodyParts); part++) unit.FatalWounds[part] = saved.FatalWounds[part];
        unit.Fire = saved.Fire;
        unit.Experience = saved.Experience;
        foreach (var (key, value) in saved.CurrentStats) unit.Stats = unit.Stats.With(key, value);
        unit.TurretType = saved.TurretType;
        unit.SetSpottedBy(BattleFaction.Player, saved.Visible);
        unit.SetSpottedBy(BattleFaction.Hostile, saved.SpottedByHostile);
        unit.SetSpottedBy(BattleFaction.Neutral, saved.SpottedByNeutral);
        for (var faction = 0; faction < 3; faction++)
        {
            unit.TurnsSinceSpotted[faction] = (byte)saved.TurnsSinceSpotted[faction];
            unit.TurnsLeftSpottedForSnipers[faction] = (byte)saved.TurnsLeftSpottedForSnipers[faction];
            unit.TurnsSinceSeenBy[faction] = saved.TurnsSinceSeenBy[faction];
            unit.TileLastSpottedBy[faction] = saved.TileLastSpottedBy[faction];
            unit.TileLastSpottedForBlindShotBy[faction] = saved.TileLastSpottedForBlindShotBy[faction];
        }
        unit.TurnsSinceStunned = (byte)saved.TurnsSinceStunned;
        unit.Rank = saved.RankInt ?? unit.Rank;
        unit.RankIntUnified = saved.RankIntUnified ?? unit.RankIntUnified;
        unit.MoraleRestored = saved.MoraleRestored;
        unit.NotificationShown = saved.NotificationShown;
        unit.KilledBy = saved.KilledBy ?? unit.KilledBy;
        unit.Kills = saved.Kills;
        unit.DontReselect = saved.DontReselect;
        unit.AiMedikitUsed = saved.AiMedikitUsed;
        unit.IsBrutal = saved.IsBrutal;
        unit.IsNotBrutal = saved.IsNotBrutal;
        unit.IsCheatOnMovement = saved.IsCheatOnMovement;
        unit.SpawnUnit = saved.SpawnUnit;
        unit.Respawn = saved.Respawn;
        unit.SpawnUnitFaction = saved.SpawnUnitFaction;
        unit.MotionPoints = saved.MotionPoints;
        unit.CustomMarker = saved.CustomMarker;
        unit.AlreadyRespawned = saved.AlreadyRespawned;
        unit.ActiveHand = saved.ActiveHand;
        unit.PreferredHandForReactions = saved.PreferredHandForReactions;
        unit.ReactionsDisabledForLeftHand = saved.ReactionsDisabledForLeftHand;
        unit.ReactionsDisabledForRightHand = saved.ReactionsDisabledForRightHand;
        unit.Statistics = saved.Statistics;
        unit.MurdererId = saved.MurdererId;
        unit.FatalShotSide = saved.FatalShotSide;
        unit.FatalShotBodyPart = saved.FatalShotBodyPart;
        unit.MurdererWeapon = saved.MurdererWeapon;
        unit.MurdererWeaponAmmo = saved.MurdererWeaponAmmo;
        if (saved.Recolor is { } recolor)
        {
            unit.Recolor.Clear();
            unit.Recolor.AddRange(recolor);
        }
        unit.MindControllerId = saved.MindControllerId;
        unit.SummonedPlayerUnit = saved.SummonedPlayerUnit;
        unit.ResummonedFakeCivilian = saved.ResummonedFakeCivilian;
        unit.PickUpWeaponsMoreActively = saved.PickUpWeaponsMoreActively ?? unit.PickUpWeaponsMoreActively;
        unit.DisableIndicators = saved.DisableIndicators;
        unit.MovementType = saved.MovementType ?? unit.MovementType;
        var costs = unit.MoveCosts;
        unit.MoveCosts = new RuntimeArmorMoveCosts(saved.MoveCosts.Base ?? costs.Base, saved.MoveCosts.Fly ?? costs.Fly,
            saved.MoveCosts.Climb ?? costs.Climb, saved.MoveCosts.Normal ?? costs.Normal);
        unit.IsVip = saved.Vip ?? unit.IsVip;
        unit.BannedInNextStage = saved.BannedInNextStage;
        unit.MeleeAttackedBy.Clear();
        unit.MeleeAttackedBy.AddRange(saved.MeleeAttackedBy);
        unit.AllowAutoCombat = saved.AllowAutoCombat ?? unit.AllowAutoCombat;
        unit.Aggression = saved.Aggression ?? unit.Aggression;
        unit.HasPanickedLastTurn = saved.HasPanickedLastTurn;
        unit.ScriptValues = RestoreUnitScriptValues(content, saved);
        // The load creates the AI module afterwards from the save's AI (RestoreUnits).
        unit.Ai = null;
    }

    private static ScriptValueState RestoreUnitScriptValues(RuntimeContent content, BattleUnitSnapshot saved)
    {
        // The script API always declares BattleUnit.Tag, so every content has the owner type.
        var owner = content.Tags.Types.First(static candidate => candidate.Name == BattleUnitSnapshot.ScriptOwner).Id;
        if (!ScriptValueState.TryRestore(content.Tags, owner, saved.ScriptValues, out var state, out var diagnostics))
            throw new InvalidDataException($"Battle save unit {saved.Id}: " +
                string.Join(Environment.NewLine, diagnostics.Select(static item => item.Message)));
        return state!;
    }
}

using Oxce.Gameplay.Battles.Generation;
using Oxce.Gameplay.Campaigns;
using Oxce.Mods.Rulesets.Runtime;

namespace Oxce.Gameplay.Battles;

/// <summary>
/// What a battle asks of the campaign that holds it: the mission count the kill statistics carry, the difficulty a converted hostile unit's
/// statistics are adjusted with, the name and rank string of a soldier (<c>BattleUnitKills::setUnitStats</c>), and the memorial
/// (<c>SavedGame::killSoldier</c>). A battle built by hand, or restored for a trial run, has none: the statistics then name no soldier and no
/// memorial changes.
/// </summary>
internal interface IBattleCampaignLink
{
    /// <summary><c>getMissionStatistics()->size()</c>: the number of missions the saved game has recorded.</summary>
    int MissionCount { get; }

    /// <summary>The campaign's difficulty level (<c>SavedGame::getDifficulty</c>).</summary>
    int Difficulty { get; }

    /// <summary>A geoscape soldier's name and rank string (<c>Soldier::getName</c>, <c>getRankString</c>), or null for an ID no soldier has.</summary>
    BattleSoldierDescription? DescribeSoldier(int soldierId);

    /// <summary><c>SavedGame::killSoldier(false, soldier, cause)</c>: the soldier goes to the memorial during the battle.</summary>
    void KillSoldier(int soldierId, BattleUnitKillSnapshot cause);

    /// <summary>What the saved game knows that using a weapon asks about (<c>isResearched</c>, the debug mode); a battle without a link has every research done.</summary>
    BattleResearchGate Research => BattleResearchGate.Everything;
}

/// <summary>A geoscape soldier's name and rank string, as the kill statistics name it.</summary>
internal sealed record BattleSoldierDescription(string Name, string Rank);

/// <summary>A unit that left the battle's living by <see cref="BattleState.CheckForCasualties"/>: how, and the unit it became (an infected unit, a zombie).</summary>
internal readonly record struct BattleCasualty(int UnitId, BattleUnitStatus Status, int? ConvertedTo = null);

/// <summary>
/// Casualties (Phase 7 branch 5 S4): <c>BattlescapeGame::checkForCasualties</c> (the kill statistics, the murderer, the morale a death or a stun
/// brings), <c>UnitDieBState</c> collapsed to its outcome (the fall to dead or unconscious, the corpse items, the inventory on the ground, the
/// spotting resets, the nodes a dying alien makes dangerous, the converted unit), <c>SavedGame::killSoldier</c>, <c>BattleUnit::knockOut</c>,
/// <c>convertUnit</c>, <c>convertInfected</c>, <c>reviveUnconsciousUnits</c> with <c>placeUnitNearPosition</c> and <c>removeUnconsciousBodyItem</c>.
/// The animation (the turn to face direction 3, the collapse frames, the sounds, the infoboxes) is presentation and collapses; the outcome of
/// each state is applied in the reference's queue order (<c>statePushNext</c>).
/// </summary>
internal sealed partial class BattleState
{
    /// <summary>What the campaign gives the battle (see <see cref="IBattleCampaignLink"/>); null for a battle built by hand. The <see cref="BattleRuntime"/> sets it once; a trial copy has the same values with the memorial left alone.</summary>
    internal IBattleCampaignLink? CampaignLink { get; set; }

    /// <summary><c>Mod::getDamageType(DT_HE)</c> and <c>DT_NONE</c>, which <c>checkForCasualties</c> hands the dying unit when no weapon did.</summary>
    private static readonly RuntimeItemDamageRule HighExplosiveType = RuntimeItemDamageRule.Predefined(RuntimeItemDamageRule.DamageHighExplosive);

    private static readonly RuntimeItemDamageRule NoDamageType = RuntimeItemDamageRule.Predefined(RuntimeItemDamageRule.DamageNone);

    /// <summary>Armor death of a unit facing: <c>UnitDieBState</c> turns a unit killed by direct damage to face direction 3 (the south east).</summary>
    private const int DeathDirection = 3;

    private const string UnknownWeapon = "STR_WEAPON_UNKNOWN";
    private const string GunButtAmmo = "__GUNBUTT";
    private const int CorpseItemBattleType = 11;

    /// <summary>One <c>UnitDieBState</c> pushed by <c>checkForCasualties</c>, with what its constructor fixed (the overkill of the moment).</summary>
    internal sealed class PendingDeath(BattleUnit unit, RuntimeItemDamageRule type, bool noSound, bool overKill)
    {
        public BattleUnit Unit { get; } = unit;
        public RuntimeItemDamageRule Type { get; } = type;
        public bool NoSound { get; } = noSound;
        public bool OverKill { get; } = overKill;

        /// <summary>The constructor already turned the unit into a corpse (the battle is not under way yet): the state does nothing more.</summary>
        public bool Finished { get; set; }
    }

    // ---- small rule helpers ----

    /// <summary>SavedBattleGame::getMoraleLossModifierWhenKilled: the soldier type's or the unit rule's, never below 0.</summary>
    private int MoraleLossWhenKilled(BattleUnit unit)
    {
        var result = 100;
        var equipment = RequiredRules.Battle.Equipment;
        if (unit.SoldierId is not null) result = equipment.SoldierMoraleLossWhenKilled.GetValueOrDefault(unit.UnitType, 100);
        else if (equipment.Units.TryGetValue(unit.UnitType, out var rule)) result = rule.MoraleLossWhenKilled;
        return Math.Max(0, result);
    }

    /// <summary>SavedBattleGame::getUnitMoraleModifier: X-COM's own units lose more for a fallen officer.</summary>
    private static int UnitMoraleModifier(BattleUnit unit)
    {
        var result = 100;
        if (unit.OriginalFaction != BattleFaction.Player) return result;
        switch (unit.Rank)
        {
            case 5: result += 25; goto case 4;
            case 4: result += 20; goto case 3;
            case 3: result += 10; goto case 2;
            case 2: result += 20; break;
        }
        return result;
    }

    /// <summary>SavedBattleGame::getFactionMoraleModifier: X-COM's by its highest ranked living unit, the aliens' by their number.</summary>
    private int FactionMoraleModifier(bool player)
    {
        if (player) return PlayerMoraleModifier();
        var number = 0;
        foreach (var unit in _units)
            if (unit.OriginalFaction == BattleFaction.Hostile && !unit.IsOut) number++;
        return Math.Max(6 * number, 100);
    }

    /// <summary>BattleUnit::isCosmetic: a unit rule's <c>cosmetic</c>.</summary>
    private bool IsCosmetic(BattleUnit unit) =>
        unit.SoldierId is null && RequiredRules.Battle.Equipment.Units.TryGetValue(unit.UnitType, out var rule) && rule.Cosmetic;

    /// <summary>BattleUnit::getCapturable: a unit rule's <c>capturable</c>; soldiers are.</summary>
    private bool IsCapturable(BattleUnit unit) =>
        unit.SoldierId is not null || !RequiredRules.Battle.Equipment.Units.TryGetValue(unit.UnitType, out var rule) || rule.Capturable;

    /// <summary>
    /// Whether the unit's <c>spawnUnit</c> names a unit the mod has. The reference dereferences a missing rule when it converts such a unit
    /// (undefined behaviour); the port treats a unit that names a missing one as having none (ADR 0041).
    /// </summary>
    private bool HasSpawnUnit(BattleUnit unit) =>
        unit.SpawnUnit.Length != 0 && RequiredRules.Units.TryGet(unit.SpawnUnit, out _) && RequiredRules.Battle.Equipment.Units.ContainsKey(unit.SpawnUnit);

    /// <summary>
    /// <c>UnitDieBState::init</c>: a unit that is no longer on the map (dead already, or carried) is skipped, but for an X-COM unit, unless its notice
    /// was shown (every other side's is skipped).
    /// </summary>
    private static bool SkippedByInit(BattleUnit unit) =>
        !HasTile(unit) && (unit.OriginalFaction != BattleFaction.Player || unit.NotificationShown == 2);

    /// <summary>Whether a unit is linked to a tile (<c>BattleUnit::getTile</c>): it stands on the map, whatever its status.</summary>
    private static bool HasTile(BattleUnit unit) => unit.Position is not null;

    /// <summary>Whether another unit carries the unit as an unconscious body (the reference gives it the position (-1, -1, -1)).</summary>
    internal bool IsCarried(BattleUnit unit) => _items.Exists(item => item.CorpseUnitId == unit.Id && item.OwnerUnitId is not null);

    /// <summary>
    /// <c>BattleUnit::getPosition</c> for a unit that is out: where it lies, or null while it is carried. A unit on the map has its tile;
    /// one off the map keeps the position it left (<see cref="BattleUnit.SavedPosition"/>).
    /// </summary>
    internal BattlePosition? RestingPosition(BattleUnit unit)
    {
        if (unit.Position is { } position) return position;
        if (IsCarried(unit)) return null;
        return Contains(unit.SavedPosition) ? unit.SavedPosition : null;
    }

    /// <summary>BattleUnit::setTile(nullptr): the tiles under the unit name it no more; the unit keeps the position it had.</summary>
    private void ClearTile(BattleUnit unit)
    {
        if (unit.Position is { } position)
        {
            foreach (var covered in BattleMap.Footprint(position, unit.ArmorSize))
                if (Contains(covered) && Map.UnitAt(Map.TileIndex(covered)) == unit.Id) Map.SetUnitAt(Map.TileIndex(covered), null);
            unit.SavedPosition = position;
            unit.Position = null;
        }
        unit.HaveNoFloorBelow = false;
        unit.Floating = false;
        MarkChanged();
    }

    /// <summary>SavedBattleGame::clearUnitSelection: the selected and the undo unit are none when they are this unit.</summary>
    internal void ClearUnitSelection(BattleUnit unit)
    {
        if (SelectedUnitId == unit.Id) SelectedUnitId = null;
        if (UndoUnitId == unit.Id) UndoUnitId = null;
    }

    // ---- BattlescapeGame::checkForCasualties ----

    /// <summary>
    /// <c>BattlescapeGame::checkForCasualties</c> and the <c>UnitDieBState</c>s it pushes, run to their end. <paramref name="damageType"/> is the
    /// type of the damage that caused the casualties (null when none did); <paramref name="hiddenExplosion"/> is the power sources' explosion
    /// and the check of a battle that opens; <paramref name="terrainExplosion"/> a terrain explosion. <paramref name="statesQueued"/> says
    /// another state is running (an explosion, a fall): the reference then inserts each death behind it, the last victim first.
    /// </summary>
    internal IReadOnlyList<BattleCasualty> CheckForCasualties(RuntimeItemDamageRule? damageType, in BattleAttack attack,
        bool hiddenExplosion = false, bool terrainExplosion = false, bool statesQueued = false)
    {
        var pending = QueueCasualties(damageType, attack, hiddenExplosion, terrainExplosion, statesQueued);
        return RunDeaths(pending);
    }

    /// <summary>
    /// The loop of <c>checkForCasualties</c>: kill statistics, the murderer, morale, the memorial, and each death's state, constructed (so its
    /// constructor's effects are done) and returned in the order the queue will run them. Units that fall without a floor queue to fall.
    /// With <paramref name="pushNext"/> each death is handed to that caller in the order the reference pushed it, and nothing is returned.
    /// </summary>
    internal List<PendingDeath> QueueCasualties(RuntimeItemDamageRule? damageType, in BattleAttack attack,
        bool hiddenExplosion = false, bool terrainExplosion = false, bool statesQueued = false, Action<PendingDeath>? pushNext = null)
    {
        var original = attack.Attacker;
        // If the victim was killed by the murderer's death explosion, the one who killed the murderer is the murderer.
        if (original is { Status: BattleUnitStatus.Dead } dead && SpecialAbilityOf(dead) is 1 or 3 && dead.MurdererId != 0 &&
            UnitById(dead.MurdererId) is { } killer)
            original = killer;

        // The murder weapon.
        string tempWeapon = UnknownWeapon, tempAmmo = UnknownWeapon;
        if (original is not null)
        {
            if (attack.Weapon is { } weapon) tempWeapon = weapon.ItemType;
            if (attack.DamageItem is { } damageItem)
            {
                // The secondary melee data is represented by the ammunition "__GUNBUTT"; a melee item's own attack uses the item itself.
                tempAmmo = attack.Type == BattleActionType.Hit && ItemRuleOf(damageItem.ItemType).BattleType != BattleTypeMelee
                    ? GunButtAmmo : damageItem.ItemType;
            }
        }

        var pushed = new List<PendingDeath>();
        var mission = CampaignLink?.MissionCount ?? 0;
        foreach (var victim in _units.ToArray())
        {
            if (victim.Status == BattleUnitStatus.IgnoreMe) continue;
            var murderer = original;
            var kill = DescribeUnit(victim) with
            {
                Mission = mission,
                Turn = Turn * 3 + (int)Side,
                Faction = (int)victim.OriginalFaction,
                Side = victim.FatalShotSide,
                BodyPart = victim.FatalShotBodyPart,
                Id = victim.Id,
                Weapon = tempWeapon,
                WeaponAmmo = tempAmmo,
            };

            // Determine the murder type.
            if (victim.Status != BattleUnitStatus.Dead)
            {
                if (victim.Health <= 0) kill = kill with { Status = (int)BattleUnitStatus.Dead };
                else if (victim.StunLevel >= victim.Health && victim.Status != BattleUnitStatus.Unconscious)
                    kill = kill with { Status = (int)BattleUnitStatus.Unconscious };
            }

            // Without a murderer and an explosion, the last unit to hit the victim is the murderer (bleeding, fire, smoke).
            if (murderer is null && !terrainExplosion && UnitById(victim.MurdererId) is { } last)
            {
                murderer = last;
                kill = kill with { Weapon = victim.MurdererWeapon, WeaponAmmo = victim.MurdererWeaponAmmo };
            }

            if (murderer is not null && kill.Status != (int)BattleUnitStatus.IgnoreMe) CreditKill(victim, murderer, kill);

            if (victim.Status != BattleUnitStatus.Dead)
            {
                if (victim.Health <= 0)
                {
                    pushed.Add(Perish(victim, murderer, kill, damageType, hiddenExplosion, terrainExplosion));
                }
                else if (victim.StunLevel >= victim.Health && victim.Status != BattleUnitStatus.Unconscious)
                {
                    // Morale changes when an enemy is stunned (only the first time).
                    if (RequiredRules.Campaign.Combat.StunningImprovesMorale && murderer is not null && !victim.Statistics.WasUnconscious &&
                        (victim.OriginalFaction == BattleFaction.Player && murderer.Faction == BattleFaction.Hostile ||
                         victim.OriginalFaction == BattleFaction.Hostile && murderer.Faction == BattleFaction.Player))
                    {
                        // The murderer gets a morale bonus when it is of a different faction (neutrals excluded); the winning squad gains.
                        MoraleChange(ArmorOf(murderer), murderer, 20);
                        foreach (var winner in _units)
                            if (!winner.IsOut && winner.OriginalFaction == murderer.OriginalFaction) MoraleChange(ArmorOf(winner), winner, 10);
                    }
                    victim.Statistics = victim.Statistics with { WasUnconscious = true };
                    pushed.Add(BeginDeath(victim, NoDamageType, noSound: true));
                    Events.Add(new UnitKnockedOut(victim.Id, murderer?.Id ?? -1));
                }
                else if (victim.HaveNoFloorBelow && victim.MovementType != MovementTypeFly)
                {
                    // Piggyback of the cleanup after a script that changes the move type.
                    AddFallingUnit(victim);
                }
            }
        }

        // A caller with a real state queue (the explosions, S5) takes each death in the order the reference pushed it and pushes it itself.
        if (pushNext is not null)
        {
            foreach (var death in pushed) pushNext(death);
            MarkChanged();
            return [];
        }

        // statePushNext: the first push into an empty queue runs first; every other push goes right behind the running state.
        if (statesQueued) pushed.Reverse();
        else
        {
            // A state pushed into an empty queue is initialised at once, and one that init skips pops, so the next push finds the queue empty again.
            var queue = new List<PendingDeath>();
            foreach (var death in pushed)
            {
                if (queue.Count == 0)
                {
                    if (!SkippedByInit(death.Unit)) queue.Add(death);
                }
                else
                {
                    queue.Insert(1, death);
                }
            }
            pushed = queue;
        }
        MarkChanged();
        return pushed;
    }

    /// <summary>BattleUnitKills::setUnitStats: the victim's name or type, rank and race.</summary>
    private BattleUnitKillSnapshot DescribeUnit(BattleUnit unit)
    {
        string name = "", type = "", rank, race;
        var described = unit.SoldierId is { } soldierId ? CampaignLink?.DescribeSoldier(soldierId) : null;
        if (unit.SoldierId is not null) name = described?.Name ?? "";
        else type = unit.UnitType;
        var rule = unit.SoldierId is null && RequiredRules.Battle.Equipment.Units.TryGetValue(unit.UnitType, out var found) ? found : null;
        switch (unit.OriginalFaction)
        {
            case BattleFaction.Player:
                rank = unit.SoldierId is not null
                    ? described is { Rank.Length: > 0 } ? described.Rank : "STR_SOLDIER"
                    : rule is { Rank.Length: > 0 } ? rule.Rank : "STR_HWPS";
                race = rule is { Race.Length: > 0 } ? rule.Race : "STR_FRIENDLY";
                break;
            case BattleFaction.Hostile:
                rank = rule is { Rank.Length: > 0 } ? rule.Rank : "STR_LIVE_SOLDIER";
                race = rule is { Race.Length: > 0 } ? rule.Race : "STR_HOSTILE";
                break;
            case BattleFaction.Neutral:
                rank = rule is { Rank.Length: > 0 } ? rule.Rank : "STR_CIVILIAN";
                race = rule is { Race.Length: > 0 } ? rule.Race : "STR_NEUTRAL";
                break;
            default:
                rank = race = "STR_UNKNOWN";
                break;
        }
        return new BattleUnitKillSnapshot { Name = name, Type = type, Rank = rank, Race = race };
    }

    /// <summary>The credit the murderer's statistics get for a kill or stun (a mind controlled unit's goes to the unit that controls it).</summary>
    private void CreditKill(BattleUnit victim, BattleUnit murderer, BattleUnitKillSnapshot kill)
    {
        if (murderer.Faction == BattleFaction.Player && murderer.OriginalFaction != BattleFaction.Player)
        {
            // This must be a mind controlled unit: the kill goes to the soldier who controls it.
            if (UnitById(murderer.MindControllerId) is { SoldierId: not null } controller)
            {
                if (!IsCosmetic(victim))
                {
                    controller.Statistics = controller.Statistics with
                    {
                        Kills = [.. controller.Statistics.Kills, kill],
                        SlaveKills = controller.Statistics.SlaveKills + (victim.Faction == BattleFaction.Hostile ? 1 : 0),
                    };
                }
                victim.MurdererId = controller.Id;
            }
        }
        else if (!murderer.Statistics.Kills.Any(entry => entry.Id == victim.Id && entry.Status == kill.Status))
        {
            if (!IsCosmetic(victim)) murderer.Statistics = murderer.Statistics with { Kills = [.. murderer.Statistics.Kills, kill] };
            victim.MurdererId = murderer.Id;
        }
    }

    /// <summary>
    /// The branch of <c>checkForCasualties</c> for a unit with no health left: the morale of the murderer and of both squads, the soldier's
    /// memorial, and the death's state.
    /// </summary>
    private PendingDeath Perish(BattleUnit victim, BattleUnit? murderer, BattleUnitKillSnapshot kill,
        RuntimeItemDamageRule? damageType, bool hiddenExplosion, bool terrainExplosion)
    {
        var moraleLossWhenKilled = MoraleLossWhenKilled(victim);
        if (murderer is not null)
        {
            murderer.Kills++;
            victim.KilledBy = (int)murderer.Faction;
            var modifier = murderer.Faction == BattleFaction.Player ? FactionMoraleModifier(true) : 100;
            var armor = ArmorOf(murderer);
            // A known murderer gets a morale bonus if it is of the other side (neutrals excluded)...
            if (victim.OriginalFaction == BattleFaction.Player && murderer.Faction == BattleFaction.Hostile ||
                victim.OriginalFaction == BattleFaction.Hostile && murderer.Faction == BattleFaction.Player)
                MoraleChange(armor, murderer, 20 * modifier / 100);
            // ...and a penalty for friendly fire.
            if (victim.OriginalFaction == murderer.OriginalFaction)
                MoraleChange(armor, murderer, -(2000 * moraleLossWhenKilled / modifier / 100));
            if (victim.OriginalFaction == BattleFaction.Neutral)
            {
                // X-COM killing civilians loses morale; anyone else gains a little.
                if (murderer.OriginalFaction == BattleFaction.Player) MoraleChange(armor, murderer, -(1000 * moraleLossWhenKilled / modifier / 100));
                else MoraleChange(armor, murderer, 10);
            }
        }

        if (victim.Faction != BattleFaction.Neutral)
        {
            var modifier = UnitMoraleModifier(victim);
            var loserModifier = FactionMoraleModifier(victim.OriginalFaction != BattleFaction.Hostile);
            var winnerModifier = FactionMoraleModifier(victim.OriginalFaction == BattleFaction.Hostile);
            foreach (var other in _units)
            {
                if (other.IsOut) continue;
                if (other.OriginalFaction == victim.OriginalFaction)
                {
                    // The losing squad all get a morale loss for losing a team member (mind controlled units aside).
                    var bravery = ReduceByBravery(other, 10);
                    MoraleChange(ArmorOf(other), other, -(modifier * moraleLossWhenKilled * 200 * bravery / loserModifier / 100 / 100));
                    if (victim.Faction == BattleFaction.Hostile && murderer is not null && (murderer.Position ?? murderer.SavedPosition) is var where)
                    {
                        // The murderer is exposed to the aliens (the hostile entry of its spotting turns, and the tile they last saw it on).
                        var index = Contains(where) ? Map.TileIndex(where) : -1;
                        murderer.TurnsSinceSpotted[(int)BattleFaction.Hostile] = 0;
                        murderer.TileLastSpottedBy[(int)victim.Faction] = index;
                        murderer.TileLastSpottedForBlindShotBy[(int)victim.Faction] = index;
                    }
                }
                else
                {
                    // The winning squad all get a morale increase.
                    MoraleChange(ArmorOf(other), other, 10 * winnerModifier / 100);
                }
            }
        }

        var noSound = false;
        RuntimeItemDamageRule type;
        if (damageType is not null) type = damageType;
        else if (hiddenExplosion)
        {
            // Instant death from the power sources of a UFO, without screaming sounds.
            noSound = true;
            type = HighExplosiveType;
        }
        else if (terrainExplosion) type = HighExplosiveType;
        else type = NoDamageType; // no murderer and no terrain explosion: a fatal wound
        var death = BeginDeath(victim, type, noSound);
        Events.Add(new UnitDied(victim.Id, murderer?.Id ?? -1));

        // One of our own died: the memorial records the murderer in the cause.
        if (victim.SoldierId is { } soldierId)
        {
            var cause = kill;
            if (murderer is not null) cause = DescribeUnit(murderer) with
            {
                Mission = kill.Mission,
                Turn = kill.Turn,
                Side = kill.Side,
                BodyPart = kill.BodyPart,
                Id = kill.Id,
                Weapon = kill.Weapon,
                WeaponAmmo = kill.WeaponAmmo,
                Status = kill.Status,
                Faction = (int)murderer.OriginalFaction,
            };
            CampaignLink?.KillSoldier(soldierId, cause);
        }
        return death;
    }

    // ---- UnitDieBState ----

    /// <summary>
    /// The constructor of <c>UnitDieBState</c>: a unit that dies by an explosion or fall, or that is already unconscious, skips its pirouette
    /// (it must face direction 3 first) and falls at once; its sight is cleared; a dying alien makes the route nodes around it dangerous.
    /// Before the game starts the unit becomes its corpse here and now.
    /// </summary>
    internal PendingDeath BeginDeath(BattleUnit unit, RuntimeItemDamageRule type, bool noSound)
    {
        var death = new PendingDeath(unit, type, noSound, OverKillDamage(unit) > 0);
        if (!type.IsDirect || unit.Status == BattleUnitStatus.Unconscious)
        {
            unit.SetDirection(DeathDirection);
            unit.InstaFalling();
            if (IsBeforeGame)
            {
                ConvertToCorpse(death);
                death.Finished = true;
            }
        }
        unit.ClearVisibleTiles(Map);
        unit.ClearVisibleUnits();
        // freePatrolTarget: the node the AI was walking to is free again.
        if (unit.Ai is { ToNode: >= 0 } ai && ai.ToNode < Map.Nodes.Count) Map.Nodes[ai.ToNode].Allocated = false;
        if (!IsBeforeGame && unit.Faction == BattleFaction.Hostile)
        {
            // The unit's position, tile or not (a carried unit's is the invalid (-1, -1, -1)).
            var at = RestingPosition(unit) ?? new BattlePosition(-1, -1, -1);
            foreach (var node in Map.Nodes)
            {
                if (node.IsDummy) continue;
                var (dx, dy, dz) = (node.Position.X - at.X, node.Position.Y - at.Y, node.Position.Z - at.Z);
                if (dx * dx + dy * dy + dz * dz < 4) node.Type |= BattleNode.TypeDangerous;
            }
        }
        return death;
    }

    /// <summary>The queue's turn: each death's state runs to its end, in order.</summary>
    internal IReadOnlyList<BattleCasualty> RunDeaths(IEnumerable<PendingDeath> pending)
    {
        var casualties = new List<BattleCasualty>();
        foreach (var death in pending)
            if (RunDeath(death) is { } casualty) casualties.Add(casualty);
        return casualties;
    }

    /// <summary><c>UnitDieBState::init</c> and <c>think</c> to the state's end; the outcome, or null when the state skipped the unit.</summary>
    private BattleCasualty? RunDeath(PendingDeath death)
    {
        var unit = death.Unit;
        if (death.Finished) return new(unit.Id, unit.Status);
        if (SkippedByInit(unit)) return null;

        // think: a unit hit directly turns to face direction 3 one eighth at a time, then falls.
        if (unit.Direction != DeathDirection && death.Type.IsDirect)
        {
            while (unit.Direction != DeathDirection)
            {
                unit.LookAt((unit.Direction + 1) % 8, force: false);
                unit.Turn(turret: false);
            }
        }
        if (!unit.IsOut)
        {
            unit.StartFalling();
            // The frames of the collapse end in the status the health decides (keepFalling).
            unit.Status = unit.Health <= 0 ? BattleUnitStatus.Dead : BattleUnitStatus.Unconscious;
        }

        // The unit is out: its sight and spotting end, and it becomes its spawn unit or its corpse.
        var where = RestingPosition(unit);
        var size = unit.ArmorSize;
        int? convertedTo = null;
        if (unit.Status == BattleUnitStatus.Unconscious && !IsCapturable(unit)) unit.InstaKill();
        Array.Fill(unit.TurnsSinceSpotted, byte.MaxValue);
        Array.Fill(unit.TurnsLeftSpottedForSnipers, (byte)0);
        for (var faction = 0; faction < unit.TurnsSinceSeenBy.Length; faction++)
        {
            if (unit.TurnsSinceSeenBy[faction] < 255) unit.TurnsSinceSeenBy[faction] = 255;
            if (unit.TileLastSpottedBy[faction] >= 0) unit.TileLastSpottedBy[faction] = -1;
            if (unit.TileLastSpottedForBlindShotBy[faction] >= 0) unit.TileLastSpottedForBlindShotBy[faction] = -1;
        }
        if (HasSpawnUnit(unit) && !death.OverKill)
        {
            if (!unit.AlreadyRespawned) convertedTo = ConvertUnit(unit)?.Id;
        }
        else
        {
            ConvertToCorpse(death);
        }
        ClearUnitSelection(unit);

        // The last frames: the light of the items and everyone's sight of the place are updated, and the notices of X-COM's own are recorded.
        if (where is { } place)
        {
            CalculateLighting(BattleLightLayer.Items, place, size);
            Sight().CalculateFov(place, size, updateTiles: false);
        }
        if (unit.OriginalFaction == BattleFaction.Player)
        {
            if (unit.Status == BattleUnitStatus.Dead)
            {
                if (death.Type.ResistType == RuntimeItemDamageRule.DamageNone && !HasSpawnUnit(unit) && unit.NotificationShown < 2)
                    unit.NotificationShown = 2;
            }
            else if (!unit.DisableIndicators && unit.NotificationShown < 1)
            {
                unit.NotificationShown = 1;
            }
        }
        MarkChanged();
        return new(unit.Id, unit.Status, convertedTo);
    }

    // ---- corpses and the dead unit's things ----

    /// <summary>
    /// <c>UnitDieBState::convertUnitToCorpse</c>: the unit's body item is removed (it lies on the map), the inventory goes to the ground, the unit
    /// leaves its tile, and one corpse item per tile it covered lies where it fell (the corpse of a carried unit replaces its body item); with
    /// overkill nothing is left.
    /// </summary>
    private void ConvertToCorpse(PendingDeath death)
    {
        var unit = death.Unit;
        var last = RestingPosition(unit);
        var size = unit.ArmorSize;
        // Options::weaponSelfDestruction is off, so a unit with an inventory always drops it.
        var dropItems = HasInventory(unit);
        var tile = unit.Position;
        // Remove the unconscious body item of this unit; a carried unit keeps its body item until it is replaced below.
        if (last is not null) RemoveUnconsciousBodyItem(unit);
        if (dropItems && tile is { } at) ItemDropInventory(at, unit, deleteFixedItems: false);
        ClearTile(unit);

        if (last is null)
        {
            // We are being carried.
            if (death.OverKill)
            {
                RemoveUnconsciousBodyItem(unit);
            }
            else if (_items.Find(item => item.CorpseUnitId == unit.Id) is { } body)
            {
                // We are in an inventory, so we are a 1x1 unit.
                var corpses = CorpseItems(unit);
                if (corpses.Count != 0 && corpses[0] is { } corpse) ConvertItemToCorpse(body, corpse);
            }
            return;
        }
        if (death.OverKill)
        {
            ApplyGravity(last);
            return;
        }
        var corpseRules = CorpseItems(unit);
        var index = size * size - 1;
        for (var y = size - 1; y >= 0; y--)
        {
            for (var x = size - 1; x >= 0; x--)
            {
                // A mod whose armor lists too few corpses would crash the reference; the part leaves no corpse (ADR 0041).
                if (index < corpseRules.Count && corpseRules[index] is { } corpseType)
                {
                    var item = AddItemOnTile(ConstructItem(RequiredRules, TakeItemId(), corpseType), null);
                    item.CorpseUnitId = unit.Id;
                    ItemDrop(new BattlePosition(last.Value.X + x, last.Value.Y + y, last.Value.Z), item, updateLight: true);
                }
                index--;
            }
        }
    }

    /// <summary>Armor::getCorpseBattlescape: the item type of each tile part of the unit's body (null for a part the armor lacks).</summary>
    private IReadOnlyList<string?> CorpseItems(BattleUnit unit)
    {
        if (!RequiredRules.Armors.TryGet(unit.ArmorId, out var armor)) return [];
        return [.. RequiredRules.Armors[armor].Value.Script.CorpseBattle.Select(handle => handle is { } item ? RequiredRules.Items.GetExternalId(item) : null)];
    }

    /// <summary>BattleItem::convertToCorpse: a corpse item takes the corpse rule of another armor.</summary>
    private void ConvertItemToCorpse(BattleItem item, string corpseType)
    {
        if (item.CorpseUnitId is null || ItemRuleOf(item.ItemType).BattleType != CorpseItemBattleType ||
            ItemRuleOf(corpseType).BattleType != CorpseItemBattleType)
            return;
        item.ItemType = corpseType;
        MarkChanged();
    }

    /// <summary>
    /// SavedBattleGame::removeUnconsciousBodyItem: the body item of the unit leaves the battle (a unit's body is one item per tile it
    /// covers, so up to its size squared).
    /// </summary>
    internal void RemoveUnconsciousBodyItem(BattleUnit unit)
    {
        var size = unit.ArmorSize * unit.ArmorSize;
        foreach (var item in _items.Where(item => item.CorpseUnitId == unit.Id).ToArray())
        {
            RemoveItem(item);
            if (--size == 0) break;
        }
    }

    /// <summary>
    /// TileEngine::itemDropInventory: the unit's items go to the tile, but for the fixed ones, which cannot be dropped (and with
    /// <paramref name="deleteFixedItems"/> are removed, their ammunition dropped first); the unit's special weapons go with them.
    /// </summary>
    internal void ItemDropInventory(BattlePosition tile, BattleUnit unit, bool deleteFixedItems)
    {
        foreach (var item in OrderedInventoryOf(unit).ToArray())
        {
            if (!RequiredRules.Battle.Equipment.Items[item.ItemType].Fixed)
            {
                MoveToOwner(item, null);
                item.Slot = GroundSlot;
                AddToTile(item, tile);
                // An unconscious unit that lies in the inventory comes to lie on the tile.
                if (item.CorpseUnitId is { } corpse && UnitById(corpse) is { Status: BattleUnitStatus.Unconscious } lying) SetLyingPosition(lying, tile);
            }
            else if (deleteFixedItems)
            {
                // First unload all ammunition, which lies on the ground.
                for (var slot = 0; slot < BattleItem.AmmoSlotCount; slot++)
                {
                    if (item.AmmoSlots[slot] is { } ammoId && ammoId != item.Id && FindItem(ammoId) is { } ammo)
                    {
                        item.AmmoSlots[slot] = null;
                        ammo.IsAmmo = false;
                        ItemDrop(tile, ammo, updateLight: false);
                    }
                }
                MoveToOwner(item, null);
                RemoveItem(item);
            }
        }
        // Handle the special built-in items.
        if (deleteFixedItems)
        {
            foreach (var id in unit.SpecialWeaponIds.ToArray())
            {
                if (FindItem(id) is not { } special) continue;
                MoveToOwner(special, null);
                special.IsSpecialWeapon = false;
                RemoveItem(special);
            }
            unit.SpecialWeaponIds.Clear();
        }
        MarkChanged();
    }

    /// <summary>
    /// TileEngine::itemDrop: the item lies on the tile (whoever stands there learns the tile as a clue), gravity takes it down to a floor, and the
    /// light and sight around it are updated. A fixed item is never dropped. A live grenade dropped where a unit stands sets the danger zone first.
    /// </summary>
    internal void ItemDrop(BattlePosition tile, BattleItem item, bool updateLight)
    {
        if (!Contains(tile)) return;
        if (RequiredRules.Battle.Equipment.Items[item.ItemType].Fixed) return;
        var index = Map.TileIndex(tile);
        var dropper = UnitOnTile(index);
        // A primed grenade dropped where a unit stands makes a danger zone (the AI keeps away from it for the turn).
        var dropRule = ItemRuleOf(item.ItemType);
        if (dropper is not null && dropRule.BattleType == GrenadeBattleType && item.FuseEnabled && dropRule.Damage.ResistType != RuntimeItemDamageRule.DamageNone)
            SetDangerZone(tile, ExplosionRadius(item, new BattleAttack(BattleActionType.Throw, dropper, item, item)), dropper);
        if (Side != BattleFaction.Player) item.DroppedOnAlienTurn = true;

        // itemMoveInventory to the ground.
        dropper?.UpdateEnemyKnowledge(index);
        MoveToOwner(item, null);
        item.Slot = GroundSlot;
        item.SlotX = 0;
        item.SlotY = 0;
        AddToTile(item, tile);
        if (item.CorpseUnitId is { } corpse && UnitById(corpse) is { Status: BattleUnitStatus.Unconscious } lying) SetLyingPosition(lying, tile);

        ApplyGravity(tile);
        if (updateLight)
        {
            CalculateLighting(BattleLightLayer.Items, tile);
            Sight().CalculateFov(tile, VisibilityUpdateRange(item), updateTiles: false);
        }
        MarkChanged();
    }

    /// <summary>BattleUnit::setPosition for an unconscious body that comes to lie on a tile (the unit's tile link, if it has one, follows).</summary>
    internal static void SetLyingPosition(BattleUnit unit, BattlePosition tile)
    {
        unit.SavedPosition = tile;
        if (unit.Position is not null) unit.Position = tile;
    }

    // ---- BattleUnit::knockOut, convertUnit, convertInfected ----

    /// <summary>
    /// BattleUnit::knockOut: the stun reaches the health (a unit with a spawn unit turns into it first, and the new one is knocked out; the new
    /// one's own spawn unit is cleared so that it cannot loop).
    /// </summary>
    internal void KnockOut(BattleUnit unit)
    {
        if (HasSpawnUnit(unit))
        {
            unit.Respawn = false;
            if (ConvertUnit(unit) is { } created)
            {
                // Scripts or rulesets could make a new chryssalid from a chryssalid: clearing the spawn unit breaks the loop.
                // clearSpawnUnit
                created.SpawnUnit = "";
                created.SpawnUnitFaction = (int)BattleFaction.Hostile;
                created.Respawn = false;
                KnockOut(created);
            }
        }
        else
        {
            unit.StunLevel = Math.Max(unit.Health, 1);
        }
    }

    /// <summary>
    /// SavedBattleGame::convertUnit: the unit dies at once (it respawns only once), its inventory goes to the ground, and a unit of its spawn
    /// type stands where it was, facing as it did, with no time units. Nothing is spawned for a carried unit or when another unit stands on the place.
    /// </summary>
    internal BattleUnit? ConvertUnit(BattleUnit unit)
    {
        // Only ever respawn once.
        unit.AlreadyRespawned = true;
        var visible = unit.IsVisibleToPlayer(RequiredRules);
        ClearUnitSelection(unit);
        var carried = IsCarried(unit);
        // In case the unit was unconscious.
        RemoveUnconsciousBodyItem(unit);
        unit.InstaKill();

        var place = carried ? null : RestingPosition(unit);
        // An unconscious unit could be stood upon, or taken into an inventory: then nothing is spawned.
        if (place is not { } tile || UnitOnTile(Map.TileIndex(tile)) is { } other && other != unit) return null;
        if (!HasSpawnUnit(unit)) return null;

        ItemDropInventory(tile, unit, deleteFixedItems: true);
        var direction = unit.Direction;
        var faction = (BattleFaction)unit.SpawnUnitFaction;
        ClearTile(unit);

        var (unitRule, armor) = UnitRule(unit.SpawnUnit);
        // A spawn unit bigger than the unit it replaces could cover a tile that is off the map or taken, where the reference would overwrite
        // the tile's unit; the port spawns nothing then (ADR 0041).
        foreach (var covered in BattleMap.Footprint(tile, armor.Size))
            if (!Contains(covered) || UnitOnTile(Map.TileIndex(covered)) is not null)
                return null;
        // createTempUnit: the next ID after the last unit's; a player unit made this way is a summoned one.
        var created = TempUnit((_units.Count == 0 ? -1 : _units[^1].Id) + 1, faction,
            faction == BattleFaction.Player ? BattleController.Human(0) : BattleController.Ai, unit.SpawnUnit, unitRule, armor,
            CampaignLink?.Difficulty ?? 0, summoned: faction == BattleFaction.Player);
        created.SpawnUnitFaction = unit.SpawnUnitFaction; // an allied or neutral zombie spawns an allied or neutral chryssalid
        created.TimeUnits = 0;
        created.Position = tile;
        created.SetDirection(direction);
        AddUnit(created);
        SetSpotted(created, BattleFaction.Player, visible);
        new UnitEquipment(this, RequiredRules, Random).InitUnit(created, unitRule, "");
        // This happens fairly rarely, so do a full recalculation of the units in range.
        Sight().CalculateFov(tile);
        ApplyGravity(tile);
        created.DontReselect = true;
        MarkChanged();
        Events.Add(new UnitConverted(unit.Id, created.Id));
        return created;
    }

    /// <summary>
    /// BattlescapeGame::convertInfected: every unit with the respawn flag that is not out turns into its spawn unit. Returns whether any did.
    /// </summary>
    internal bool ConvertInfected()
    {
        var infected = new List<BattleUnit>();
        foreach (var unit in _units)
        {
            if (unit.IsOutThresholdExceeded || !unit.Respawn) continue;
            unit.Respawn = false;
            infected.Add(unit);
        }
        foreach (var unit in infected)
            if (HasSpawnUnit(unit)) ConvertUnit(unit);
        if (infected.Count != 0) MarkChanged();
        return infected.Count != 0;
    }

    // ---- revival ----

    /// <summary>
    /// SavedBattleGame::reviveUnconsciousUnits: a small unconscious unit over the out threshold no more wakes up where it lies, or on the nearest free
    /// tile (it stays unconscious when there is none), stands, and loses its time units (<paramref name="noTU"/>) or recovers them as a new turn does,
    /// cut by <c>tuRecoveryWakeUpNewTurn</c>.
    /// </summary>
    internal void ReviveUnconsciousUnits(bool noTU)
    {
        var equipment = new UnitEquipment(this, RequiredRules, Seedless);
        foreach (var unit in _units)
        {
            if (unit.ArmorSize != 1 || unit.Status == BattleUnitStatus.IgnoreMe) continue;
            var original = RestingPosition(unit);
            if (original is null && _items.Find(item => item.CorpseUnitId == unit.Id && item.OwnerUnitId is not null) is { OwnerUnitId: { } holder } &&
                UnitById(holder) is { } owner)
                original = owner.Position ?? owner.SavedPosition;
            if (unit.Status != BattleUnitStatus.Unconscious || unit.IsOutThresholdExceeded || original is not { } at || !Contains(at)) continue;
            var onTile = UnitOnTile(Map.TileIndex(at));
            var largeUnit = onTile is not null && onTile != unit && onTile.IsBigUnit;
            if (!PlaceUnitNearPosition(unit, at, largeUnit)) continue;

            // Recover from unconscious.
            unit.NotificationShown = 0;
            unit.Turn(turret: false); // makes the unit stand up again
            unit.Kneel(false);
            unit.AlreadyExploded = false;
            if (noTU)
            {
                unit.TimeUnits = 0;
            }
            else
            {
                PrepareTimeUnits(unit, unit.Stats.TimeUnits, equipment);
                PrepareEnergy(unit, unit.EnergyRegeneration);
                if (RequiredRules.Campaign.Combat.TuRecoveryWakeUpNewTurn < 100)
                    unit.TimeUnits = unit.TimeUnits * RequiredRules.Campaign.Combat.TuRecoveryWakeUpNewTurn / 100;
            }
            RemoveUnconsciousBodyItem(unit);
            MarkChanged();
        }
    }

    /// <summary>
    /// SavedBattleGame::placeUnitNearPosition: the unit takes the position, or the first of the eight tiles around it (a large friend on the
    /// tile pushes them out by a tile) that no wall closes off and where it fits; a flying unit may take the tile above. Returns whether it was placed.
    /// </summary>
    internal bool PlaceUnitNearPosition(BattleUnit unit, BattlePosition entry, bool largeFriend)
    {
        if (SetUnitPosition(unit, entry)) return true;

        var me = -unit.ArmorSize;
        var you = largeFriend ? 2 : 1;
        int[] xs = [0, you, you, you, 0, me, me, me];
        int[] ys = [me, me, 0, you, you, you, 0, me];
        BattlePathfinder? pathfinder = null;
        for (var direction = 0; direction <= 7; direction++)
        {
            var offset = new BattlePosition(xs[direction], ys[direction], 0);
            var target = new BattlePosition(entry.X + offset.X, entry.Y + offset.Y, entry.Z);
            if (!Contains(target)) continue;
            var middle = new BattlePosition(entry.X + offset.X / 2, entry.Y + offset.Y / 2, entry.Z);
            pathfinder ??= new BattlePathfinder(this, RequiredRules);
            if (!pathfinder.IsBlockedDirection(unit, middle, direction) && SetUnitPosition(unit, target)) return true;
        }
        if (unit.MovementType == MovementTypeFly)
        {
            var above = new BattlePosition(entry.X, entry.Y, entry.Z + 1);
            if (Contains(above) && Map.HasNoFloor(above) && SetUnitPosition(unit, above)) return true;
        }
        return false;
    }

    /// <summary>SavedBattleGame::setUnitPosition: the unit takes the position if it fits there (see <see cref="BattlePlacement.TryPosition(BattleMap, BattleUnit, BattlePosition)"/>).</summary>
    private bool SetUnitPosition(BattleUnit unit, BattlePosition position)
    {
        if (BattlePlacement.TryPosition(Map, unit, position) is not { } placed) return false;
        unit.InventoryTile = null;
        unit.LastPosition = unit.Position ?? unit.SavedPosition;
        SetTile(unit, placed);
        unit.SavedPosition = placed;
        return true;
    }
}

internal sealed partial class BattleUnit
{
    /// <summary>BattleUnit::startFalling: the collapse begins.</summary>
    internal void StartFalling()
    {
        Status = BattleUnitStatus.Collapsing;
        TurnsSinceStunned = 0;
    }

    /// <summary>BattleUnit::instaFalling: the final state of the fall at once, dead for a unit with no health, else unconscious.</summary>
    internal void InstaFalling()
    {
        StartFalling();
        Status = Health <= 0 ? BattleUnitStatus.Dead : BattleUnitStatus.Unconscious;
    }

    /// <summary>BattleUnit::instaKill: no health, dead.</summary>
    internal void InstaKill()
    {
        Health = 0;
        Status = BattleUnitStatus.Dead;
        TurnsSinceStunned = 0;
    }
}

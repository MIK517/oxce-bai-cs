using Oxce.Mods.Rulesets.Runtime;

namespace Oxce.Gameplay.Campaigns;

/// <summary>The item fields a kill criterion reads: RuleItem battle type and its damage and melee resist types.</summary>
internal readonly record struct CommendationItem(int BattleType, int DamageResistType, int MeleeResistType);

/// <summary>What SoldierDiary::manageCommendations reads besides the soldier.</summary>
internal sealed record CommendationInputs(
    IReadOnlyList<(string Id, RuntimeCommendationRule Rule)> Commendations,
    Func<IReadOnlyList<string>, bool> Researched,
    IReadOnlyList<MissionStatisticSnapshot> MissionStatistics,
    Func<string, CommendationItem?> Item,
    int MaximumDarknessToSeeUnits);

/// <summary>
/// SoldierDiary::manageCommendations at reference 4df3a5e: every commendation in name order (std::map, UTF-8 bytes)
/// that the soldier type supports and whose research is known is awarded while its criteria hold, raising the
/// level once per award. Pinned by the <c>strategic-commendations</c> oracle; ADR 0036 records the paths the
/// reference leaves undefined.
/// </summary>
internal static class SoldierCommendations
{
    private const string NoNoun = "noNoun";

    private static readonly string[] BattleTypes =
    [
        "BT_NONE", "BT_FIREARM", "BT_AMMO", "BT_MELEE", "BT_GRENADE", "BT_PROXIMITYGRENADE", "BT_MEDIKIT", "BT_SCANNER",
        "BT_MINDPROBE", "BT_PSIAMP", "BT_FLARE", "BT_CORPSE", "BT_END",
    ];

    private static readonly string[] DamageTypes =
    [
        "DT_NONE", "DT_AP", "DT_IN", "DT_HE", "DT_LASER", "DT_PLASMA", "DT_STUN", "DT_MELEE", "DT_ACID", "DT_SMOKE",
        "DT_10", "DT_11", "DT_12", "DT_13", "DT_14", "DT_15", "DT_16", "DT_17", "DT_18", "DT_19", "DT_END",
    ];

    /// <summary>Awards what the soldier now deserves into <paramref name="commendations"/>; true when anything was awarded.</summary>
    public static bool Manage(string soldierType, SoldierPersonalState personal, List<SoldierCommendation> commendations,
        CommendationInputs inputs)
    {
        var awarded = false;
        var diary = personal.Diary;
        // Mod::getItem: STR_NONE names no item, even when a rule has that name.
        var item = inputs.Item;
        inputs = inputs with { Item = id => id == "STR_NONE" ? null : item(id) };
        var levels = new Dictionary<string, int>(StringComparer.Ordinal);
        var modular = new List<string>();
        for (var index = 0; index < inputs.Commendations.Count;)
        {
            var (type, rule) = inputs.Commendations[index];
            // A commendation without criteria (such as the original eight's, which the reference awards in code) holds
            // for ever, and the reference would award it without end; the port never awards it here (ADR 0036).
            if (rule.Criteria.Count == 0 ||
                rule.Units.Count != 0 && !rule.Units.Contains(soldierType, StringComparer.Ordinal) ||
                !inputs.Researched(rule.Requires))
            {
                index++;
                continue;
            }

            var award = true;
            levels.Clear();
            levels[NoNoun] = 0;
            modular.Clear();
            foreach (var owned in commendations)
                if (owned.RuleId == type) levels[owned.Noun] = owned.DecorationLevel + 1;
            foreach (var (name, thresholds) in rule.Criteria)
            {
                var nextLevel = levels[NoNoun];
                // Skip this commendation once its last level is reached.
                if (nextLevel >= thresholds.Count)
                {
                    award = false;
                    break;
                }
                var threshold = thresholds[nextLevel];
                if (Fails(name, threshold, personal, diary, rule, inputs))
                {
                    award = false;
                    break;
                }
                if (name is "totalKillsWithAWeapon" or "totalMissionsInARegion" or "totalKillsByRace" or "totalKillsByRank")
                {
                    foreach (var (noun, count) in NounTotals(name, diary, inputs.MissionStatistics))
                    {
                        var criterion = -1;
                        if (!levels.TryGetValue(noun, out var level)) criterion = thresholds[0];
                        // A level past the last one reads past the list in the reference (ADR 0036); it awards nothing.
                        else if (level < thresholds.Count) criterion = thresholds[level];
                        if (criterion != -1 && count >= criterion) modular.Add(noun);
                    }
                    if (modular.Count == 0)
                    {
                        award = false;
                        break;
                    }
                }
                else if (name is "killsWithCriteriaCareer" or "killsWithCriteriaMission" or "killsWithCriteriaTurn" &&
                    !EnoughKillGroups(name, threshold, rule, diary.Kills, inputs.Item))
                {
                    award = false;
                    break;
                }
            }
            if (!award)
            {
                index++;
                continue;
            }

            if (modular.Count == 0) modular.Add(NoNoun);
            foreach (var noun in modular)
            {
                var existing = commendations.FindIndex(owned => owned.RuleId == type && owned.Noun == noun);
                if (existing >= 0)
                    commendations[existing] = commendations[existing] with { DecorationLevel = commendations[existing].DecorationLevel + 1 };
                else commendations.Add(new SoldierCommendation(type, noun, 0));
            }
            // The reference tries the same commendation again until it no longer holds.
            awarded = true;
        }
        return awarded;
    }

    /// <summary>The criteria without nouns: true when the soldier falls short of <paramref name="threshold"/>.</summary>
    private static bool Fails(string name, int threshold, SoldierPersonalState personal, SoldierDiaryState diary,
        RuntimeCommendationRule rule, CommendationInputs inputs)
    {
        var statistics = inputs.MissionStatistics;
        return name switch
        {
            "totalKills" => KillTotal(diary, SoldierKill.StatusDead) < threshold,
            "totalMissions" => MissionTotalFiltered(diary, statistics, rule) < threshold,
            "totalWins" => Missions(diary, statistics).Count(static s => s.Success) < threshold,
            "totalScore" => Missions(diary, statistics).Sum(static s => s.Score) < threshold,
            "totalStuns" => KillTotal(diary, SoldierKill.StatusUnconscious) < threshold,
            "totalDaysWounded" => diary.Counter("daysWoundedTotal") < threshold,
            "totalBaseDefenseMissions" => Missions(diary, statistics).Count(static s => s.Success && s.IsBaseDefense) < threshold,
            "totalTerrorMissions" => Missions(diary, statistics).Count(static s => s.Success && !s.IsBaseDefense && !s.IsUfoMission &&
                !s.IsAlienBase) < threshold,
            "totalNightMissions" => Missions(diary, statistics).Count(s => s.Success && s.Daylight > inputs.MaximumDarknessToSeeUnits &&
                !s.IsBaseDefense && !s.IsAlienBase) < threshold,
            "totalNightTerrorMissions" => Missions(diary, statistics).Count(s => s.Success && s.Daylight > inputs.MaximumDarknessToSeeUnits &&
                !s.IsBaseDefense && !s.IsUfoMission && !s.IsAlienBase) < threshold,
            "totalMonthlyService" => diary.Counter("monthsService") < threshold,
            "totalFellUnconcious" => diary.Counter("unconciousTotal") < threshold,
            "totalShotAt10Times" => diary.Counter("shotAtCounter10in1Mission") < threshold,
            "totalHit5Times" => diary.Counter("hitCounter5in1Mission") < threshold,
            "totalFriendlyFired" => diary.Counter("totalShotByFriendlyCounter") < threshold || diary.Counter("killedInAction") != 0 ||
                diary.Counter("missingInAction") != 0,
            "total_lone_survivor" => diary.Counter("loneSurvivorTotal") < threshold,
            "totalIronMan" => diary.Counter("ironManTotal") < threshold,
            "totalImportantMissions" => Missions(diary, statistics).Count(static s => s.Success && s.Type != "STR_UFO_CRASH_RECOVERY") < threshold,
            "totalLongDistanceHits" => diary.Counter("longDistanceHitCounterTotal") < threshold,
            "totalLowAccuracyHits" => diary.Counter("lowAccuracyHitCounterTotal") < threshold,
            "totalReactionFire" => diary.Kills.Count(kill => kill.HostileTurn && inputs.Item(kill.Weapon) is { } item &&
                !IsGrenade(item.BattleType)) < threshold,
            "totalTimesWounded" => diary.Counter("timesWoundedTotal") < threshold,
            "totalValientCrux" => Missions(diary, statistics).Count(static s => s.ValiantCrux) < threshold,
            "isDead" => diary.Counter("killedInAction") < threshold,
            "totalTrapKills" => diary.Kills.Count(kill => kill.HostileTurn && (inputs.Item(kill.Weapon) is not { } item ||
                IsGrenade(item.BattleType))) < threshold,
            "totalAlienBaseAssaults" => Missions(diary, statistics).Count(static s => s.Success && s.IsAlienBase) < threshold,
            "totalAllAliensKilled" => diary.Counter("allAliensKilledTotal") < threshold,
            "totalAllAliensStunned" => diary.Counter("allAliensStunnedTotal") < threshold,
            "totalUfosShotDown" => personal.UfosShotDownTotal < threshold,
            "totalUfosDamage" => personal.UfosDamageTotal < threshold,
            "totalWoundsHealed" => diary.Counter("woundsHealedTotal") < threshold,
            "totalAllUFOs" => diary.Counter("allUFOs") < threshold,
            "totalAllMissionTypes" => diary.Counter("allMissionTypes") < threshold,
            "totalStatGain" => diary.Counter("statGainTotal") < threshold,
            "totalRevives" => diary.Counter("revivedUnitTotal") < threshold,
            "totalSoldierRevives" => diary.Counter("revivedSoldierTotal") < threshold,
            "totalHostileRevives" => diary.Counter("revivedHostileTotal") < threshold,
            "totalNeutralRevives" => diary.Counter("revivedNeutralTotal") < threshold,
            "totalWholeMedikit" => diary.Counter("wholeMedikitTotal") < threshold,
            "totalBraveryGain" => diary.Counter("braveryGainTotal") < threshold,
            "bestOfRank" => diary.Counter("bestOfRank") < threshold,
            "bestSoldier" => diary.Counter("bestSoldier") < threshold,
            "isMIA" => diary.Counter("missingInAction") < threshold,
            "totalMartyrKills" => diary.Counter("martyrKillsTotal") < threshold,
            "totalPostMortemKills" => diary.Counter("postMortemKills") < threshold,
            "globeTrotter" => diary.Counter("globeTrotter") < threshold,
            "totalSlaveKills" => diary.Counter("slaveKillsTotal") < threshold,
            // Unknown names, the noun criteria and the kill criteria are not decided here.
            _ => false,
        };
    }

    private static bool IsGrenade(int battleType) => battleType is 4 or 5;

    private static int KillTotal(SoldierDiaryState diary, int status) =>
        diary.Kills.Count(kill => kill.Status == status && kill.Faction == SoldierKill.FactionHostile);

    /// <summary>
    /// The statistics loops: every statistic once for each of the soldier's mission IDs that matches it, so a repeated
    /// ID counts its mission again, as the reference's nested loops do.
    /// </summary>
    private static IEnumerable<MissionStatisticSnapshot> Missions(SoldierDiaryState diary, IReadOnlyList<MissionStatisticSnapshot> statistics)
    {
        foreach (var statistic in statistics)
            foreach (var id in diary.MissionIds)
                if (id == statistic.Id) yield return statistic;
    }

    /// <summary>SoldierDiary::getMissionTotalFiltered: a type filter wins over a marker filter; std::find counts each statistic once.</summary>
    private static int MissionTotalFiltered(SoldierDiaryState diary, IReadOnlyList<MissionStatisticSnapshot> statistics,
        RuntimeCommendationRule rule)
    {
        if (rule.MissionTypes.Count != 0)
            return statistics.Count(s => s.Success && diary.MissionIds.Contains(s.Id) && rule.MissionTypes.Contains(s.Type, StringComparer.Ordinal));
        if (rule.MissionMarkers.Count != 0)
            return statistics.Count(s => s.Success && diary.MissionIds.Contains(s.Id) &&
                rule.MissionMarkers.Contains(s.MarkerName, StringComparer.Ordinal));
        return diary.MissionIds.Count;
    }

    /// <summary>The noun criteria's std::map totals, in UTF-8 byte order of their nouns.</summary>
    private static IEnumerable<(string Noun, int Count)> NounTotals(string name, SoldierDiaryState diary,
        IReadOnlyList<MissionStatisticSnapshot> statistics)
    {
        IEnumerable<string> nouns = name switch
        {
            "totalKillsWithAWeapon" => diary.Kills.Where(static kill => kill.Faction == SoldierKill.FactionHostile).Select(static kill => kill.Weapon),
            "totalMissionsInARegion" => Missions(diary, statistics).Select(static s => s.Region),
            "totalKillsByRace" => diary.Kills.Select(static kill => kill.Race),
            _ => diary.Kills.Select(static kill => kill.Rank),
        };
        return nouns.GroupBy(static noun => noun, StringComparer.Ordinal)
            .Select(static group => (group.Key, group.Count()))
            .OrderBy(static pair => System.Text.Encoding.UTF8.GetBytes(pair.Key), Utf8ByteOrder);
    }

    private static readonly Comparer<byte[]> Utf8ByteOrder =
        Comparer<byte[]>.Create(static (left, right) => left.AsSpan().SequenceCompareTo(right));

    /// <summary>
    /// The kill criteria: kill groups that fill one AND block of an OR group, counted once per mission or turn for the
    /// peak criteria and in repeated batches for the career criterion, across all OR groups.
    /// </summary>
    private static bool EnoughKillGroups(string name, int needed, RuntimeCommendationRule rule, IReadOnlyList<SoldierKill> kills,
        Func<string, CommendationItem?> item)
    {
        var groups = 0;
        foreach (var or in rule.KillCriteria)
        {
            var reference = or.Select(static criterion => criterion.Threshold).ToArray();
            var referenceTotal = reference.Sum();
            // The mission and turn criteria start without counters; the reference then reads an empty vector for a
            // kill whose span is -1 (ADR 0036), which the port treats as a fresh span.
            var current = (int[])reference.Clone();
            var currentTotal = referenceTotal;
            var lastSpan = -1;
            var skip = false;
            foreach (var kill in kills)
            {
                var span = name == "killsWithCriteriaMission" ? kill.Mission : name == "killsWithCriteriaTurn" ? kill.Turn : -1;
                if (span != lastSpan)
                {
                    lastSpan = span;
                    skip = false;
                    current = (int[])reference.Clone();
                    currentTotal = referenceTotal;
                }
                else if (skip) continue;

                var met = false;
                for (var block = 0; block < or.Count; block++)
                {
                    if (!or[block].Values.All(detail => Matches(kill, detail, item))) continue;
                    // Counters may go negative, which keeps career batches whole.
                    if (current[block]-- > 0 && --currentTotal <= 0)
                    {
                        met = true;
                        break;
                    }
                }
                if (!met) continue;
                if (++groups >= needed) return true;
                if (name != "killsWithCriteriaCareer") skip = true;
                else
                {
                    currentTotal = 0;
                    for (var block = 0; block < current.Length; block++)
                    {
                        current[block] += reference[block];
                        currentTotal += Math.Max(current[block], 0);
                    }
                }
            }
        }
        return false;
    }

    private static bool Matches(SoldierKill kill, string detail, Func<string, CommendationItem?> item)
    {
        if (kill.Rank == detail || kill.Race == detail || kill.Weapon == detail || kill.WeaponAmmo == detail ||
            kill.StatusName == detail || kill.FactionName == detail || kill.SideName == detail || kill.BodyPartName == detail)
            return true;
        if (item(kill.Weapon) is not { } weapon) return false;
        if (weapon.BattleType >= 0 && weapon.BattleType < BattleTypes.Length && BattleTypes[weapon.BattleType] == detail) return true;
        var damageType = item(kill.WeaponAmmo) is { } ammo ? ammo.DamageResistType
            // The gun's secondary melee attack.
            : kill.WeaponAmmo == "__GUNBUTT" ? weapon.MeleeResistType : -1;
        return damageType >= 0 && damageType < DamageTypes.Length && DamageTypes[damageType] == detail;
    }
}

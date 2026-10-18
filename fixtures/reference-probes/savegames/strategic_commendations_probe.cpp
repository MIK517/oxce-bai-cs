// Monthly commendations: SoldierDiary::manageCommendations, addMonthlyService and the totals it reads
// (kills, stuns, missions, wins, score, terror, night, base defense, alien base, important, valiant crux,
// trap and reaction-fire kills, and the weapon, region, race and rank noun maps), the SoldierCommendations
// constructor and accessors, and the BattleUnitKills and MissionStatistics helpers are inserted verbatim by
// the capture script. Rules, items, research and soldiers are stubs holding what those methods read. Each
// scenario runs addMonthlyService then manageCommendations, as MonthlyReportState::btnOkClick does.
#include <algorithm>
#include <cstdint>
#include <cstdio>
#include <map>
#include <sstream>
#include <string>
#include <utility>
#include <vector>

namespace OpenXcom {

enum UnitStatus {STATUS_STANDING, STATUS_WALKING, STATUS_FLYING, STATUS_TURNING, STATUS_AIMING, STATUS_COLLAPSING, STATUS_DEAD, STATUS_UNCONSCIOUS, STATUS_PANICKING, STATUS_BERSERK, STATUS_IGNORE_ME};
enum UnitFaction : int { FACTION_PLAYER, FACTION_HOSTILE, FACTION_NEUTRAL };
enum UnitSide : uint8_t { SIDE_FRONT, SIDE_LEFT, SIDE_RIGHT, SIDE_REAR, SIDE_UNDER, SIDE_LEFT_FRONT, SIDE_LEFT_REAR, SIDE_RIGHT_FRONT, SIDE_RIGHT_REAR, SIDE_MAX };
enum UnitBodyPart : int { BODYPART_HEAD, BODYPART_TORSO, BODYPART_RIGHTARM, BODYPART_LEFTARM, BODYPART_RIGHTLEG, BODYPART_LEFTLEG, BODYPART_MAX };
enum BattleType { BT_NONE, BT_FIREARM, BT_AMMO, BT_MELEE, BT_GRENADE, BT_PROXIMITYGRENADE, BT_MEDIKIT, BT_SCANNER, BT_MINDPROBE, BT_PSIAMP, BT_FLARE, BT_CORPSE };

struct RuleDamageType { int ResistType = 0; };
struct RuleItem
{
    std::string name; int battleType = BT_NONE; RuleDamageType damage, melee;
    int getBattleType() const { return battleType; }
    const RuleDamageType* getDamageType() const { return &damage; }
    const RuleDamageType* getMeleeType() const { return &melee; }
};
struct RuleResearch { std::string name; };
struct RuleSoldier { std::string type; };
struct Soldier { const RuleSoldier* rules = nullptr; const RuleSoldier* getRules() const { return rules; } };

class RuleCommendations
{
public:
    std::map<std::string, std::vector<int> > _criteria;
    std::vector<std::vector<std::pair<int, std::vector<std::string> > > > _killCriteria;
    std::vector<std::string> _missionMarkerNames, _missionTypeNames;
    std::vector<const RuleResearch*> _requires;
    std::vector<const RuleSoldier*> _units;
    const std::map<std::string, std::vector<int> > *getCriteria() const { return &_criteria; }
    const std::vector<std::vector<std::pair<int, std::vector<std::string> > > > *getKillCriteria() const { return &_killCriteria; }
    const std::vector<std::string>& getMissionMarkerNames() const { return _missionMarkerNames; }
    const std::vector<std::string>& getMissionTypeNames() const { return _missionTypeNames; }
    const std::vector<const RuleResearch*>& getRequires() const { return _requires; }
    // Collections::sortVectorHave over the sorted _units.
    bool isSupportedBy(const RuleSoldier* soldier) const
    {
        return _units.empty() || std::find(_units.begin(), _units.end(), soldier) != _units.end();
    }
};

class Mod
{
public:
    std::map<std::string, RuleCommendations*> _commendations;
    std::map<std::string, RuleItem*> _items;
    int _maxDarknessToSeeUnits = 9;
    const std::map<std::string, RuleCommendations *> &getCommendationsList() const { return _commendations; }
    RuleCommendations *getCommendation(const std::string &id, bool = false) const
    {
        auto found = _commendations.find(id); return found == _commendations.end() ? nullptr : found->second;
    }
    RuleItem *getItem(const std::string &id, bool = false) const
    {
        auto found = _items.find(id); return found == _items.end() ? nullptr : found->second;
    }
    int getMaxDarknessToSeeUnits() const { return _maxDarknessToSeeUnits; }
};

struct MissionStatistics
{
    int id = 0; std::string markerName; std::string region, country, type, ufo = "NO_UFO";
    bool success = false; int score = 0; int daylight = 0; bool valiantCrux = false; int lootValue = 0;
    // MISSION_STATISTICS_METHODS
};

struct BattleUnitKills
{
    std::string name;
    std::string type, rank, race, weapon, weaponAmmo;
    UnitFaction faction = FACTION_HOSTILE;
    UnitStatus status = STATUS_IGNORE_ME;
    int mission = 0, turn = 0, id = 0;
    UnitSide side = SIDE_FRONT;
    UnitBodyPart bodypart = BODYPART_HEAD;
    // BATTLE_UNIT_KILLS_METHODS
};

class SavedGame
{
public:
    std::vector<MissionStatistics*> _missionStatistics;
    std::vector<const RuleResearch*> _discovered;
    std::vector<MissionStatistics*> *getMissionStatistics() { return &_missionStatistics; }
    bool isResearched(const std::vector<const RuleResearch *> &research, bool considerDebugMode = true, bool skipDisabled = false) const
    {
        (void)considerDebugMode; (void)skipDisabled;
        for (const auto* res : research)
            if (std::find(_discovered.begin(), _discovered.end(), res) == _discovered.end()) return false;
        return true;
    }
};

class SoldierCommendations
{
public:
    std::string _type, _noun; int _decorationLevel = 0; bool _isNew = false; const RuleCommendations* _rule = nullptr;
    SoldierCommendations(const std::string& commendationName, const std::string& noun, const Mod* mod);
    const std::string& getType() const;
    const std::string& getNoun() const;
    int getDecorationLevelInt() const;
    void addDecoration();
};

// SOLDIER_COMMENDATIONS_METHODS

class SoldierDiary
{
public:
    std::vector<SoldierCommendations*> _commendations;
    std::vector<BattleUnitKills*> _killList;
    std::vector<int> _missionIdList;
    int _daysWoundedTotal = 0, _totalShotByFriendlyCounter = 0, _totalShotFriendlyCounter = 0, _loneSurvivorTotal = 0,
        _monthsService = 0, _unconciousTotal = 0, _shotAtCounterTotal = 0, _hitCounterTotal = 0, _ironManTotal = 0,
        _longDistanceHitCounterTotal = 0, _lowAccuracyHitCounterTotal = 0, _shotsFiredCounterTotal = 0, _shotsLandedCounterTotal = 0,
        _shotAtCounter10in1Mission = 0, _hitCounter5in1Mission = 0, _timesWoundedTotal = 0, _KIA = 0, _allAliensKilledTotal = 0,
        _allAliensStunnedTotal = 0, _ufosShotDownTotal = 0, _ufosDamageTotal = 0, _woundsHealedTotal = 0, _allUFOs = 0,
        _allMissionTypes = 0, _statGainTotal = 0, _revivedUnitTotal = 0, _wholeMedikitTotal = 0, _braveryGainTotal = 0,
        _bestOfRank = 0, _MIA = 0, _martyrKillsTotal = 0, _postMortemKills = 0, _slaveKillsTotal = 0, _revivedSoldierTotal = 0,
        _revivedHostileTotal = 0, _revivedNeutralTotal = 0;
    bool _bestSoldier = false, _globeTrotter = false;

    bool manageCommendations(const Mod* mod, SavedGame* save, const Soldier* soldier);
    std::map<std::string, int> getAlienRankTotal() const;
    std::map<std::string, int> getAlienRaceTotal() const;
    std::map<std::string, int> getWeaponTotal() const;
    std::map<std::string, int> getRegionTotal(std::vector<MissionStatistics*> *missionStatistics) const;
    int getKillTotal() const;
    int getMissionTotalFiltered(std::vector<MissionStatistics*>* missionStatistics, const RuleCommendations* rule) const;
    int getWinTotal(std::vector<MissionStatistics*> *missionStatistics) const;
    int getStunTotal() const;
    void addMonthlyService();
    int getTrapKillTotal(const Mod* mod) const;
    int getReactionFireKillTotal(const Mod* mod) const;
    int getTerrorMissionTotal(std::vector<MissionStatistics*> *missionStatistics) const;
    int getNightMissionTotal(std::vector<MissionStatistics*> *missionStatistics, const Mod* mod) const;
    int getNightTerrorMissionTotal(std::vector<MissionStatistics*> *missionStatistics, const Mod* mod) const;
    int getBaseDefenseMissionTotal(std::vector<MissionStatistics*> *missionStatistics) const;
    int getAlienBaseAssaultTotal(std::vector<MissionStatistics*> *missionStatistics) const;
    int getImportantMissionTotal(std::vector<MissionStatistics*> *missionStatistics) const;
    int getScoreTotal(std::vector<MissionStatistics*> *missionStatistics) const;
    int getValiantCruxTotal(std::vector<MissionStatistics*> *missionStatistics) const;

    int* counter(const std::string& key)
    {
        static const std::map<std::string, int SoldierDiary::*> fields = {
            {"daysWoundedTotal", &SoldierDiary::_daysWoundedTotal}, {"totalShotByFriendlyCounter", &SoldierDiary::_totalShotByFriendlyCounter},
            {"totalShotFriendlyCounter", &SoldierDiary::_totalShotFriendlyCounter}, {"loneSurvivorTotal", &SoldierDiary::_loneSurvivorTotal},
            {"monthsService", &SoldierDiary::_monthsService}, {"unconciousTotal", &SoldierDiary::_unconciousTotal},
            {"shotAtCounterTotal", &SoldierDiary::_shotAtCounterTotal}, {"hitCounterTotal", &SoldierDiary::_hitCounterTotal},
            {"ironManTotal", &SoldierDiary::_ironManTotal}, {"longDistanceHitCounterTotal", &SoldierDiary::_longDistanceHitCounterTotal},
            {"lowAccuracyHitCounterTotal", &SoldierDiary::_lowAccuracyHitCounterTotal}, {"shotsFiredCounterTotal", &SoldierDiary::_shotsFiredCounterTotal},
            {"shotsLandedCounterTotal", &SoldierDiary::_shotsLandedCounterTotal}, {"shotAtCounter10in1Mission", &SoldierDiary::_shotAtCounter10in1Mission},
            {"hitCounter5in1Mission", &SoldierDiary::_hitCounter5in1Mission}, {"timesWoundedTotal", &SoldierDiary::_timesWoundedTotal},
            {"killedInAction", &SoldierDiary::_KIA}, {"allAliensKilledTotal", &SoldierDiary::_allAliensKilledTotal},
            {"allAliensStunnedTotal", &SoldierDiary::_allAliensStunnedTotal}, {"woundsHealedTotal", &SoldierDiary::_woundsHealedTotal},
            {"allUFOs", &SoldierDiary::_allUFOs}, {"allMissionTypes", &SoldierDiary::_allMissionTypes},
            {"statGainTotal", &SoldierDiary::_statGainTotal}, {"revivedUnitTotal", &SoldierDiary::_revivedUnitTotal},
            {"revivedSoldierTotal", &SoldierDiary::_revivedSoldierTotal}, {"revivedHostileTotal", &SoldierDiary::_revivedHostileTotal},
            {"revivedNeutralTotal", &SoldierDiary::_revivedNeutralTotal}, {"wholeMedikitTotal", &SoldierDiary::_wholeMedikitTotal},
            {"braveryGainTotal", &SoldierDiary::_braveryGainTotal}, {"bestOfRank", &SoldierDiary::_bestOfRank},
            {"missingInAction", &SoldierDiary::_MIA}, {"martyrKillsTotal", &SoldierDiary::_martyrKillsTotal},
            {"postMortemKills", &SoldierDiary::_postMortemKills}, {"slaveKillsTotal", &SoldierDiary::_slaveKillsTotal},
            {"ufosShotDownTotal", &SoldierDiary::_ufosShotDownTotal}, {"ufosDamageTotal", &SoldierDiary::_ufosDamageTotal},
        };
        auto found = fields.find(key);
        return found == fields.end() ? nullptr : &(this->*(found->second));
    }
};

// SOLDIER_DIARY_METHODS

// ---- scenarios ----------------------------------------------------------------------------------------

struct CommendationInput
{
    std::string type;
    std::vector<std::pair<std::string, std::vector<int>>> criteria;
    std::vector<std::vector<std::pair<int, std::vector<std::string>>>> killCriteria;
    std::vector<std::string> missionTypes, missionMarkers, research, units;
};
struct ItemInput { std::string name; int battleType, damage, melee; };
struct KillInput { std::string rank, race, weapon, ammo; int status, faction, mission, turn, side, bodypart; };
struct StatisticInput { int id; std::string marker, region, country, type, ufo; bool success; int score, daylight; bool valiantCrux; };
struct OwnedInput { std::string type, noun; int level; };
struct Scenario
{
    std::string name, soldierType;
    std::vector<CommendationInput> commendations;
    std::vector<ItemInput> items;
    std::vector<std::string> researched;
    std::vector<std::pair<std::string, int>> counters;
    std::vector<KillInput> kills;
    std::vector<int> missions;
    std::vector<StatisticInput> statistics;
    std::vector<OwnedInput> owned;
    int maxDarkness = 9;
};

std::string quote(const std::string& value) { return "\"" + value + "\""; }
template <typename T, typename F> std::string list(const std::vector<T>& values, F format)
{
    std::string out = "[";
    for (size_t i = 0; i < values.size(); ++i) out += (i ? "," : "") + format(values[i]);
    return out + "]";
}
std::string strings(const std::vector<std::string>& values) { return list(values, quote); }
std::string ints(const std::vector<int>& values) { return list(values, [](int v) { return std::to_string(v); }); }

std::string run(const Scenario& s)
{
    Mod mod; SavedGame save; SoldierDiary diary;
    mod._maxDarknessToSeeUnits = s.maxDarkness;
    std::map<std::string, RuleResearch*> research; std::map<std::string, RuleSoldier*> soldiers;
    auto researchRule = [&](const std::string& name) { auto*& r = research[name]; if (!r) r = new RuleResearch{name}; return r; };
    auto soldierRule = [&](const std::string& name) { auto*& r = soldiers[name]; if (!r) r = new RuleSoldier{name}; return r; };
    for (const auto& c : s.commendations)
    {
        auto* rule = new RuleCommendations();
        for (const auto& [name, thresholds] : c.criteria) rule->_criteria[name] = thresholds;
        rule->_killCriteria = c.killCriteria;
        rule->_missionTypeNames = c.missionTypes; rule->_missionMarkerNames = c.missionMarkers;
        for (const auto& r : c.research) rule->_requires.push_back(researchRule(r));
        for (const auto& u : c.units) rule->_units.push_back(soldierRule(u));
        mod._commendations[c.type] = rule;
    }
    for (const auto& i : s.items)
    {
        auto* item = new RuleItem(); item->name = i.name; item->battleType = i.battleType;
        item->damage.ResistType = i.damage; item->melee.ResistType = i.melee; mod._items[i.name] = item;
    }
    for (const auto& r : s.researched) save._discovered.push_back(researchRule(r));
    for (const auto& [key, value] : s.counters)
    {
        if (key == "bestSoldier") diary._bestSoldier = value != 0;
        else if (key == "globeTrotter") diary._globeTrotter = value != 0;
        else *diary.counter(key) = value;
    }
    for (const auto& k : s.kills)
    {
        auto* kill = new BattleUnitKills();
        kill->rank = k.rank; kill->race = k.race; kill->weapon = k.weapon; kill->weaponAmmo = k.ammo;
        kill->status = (UnitStatus)k.status; kill->faction = (UnitFaction)k.faction; kill->mission = k.mission; kill->turn = k.turn;
        kill->side = (UnitSide)k.side; kill->bodypart = (UnitBodyPart)k.bodypart;
        diary._killList.push_back(kill);
    }
    diary._missionIdList = s.missions;
    for (const auto& m : s.statistics)
    {
        auto* statistic = new MissionStatistics();
        statistic->id = m.id; statistic->markerName = m.marker; statistic->region = m.region; statistic->country = m.country;
        statistic->type = m.type; statistic->ufo = m.ufo; statistic->success = m.success; statistic->score = m.score;
        statistic->daylight = m.daylight; statistic->valiantCrux = m.valiantCrux;
        save._missionStatistics.push_back(statistic);
    }
    for (const auto& o : s.owned)
    {
        auto* owned = new SoldierCommendations(o.type, o.noun, &mod);
        owned->_decorationLevel = o.level; owned->_isNew = false;
        diary._commendations.push_back(owned);
    }
    Soldier soldier; soldier.rules = soldierRule(s.soldierType);

    diary.addMonthlyService();
    bool awarded = diary.manageCommendations(&mod, &save, &soldier);

    std::ostringstream out;
    out << "{\"name\":" << quote(s.name) << ",\"input\":{\"soldierType\":" << quote(s.soldierType)
        << ",\"maxDarkness\":" << s.maxDarkness << ",\"commendations\":" << list(s.commendations, [](const CommendationInput& c) {
            return "{\"type\":" + quote(c.type) + ",\"criteria\":" + list(c.criteria, [](const auto& p) { return "[" + quote(p.first) + "," + ints(p.second) + "]"; })
                + ",\"killCriteria\":" + list(c.killCriteria, [](const auto& orGroup) { return list(orGroup, [](const auto& andBlock) {
                    return "[" + std::to_string(andBlock.first) + "," + strings(andBlock.second) + "]"; }); })
                + ",\"missionTypes\":" + strings(c.missionTypes) + ",\"missionMarkers\":" + strings(c.missionMarkers)
                + ",\"requires\":" + strings(c.research) + ",\"units\":" + strings(c.units) + "}"; })
        << ",\"items\":" << list(s.items, [](const ItemInput& i) { return "[" + quote(i.name) + "," + std::to_string(i.battleType) + ","
            + std::to_string(i.damage) + "," + std::to_string(i.melee) + "]"; })
        << ",\"researched\":" << strings(s.researched)
        << ",\"counters\":" << list(s.counters, [](const auto& p) { return "[" + quote(p.first) + "," + std::to_string(p.second) + "]"; })
        << ",\"kills\":" << list(s.kills, [](const KillInput& k) { return "[" + quote(k.rank) + "," + quote(k.race) + "," + quote(k.weapon) + ","
            + quote(k.ammo) + "," + ints({k.status, k.faction, k.mission, k.turn, k.side, k.bodypart}).substr(1); })
        << ",\"missions\":" << ints(s.missions)
        << ",\"statistics\":" << list(s.statistics, [](const StatisticInput& m) { return "[" + std::to_string(m.id) + "," + quote(m.marker) + ","
            + quote(m.region) + "," + quote(m.country) + "," + quote(m.type) + "," + quote(m.ufo) + "," + (m.success ? "true" : "false") + ","
            + std::to_string(m.score) + "," + std::to_string(m.daylight) + "," + (m.valiantCrux ? "true" : "false") + "]"; })
        << ",\"owned\":" << list(s.owned, [](const OwnedInput& o) { return "[" + quote(o.type) + "," + quote(o.noun) + "," + std::to_string(o.level) + "]"; })
        << "},\"awarded\":" << (awarded ? "true" : "false") << ",\"monthsService\":" << diary._monthsService
        << ",\"commendations\":" << list(diary._commendations, [](const SoldierCommendations* c) {
            return "[" + quote(c->getType()) + "," + quote(c->getNoun()) + "," + std::to_string(c->getDecorationLevelInt()) + "]"; })
        << "}";
    return out.str();
}

}

int main()
{
    using namespace OpenXcom;
    // Status 6 dead, 7 unconscious; faction 1 hostile; a turn 3n+1 is a hostile turn.
    const KillInput sectoidKill{"STR_LIVE_SOLDIER", "STR_SECTOID", "STR_RIFLE", "STR_RIFLE_CLIP", 6, 1, 1, 3, 0, 1};
    const KillInput floaterKill{"STR_LIVE_LEADER", "STR_FLOATER", "STR_RIFLE", "STR_RIFLE_CLIP", 6, 1, 1, 6, 3, 0};
    const KillInput grenadeKill{"STR_LIVE_SOLDIER", "STR_SECTOID", "STR_GRENADE", "", 6, 1, 2, 4, 0, 1};
    const KillInput stunKill{"STR_LIVE_SOLDIER", "STR_SECTOID", "STR_STUN_ROD", "", 7, 1, 2, 9, 0, 1};
    const KillInput gunButtKill{"STR_LIVE_SOLDIER", "STR_FLOATER", "STR_RIFLE", "__GUNBUTT", 6, 1, 3, 7, 4, 2};
    const KillInput civilianKill{"STR_CIVILIAN", "STR_HUMAN", "STR_RIFLE", "STR_RIFLE_CLIP", 6, 2, 3, 9, 0, 1};
    const std::vector<ItemInput> items = {
        {"STR_RIFLE", 1, 1, 7}, {"STR_RIFLE_CLIP", 2, 4, 7}, {"STR_GRENADE", 4, 3, 7}, {"STR_STUN_ROD", 3, 6, 6},
    };
    const std::vector<StatisticInput> statistics = {
        {1, "", "STR_EUROPE", "STR_FRANCE", "STR_UFO_CRASH_RECOVERY", "STR_SMALL_SCOUT", true, 150, 0, false},
        {2, "STR_TERROR_SITE", "STR_EUROPE", "STR_GERMANY", "STR_TERROR_MISSION", "NO_UFO", true, 300, 12, true},
        {3, "", "STR_ASIA", "STR_JAPAN", "STR_BASE_DEFENSE", "NO_UFO", true, 500, 15, false},
        {4, "", "STR_ASIA", "STR_CHINA", "STR_ALIEN_BASE_ASSAULT", "NO_UFO", false, -200, 0, false},
        {5, "STR_TERROR_SITE", "STR_EUROPE", "STR_ITALY", "STR_TERROR_MISSION", "NO_UFO", true, 250, 3, false},
    };
    const std::vector<Scenario> scenarios = {
        // addMonthlyService first: 2 -> 3 months reaches two levels, awarded in one call.
        {"service-reaches-two-levels", "STR_SOLDIER",
            {{"STR_MEDAL_SERVICE", {{"totalMonthlyService", {1, 3, 6}}}, {}, {}, {}, {}, {}}},
            {}, {}, {{"monthsService", 2}}, {}, {}, {}, {}},
        {"maximum-level-reached", "STR_SOLDIER",
            {{"STR_MEDAL_SERVICE", {{"totalMonthlyService", {1, 3}}}, {}, {}, {}, {}, {}}},
            {}, {}, {{"monthsService", 10}}, {}, {}, {}, {{"STR_MEDAL_SERVICE", "noNoun", 1}}},
        // The units filter and research gates, then std::map order: a failing criterion stops the rest.
        {"units-research-and-map-order", "STR_SOLDIER",
            {{"STR_A_ROBOT_ONLY", {{"totalMonthlyService", {1}}}, {}, {}, {}, {}, {"STR_ROBOT"}},
             {"STR_B_NEEDS_RESEARCH", {{"totalMonthlyService", {1}}}, {}, {}, {}, {"STR_ALIEN_ORIGINS"}, {}},
             {"STR_C_RESEARCHED", {{"totalMonthlyService", {1}}}, {}, {}, {}, {"STR_LASERS"}, {"STR_SOLDIER", "STR_ROBOT"}},
             {"STR_D_KILLS_FIRST", {{"totalKills", {5}}, {"totalMonthlyService", {1}}}, {}, {}, {}, {}, {}},
             {"STR_E_UNKNOWN_CRITERION", {{"noSuchCriterion", {100, 200}}}, {}, {}, {}, {}, {}}},
            {}, {"STR_LASERS"}, {}, {}, {}, {}, {}},
        // Kills and stuns count hostile victims by status; trap kills are hostile-turn grenades or unknown weapons.
        {"kill-totals", "STR_SOLDIER",
            {{"STR_KILLS", {{"totalKills", {2, 3, 4}}}, {}, {}, {}, {}, {}},
             {"STR_STUNS", {{"totalStuns", {1, 2}}}, {}, {}, {}, {}, {}},
             {"STR_TRAPS", {{"totalTrapKills", {1, 2}}}, {}, {}, {}, {}, {}},
             {"STR_REACTION", {{"totalReactionFire", {1, 2}}}, {}, {}, {}, {}, {}}},
            items, {}, {}, {sectoidKill, floaterKill, grenadeKill, stunKill, gunButtKill, civilianKill}, {}, {}, {}},
        // Statistics loops count a repeated mission ID twice; std::find in the filtered total counts it once.
        {"mission-totals", "STR_SOLDIER",
            {{"STR_MISSIONS", {{"totalMissions", {3, 5, 6}}}, {}, {}, {}, {}, {}},
             {"STR_WINS", {{"totalWins", {4, 5}}}, {}, {}, {}, {}, {}},
             {"STR_SCORE", {{"totalScore", {1000, 1500}}}, {}, {}, {}, {}, {}},
             {"STR_TERROR", {{"totalTerrorMissions", {2, 3}}}, {}, {}, {}, {}, {}},
             {"STR_NIGHT", {{"totalNightMissions", {1, 3}}}, {}, {}, {}, {}, {}},
             {"STR_NIGHT_TERROR", {{"totalNightTerrorMissions", {1, 2}}}, {}, {}, {}, {}, {}},
             {"STR_DEFENSE", {{"totalBaseDefenseMissions", {1}}}, {}, {}, {}, {}, {}},
             {"STR_ASSAULT", {{"totalAlienBaseAssaults", {1}}}, {}, {}, {}, {}, {}},
             {"STR_IMPORTANT", {{"totalImportantMissions", {2, 4, 5}}}, {}, {}, {}, {}, {}},
             {"STR_CRUX", {{"totalValientCrux", {1, 3}}}, {}, {}, {}, {}, {}},
             {"STR_TERROR_TYPE", {{"totalMissions", {1, 2, 3}}}, {}, {"STR_TERROR_MISSION"}, {"STR_TERROR_SITE"}, {}, {}},
             {"STR_TERROR_MARKER", {{"totalMissions", {1, 2, 3}}}, {}, {}, {"STR_TERROR_SITE"}, {}, {}}},
            {}, {}, {}, {}, {1, 2, 2, 3, 4, 5}, statistics, {}, 9},
        // Noun criteria: std::map nouns; an owned noun at its last level awards nothing, another noun starts at 0.
        {"noun-criteria", "STR_SOLDIER",
            {{"STR_RACE", {{"totalKillsByRace", {1, 2}}}, {}, {}, {}, {}, {}},
             {"STR_RANK", {{"totalKillsByRank", {1, 3}}}, {}, {}, {}, {}, {}},
             {"STR_WEAPON", {{"totalKillsWithAWeapon", {2, 4}}}, {}, {}, {}, {}, {}},
             {"STR_REGION", {{"totalMissionsInARegion", {2, 3}}}, {}, {}, {}, {}, {}},
             {"STR_TWO_NOUNS", {{"totalKillsByRace", {1}}, {"totalKillsByRank", {1}}}, {}, {}, {}, {}, {}}},
            items, {}, {}, {sectoidKill, floaterKill, grenadeKill, stunKill, gunButtKill, civilianKill}, {1, 2, 5}, statistics,
            {{"STR_RACE", "STR_FLOATER", 1}, {"STR_RANK", "STR_LIVE_SOLDIER", 0}}},
        // Kill criteria: details match rank, race, weapon, status, faction, side, body part, battle type and damage type.
        {"kill-criteria", "STR_SOLDIER",
            {{"STR_CAREER", {{"killsWithCriteriaCareer", {1, 2, 3}}},
                {{{1, {"STR_SECTOID", "FACTION_HOSTILE"}}, {1, {"BT_FIREARM", "DT_LASER"}}}}, {}, {}, {}, {}},
             {"STR_MISSION", {{"killsWithCriteriaMission", {1, 2, 3}}},
                {{{2, {"STATUS_DEAD"}}}, {{1, {"BT_GRENADE"}}}}, {}, {}, {}, {}},
             {"STR_TURN", {{"killsWithCriteriaTurn", {1, 2}}},
                {{{1, {"DT_MELEE", "SIDE_UNDER"}}}, {{1, {"BODYPART_TORSO", "STR_LIVE_SOLDIER"}}}}, {}, {}, {}, {}},
             {"STR_EMPTY_BLOCK", {{"killsWithCriteriaCareer", {6, 7}}}, {{{1, {}}}}, {}, {}, {}, {}}},
            items, {}, {}, {sectoidKill, floaterKill, grenadeKill, stunKill, gunButtKill, civilianKill}, {}, {}, {}},
        // Friendly fire fails for the dead or missing; counters and flags; interception totals.
        {"counters-and-flags", "STR_SOLDIER",
            {{"STR_FRIENDLY", {{"totalFriendlyFired", {1}}}, {}, {}, {}, {}, {}},
             {"STR_MIA", {{"isMIA", {1}}}, {}, {}, {}, {}, {}},
             {"STR_BEST", {{"bestSoldier", {1}}}, {}, {}, {}, {}, {}},
             {"STR_GLOBE", {{"globeTrotter", {1, 2}}}, {}, {}, {}, {}, {}},
             {"STR_PILOT", {{"totalUfosShotDown", {1, 2}}, {"totalUfosDamage", {100, 500}}}, {}, {}, {}, {}, {}},
             {"STR_WOUNDS", {{"totalDaysWounded", {10, 20}}, {"totalTimesWounded", {2}}}, {}, {}, {}, {}, {}}},
            {}, {}, {{"totalShotByFriendlyCounter", 3}, {"missingInAction", 1}, {"bestSoldier", 1}, {"globeTrotter", 1},
                {"ufosShotDownTotal", 2}, {"ufosDamageTotal", 300}, {"daysWoundedTotal", 25}, {"timesWoundedTotal", 1}}, {}, {}, {}, {}},
        {"friendly-fire-alive", "STR_SOLDIER",
            {{"STR_FRIENDLY", {{"totalFriendlyFired", {1, 2, 3}}}, {}, {}, {}, {}, {}}},
            {}, {}, {{"totalShotByFriendlyCounter", 2}}, {}, {}, {}, {}},
    };
    std::printf("{\"scenarios\":[");
    for (size_t i = 0; i < scenarios.size(); ++i) std::printf("%s%s", i ? "," : "", run(scenarios[i]).c_str());
    std::printf("]}\n");
    return 0;
}

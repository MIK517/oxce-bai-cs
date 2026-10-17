// Event payloads: GeoscapeEventState::eventLogic and btnOkClick, SavedGame::canSpawnInstantEvent and
// selectSoldierNationalityByLocation and WeightedOptions::choose are inserted verbatim by the capture script.
// Soldier generation, templates, crafts, research completion and its side effects are recorded calls (their
// behaviour is pinned by the logistics, readiness and research probes); widgets and states are stubs. RNG
// returns scripted raw values through the RandomState::generate formula.
#include <algorithm>
#include <cstdint>
#include <cstdio>
#include <map>
#include <set>
#include <stdexcept>
#include <sstream>
#include <string>
#include <vector>

namespace OpenXcom {

std::ostringstream trace;
void record(const std::string& entry) { trace << (trace.tellp() > 0 ? "," : "") << '"' << entry << '"'; }

namespace RNG {
std::vector<uint64_t> script; size_t next = 0; std::ostringstream calls; int count = 0;
int generate(int min, int max)
{
    uint64_t raw = next < script.size() ? script[next] : 0; ++next; ++count;
    int result = (int)(raw % (max - min + 1) + min);
    calls << (count > 1 ? "," : "") << '[' << min << ',' << max << ',' << result << ']';
    return result;
}
bool percent(int value) { return generate(0, 99) < value; }
}

namespace Options { bool oxceGeoscapeEventsInstantDelivery = true, storageLimitsEnforced = false; int oxceGeoscapeDebugLogMaxEntries = 0; }
namespace YAML { struct YamlRootNodeReader { std::string text; YamlRootNodeReader(const std::string& t, const char*) : text(t) {} }; }

struct LocalizedText
{
    std::string text;
    LocalizedText(const std::string& t = "") : text(t) {}
    LocalizedText arg(const std::string& value) const { return LocalizedText(text + "{" + value + "}"); }
    operator std::string() const { return text; }
    const char* c_str() const { return text.c_str(); }
};

struct Text { std::string text; void setText(const std::string& t) { text = t; } };
struct TextList
{
    std::vector<std::pair<std::string, std::string>> rows;
    void addRow(int, const char* name, const char* quantity) { rows.emplace_back(name, quantity); }
};

enum GameEnding { END_NONE, END_WIN, END_LOSE };
enum class DiscoverySourceType { NONE, EVENT, FREE_FROM };

class WeightedOptions
{
public:
    std::map<std::string, size_t> _choices; size_t _totalWeight = 0;
    bool empty() const { return _totalWeight == 0; }
    std::string choose() const;
};

struct Target { std::string name; std::string country, region; };
struct City : Target { std::string getName(const std::string&) const { return name; } };
struct RuleRegion
{
    std::string type; std::vector<City*> cities;
    const std::string& getType() const { return type; }
    std::vector<City*>* getCities() { return &cities; }
};
struct RuleCountry { std::string type; const std::string& getType() const { return type; } };
struct SoldierNamePool
{
    std::string country, region; int weight = 1;
    const std::string& getCountry() const { return country; } const std::string& getRegion() const { return region; }
    int getGlobalWeight() const { return weight; }
};
struct RuleSoldier { std::string type; std::vector<SoldierNamePool*> names; const std::vector<SoldierNamePool*>& getNames() const { return names; } };
struct RuleItem { std::string type; const std::string& getType() const { return type; } };
struct RuleCraft { std::string type; const std::string& getType() const { return type; } };
struct RuleResearch
{
    std::string name; const RuleResearch* lookup = nullptr;
    const std::string& getName() const { return name; } const RuleResearch* getLookup() const { return lookup; }
};
struct RuleVideo { bool win = false, lose = false; bool getWinGame() const { return win; } bool getLoseGame() const { return lose; } };
struct RuleInterfaceElement { int color = 0, color2 = 0; };
struct RuleInterface { RuleInterfaceElement element; RuleInterfaceElement* getElement(const std::string&) { return &element; } };

struct RuleEvent
{
    std::string _name, _description, _cutscene; std::vector<std::string> _regionList; bool _city = false;
    int _points = 0, _funds = 0; std::string _spawnedCraftType; int _spawnedPersons = 0;
    std::string _spawnedPersonType, _spawnedPersonName, _spawnedSoldier = "template";
    std::map<std::string, int> _everyMultiItemList; std::vector<std::string> _everyItemList, _randomItemList;
    std::vector<std::map<std::string, int>> _randomMultiItemList; WeightedOptions _weightedItemList;
    std::vector<const RuleResearch*> _research; std::vector<std::string> _adhocMissionScriptTags; std::string _interruptResearch;
    bool _invert = false; std::map<std::string, int> _everyMultiSoldierList; std::vector<std::map<std::string, int>> _randomMultiSoldierList;
    const std::string& getName() const { return _name; } const std::string& getDescription() const { return _description; }
    const std::string& getCutscene() const { return _cutscene; } const std::vector<std::string>& getRegionList() const { return _regionList; }
    bool isCitySpecific() const { return _city; } int getPoints() const { return _points; } int getFunds() const { return _funds; }
    const std::string& getSpawnedCraftType() const { return _spawnedCraftType; } int getSpawnedPersons() const { return _spawnedPersons; }
    const std::string& getSpawnedPersonType() const { return _spawnedPersonType; }
    const std::string& getSpawnedPersonName() const { return _spawnedPersonName; }
    const std::string& getSpawnedSoldierTemplate() const { return _spawnedSoldier; }
    const std::map<std::string, int>& getEveryMultiItemList() const { return _everyMultiItemList; }
    const std::vector<std::string>& getEveryItemList() const { return _everyItemList; }
    const std::vector<std::string>& getRandomItemList() const { return _randomItemList; }
    const std::vector<std::map<std::string, int>>& getRandomMultiItemList() const { return _randomMultiItemList; }
    const WeightedOptions& getWeightedItemList() const { return _weightedItemList; }
    const std::vector<const RuleResearch*>& getResearchList() const { return _research; }
    const std::vector<std::string>& getAdhocMissionScriptTags() const { return _adhocMissionScriptTags; }
    const std::string& getInterruptResearch() const { return _interruptResearch; }
    bool getInvert() const { return _invert; }
    const std::map<std::string, int>& getEveryMultiSoldierList() const { return _everyMultiSoldierList; }
    const std::vector<std::map<std::string, int>>& getRandomMultiSoldierList() const { return _randomMultiSoldierList; }
};

class SavedGame;
class Mod;
struct Soldier
{
    std::string type, name; int nationality = -1, id = 0;
    void load(const YAML::YamlRootNodeReader& reader, const Mod*, SavedGame*, const void*, bool soldierTemplate)
    { record("soldier " + std::to_string(id) + " load " + reader.text + (soldierTemplate ? " as template" : "")); }
    void setName(const std::string& n) { name = n; record("soldier " + std::to_string(id) + " name " + n); }
    void genName() { name = "generated"; record("soldier " + std::to_string(id) + " genName"); }
};
struct ItemContainer
{
    std::map<std::string, int> items;
    int getItem(const RuleItem* r) const { auto i = items.find(r->type); return i == items.end() ? 0 : i->second; }
    void removeItem(const RuleItem* r, int q) { items[r->type] -= q; if (items[r->type] <= 0) items.erase(r->type); }
    void addItem(const RuleItem* r, int q) { items[r->type] += q; }
};
struct Base;
struct Craft
{
    std::string type, status = "STR_READY"; int id = 0; ItemContainer items;
    Craft(const RuleCraft* rule, Base*, int i) : type(rule->getType()), id(i) {}
    void initFixedWeapons(const Mod*) { record("craft " + type + "-" + std::to_string(id) + " initFixedWeapons"); }
    void checkup() { record("craft " + type + "-" + std::to_string(id) + " checkup"); }
    void setStatus(const std::string& s) { status = s; }
    const std::string& getStatus() const { return status; }
    ItemContainer* getItems() { return &items; }
};
struct Transfer
{
    int hours, scientists = 0, engineers = 0, quantity = 0; Soldier* soldier = nullptr; const RuleItem* item = nullptr; Craft* craft = nullptr;
    explicit Transfer(int h) : hours(h) {}
    void setScientists(int n) { scientists = n; } void setEngineers(int n) { engineers = n; }
    void setSoldier(Soldier* s) { soldier = s; } void setItems(const RuleItem* r, int q = 1) { item = r; quantity = q; }
    void setCraft(Craft* c) { craft = c; }
};
struct Base
{
    std::string name; ItemContainer storage; std::vector<Craft*> crafts; std::vector<Transfer*> transfers; bool overfull = false;
    ItemContainer* getStorageItems() { return &storage; } std::vector<Craft*>* getCrafts() { return &crafts; }
    std::vector<Transfer*>* getTransfers() { return &transfers; } bool storesOverfull() const { return overfull; }
    const std::string& getName() const { return name; }
};
struct Region
{
    RuleRegion* rules; int xcom = 0;
    RuleRegion* getRules() const { return rules; } void addActivityXcom(int a) { xcom += a; }
};
struct Country { RuleCountry* rules; RuleCountry* getRules() const { return rules; } };
struct ResearchDiaryEntry
{
    const RuleResearch* research; std::string date;
    struct { DiscoverySourceType type = DiscoverySourceType::NONE; const RuleEvent* event = nullptr; const RuleResearch* research = nullptr; std::string name; } source;
    explicit ResearchDiaryEntry(const RuleResearch* r) : research(r) {}
    void setDate(const struct GameTime*) {}
};
struct GameTime { std::string getFullString() const { return "time"; } };

class Mod
{
public:
    std::map<std::string, RuleRegion*> regions; std::map<std::string, RuleSoldier*> soldiers; std::map<std::string, RuleItem*> items;
    std::map<std::string, RuleCraft*> crafts; std::map<std::string, RuleVideo> videos; RuleInterface interface_;
    int hireByCountryOdds = 0, hireByRegionOdds = 0;
    mutable int soldierIds = 0;
    // Mod::getRule: an empty name or STR_NULL names no rule; another unknown name throws when error is set.
    static bool isEmptyRuleName(const std::string& s) { return s.empty() || s == "STR_NULL"; }
    template<class T> static T* getRule(const std::string& id, const std::string& name, const std::map<std::string, T*>& map, bool error)
    {
        if (isEmptyRuleName(id)) return nullptr;
        auto i = map.find(id);
        if (i != map.end()) return i->second;
        if (error) throw std::runtime_error(name + " " + id + " not found");
        return nullptr;
    }
    RuleRegion* getRegion(const std::string& id, bool error) const { return getRule(id, "Region", regions, error); }
    RuleSoldier* getSoldier(const std::string& id, bool error = false) const { return getRule(id, "Soldier", soldiers, error); }
    // Mod::getItem: Armor::NONE (STR_NONE) names no item either.
    RuleItem* getItem(const std::string& id, bool error = false) const { return id == "STR_NONE" ? nullptr : getRule(id, "Item", items, error); }
    RuleCraft* getCraft(const std::string& id, bool error = false) const { return getRule(id, "Craft", crafts, error); }
    const RuleVideo* getVideo(const std::string& id, bool) const { return &videos.at(id); }
    RuleInterface* getInterface(const std::string&) { return &interface_; }
    int getHireByCountryOdds() const { return hireByCountryOdds; } int getHireByRegionOdds() const { return hireByRegionOdds; }
    const void* getScriptGlobal() const { return nullptr; }
    Soldier* genSoldier(SavedGame*, const RuleSoldier* rule, int nationality) const
    {
        auto* s = new Soldier{rule->type, "", nationality, ++soldierIds};
        record("genSoldier " + rule->type + " " + std::to_string(nationality) + " -> " + std::to_string(s->id));
        return s;
    }
};

class SavedGame
{
public:
    std::vector<Base*> bases; std::vector<Region*> regions; std::vector<Country*> countries;
    int64_t funds = 0; int researchScore = 0, monthsPassed = 0, ending = END_NONE;
    std::set<std::string> researched, undiscoveredFree, disabled; std::map<std::string, const RuleResearch*> freeBonus;
    std::map<std::string, int> ids, _generatedEvents; std::vector<std::string> diary, debugLog; GameTime time;
    std::vector<Base*>* getBases() { return &bases; } std::vector<Region*>* getRegions() { return &regions; }
    void addResearchScore(int s) { researchScore += s; }
    int64_t getFunds() const { return funds; } void setFunds(int64_t f) { funds = f; }
    int getId(const std::string& name) { auto i = ids.find(name); if (i != ids.end()) return i->second++; ids[name] = 1; return ids[name]++; }
    bool isResearched(const RuleResearch* r, bool) const { return researched.count(r->name) != 0; }
    bool isResearched(const std::string& name, bool) const { return researched.count(name) != 0; }
    bool hasUndiscoveredGetOneFree(const RuleResearch* r, bool) const { return undiscoveredFree.count(r->name) != 0; }
    bool isResearchRuleStatusDisabled(const std::string& name) const { return disabled.count(name) != 0; }
    void addResearchDiaryEntry(ResearchDiaryEntry* e)
    {
        diary.push_back(e->research->name + (e->source.type == DiscoverySourceType::EVENT ? " event " : " free ") + e->source.name);
        delete e;
    }
    void addFinishedResearch(const RuleResearch* r, const Mod*, Base*, bool score)
    { researched.insert(r->name); record("addFinishedResearch " + r->name + (score ? " score" : "")); }
    const RuleResearch* selectGetOneFree(const RuleResearch* r)
    { auto i = freeBonus.find(r->name); return i == freeBonus.end() || researched.count(i->second->name) ? nullptr : i->second; }
    void handlePrimaryResearchSideEffects(const std::vector<const RuleResearch*>& topics, const Mod*, Base*)
    {
        std::string text = "sideEffects";
        for (auto* t : topics) text += " " + t->name;
        record(text);
    }
    GameTime* getTime() { return &time; } std::vector<std::string>& getGeoscapeDebugLog() { return debugLog; }
    int getMonthsPassed() const { return monthsPassed; } int getEnding() const { return ending; } void setEnding(GameEnding e) { ending = e; }
    void addGeneratedEvent(const RuleEvent* rule) { _generatedEvents[rule->getName()] += 1; }
    Country* locateCountry(const Target& target) const
    { for (auto* c : countries) if (c->rules->type == target.country) return c; return nullptr; }
    Region* locateRegion(const Target& target) const
    { for (auto* r : regions) if (r->rules->type == target.region) return r; return nullptr; }
    bool canSpawnInstantEvent(const RuleEvent* eventRules);
    int selectSoldierNationalityByLocation(const Mod* mod, const RuleSoldier* rule, const Target* target) const;
};

struct State { virtual ~State() {} virtual std::string name() const = 0; };
struct NamedState : State { std::string id; NamedState(std::string i) : id(std::move(i)) {} std::string name() const override { return id; } };
struct CutsceneState : NamedState { CutsceneState(const std::string& id) : NamedState("CutsceneState " + id) {} };
struct SellState : NamedState { SellState(Base* base, int) : NamedState("SellState " + base->name) {} };
struct ErrorMessageState : NamedState
{ ErrorMessageState(const LocalizedText& text, int, int, const std::string&, int) : NamedState("ErrorMessageState " + text.text) {} };
struct GeoscapeState
{ void determineAlienMissions(bool month, const RuleEvent* rule) { record(std::string("determineAlienMissions ") + (month ? "monthly " : "adhoc ") + rule->getName()); } };
struct Game
{
    SavedGame* save; Mod* mod; GeoscapeState geo;
    SavedGame* getSavedGame() { return save; } Mod* getMod() { return mod; } GeoscapeState* getGeoscapeState() { return &geo; }
    std::string getLanguage() { return "en"; }
    void popState() { record("popState"); }
    void pushState(State* state) { record("pushState " + state->name()); delete state; }
};
namespace Ufopaedia { void openArticle(Game*, const std::string& id) { record("openArticle " + id); } }

class GeoscapeEventState
{
public:
    Game* _game; const RuleEvent& _eventRule; int _palette = 0;
    Text *_txtTitle = new Text(), *_txtMessage = new Text(); TextList* _lstTransfers = new TextList();
    std::string _researchName, _bonusResearchName;
    GeoscapeEventState(Game* game, const RuleEvent& rule) : _game(game), _eventRule(rule)
    { _txtTitle->setText(rule.getName()); _txtMessage->setText(rule.getDescription()); }
    LocalizedText tr(const std::string& id) const { return LocalizedText(id); }
    void eventLogic();
    void btnOkClick(struct Action*);
};

// METHODS
}

using namespace OpenXcom;

struct Scenario
{
    const char* name; RuleEvent rule; bool instantDelivery, storageLimits, overfull; int monthsPassed, ending;
    int hireByCountryOdds, hireByRegionOdds; std::vector<std::string> researched, undiscoveredFree, disabled;
    std::vector<uint64_t> rng;
};

std::string quote(const std::string& s) { return '"' + s + '"'; }

int main()
{
    RuleItem rifle{"STR_RIFLE"}, clip{"STR_RIFLE_CLIP"}, grenade{"STR_GRENADE"}, medkit{"STR_MEDI_KIT"}, elerium{"STR_ELERIUM_115"};
    RuleCraft skyranger{"STR_SKYRANGER"};
    // std::map<const RuleSoldier*, int> orders soldiers by rule address; rules allocated in load order keep it.
    RuleSoldier soldierRules[2] = {{"STR_SOLDIER"}, {"STR_HYBRID"}};
    RuleSoldier& xcom = soldierRules[0]; RuleSoldier& hybrid = soldierRules[1];
    SoldierNamePool american{"STR_USA", "STR_NORTH_AMERICA", 3}, russian{"STR_RUSSIA", "STR_EUROPE", 2}, canadian{"STR_CANADA", "STR_NORTH_AMERICA", 1};
    xcom.names = {&russian, &american, &canadian}; hybrid.names = {&american};
    City washington; washington.name = "Washington"; washington.country = "STR_USA"; washington.region = "STR_NORTH_AMERICA";
    City ottawa; ottawa.name = "Ottawa"; ottawa.country = "STR_CANADA"; ottawa.region = "STR_NORTH_AMERICA";
    RuleRegion northAmerica{"STR_NORTH_AMERICA", {&washington, &ottawa}}, europe{"STR_EUROPE", {}};
    RuleCountry usa{"STR_USA"}, canada{"STR_CANADA"};
    RuleResearch plasmaLookup{"STR_PLASMA_LOOKUP"}, plasma{"STR_PLASMA", &plasmaLookup}, laser{"STR_LASER"}, alloys{"STR_ALLOYS"},
        bonusLookup{"STR_BONUS_LOOKUP"}, bonus{"STR_BONUS", &bonusLookup};

    auto event = [](const char* name) { RuleEvent e; e._name = name; e._description = std::string(name) + "_DESC"; return e; };
    std::vector<Scenario> scenarios;
    {
        auto e = event("STR_CITY_EVENT"); e._regionList = {"STR_EUROPE", "STR_NORTH_AMERICA"}; e._city = true; e._points = 120;
        e._funds = -25000; e._spawnedPersons = 3; e._spawnedPersonType = "STR_SCIENTIST";
        scenarios.push_back({"city-region-score-funds-scientists", e, true, false, false, 2, END_NONE, 0, 0, {}, {}, {}, {1, 1}});
    }
    {
        auto e = event("STR_REGION_EVENT"); e._regionList = {"STR_NORTH_AMERICA"}; e._points = -40;
        e._spawnedPersons = 2; e._spawnedPersonType = "STR_ENGINEER";
        scenarios.push_back({"region-not-city-specific-still-picks-a-city", e, true, false, false, 2, END_NONE, 0, 0, {}, {}, {}, {0, 1}});
    }
    {
        auto e = event("STR_NAMED_SOLDIERS"); e._points = 15; e._spawnedPersons = 2; e._spawnedPersonType = "STR_SOLDIER";
        e._spawnedPersonName = "STR_HERO"; e._spawnedSoldier = "rank: 3";
        scenarios.push_back({"no-region-research-score-named-soldiers", e, true, false, false, 2, END_NONE, 50, 50, {}, {}, {}, {}});
    }
    {
        auto e = event("STR_SOLDIER_LISTS"); e._regionList = {"STR_NORTH_AMERICA"}; e._city = true;
        e._everyMultiSoldierList = {{"STR_SOLDIER", 1}, {"STR_HYBRID", 1}, {"STR_NULL", 2}};
        e._randomMultiSoldierList = {{{"STR_SOLDIER", 1}}, {{"STR_HYBRID", 2}, {"STR_SOLDIER", 1}}};
        scenarios.push_back({"multi-soldier-lists-merge-by-rule-and-locate-nationality", e, true, false, false, 2, END_NONE, 50, 100, {}, {}, {},
            {0, 0, 1, 10, 99, 4, 30, 2, 0, 80, 5}});
    }
    {
        auto e = event("STR_ITEMS"); e._everyMultiItemList = {{"STR_RIFLE", 2}, {"STR_NONE", 5}, {"", 1}}; e._everyItemList = {"STR_RIFLE", "STR_GRENADE", "STR_RIFLE"};
        e._randomItemList = {"STR_MEDI_KIT", "STR_RIFLE_CLIP"}; e._randomMultiItemList = {{{"STR_RIFLE_CLIP", 4}}, {{"STR_GRENADE", 3}, {"STR_RIFLE", 1}}};
        e._weightedItemList._choices = {{"STR_ELERIUM_115", 3}, {"STR_GRENADE", 1}}; e._weightedItemList._totalWeight = 4;
        scenarios.push_back({"items-instant-delivery", e, true, false, false, 2, END_NONE, 0, 0, {}, {}, {}, {1, 1, 2}});
        scenarios.push_back({"items-by-transfer", e, false, false, false, 2, END_NONE, 0, 0, {}, {}, {}, {0, 0, 3}});
    }
    {
        // Mod::getItem(name, true) throws for an unknown item after the score and funds have changed; the
        // reference game stops there. An unknown spawnedPersonType is looked up without an error: nobody spawns.
        auto e = event("STR_BAD_ITEM"); e._points = 7; e._funds = 500; e._everyItemList = {"STR_RIFLE", "STR_BOGUS"};
        e._spawnedPersons = 2; e._spawnedPersonType = "STR_NOBODY";
        scenarios.push_back({"unknown-item-throws-after-score-and-funds", e, true, false, false, 2, END_NONE, 0, 0, {}, {}, {}, {}});
    }
    {
        auto e = event("STR_THEFT"); e._invert = true; e._everyMultiItemList = {{"STR_RIFLE", 7}, {"STR_ELERIUM_115", 50}, {"STR_GRENADE", 1}};
        scenarios.push_back({"invert-removes-from-stores-then-crafts-not-out", e, true, false, false, 2, END_NONE, 0, 0, {}, {}, {}, {}});
    }
    {
        auto e = event("STR_CRAFT_GIFT"); e._spawnedCraftType = "STR_SKYRANGER";
        scenarios.push_back({"craft-instant-delivery", e, true, false, false, 2, END_NONE, 0, 0, {}, {}, {}, {}});
        scenarios.push_back({"craft-by-transfer", e, false, false, false, 2, END_NONE, 0, 0, {}, {}, {}, {}});
    }
    {
        auto e = event("STR_RESEARCH_GIFT"); e._research = {&laser, &plasma, &alloys};
        scenarios.push_back({"research-skips-known-topics-and-adds-lookup-and-bonus", e, true, false, false, 2, END_NONE, 0, 0,
            {"STR_LASER"}, {}, {}, {0}});
        scenarios.push_back({"research-known-with-undiscovered-free-topic-shows-no-article", e, true, false, false, 2, END_NONE, 0, 0,
            {"STR_LASER", "STR_PLASMA", "STR_PLASMA_LOOKUP", "STR_ALLOYS"}, {"STR_PLASMA"}, {"STR_BONUS"}, {0}});
    }
    {
        auto e = event("STR_ADHOC"); e._adhocMissionScriptTags = {"TAG_A"}; e._funds = 1000;
        scenarios.push_back({"adhoc-scripts-run-first", e, true, false, false, 2, END_NONE, 0, 0, {}, {}, {}, {}});
    }
    {
        auto e = event("STR_ENDING"); e._cutscene = "winVideo";
        scenarios.push_back({"cutscene-wins-when-no-ending", e, true, true, true, 2, END_NONE, 0, 0, {}, {}, {}, {}});
        scenarios.push_back({"cutscene-keeps-an-existing-ending", e, true, true, true, 2, END_LOSE, 0, 0, {}, {}, {}, {}});
        auto f = event("STR_PLAIN"); f._cutscene = "plainVideo"; f._research = {&alloys};
        scenarios.push_back({"overfull-stores-open-the-sell-screen", f, true, true, true, 2, END_NONE, 0, 0, {}, {}, {}, {}});
        scenarios.push_back({"overfull-ignored-before-month-zero", f, true, true, true, -1, END_NONE, 0, 0, {}, {}, {}, {}});
    }

    std::printf("{\"scenarios\":[");
    bool first = true;
    for (auto& s : scenarios)
    {
        RNG::script = s.rng; RNG::next = 0; RNG::calls.str(""); RNG::calls.clear(); RNG::count = 0;
        trace.str(""); trace.clear();
        Options::oxceGeoscapeEventsInstantDelivery = s.instantDelivery; Options::storageLimitsEnforced = s.storageLimits;
        Mod mod; mod.regions = {{"STR_NORTH_AMERICA", &northAmerica}, {"STR_EUROPE", &europe}};
        mod.soldiers = {{"STR_SOLDIER", &xcom}, {"STR_HYBRID", &hybrid}};
        mod.items = {{"STR_RIFLE", &rifle}, {"STR_RIFLE_CLIP", &clip}, {"STR_GRENADE", &grenade}, {"STR_MEDI_KIT", &medkit}, {"STR_ELERIUM_115", &elerium}};
        mod.crafts = {{"STR_SKYRANGER", &skyranger}};
        mod.videos["winVideo"].win = true; mod.videos["plainVideo"];
        mod.hireByCountryOdds = s.hireByCountryOdds; mod.hireByRegionOdds = s.hireByRegionOdds;
        SavedGame save; save.funds = 100000; save.monthsPassed = s.monthsPassed; save.ending = s.ending;
        save.researched.insert(s.researched.begin(), s.researched.end());
        save.undiscoveredFree.insert(s.undiscoveredFree.begin(), s.undiscoveredFree.end());
        save.disabled.insert(s.disabled.begin(), s.disabled.end());
        save.freeBonus["STR_PLASMA"] = &bonus;
        save.ids["STR_SKYRANGER"] = 3;
        Base hq{"HQ"}, second{"Second"};
        hq.overfull = s.overfull; hq.storage.items = {{"STR_RIFLE", 3}, {"STR_ELERIUM_115", 10}};
        second.storage.items = {{"STR_RIFLE", 1}, {"STR_ELERIUM_115", 15}};
        Craft out(&skyranger, &hq, 1), home(&skyranger, &second, 2);
        out.status = "STR_OUT"; out.items.items = {{"STR_RIFLE", 5}, {"STR_ELERIUM_115", 5}};
        home.items.items = {{"STR_RIFLE", 4}, {"STR_ELERIUM_115", 30}};
        hq.crafts = {&out}; second.crafts = {&home};
        save.bases = {&hq, &second};
        Region na{&northAmerica}, eu{&europe}; save.regions = {&eu, &na};
        Country usaCountry{&usa}, canadaCountry{&canada}; save.countries = {&usaCountry, &canadaCountry};
        Game game{&save, &mod};
        GeoscapeEventState state(&game, s.rule);
        std::string exception;
        try { state.eventLogic(); } catch (const std::runtime_error& error) { exception = error.what(); }
        std::string logic = trace.str();
        trace.str(""); trace.clear();
        if (exception.empty()) state.btnOkClick(nullptr);
        std::string ok = trace.str();
        std::printf("%s{\"name\":\"%s\",\"instantDelivery\":%s,\"storageLimitsEnforced\":%s,\"hqOverfull\":%s,\"monthsPassed\":%d,"
            "\"endingBefore\":%d,\"hireByCountryOdds\":%d,\"hireByRegionOdds\":%d,\"rng\":[", first ? "" : ",", s.name,
            s.instantDelivery ? "true" : "false", s.storageLimits ? "true" : "false", s.overfull ? "true" : "false", s.monthsPassed, s.ending,
            s.hireByCountryOdds, s.hireByRegionOdds);
        for (size_t i = 0; i < s.rng.size(); ++i) std::printf("%s%llu", i ? "," : "", (unsigned long long)s.rng[i]);
        std::printf("],\"title\":%s,\"message\":%s,\"regions\":{\"STR_EUROPE\":%d,\"STR_NORTH_AMERICA\":%d},\"researchScore\":%d,\"funds\":%lld,\"transfers\":[",
            quote(state._txtTitle->text).c_str(), quote(state._txtMessage->text).c_str(), eu.xcom, na.xcom, save.researchScore, (long long)save.funds);
        for (size_t i = 0; i < hq.transfers.size(); ++i)
        {
            auto* t = hq.transfers[i];
            std::printf("%s{\"hours\":%d", i ? "," : "", t->hours);
            if (t->scientists) std::printf(",\"scientists\":%d", t->scientists);
            if (t->engineers) std::printf(",\"engineers\":%d", t->engineers);
            if (t->soldier) std::printf(",\"soldier\":{\"type\":\"%s\",\"id\":%d,\"nationality\":%d,\"name\":\"%s\"}", t->soldier->type.c_str(),
                t->soldier->id, t->soldier->nationality, t->soldier->name.c_str());
            if (t->item) std::printf(",\"item\":\"%s\",\"quantity\":%d", t->item->type.c_str(), t->quantity);
            if (t->craft) std::printf(",\"craft\":{\"type\":\"%s\",\"id\":%d,\"status\":\"%s\"}", t->craft->type.c_str(), t->craft->id, t->craft->status.c_str());
            std::printf("}");
        }
        auto container = [](const ItemContainer& c)
        {
            std::printf("{"); bool f = true;
            for (const auto& p : c.items) { std::printf("%s\"%s\":%d", f ? "" : ",", p.first.c_str(), p.second); f = false; }
            std::printf("}");
        };
        std::printf("],\"stores\":["); container(hq.storage); std::printf(","); container(second.storage);
        std::printf("],\"craftItems\":["); container(out.items); std::printf(","); container(home.items);
        std::printf("],\"hqCrafts\":[");
        for (size_t i = 0; i < hq.crafts.size(); ++i) std::printf("%s\"%s-%d %s\"", i ? "," : "", hq.crafts[i]->type.c_str(), hq.crafts[i]->id, hq.crafts[i]->status.c_str());
        std::printf("],\"rows\":[");
        for (size_t i = 0; i < state._lstTransfers->rows.size(); ++i)
            std::printf("%s[\"%s\",\"%s\"]", i ? "," : "", state._lstTransfers->rows[i].first.c_str(), state._lstTransfers->rows[i].second.c_str());
        std::printf("],\"diary\":[");
        for (size_t i = 0; i < save.diary.size(); ++i) std::printf("%s\"%s\"", i ? "," : "", save.diary[i].c_str());
        std::printf("],\"researchName\":\"%s\",\"bonusResearchName\":\"%s\",\"ending\":%d,\"exception\":\"%s\",\"logic\":[%s],\"ok\":[%s],\"rngCalls\":[%s]}",
            state._researchName.c_str(), state._bonusResearchName.c_str(), save.ending, exception.c_str(), logic.c_str(), ok.c_str(),
            RNG::calls.str().c_str());
        first = false;
    }

    // SavedGame::canSpawnInstantEvent: a missing rule, an interrupted rule and a rule that counts once more.
    RuleEvent despawn; despawn._name = "STR_DESPAWN"; despawn._interruptResearch = "STR_STOP";
    SavedGame save;
    bool missing = save.canSpawnInstantEvent(nullptr);
    bool allowed = save.canSpawnInstantEvent(&despawn);
    save.researched.insert("STR_STOP");
    bool interrupted = save.canSpawnInstantEvent(&despawn);
    std::printf("],\"instantEvents\":{\"missing\":%s,\"allowed\":%s,\"interrupted\":%s,\"generated\":%d}}\n",
        missing ? "true" : "false", allowed ? "true" : "false", interrupted ? "true" : "false", save._generatedEvents["STR_DESPAWN"]);
    return 0;
}

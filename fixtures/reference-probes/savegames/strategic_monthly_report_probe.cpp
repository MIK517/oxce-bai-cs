// Month rollover: GeoscapeState::time1Month, SavedGame::addMonth/monthlyFunding/getCountryFunding/
// getBaseMaintenance/getDifficultyCoefficient/getFunds/setFunds, Region::newMonth, Country::newMonth/
// getSatisfaction/addActivityAlien, Mod::getRandomMission, RuleAlienMission::getWeight and the
// MonthlyReportState constructor (from calculateChanges() on), calculateChanges, countryList and btnOkClick
// are inserted verbatim by the capture script. The report's widget construction before calculateChanges()
// is skipped; widgets, translations and states are stubs that record what the methods do. RNG returns
// scripted raw values through the RandomState::generate formula. The newMonthCountry script is replaced by
// an optional per-country override of its four outputs.
#include <algorithm>
#include <cstdint>
#include <cstdio>
#include <map>
#include <optional>
#include <sstream>
#include <string>
#include <tuple>
#include <vector>

namespace OpenXcom {

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

namespace Options { bool anytimePsiTraining = false, psiStrengthEval = false, autosave = true; }
namespace Unicode
{
const char TOK_COLOR_FLIP = '^';
std::string formatFunding(int64_t funds) { return "$" + std::to_string(funds); }
}

std::ostringstream trace;
void record(const std::string& entry) { trace << (trace.tellp() > 0 ? "," : "") << '"' << entry << '"'; }
struct Action;

struct LocalizedText
{
    std::string text;
    LocalizedText(const std::string& t = "") : text(t) {}
    LocalizedText arg(const std::string& value) const { return LocalizedText(text + "{" + value + "}"); }
    LocalizedText arg(const LocalizedText& value) const { return arg(value.text); }
    LocalizedText arg(int value) const { return arg(std::to_string(value)); }
    operator std::string() const { return text; }
};
std::ostream& operator<<(std::ostream& out, const LocalizedText& text) { return out << text.text; }

struct Text
{
    std::string name, text; int y = 0; bool visible = true;
    void setText(const std::string& t) { text = t; }
    int getY() const { return y; } void setY(int value) { y = value; }
    void setVisible(bool v) { visible = v; } bool getVisible() const { return visible; }
    void setWordWrap(bool) {} void setScrollable(bool) {}
};
struct TextButton { void setVisible(bool) {} };
struct Window { void setColor(int) {} };

enum GameEnding { END_NONE, END_WIN, END_LOSE };
enum GameDifficulty { DIFF_BEGINNER = 0, DIFF_EXPERIENCED, DIFF_VETERAN, DIFF_GENIUS, DIFF_SUPERHUMAN };
enum MissionObjective { OBJECTIVE_SCORE, OBJECTIVE_INFILTRATION, OBJECTIVE_BASE, OBJECTIVE_SITE, OBJECTIVE_RETALIATION, OBJECTIVE_SUPPLY };
enum OptionsOrigin { OPT_GEOSCAPE };
enum SaveType { SAVE_IRONMAN, SAVE_AUTO_GEOSCAPE };

struct RuleVideo { bool loseGame = false; bool getLoseGame() const { return loseGame; } };
struct RuleEvent { std::string name; };
struct RuleCountry
{
    std::string type, signedPactEvent, rejoinedXcomEvent; int fundingCap = 0;
    const std::string& getType() const { return type; }
    int getFundingCap() const { return fundingCap; }
    const RuleEvent* signed_ = nullptr; const RuleEvent* rejoined = nullptr;
    const RuleEvent* getSignedPactEvent() const { return signed_; }
    const RuleEvent* getRejoinedXcomEvent() const { return rejoined; }
    template<class T> const void* getScript() const { return nullptr; }
};
struct RuleAlienMission
{
    std::string type; MissionObjective objective = OBJECTIVE_INFILTRATION; int points = 0;
    std::map<size_t, int> _weights;
    MissionObjective getObjective() const { return objective; }
    int getPoints() const { return points; }
    int getWeight(const size_t monthsPassed) const;
};
struct RuleInterfaceElement { int color2 = 0; };
struct RuleInterface { RuleInterfaceElement element; RuleInterfaceElement* getElement(const std::string&) { return &element; } };
struct StatString {};

class Mod
{
public:
    static int DIFFICULTY_COEFFICIENT[5];
    std::map<std::string, RuleAlienMission*> _alienMissions;
    std::map<std::string, RuleCountry*> countries;
    std::map<std::string, RuleVideo> videos;
    std::map<int, std::string> _monthlyRatings;
    std::vector<int> _monthlyRatingThresholds;
    std::string _loseMoney = "loseGame", _loseRating = "loseGame";
    int defeatScore = 0, defeatFunds = 0, chanceToDetect = 20;
    double _performanceBonusFactor = 0;
    RuleInterface interface_;
    std::vector<StatString*> statStrings;
    const RuleAlienMission* getRandomMission(MissionObjective objective, size_t monthsPassed) const;
    int getDefeatScore() const { return defeatScore; }
    int getDefeatFunds() const { return defeatFunds; }
    const std::vector<int>& getMonthlyRatingThresholds() { return _monthlyRatingThresholds; }
    const std::map<int, std::string>* getMonthlyRatings() const { return &_monthlyRatings; }
    int getPerformanceBonus(int score) const { return (int)(score * _performanceBonusFactor); }
    const RuleCountry* getCountry(const std::string& id, bool) const
    { auto i = countries.find(id); return i == countries.end() ? nullptr : i->second; }
    const std::string& getLoseRatingCutscene() const { return _loseRating; }
    const std::string& getLoseMoneyCutscene() const { return _loseMoney; }
    const RuleVideo* getVideo(const std::string& id, bool) const { return &videos.at(id); }
    RuleInterface* getInterface(const std::string&) { return &interface_; }
    void playMusic(const std::string& id) { record("music " + id); }
    int getChanceToDetectAlienBaseEachMonth() const { return chanceToDetect; }
    const std::vector<StatString*>& getStatStrings() const { return statStrings; }
    const void* getPsiRequirements() const { return nullptr; }
};
int Mod::DIFFICULTY_COEFFICIENT[5] = {0, 1, 2, 3, 4};

class SavedGame;
class Country
{
public:
    enum class Satisfaction : int { ALIEN_PACT, UNHAPPY, SATISFIED, HAPPY };
    RuleCountry* _rules;
    bool _pact, _newPact, _cancelPact;
    std::vector<int> _funding, _activityXcom, _activityAlien;
    Satisfaction _satisfaction = Satisfaction::SATISFIED;
    std::optional<std::tuple<int, int, int, int>> scriptOutput;
    RuleCountry* getRules() const { return _rules; }
    std::vector<int>& getFunding() { return _funding; }
    const std::vector<int>& getFunding() const { return _funding; }
    bool getPact() const { return _pact; }
    Satisfaction getSatisfaction() const;
    void addActivityAlien(int activity);
    void newMonth(int xcomTotal, int alienTotal, int pactScore, int averageFunding, const SavedGame* save);
};
namespace ModScript
{
struct NewMonthCountry
{
    struct Output
    {
        std::tuple<int, int, int, int> data;
        Output(int funding, int satisfaction, bool newPact, bool cancelPact) : data(funding, satisfaction, newPact, cancelPact) {}
    };
    struct Worker
    {
        Country* country; const SavedGame* save; int xcomTotal, alienTotal;
        void execute(const void*, Output& output)
        {
            if (country->scriptOutput) output.data = *country->scriptOutput;
        }
    };
};
}

class Region
{
public:
    std::vector<int> _activityXcom, _activityAlien;
    std::vector<int>& getActivityXcom() { return _activityXcom; }
    std::vector<int>& getActivityAlien() { return _activityAlien; }
    void newMonth();
};

struct SoldierDiary
{
    int monthsService = 0;
    void addMonthlyService() { ++monthsService; }
    bool manageCommendations(Mod*, SavedGame*, const struct Soldier*) { return false; }
};
struct Soldier
{
    std::string name; bool psiTraining = false; SoldierDiary diary;
    SoldierDiary* getDiary() { return &diary; }
    bool isInPsiTraining() const { return psiTraining; }
    void trainPsi() { record("trainPsi " + name); }
    void calcStatString(const std::vector<StatString*>&, bool psi) { record(std::string("statString ") + name + (psi ? " psi" : "")); }
};
struct Base
{
    int maintenance = 0, psiLabs = 0; std::vector<Soldier*> soldiers;
    int getMonthlyMaintenace() const { return maintenance; }
    int getAvailablePsiLabs() const { return psiLabs; }
    std::vector<Soldier*>* getSoldiers() { return &soldiers; }
};
struct AlienBase
{
    int id = 0; bool discovered = false;
    bool isDiscovered() const { return discovered; } void setDiscovered(bool d) { discovered = d; }
};
struct GameTime { int month = 1, year = 1999; int getMonth() const { return month; } int getYear() const { return year; } };

class SavedGame
{
public:
    GameDifficulty _difficulty = DIFF_BEGINNER;
    std::vector<int> _researchScores;
    std::vector<int64_t> _funds, _maintenance, _incomes, _expenditures;
    std::map<std::string, int> _monthlyPurchaseLimitLog;
    bool _warned = false, _ironman = false, psiResearched = false;
    int _monthsPassed = 0, ending = END_NONE;
    std::vector<Region*> _regions; std::vector<Country*> _countries; std::vector<Base*> _bases;
    std::vector<AlienBase*> _alienBases;
    GameTime _time;
    std::vector<std::string> spawned;
    void addMonth();
    void monthlyFunding();
    int getCountryFunding() const;
    int getBaseMaintenance() const;
    int getDifficultyCoefficient() const;
    int64_t getFunds() const;
    void setFunds(int64_t funds);
    int getDifficulty() const { return _difficulty; }
    std::vector<int64_t>& getFundsList() { return _funds; }
    std::vector<int>& getResearchScores() { return _researchScores; }
    int getMonthsPassed() const { return _monthsPassed; }
    std::vector<Region*>* getRegions() { return &_regions; }
    std::vector<Country*>* getCountries() { return &_countries; }
    std::vector<Base*>* getBases() { return &_bases; }
    std::vector<AlienBase*>* getAlienBases() { return &_alienBases; }
    GameTime* getTime() { return &_time; }
    bool getWarned() const { return _warned; } void setWarned(bool w) { _warned = w; }
    bool isIronman() const { return _ironman; }
    void setEnding(GameEnding e) { ending = e; }
    bool isResearched(const void*) const { return psiResearched; }
    bool spawnEvent(const RuleEvent* rule) { if (!rule) return false; spawned.push_back(rule->name); return true; }
};

struct State { virtual ~State() {} virtual std::string name() const = 0; };
struct NamedState : State { std::string id; NamedState(std::string i) : id(std::move(i)) {} std::string name() const override { return id; } };
struct CommendationState : NamedState { CommendationState(const std::vector<Soldier*>&) : NamedState("CommendationState") {} };
struct PsiTrainingState : NamedState { PsiTrainingState() : NamedState("PsiTrainingState") {} };
struct CutsceneState : NamedState { CutsceneState(const std::string& id) : NamedState("CutsceneState " + id) {} };
struct SaveGameState : NamedState
{ SaveGameState(OptionsOrigin, SaveType type, int) : NamedState(type == SAVE_IRONMAN ? "SaveGameState ironman" : "SaveGameState auto") {} };
struct Globe {};

struct Game
{
    SavedGame* save; Mod* mod;
    SavedGame* getSavedGame() { return save; }
    Mod* getMod() { return mod; }
    void popState() { record("popState"); }
    void pushState(State* state) { record("pushState " + state->name()); delete state; }
};
Game* currentGame = nullptr;

class MonthlyReportState : public State
{
public:
    Game* _game = currentGame; int _palette = 0;
    TextButton *_btnOk = new TextButton(), *_btnBigOk = new TextButton();
    Window* _window = new Window();
    Text *_txtTitle = new Text(), *_txtMonth = new Text(), *_txtRating = new Text();
    Text *_txtIncome = new Text(), *_txtMaintenance = new Text{"", "", 40}, *_txtBalance = new Text{"", "", 40},
        *_txtBonus = new Text{"", "", 48};
    Text *_txtDesc = new Text{"", "", 56}, *_txtFailure = new Text();
    int _gameOver;
    int _ratingTotal, _fundingDiff, _lastMonthsRating;
    std::vector<std::string> _happyList, _sadList, _pactList, _cancelPactList;
    Globe* _globe;
    std::vector<Soldier*> _soldiersMedalled;
    std::string name() const override { return "MonthlyReportState"; }
    LocalizedText tr(const std::string& id) const { return LocalizedText(id); }
    std::string countryList(const std::vector<std::string>& countries, const std::string& singular, const std::string& plural);
    MonthlyReportState(Globe* globe);
    void btnOkClick(Action* action);
    void calculateChanges();
};
MonthlyReportState* lastReport = nullptr;

class GeoscapeState
{
public:
    Game* _game; Globe* _globe = nullptr; bool _pause = false;
    std::vector<State*> _popups;
    void determineAlienMissions() { record("determineAlienMissions"); }
    void timerReset() { record("timerReset"); }
    void popup(State* state)
    {
        _pause = true; _popups.push_back(state); record("popup " + state->name());
        if (auto* report = dynamic_cast<MonthlyReportState*>(state)) lastReport = report;
    }
    void time1Month();
};
struct AlienBaseState : NamedState
{ AlienBaseState(AlienBase* base, GeoscapeState*) : NamedState("AlienBaseState " + std::to_string(base->id)) {} };

// SAVEDGAME_METHODS
// REGION_METHODS
// COUNTRY_METHODS
// MISSION_METHODS
MonthlyReportState::MonthlyReportState(Globe* globe) : _gameOver(0), _ratingTotal(0), _fundingDiff(0), _lastMonthsRating(0), _globe(globe)
{
    _txtFailure->setVisible(false);
    // REPORT_CONSTRUCTOR_TAIL
// REPORT_METHODS
// TIME1MONTH

}

using namespace OpenXcom;

struct CountryInput
{
    const char* type; int fundingCap; std::vector<int> funding, xcom, alien; bool pact, newPact, cancelPact;
    const char* signedPactEvent; const char* rejoinedEvent; std::optional<std::tuple<int, int, int, int>> script;
};
struct MissionInput { const char* type; int points; std::map<size_t, int> weights; };
struct Scenario
{
    const char* name; int difficulty, monthsPassed;
    std::vector<int64_t> funds, maintenance, incomes, expenditures; std::vector<int> researchScores;
    std::vector<std::pair<std::vector<int>, std::vector<int>>> regions;
    std::vector<CountryInput> countries; std::vector<int> baseMaintenance;
    int defeatScore, defeatFunds; std::vector<int> ratingThresholds; std::map<int, std::string> ratings;
    double bonusFactor; bool warned; std::vector<MissionInput> missions;
    std::vector<bool> alienBases; int chanceToDetect;
    bool loseRatingVideoEnds, loseMoneyVideoEnds, ironman, autosave, anytimePsi, psiStrengthEval, psiResearched;
    int psiLabs; std::vector<uint64_t> rng;
};

template<class T> void printArray(const std::vector<T>& values)
{
    std::printf("[");
    for (size_t i = 0; i < values.size(); ++i) std::printf("%s%lld", i ? "," : "", (long long)values[i]);
    std::printf("]");
}
void strings(const std::vector<std::string>& values)
{
    std::printf("[");
    for (size_t i = 0; i < values.size(); ++i) std::printf("%s\"%s\"", i ? "," : "", values[i].c_str());
    std::printf("]");
}

int main()
{
    CountryInput us{"STR_USA", 2000, {600000}, {120}, {10}, false, false, false, "STR_USA_PACT", "STR_USA_BACK", std::nullopt};
    CountryInput ru{"STR_RUSSIA", 1000, {400000}, {5}, {300}, false, false, false, "", "", std::nullopt};
    MissionInput infiltration{"STR_ALIEN_INFILTRATION", 150, {}};
    const std::vector<Scenario> scenarios = {
        {"first-month-happy-and-unhappy", 0, 0, {5000000}, {0}, {0}, {0}, {200},
            {{{300}, {50}}, {{20}, {400}}}, {us, ru}, {700000, 300000}, 0, 0, {}, {}, 0.0, false, {infiltration}, {}, 20,
            true, true, false, true, false, false, false, 0, {0, 3, 100, 5, 200}},
        {"second-month-council-bonus-and-cap", 1, 1, {4000000, 4500000}, {800000, 0}, {600000, 0}, {100000, 0}, {50, 120},
            {{{0, 900}, {0, 10}}}, {{"STR_USA", 700, {600000, 690000}, {0, 300}, {0, 0}, false, false, false, "", "", std::nullopt}},
            {500000}, 0, 0, {}, {}, 0.5, false, {}, {}, 20, true, true, false, false, false, false, false, 0, {19, 1000}},
        {"pact-signed-and-cancelled", 2, 2, {3000000, 3000000}, {0, 0}, {0, 0}, {0, 0}, {0, 0},
            {{{0, 0}, {0, 0}}},
            {{"STR_USA", 2000, {600000, 600000}, {0, 0}, {0, 0}, false, true, false, "STR_USA_PACT", "", std::nullopt},
             {"STR_RUSSIA", 1000, {0, 0}, {0, 0}, {0, 0}, true, false, true, "", "STR_RUSSIA_BACK", std::nullopt},
             {"STR_CHINA", 1000, {500000, 300000}, {0, 0}, {0, 0}, true, false, true, "", "", std::nullopt}},
            {100000}, 0, 0, {}, {}, 0.0, false, {{"STR_INF_A", 100, {{0, 2}, {2, 0}}}, {"STR_INF_B", 250, {}}}, {}, 20,
            true, true, false, false, false, false, false, 0, {0, 0, 0, 0}},
        {"two-bad-months-rating-game-over", 0, 3, {100000, 100000, 100000}, {0, 0, 0}, {0, 0, 0}, {0, 0, 0}, {0, 0, 0},
            {{{0, 0, 0}, {0, 2000, 1500}}}, {ru}, {50000}, 0, 0, {}, {}, 0.0, false, {}, {}, 20,
            true, true, true, true, false, false, false, 0, {0}},
        {"debt-warning-then-continue", 0, 4, {-100000}, {0}, {0}, {0}, {800},
            {{{100}, {0}}}, {us}, {900000}, 0, 0, {}, {}, 0.0, false, {}, {}, 20,
            true, true, false, true, false, false, false, 0, {0}},
        {"debt-while-warned-money-video-without-ending", 0, 4, {-100000}, {0}, {0}, {0}, {800},
            {{{100}, {0}}}, {us}, {900000}, 0, 0, {}, {}, 0.0, true, {}, {}, 20,
            true, false, false, true, false, false, false, 0, {0}},
        {"warned-but-solvent-resets", 3, 4, {9000000}, {0}, {0}, {0}, {800},
            {{{100}, {0}}}, {us}, {300000}, 0, 0, {}, {}, 0.0, true, {}, {}, 20,
            true, true, false, false, false, false, false, 0, {0}},
        {"custom-thresholds-and-ratings", 3, 5, {2000000, 2000000}, {0, 0}, {0, 0}, {0, 0}, {0, 0},
            {{{0, 0}, {0, 250}}}, {us}, {100000}, -1000, 0, {0, -50, 120, -400}, {{-100000, "STR_AWFUL"}, {-300, "STR_MEH"}, {0, "STR_FINE"}},
            0.0, false, {}, {}, 20, true, true, false, false, false, false, false, 0, {0}},
        {"negative-score-ignores-performance-bonus", 0, 2, {2000000, 2000000}, {0, 0}, {0, 0}, {0, 0}, {0, 0},
            {{{0, 0}, {0, 3000}}}, {ru}, {100000}, -100000, 0, {}, {}, 2.0, false, {}, {}, 20,
            true, true, false, false, false, false, false, 0, {0}},
        {"script-overrides-funding-and-pacts", 0, 2, {2000000, 2000000}, {0, 0}, {0, 0}, {0, 0}, {0, 0},
            {{{0, 0}, {0, 0}}},
            {{"STR_USA", 2000, {600000, 600000}, {0, 0}, {0, 0}, false, false, false, "STR_USA_PACT", "", std::make_tuple(-7000, 1, 1, 0)},
             {"STR_RUSSIA", 1000, {0, 0}, {0, 0}, {0, 0}, true, false, false, "", "STR_RUSSIA_BACK", std::make_tuple(123000, 3, 0, 1)}},
            {100000}, 0, 0, {}, {}, 0.0, false, {infiltration}, {}, 20, true, true, false, false, false, false, false, 0, {0, 2, 2}},
        {"zero-funding-grows-by-a-thousand", 0, 2, {2000000, 2000000}, {0, 0}, {0, 0}, {0, 0}, {0, 0},
            {{{0, 900}, {0, 0}}}, {{"STR_USA", 2000, {0, 999}, {0, 0}, {0, 0}, false, false, false, "", "", std::nullopt}},
            {100000}, 0, 0, {}, {}, 0.0, false, {}, {}, 20, true, true, false, false, false, false, false, 0, {7, 100}},
        {"unhappy-cannot-go-below-zero", 0, 2, {2000000, 2000000}, {0, 0}, {0, 0}, {0, 0}, {0, 0},
            {{{0, 0}, {0, 6000}}}, {{"STR_RUSSIA", 1000, {0, 500}, {0, 0}, {0, 0}, false, false, false, "", "", std::nullopt}},
            {100000}, 0, 0, {}, {}, 0.0, false, {}, {}, 20, true, true, false, false, false, false, false, 0, {15, 250}},
        {"twelve-month-histories-trim", 0, 12,
            {1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12}, {1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12},
            {1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12}, {1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12},
            {1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12},
            {{{1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12}, {0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0}}},
            {{"STR_USA", 2000, {1000, 2000, 3000, 4000, 5000, 6000, 7000, 8000, 9000, 10000, 11000, 12000},
                {1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12}, {0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0}, false, false, false, "", "", std::nullopt}},
            {100}, 0, 0, {}, {}, 0.0, false, {}, {}, 20, true, true, false, false, false, false, false, 0, {0}},
        {"monthly-psi-and-base-discovery", 0, 1, {5000000}, {0}, {0}, {0}, {300},
            {{{300}, {0}}}, {us}, {100000}, 0, 0, {}, {}, 0.0, false, {}, {true, false, false}, 35,
            true, true, false, true, false, true, true, 1, {0, 34, 34}},
        {"anytime-psi-and-missed-discovery", 0, 1, {5000000}, {0}, {0}, {0}, {300},
            {{{300}, {0}}}, {us}, {100000}, 0, 0, {}, {}, 0.0, false, {}, {false}, 35,
            true, true, true, false, true, true, true, 1, {0, 34, 35}},
        // Both countries draw over a negative range (RNG::generate(0, -50)): RandomState::generate converts it to an
        // unsigned modulus, so the raw value comes back unchanged. A range of exactly -1 divides by zero.
        {"negative-activity-draws-over-a-negative-range", 0, 0, {2000000}, {0}, {0}, {0}, {0},
            {{{0}, {0}}},
            {{"STR_USA", 2000, {600000}, {-50}, {-100}, false, false, false, "", "", std::nullopt},
             {"STR_RUSSIA", 1000, {400000}, {-100}, {-50}, false, false, false, "", "", std::nullopt}},
            {100000}, 0, 0, {}, {}, 0.0, false, {}, {}, 20, true, true, false, false, false, false, false, 0, {0, 7, 0, 7}},
        {"no-alien-bases-draws-nothing", 0, 1, {5000000}, {0}, {0}, {0}, {300},
            {{{300}, {0}}}, {us}, {100000}, 0, 0, {}, {}, 0.0, false, {}, {}, 100,
            true, true, false, true, false, false, false, 0, {0}},
    };
    std::printf("{\"scenarios\":[");
    bool firstScenario = true;
    for (const auto& s : scenarios)
    {
        RNG::script = s.rng; RNG::next = 0; RNG::calls.str(""); RNG::calls.clear(); RNG::count = 0;
        trace.str(""); trace.clear();
        Options::anytimePsiTraining = s.anytimePsi; Options::psiStrengthEval = s.psiStrengthEval; Options::autosave = s.autosave;
        Mod mod; mod.defeatScore = s.defeatScore; mod.defeatFunds = s.defeatFunds; mod._monthlyRatingThresholds = s.ratingThresholds;
        mod._monthlyRatings = s.ratings; mod._performanceBonusFactor = s.bonusFactor; mod.chanceToDetect = s.chanceToDetect;
        mod._loseRating = "loseRatingVideo"; mod._loseMoney = "loseMoneyVideo";
        mod.videos["loseRatingVideo"].loseGame = s.loseRatingVideoEnds; mod.videos["loseMoneyVideo"].loseGame = s.loseMoneyVideoEnds;
        std::vector<RuleAlienMission> missions(s.missions.size());
        for (size_t i = 0; i < s.missions.size(); ++i)
        {
            missions[i].type = s.missions[i].type; missions[i].points = s.missions[i].points; missions[i]._weights = s.missions[i].weights;
            mod._alienMissions[missions[i].type] = &missions[i];
        }
        SavedGame save; save._difficulty = (GameDifficulty)s.difficulty; save._monthsPassed = s.monthsPassed;
        save._funds = s.funds; save._maintenance = s.maintenance; save._incomes = s.incomes; save._expenditures = s.expenditures;
        save._researchScores = s.researchScores; save._warned = s.warned; save._ironman = s.ironman; save.psiResearched = s.psiResearched;
        save._monthlyPurchaseLimitLog["STR_RIFLE"] = 3;
        std::vector<Region> regions(s.regions.size());
        for (size_t i = 0; i < regions.size(); ++i)
        {
            regions[i]._activityXcom = s.regions[i].first; regions[i]._activityAlien = s.regions[i].second;
            save._regions.push_back(&regions[i]);
        }
        std::vector<RuleCountry> countryRules(s.countries.size()); std::vector<RuleEvent> events(2 * s.countries.size());
        std::vector<Country> countries(s.countries.size());
        for (size_t i = 0; i < countries.size(); ++i)
        {
            const auto& c = s.countries[i];
            countryRules[i].type = c.type; countryRules[i].fundingCap = c.fundingCap;
            events[2 * i].name = c.signedPactEvent; events[2 * i + 1].name = c.rejoinedEvent;
            countryRules[i].signed_ = *c.signedPactEvent ? &events[2 * i] : nullptr;
            countryRules[i].rejoined = *c.rejoinedEvent ? &events[2 * i + 1] : nullptr;
            mod.countries[c.type] = &countryRules[i];
            countries[i]._rules = &countryRules[i]; countries[i]._funding = c.funding; countries[i]._activityXcom = c.xcom;
            countries[i]._activityAlien = c.alien; countries[i]._pact = c.pact; countries[i]._newPact = c.newPact;
            countries[i]._cancelPact = c.cancelPact; countries[i].scriptOutput = c.script;
            save._countries.push_back(&countries[i]);
        }
        std::vector<Base> bases(s.baseMaintenance.size()); std::vector<Soldier> soldiers(2);
        soldiers[0].name = "Alpha"; soldiers[0].psiTraining = true; soldiers[1].name = "Beta";
        for (size_t i = 0; i < bases.size(); ++i)
        {
            bases[i].maintenance = s.baseMaintenance[i];
            if (i == 0) { bases[i].psiLabs = s.psiLabs; bases[i].soldiers = {&soldiers[0], &soldiers[1]}; }
            save._bases.push_back(&bases[i]);
        }
        std::vector<AlienBase> alienBases(s.alienBases.size());
        for (size_t i = 0; i < alienBases.size(); ++i)
        {
            alienBases[i].id = (int)i + 1; alienBases[i].discovered = s.alienBases[i];
            save._alienBases.push_back(&alienBases[i]);
        }
        save._time.month = 1 + s.monthsPassed % 12; save._time.year = 1999 + (s.monthsPassed + 1) / 12;
        Game game{&save, &mod}; currentGame = &game; lastReport = nullptr;
        GeoscapeState geo; geo._game = &game;
        geo.time1Month();
        std::string afterTick = trace.str();
        trace.str(""); trace.clear();
        auto* report = lastReport;
        report->btnOkClick(nullptr);
        if (report->_gameOver) report->btnOkClick(nullptr);
        std::string afterOk = trace.str();

        std::printf("%s{\"name\":\"%s\",\"input\":{\"difficulty\":%d,\"monthsPassed\":%d,\"funds\":", firstScenario ? "" : ",",
            s.name, s.difficulty, s.monthsPassed);
        printArray(s.funds); std::printf(",\"maintenance\":"); printArray(s.maintenance); std::printf(",\"incomes\":"); printArray(s.incomes);
        std::printf(",\"expenditures\":"); printArray(s.expenditures); std::printf(",\"researchScores\":"); printArray(s.researchScores);
        std::printf(",\"regions\":[");
        for (size_t i = 0; i < s.regions.size(); ++i)
        {
            std::printf("%s{\"xcom\":", i ? "," : ""); printArray(s.regions[i].first); std::printf(",\"alien\":"); printArray(s.regions[i].second);
            std::printf("}");
        }
        std::printf("],\"countries\":[");
        for (size_t i = 0; i < s.countries.size(); ++i)
        {
            const auto& c = s.countries[i];
            std::printf("%s{\"type\":\"%s\",\"fundingCap\":%d,\"funding\":", i ? "," : "", c.type, c.fundingCap);
            printArray(c.funding); std::printf(",\"xcom\":"); printArray(c.xcom); std::printf(",\"alien\":"); printArray(c.alien);
            std::printf(",\"pact\":%s,\"newPact\":%s,\"cancelPact\":%s,\"signedPactEvent\":\"%s\",\"rejoinedXcomEvent\":\"%s\",\"script\":",
                c.pact ? "true" : "false", c.newPact ? "true" : "false", c.cancelPact ? "true" : "false", c.signedPactEvent, c.rejoinedEvent);
            if (c.script) std::printf("[%d,%d,%d,%d]", std::get<0>(*c.script), std::get<1>(*c.script), std::get<2>(*c.script), std::get<3>(*c.script));
            else std::printf("null");
            std::printf("}");
        }
        std::printf("],\"baseMaintenance\":"); printArray(s.baseMaintenance);
        std::printf(",\"defeatScore\":%d,\"defeatFunds\":%d,\"monthlyRatingThresholds\":", s.defeatScore, s.defeatFunds);
        printArray(s.ratingThresholds); std::printf(",\"monthlyRatings\":[");
        bool firstRating = true;
        for (const auto& pair : s.ratings) { std::printf("%s[%d,\"%s\"]", firstRating ? "" : ",", pair.first, pair.second.c_str()); firstRating = false; }
        std::printf("],\"performanceBonusFactor\":%.17g,\"warned\":%s,\"infiltrations\":[", s.bonusFactor, s.warned ? "true" : "false");
        for (size_t i = 0; i < s.missions.size(); ++i)
        {
            std::printf("%s{\"type\":\"%s\",\"points\":%d,\"weights\":[", i ? "," : "", s.missions[i].type, s.missions[i].points);
            bool firstWeight = true;
            for (const auto& weight : s.missions[i].weights)
            { std::printf("%s[%zu,%d]", firstWeight ? "" : ",", weight.first, weight.second); firstWeight = false; }
            std::printf("]}");
        }
        std::printf("],\"alienBasesDiscovered\":[");
        for (size_t i = 0; i < s.alienBases.size(); ++i) std::printf("%s%s", i ? "," : "", s.alienBases[i] ? "true" : "false");
        std::printf("],\"chanceToDetectAlienBaseEachMonth\":%d,\"loseRatingVideoEnds\":%s,\"loseMoneyVideoEnds\":%s,\"ironman\":%s,"
            "\"autosave\":%s,\"anytimePsiTraining\":%s,\"psiStrengthEval\":%s,\"psiResearched\":%s,\"psiLabs\":%d,\"rng\":",
            s.chanceToDetect, s.loseRatingVideoEnds ? "true" : "false", s.loseMoneyVideoEnds ? "true" : "false",
            s.ironman ? "true" : "false", s.autosave ? "true" : "false", s.anytimePsi ? "true" : "false",
            s.psiStrengthEval ? "true" : "false", s.psiResearched ? "true" : "false", s.psiLabs);
        printArray(s.rng);
        std::printf("},\"monthsPassed\":%d,\"purchaseLog\":%zu,\"funds\":", save._monthsPassed, save._monthlyPurchaseLimitLog.size());
        printArray(save._funds); std::printf(",\"maintenance\":"); printArray(save._maintenance); std::printf(",\"incomes\":"); printArray(save._incomes);
        std::printf(",\"expenditures\":"); printArray(save._expenditures); std::printf(",\"researchScores\":"); printArray(save._researchScores);
        std::printf(",\"regions\":[");
        for (size_t i = 0; i < regions.size(); ++i)
        {
            std::printf("%s{\"xcom\":", i ? "," : ""); printArray(regions[i]._activityXcom); std::printf(",\"alien\":"); printArray(regions[i]._activityAlien);
            std::printf("}");
        }
        std::printf("],\"countries\":[");
        for (size_t i = 0; i < countries.size(); ++i)
        {
            const auto& c = countries[i];
            std::printf("%s{\"funding\":", i ? "," : ""); printArray(c._funding); std::printf(",\"xcom\":"); printArray(c._activityXcom);
            std::printf(",\"alien\":"); printArray(c._activityAlien);
            std::printf(",\"pact\":%s,\"newPact\":%s,\"cancelPact\":%s,\"satisfaction\":%d}", c._pact ? "true" : "false",
                c._newPact ? "true" : "false", c._cancelPact ? "true" : "false", (int)c.getSatisfaction());
        }
        std::printf("],\"ratingTotal\":%d,\"lastMonthsRating\":%d,\"fundingDiff\":%d,\"gameOver\":%d,\"warned\":%s,"
            "\"ending\":%d,\"month\":\"%s\",\"rating\":\"%s\",\"income\":\"%s\",\"maintenanceText\":\"%s\",\"bonus\":\"%s\","
            "\"bonusVisible\":%s,\"balance\":\"%s\",\"description\":\"",
            report->_ratingTotal, report->_lastMonthsRating, report->_fundingDiff, report->_gameOver, save._warned ? "true" : "false",
            save.ending, report->_txtMonth->text.c_str(), report->_txtRating->text.c_str(), report->_txtIncome->text.c_str(),
            report->_txtMaintenance->text.c_str(), report->_txtBonus->text.c_str(), report->_txtBonus->visible ? "true" : "false",
            report->_txtBalance->text.c_str());
        for (char ch : report->_txtDesc->text) { if (ch == '\n') std::printf("\\n"); else std::printf("%c", ch); }
        std::printf("\",\"spawnedEvents\":"); strings(save.spawned);
        std::printf(",\"alienBasesDiscovered\":[");
        for (size_t i = 0; i < alienBases.size(); ++i) std::printf("%s%s", i ? "," : "", alienBases[i].discovered ? "true" : "false");
        std::printf("],\"monthsService\":[%d,%d],\"tick\":[%s],\"acknowledgement\":[%s],\"rngCalls\":[%s]}",
            soldiers[0].diary.monthsService, soldiers[1].diary.monthsService, afterTick.c_str(), afterOk.c_str(), RNG::calls.str().c_str());
        firstScenario = false;
        delete report;
    }
    std::printf("]}\n");
    return 0;
}

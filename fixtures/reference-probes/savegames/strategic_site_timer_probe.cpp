// The timer/follower and score statements of GeoscapeState::processMissionSite are inserted verbatim.
#include <iostream>
#include <map>
#include <string>
#include <vector>

struct Site;
class SavedGame {
    std::map<std::string, int> _ids;
public:
    std::vector<Site*> sites;
    std::vector<Site*>* getMissionSites() { return &sites; }
    void increaseCustomCounter(const std::string& name);
    void decreaseCustomCounter(const std::string& name);
    void setCounter(const std::string& name, int value) { _ids[name] = value; }
    int counter(const std::string& name) const {
        auto found = _ids.find(name);
        return found == _ids.end() ? -1 : found->second;
    }
};
// COUNTER_METHODS

struct Rule {
    bool force;
    bool despawnEvenIfTargeted() const { return force; }
};
struct Deployment {
    int points, penalty;
    int getPoints() const { return points; }
    int getDespawnPenalty() const { return penalty; }
};
struct Craft {
    bool returned = false;
    void returnToBase() { returned = true; }
};
struct Site {
    int remaining;
    Rule* rule;
    Deployment* deployment;
    std::vector<Craft*> followers;
    bool detected = false;
    bool getDetected() const { return detected; }
    void setDetected(bool value) { detected = value; }
    int getSecondsRemaining() const { return remaining; }
    void setSecondsRemaining(int value) { remaining = value; }
    Rule* getRules() const { return rule; }
    Deployment* getDeployment() const { return deployment; }
    std::vector<Craft*>* getFollowers() { return &followers; }
    std::vector<Craft*> getCraftFollowers() { return followers; }
    std::string getName(const std::string&) const { return "Site"; }
};
struct Game {
    SavedGame saved;
    SavedGame* getSavedGame() { return &saved; }
    std::string getLanguage() const { return "en-US"; }
};
struct UfoLostState {
    explicit UfoLostState(const std::string&) {}
};
struct MissionDetectedState {
    MissionDetectedState(Site*, void*) {}
};
class GeoscapeState {
    Game* _game;
public:
    explicit GeoscapeState(Game* game) : _game(game) {}
    int popups = 0;
    void popup(UfoLostState* state) { ++popups; delete state; }
    void popup(MissionDetectedState* state) { ++popups; delete state; }
    void hour() {
        // DISCOVERY
    }
    std::pair<bool, int> run(Site* site) {
        // TIMER_AND_FOLLOWERS
        // SCORE
        return {removeSite, score};
    }
};
struct Case {
    int seconds;
    bool follower, force;
    int points, penalty;
};
int main() {
    std::vector<Case> cases = {
        {3600, false, false, 4, 9},
        {1800, false, false, 4, 9},
        {1799, false, false, 4, 9},
        {0, true, false, 4, 9},
        {0, true, true, 4, 9}
    };
    std::cout << std::boolalpha << "{\"rows\":[";
    bool first = true;
    for (const auto& input : cases) {
        if (!first) std::cout << ',';
        first = false;
        Rule rule{input.force};
        Deployment deployment{input.points, input.penalty};
        Craft craft;
        Site site{input.seconds, &rule, &deployment};
        if (input.follower) site.followers.push_back(&craft);
        Game game;
        GeoscapeState geoscape(&game);
        auto [removed, score] = geoscape.run(&site);
        std::cout << '[' << input.seconds << ',' << input.follower << ',' << input.force << ','
                  << input.points << ',' << input.penalty << ',' << removed << ',' << site.remaining
                  << ',' << score << ',' << craft.returned << ',' << geoscape.popups << ']';
    }
    std::cout << "],\"counterRows\":[";
    struct CounterCase { const char* name; int start; const char* operations; };
    std::vector<CounterCase> counterCases = {
        {"site", -1, "+"}, {"site", -1, "-"}, {"site", 1, "-"},
        {"site", 3, "++-"}, {"site", 2, "--"}, {"", -1, "+-"}
    };
    first = true;
    for (const auto& input : counterCases) {
        if (!first) std::cout << ',';
        first = false;
        SavedGame game;
        if (input.start >= 0) game.setCounter(input.name, input.start);
        for (const char* op = input.operations; *op; ++op) {
            if (*op == '+') game.increaseCustomCounter(input.name);
            else game.decreaseCustomCounter(input.name);
        }
        std::cout << "[\"" << input.name << "\"," << input.start << ",\""
                  << input.operations << "\"," << game.counter(input.name) << ']';
    }
    std::cout << "],\"discoveryRows\":[";
    std::vector<std::vector<std::pair<bool, int>>> discoveries = {
        {}, {{true, 3600}, {true, 7200}},
        {{false, 0}, {false, 1}, {false, 7200}},
        {{true, 3600}, {false, 1800}, {false, 2147480047}},
        {{false, 2147483647}, {true, 3600}}
    };
    first = true;
    for (const auto& input : discoveries) {
        if (!first) std::cout << ',';
        first = false;
        Game game;
        std::vector<Site> sites;
        for (const auto& [detected, seconds] : input) {
            sites.push_back(Site{seconds, nullptr, nullptr});
            sites.back().detected = detected;
        }
        for (auto& site : sites) game.saved.sites.push_back(&site);
        GeoscapeState geoscape(&game);
        std::cout << "{\"before\":[";
        bool rowFirst = true;
        for (const auto& [detected, seconds] : input) {
            if (!rowFirst) std::cout << ',';
            rowFirst = false;
            std::cout << '[' << detected << ',' << seconds << ']';
        }
        geoscape.hour();
        std::cout << "],\"after\":[";
        rowFirst = true;
        for (const auto& site : sites) {
            if (!rowFirst) std::cout << ',';
            rowFirst = false;
            std::cout << '[' << site.detected << ',' << site.remaining << ']';
        }
        std::cout << "],\"popups\":" << geoscape.popups << '}';
    }
    std::cout << "]}\n";
}

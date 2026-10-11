// Extracted SavedGame::spawnEvent and GeoscapeEvent::think, pinned by the capture script.
// Collaborators replace rule loading, logging and RNG; event payload execution is excluded.
#include <cassert>
#include <iostream>
#include <map>
#include <sstream>
#include <string>
#include <vector>
#include <set>
namespace Options { int oxceGeoscapeDebugLogMaxEntries = 0; }
namespace RNG {
int choice = 0;
int calls = 0;
int generate(int minimum, int maximum) { ++calls; assert(choice >= minimum && choice <= maximum); return choice; }
}
struct RuleEvent {
    std::string name; int timer, random;
    std::string interrupt;
    const std::string& getInterruptResearch() const { return interrupt; }
    const std::string& getName() const { return name; }
    int getTimer() const { return timer; }
    int getTimerRandom() const { return random; }
};
struct GeoscapeEvent {
    const RuleEvent& rule; int _spawnCountdown = 0; bool _over = false;
    GeoscapeEvent(const RuleEvent& value) : rule(value) {}
    void setSpawnCountdown(size_t minutes) { _spawnCountdown = (int)minutes; }
    const RuleEvent& getRules() const { return rule; }
    void think();
};
struct GameTime { std::string getFullString() const { return ""; } };
struct SavedGame {
    std::vector<GeoscapeEvent*> _geoscapeEvents;
    std::map<std::string, int> _generatedEvents;
    std::vector<std::string> _geoscapeDebugLog;
    GameTime time; GameTime* _time = &time;
    void addGeneratedEvent(const RuleEvent* event);
    bool spawnEvent(const RuleEvent* rule);
    std::set<std::string> research;
    bool isResearched(const std::string& id, bool) const { return research.count(id) != 0; }
    bool canSpawnInstantEvent(const RuleEvent* rule);
    ~SavedGame() { for (auto* event : _geoscapeEvents) delete event; }
};
// ADD_GENERATED_EVENT
// SPAWN_EVENT
// THINK_EVENT
// INSTANT_EVENT
struct WeightedOptions {
    size_t _totalWeight = 0;
    std::map<std::string, size_t> _choices;
    std::string choose() const;
};
// WEIGHTED_CHOICE
int main() {
    struct Row { int timer, random, choice; };
    const Row rows[] = {{30,0,0},{89,0,0},{90,0,0},{31,90,89},{-60,0,0},{120,60,60}};
    std::cout << "{\"rows\":[";
    bool first = true;
    for (auto row : rows) {
        if (!first) std::cout << ','; first = false;
        SavedGame game; RuleEvent rule{"EVENT",row.timer,row.random}; RNG::choice = row.choice;
        game.spawnEvent(&rule); game.spawnEvent(&rule);
        auto* event = game._geoscapeEvents.front();
        std::cout << "{\"timer\":" << row.timer << ",\"random\":" << row.random
            << ",\"choice\":" << row.choice << ",\"generated\":" << game._generatedEvents["EVENT"]
            << ",\"countdowns\":[" << event->_spawnCountdown;
        while (!event->_over) { event->think(); std::cout << ',' << event->_spawnCountdown; }
        event->think(); // Finished events must not change on a repeated think.
        std::cout << "],\"over\":" << (event->_over ? "true" : "false") << '}';
    }
    std::cout << "],\"instantRows\":[";
    first = true;
    for (bool defined : {false, true}) for (bool interrupted : {false, true}) {
        if (!first) std::cout << ','; first = false;
        SavedGame game; RuleEvent rule{"INSTANT", 30, 0, "STOP"};
        if (interrupted) game.research.insert("STOP");
        bool canSpawn = game.canSpawnInstantEvent(defined ? &rule : nullptr);
        std::cout << '[' << (defined ? "true" : "false") << ',' << (interrupted ? "true" : "false") << ','
            << (canSpawn ? "true" : "false") << ',' << game._generatedEvents["INSTANT"] << ']';
    }
    std::cout << "],\"despawnChoices\":[";
    first = true;
    for (const std::string name : {"empty", "zero", "missing", "interrupted", "active", "mixed"}) {
        if (!first) std::cout << ','; first = false;
        WeightedOptions options;
        if (name == "missing") options._choices = {{"MISSING", 1}};
        if (name == "interrupted" || name == "active") options._choices = {{"INSTANT", 1}};
        if (name == "mixed") options._choices = {{"INSTANT", 1}, {"MISSING", 1}};
        for (const auto& pair : options._choices) options._totalWeight += pair.second;
        RNG::calls = 0; RNG::choice = 1;
        auto selected = options.choose();
        std::cout << "[\"" << name << "\",\"" << selected << "\"," << RNG::calls << ']';
    }
    std::cout << "]}\n";
}

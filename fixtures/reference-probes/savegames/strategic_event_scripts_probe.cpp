// Exact reference event selection loop; controlled rules, history and RNG collaborators.
#include <iostream>
#include <map>
#include <string>
#include <vector>
#include <stdexcept>
namespace RNG { int calls; int generate(int, int) { ++calls; return 1; } }
struct WeightedOptions {
    std::map<std::string, size_t> _choices; size_t _totalWeight = 0;
    std::string choose() const;
    std::vector<std::string> getNames();
    void set(const std::string& id, size_t weight);
    bool empty() const { return _choices.empty(); }
};
struct RuleEvent { std::string id; };
struct RuleEventScript {
    std::vector<std::string> sequential;
    WeightedOptions random;
    std::vector<std::pair<size_t, WeightedOptions*>> _eventWeights;
    const std::vector<std::string>& getOneTimeSequentialEvents() { return sequential; }
    const WeightedOptions& getOneTimeRandomEvents() { return random; }
    std::string generate(size_t monthsPassed) const;
};
struct SavedGame {
    std::map<std::string, int> _generatedEvents;
    std::vector<std::string> generated;
    bool wasEventGenerated(const std::string& eventName);
    int getMonthsPassed() { return 0; }
    void spawnEvent(const RuleEvent* rule) { ++_generatedEvents[rule->id]; generated.push_back(rule->id); }
};
struct Mod {
    std::map<std::string, RuleEvent> events = {{"A", {"A"}}, {"B", {"B"}}, {"C", {"C"}}};
    const RuleEvent* getEvent(const std::string& id, bool required) {
        auto found = events.find(id);
        if (found != events.end()) return &found->second;
        if (required) throw std::runtime_error("missing event");
        return nullptr;
    }
};
// METHODS
void run(std::vector<RuleEventScript*>& relevantEventScripts, SavedGame* save, Mod* mod) {
// EVENT_LOOP
}
int main() {
    WeightedOptions repeat, absent;
    repeat.set("A", 1); absent.set("MISSING", 1);
    RuleEventScript first, second;
    first.sequential = {"A", "A", "B"}; first.random.set("A", 1); first.random.set("B", 1);
    first._eventWeights = {{0, &repeat}};
    second.sequential = {"A", "C"}; second.random = first.random; second._eventWeights = {{0, &absent}};
    std::vector<RuleEventScript*> scripts = {&first, &second}; SavedGame save; Mod mod;
    std::cout << "{\"rows\":[";
    RuleEventScript tail;
    tail.sequential = {"A", "A", "MISSING"}; tail.random.set("A", 1); tail._eventWeights = {{0, &repeat}};
    for (int round = 0; round < 4; ++round) {
        if (round) std::cout << ',';
        if (round == 2) { save._generatedEvents.clear(); save._generatedEvents["A"] = 0; }
        if (round == 3) { save._generatedEvents.clear(); scripts = {&tail}; }
        save.generated.clear(); RNG::calls = 0; run(scripts, &save, &mod);
        std::cout << "{\"events\":[";
        for (size_t i = 0; i < save.generated.size(); ++i) {
            if (i) std::cout << ',';
            std::cout << '\"' << save.generated[i] << '\"';
        }
        std::cout << "],\"draws\":" << RNG::calls << '}';
    }
    std::cout << "]}\n";
}

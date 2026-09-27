// Reference methods inserted verbatim; rule storage and RNG are controlled collaborators.
#include <iostream>
#include <map>
#include <set>
#include <string>
#include <vector>
namespace RNG { int roll, calls; int generate(int, int) { ++calls; return roll; } }
struct WeightedOptions {
    std::map<std::string, size_t> _choices; size_t _totalWeight = 0;
    std::string choose() const;
    std::vector<std::string> getNames();
    void set(const std::string& id, size_t weight);
};
enum GenerationType { GEN_REGION, GEN_MISSION, GEN_RACE };
struct RuleMissionScript {
    std::vector<std::pair<size_t, WeightedOptions*>> _missionWeights, _regionWeights, _raceWeights;
    std::string generate(size_t monthsPassed, GenerationType type) const;
    std::vector<std::string> getMissionTypes(int month) const;
    std::vector<std::string> getRegions(int month) const;
    std::set<std::string> getAllMissionTypes() const;
};
struct RuleEventScript {
    std::vector<std::pair<size_t, WeightedOptions*>> _eventWeights;
    std::string generate(size_t monthsPassed) const;
};
// METHODS
int main() {
    WeightedOptions first, late, appended;
    first.set("M_B", 3); first.set("M_A", 2); first.set("REMOVED", 0);
    late.set("M_C", 1); appended.set("M_B", 1);
    RuleMissionScript mission;
    mission._missionWeights = {{0, &first}, {6, &late}, {2, &appended}};
    mission._regionWeights = mission._missionWeights; mission._raceWeights = mission._missionWeights;
    RuleEventScript event{mission._missionWeights};
    std::cout << "{\"rows\":[";
    bool comma = false;
    for (int month : {0, 1, 2, 6, 12}) for (int roll : {1, 2, 3, 5}) {
        if (month >= 2 && roll != 1) continue;
        if (comma) std::cout << ','; comma = true;
        RNG::roll = roll; RNG::calls = 0;
        auto m = mission.generate(month, GEN_MISSION);
        auto e = event.generate(month);
        std::cout << '[' << month << ',' << roll << ",\"" << m << "\",\"" << e << "\"," << RNG::calls << ']';
    }
    std::cout << "],\"beforeFirst\":[";
    auto names = mission.getMissionTypes(-1);
    for (size_t i = 0; i < names.size(); ++i) {
        if (i) std::cout << ',';
        std::cout << '\"' << names[i] << '\"';
    }
    RuleEventScript empty;
    RNG::calls = 0;
    std::cout << "],\"emptyEvent\":\"" << empty.generate(0) << "\",\"emptyCalls\":" << RNG::calls << "}\n";
}

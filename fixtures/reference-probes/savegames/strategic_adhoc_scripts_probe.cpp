// GeoscapeState tag-filter block is inserted verbatim; rules are controlled collaborators.
#include <iostream>
#include <string>
#include <vector>

struct RuleEvent {
    std::vector<std::string> tags;
    const std::vector<std::string>& getAdhocMissionScriptTags() const { return tags; }
};
struct RuleMissionScript {
    std::string id;
    std::vector<std::string> tags;
    const std::vector<std::string>& getAdhocMissionScriptTags() const { return tags; }
};

int main() {
    std::vector<RuleMissionScript> scripts = {
        {"SECOND_FIRST", {"second", "other"}},
        {"FOLLOW_SECOND", {"second"}},
        {"OTHER_FIRST", {"other", "second"}},
        {"BAD_SITE", {"bad"}},
        {"THRESHOLD", {"threshold"}},
        {"UNTAGGED", {}}
    };
    std::vector<std::pair<std::string, RuleEvent>> cases = {
        {"SECOND", {{"second"}}},
        {"OTHER", {{"other"}}},
        {"BOTH", {{"other", "second"}}},
        {"BAD", {{"bad"}}},
        {"THRESHOLD", {{"threshold"}}},
        {"EMPTY", {{}}}
    };
    std::cout << "{\"rows\":[";
    bool firstCase = true;
    for (auto& row : cases) {
        if (!firstCase) std::cout << ',';
        firstCase = false;
        const RuleEvent* eventRules = &row.second;
        bool isNewMonth = false;
        std::vector<std::string> selected;
        for (auto& rule : scripts) {
            RuleMissionScript* command = &rule;
            // TAG_FILTER
            selected.push_back(command->id);
        }
        std::cout << "{\"event\":\"" << row.first << "\",\"scripts\":[";
        for (size_t index = 0; index < selected.size(); ++index) {
            if (index) std::cout << ',';
            std::cout << '\"' << selected[index] << '\"';
        }
        std::cout << "]}";
    }
    std::cout << "]}\n";
}

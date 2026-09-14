#include <iostream>
#include <string>
namespace OpenXcom {
const float PROGRESS_LIMIT_UNKNOWN = 0.333f;
const float PROGRESS_LIMIT_POOR = 0.07f;
const float PROGRESS_LIMIT_AVERAGE = 0.13f;
const float PROGRESS_LIMIT_GOOD = 0.25f;
class RuleResearch { public: explicit RuleResearch(int cost) : _cost(cost) {} int getCost() const { return _cost; } private: int _cost; };
class ResearchProject {
public:
    ResearchProject(RuleResearch* project, int cost) : _project(project), _assigned(0), _spent(0), _cost(cost) {}
    bool step(); bool isFinished();
    int getAssigned() const { return _assigned; } int getSpent() const { return _spent; }
    int getCost() const { return _cost; } const RuleResearch* getRules() const { return _project; }
    std::string getResearchProgress() const; void setAssigned(int value) { _assigned = value; }
private: RuleResearch* _project; int _assigned; int _spent; int _cost;
};
class RuleManufacture { public: explicit RuleManufacture(int time) : _time(time) {} int getManufactureTime() const { return _time; } private: int _time; };
class Production {
public:
    Production(const RuleManufacture* rules, int amount) : _rules(rules), _amount(amount), _timeSpent(0) {}
    int getAmountProduced() const; int getTimeSpent() const { return _timeSpent; }
    void setTimeSpent(int value) { _timeSpent = value; }
private: const RuleManufacture* _rules; int _amount; int _timeSpent;
};
// RESEARCH_STEP
// RESEARCH_FINISHED
// RESEARCH_PROGRESS
// PRODUCTION_AMOUNT
}
int main() {
    using namespace OpenXcom;
    RuleResearch researchRule(10); ResearchProject research(&researchRule, 8); research.setAssigned(3);
    bool first = research.step(); std::string progress = research.getResearchProgress();
    bool second = research.step(); bool third = research.step();
    RuleManufacture manufactureRule(4); Production production(&manufactureRule, 3); production.setTimeSpent(11);
    std::cout << R"({"schemaVersion":1,"referenceCommit":"4df3a5e571a1a4b5e8a46d3161fb2e21a2adba15","referenceBuild":{"compiler":"MSVC","languageStandard":"c++20"},"mods":[],"research":[)"
              << first << ",\"" << progress << "\"," << second << "," << third << "],\"production\":["
              << production.getAmountProduced() << "]}\n";
}

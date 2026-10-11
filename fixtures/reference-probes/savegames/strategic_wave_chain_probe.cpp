// Extracted AlienMission::think countdown, wave counters and recursive scheduling.
// Object creation is a counted collaborator; objectives have independent lifecycle fixtures.
#include <algorithm>
#include <iostream>
#include <vector>
namespace RNG {
int calls = 0;
size_t generate(int minimum, size_t maximum) { ++calls; return minimum; }
}
struct Wave { size_t ufoCount, spawnTimer; };
struct Rule {
    std::vector<Wave> waves;
    size_t getWaveCount() const { return waves.size(); }
    const Wave& getWave(size_t index) const { return waves.at(index); }
};
struct Mission {
    Rule _rule;
    size_t _nextWave = 0, _nextUfoCounter = 0, _spawnCountdown = 0;
    int spawns = 0;
    void think(int& engine, int& globe) {
        // COUNTDOWN
        const Wave& wave = _rule.getWave(_nextWave);
        ++spawns;
        // COUNTERS
        // FOLLOWUP
    }
};
void run(const char* name, std::vector<Wave> waves, size_t counter, size_t countdown) {
    RNG::calls = 0; Mission mission; mission._rule.waves = waves;
    mission._nextUfoCounter = counter; mission._spawnCountdown = countdown;
    int engine = 0, globe = 0;
    std::cout << "{\"name\":\"" << name << "\",\"rows\":[";
    for (int tick=0; tick<3; ++tick) {
        mission.think(engine, globe);
        if (tick) std::cout << ',';
        std::cout << '[' << mission._nextWave << ',' << mission._nextUfoCounter << ','
            << mission._spawnCountdown << ',' << mission.spawns << ',' << RNG::calls << ']';
    }
    std::cout << "]}";
}
constexpr int OBJECTIVE_INFILTRATION=1, OBJECTIVE_BASE=2;
struct ObjectiveRule { int objective; int getObjective()const{return objective;} size_t getWaveCount()const{return 2;} };
bool completionRequired(int objective,size_t _nextWave,bool landing) {
 ObjectiveRule _rule{objective}; struct {bool objectiveOnTheLandingSite;} wave{landing};
 // INFILTRATION_CONDITION
 // BASE_CONDITION
 return false;
}
constexpr int OBJECTIVE_SITE=3;
bool siteRequired(bool deployment,bool marker,int objective,bool objectiveFlag){
 struct Deployment { bool marker;std::string getMarkerName()const{return marker?"SITE":"";} }value{marker};
 struct Mod {Deployment* deployment;Deployment* getDeployment(const std::string&)const{return deployment;}
  const void* getUfo(const std::string&)const{return nullptr;}}mod{deployment?&value:nullptr};
 struct {std::string ufoType;bool objective;}wave{"MISSING_UFO",objectiveFlag};ObjectiveRule _rule{objective};
 // DIRECT_SITE_CONDITION
 return false;
}
int main() {
    std::cout << "{\"cases\":[";
    run("mixed", {{2,90},{3,0},{1,1}}, 1, 0); std::cout << ',';
    run("zero", {{2,0},{3,0}}, 0, 0); std::cout << ',';
    run("rounded", {{3,29}}, 0, 0); std::cout << ',';
    run("empty-count", {{0,0},{0,0}}, 0, 0); std::cout << ',';
    run("pending", {{1,90}}, 0, 90);
    std::cout << "],\"completionRows\":[";bool first=true;
    for(int objective=0;objective<=6;objective++)for(bool last:{false,true})for(bool landing:{false,true}) {
        if(!first)std::cout<<',';first=false;
        std::cout<<'['<<objective<<','<<last<<','<<landing<<','<<completionRequired(objective,last?2:1,landing)<<']';
    }std::cout<<"],\"siteRows\":[";first=true;
    for(bool deployment:{false,true})for(bool marker:{false,true})for(int objective=0;objective<=6;objective++)for(bool objectiveFlag:{false,true}){
     if(!first)std::cout<<',';first=false;
     std::cout<<'['<<deployment<<','<<marker<<','<<objective<<','<<objectiveFlag<<','<<siteRequired(deployment,marker,objective,objectiveFlag)<<']';
    }std::cout<<"]}\n";
}

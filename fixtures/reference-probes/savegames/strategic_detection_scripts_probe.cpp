// Base::detect and Craft::detect are inserted verbatim from the pinned reference.
// The Worker records caller inputs and can substitute its outputs, as a script would.
#include <algorithm>
#include <iostream>
#include <vector>
namespace RNG {
std::vector<int> calls;
bool succeeds = false;
bool percent(int chance) { calls.push_back(chance); return chance >= 100 || (chance > 0 && succeeds); }
}
int XcomDistance(double value) { return (int)value; }
namespace OpenXcom {
enum UfoDetection { DETECTION_NONE=0, DETECTION_RADAR=1, DETECTION_HYPERWAVE=3 };
struct SavedGame {};
struct RuleUfo { template<class T> int getScript() const { return 0; } };
struct Ufo {
int visibility=0;
RuleUfo rule;
int getVisibility() const { return visibility; }
const RuleUfo* getRules() const { return &rule; }
};
struct Craft;
struct RuleFacility {
int range,chance; bool hyper;
int getRadarRange() const { return range; }
int getRadarChance() const { return chance; }
bool isHyperwave() const { return hyper; }
};
struct Facility {
RuleFacility rule; int build=0;
int getBuildTime() const { return build; }
const RuleFacility* getRules() const { return &rule; }
};
namespace ModScript {
struct Output {
int type,chance;
int getFirst() const { return type; }
int getSecond() const { return chance; }
};
std::vector<int> inputs, initial;
bool overrideOutputs=false;
struct DetectUfoFromBase {
using Output=ModScript::Output;
struct Worker {
const Ufo* ufo; const SavedGame* save;
int distance; bool tracked; int radar,range,hyper,hyperRange;
void execute(int, Output& output) const {
inputs={distance,(int)tracked,radar,range,hyper,hyperRange};
initial={output.type,output.chance};
if (overrideOutputs) output={3,100};
}
};
};
struct DetectUfoFromCraft {
using Output=ModScript::Output;
struct Worker {
const Ufo* ufo; const SavedGame* save; const Craft* craft;
int distance; bool tracked; int radar,range;
void execute(int, Output& output) const {
inputs={distance,(int)tracked,radar,range};
initial={output.type,output.chance};
if (overrideOutputs) output={3,100};
}
};
};
}
struct Base {
double distance=0;
std::vector<Facility*> _facilities;
double getDistance(const Ufo*) const { return distance; }
UfoDetection detect(const Ufo*,const SavedGame*,bool) const;
};
struct Craft {
double distance=0;
struct { int radarRange=0,radarChance=0; } _stats;
double getDistance(const Ufo*) const { return distance; }
UfoDetection detect(const Ufo*,const SavedGame*,bool) const;
};
// METHODS
}
void array(const std::vector<int>& values) {
std::cout << '[';
for (size_t i=0;i<values.size();++i) { if(i) std::cout<<','; std::cout<<values[i]; }
std::cout<<']';
}
int main() {
using namespace OpenXcom;
SavedGame save; Ufo ufo; ufo.visibility=-30;
Facility ordinary{{100,40,false}}, far{{80,70,false}}, hyper{{150,50,true}}, farHyper{{90,90,true}}, unfinished{{999,100,true},1};
Base base; base.distance=100; base._facilities={&ordinary,&far,&hyper,&farHyper,&unfinished};
Craft craft; craft.distance=100; craft._stats={100,75};
std::cout<<"{\"base\":[";
for(int i=0;i<4;++i) {
if(i) std::cout<<',';
RNG::calls.clear(); RNG::succeeds=i==1; ModScript::overrideOutputs=i==3;
int result=base.detect(&ufo,&save,i==2);
std::cout<<"{\"tracked\":"<<(i==2?"true":"false")<<",\"hyperSucceeds\":"<<(i==1?"true":"false")<<",\"override\":"<<(i==3?"true":"false")<<",\"inputs\":"; array(ModScript::inputs);
std::cout<<",\"initial\":";array(ModScript::initial);std::cout<<",\"calls\":";array(RNG::calls);std::cout<<",\"result\":"<<result<<'}';
}
std::cout<<"],\"craft\":[";
for(int i=0;i<4;++i) {
if(i) std::cout<<',';
craft.distance=i==0||i==3?100:99; RNG::calls.clear();RNG::succeeds=false;ModScript::overrideOutputs=i==3;
int result=craft.detect(&ufo,&save,i==2);
std::cout<<"{\"tracked\":"<<(i==2?"true":"false")<<",\"override\":"<<(i==3?"true":"false")<<",\"inputs\":";array(ModScript::inputs);
std::cout<<",\"initial\":";array(ModScript::initial);std::cout<<",\"calls\":";array(RNG::calls);std::cout<<",\"result\":"<<result<<'}';
}
std::cout<<"]}\n";
}

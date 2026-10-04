// Base::detect and Craft::detect are inserted verbatim from the pinned reference.
// The Worker records caller inputs and can substitute its outputs, as a script would.
#include <algorithm>
#include <iostream>
#include <vector>
#include <map>
#include <string>
#include <stdexcept>
#include <cmath>
#include <climits>
namespace RNG {
std::vector<int> calls;
bool succeeds = false;
bool percent(int chance) { calls.push_back(chance); return chance >= 100 || (chance > 0 && succeeds); }
}
int XcomDistance(double value) { return (int)value; }
namespace OpenXcom {
using Exception = std::runtime_error;
enum UfoDetection { DETECTION_NONE=0, DETECTION_RADAR=1, DETECTION_HYPERWAVE=3 };
struct RuleResearch {};
bool researchLess(const RuleResearch* a, const RuleResearch* b) { return std::less<const RuleResearch*>{}(a,b); }
// RESEARCH_FIND
struct SavedGame {
bool _debug=false;
std::vector<const RuleResearch*> _discovered;
bool isResearched(const RuleResearch*,bool considerDebugMode) const;
};
// RESEARCH_METHOD
// RESEARCH_WRAPPER
struct Mod {
std::map<std::string,RuleResearch*> _research;
static const std::string STR_NULL;
// MOD_EMPTY
template<typename T> T* getRule(const std::string&,const std::string&,const std::map<std::string,T*>&,bool) const;
RuleResearch* getResearch(const std::string&,bool error=false) const;
};
const std::string Mod::STR_NULL={ '\0' };
// MOD_METHODS
// RESEARCH_LOOKUP
class ScriptValuesBase {
std::vector<int> values;
public:
void setBase(size_t t, int i);
int getBase(size_t t) const;
};
// TAG_METHODS
struct RuleUfo { template<class T> int getScript() const { return 0; } };
struct Ufo {
int visibility=0;
int _damage=14,_shield=3,_huntMode=2,_huntBehavior=1;
bool _detected=true,_hyperDetected=false,_isHunterKiller=true,_isHunting=false,_isEscorting=true;
struct Stats { int shieldCapacity=10,damageMax=50; } _stats;
const Stats& getCraftStats() const { return _stats; }
int getDamage() const;
int getDamagePercentage() const;
int getShield() const;
int getShieldPercentage() const;
bool getDetected() const;
bool getHyperDetected() const;
bool isHunterKiller() const;
bool isHunting() const;
bool isEscorting() const;
int getHuntMode() const;
int getHuntBehavior() const;
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
// UFO_METHODS
// DAMAGE_WRAPPER
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
std::cout<<"],\"bindings\":["; array({ufo.getDamage(),ufo.getShield(),ufo.getShieldPercentage(),(int)ufo.getDetected(),(int)ufo.getHyperDetected(),(int)ufo.isHunterKiller(),(int)ufo.isHunting(),(int)ufo.isEscorting(),ufo.getHuntMode(),ufo.getHuntBehavior()}); std::cout<<"],\"tagValues\":[";
ScriptValuesBase tags; tags.setBase(0,99); tags.setBase(2,-7);
for (int index : {0,1,2,3,400}) { if(index) std::cout<<','; array({index,tags.getBase(index)}); }
std::cout<<"],\"research\":[";
RuleResearch topic; Mod mod; mod._research["TOPIC"]=&topic;
for(int i=0;i<8;++i) {
if(i)std::cout<<',';
save._debug=(i&2)!=0; save._discovered=(i&1)?std::vector<const RuleResearch*>{&topic}:std::vector<const RuleResearch*>{};
const RuleResearch* lookedUp=nullptr;
getRuleResearch((i&4)?nullptr:&mod,lookedUp,"TOPIC");
int found=-1;isResearchedScript(&save,found,lookedUp);
array({i&1,(i&2)?1:0,(i&4)?0:1,found});
}
const RuleResearch* missing=&topic;getRuleResearch(&mod,missing,"MISSING");
std::cout<<"],\"missingResearch\":"<<(missing?1:0)<<",\"emptyResearch\":[";
mod._research["0"]=&topic;
bool firstEmpty=true;
for(const std::string& id: {std::string(),std::string(1,'\0'),std::string("0")}) {
if(!firstEmpty)std::cout<<',';firstEmpty=false;
getRuleResearch(&mod,missing,id);std::cout<<(missing?1:0);
}
std::cout<<"],\"damage\":[";
bool firstDamage=true;
for(auto row: std::vector<std::pair<int,int>>{{0,50},{14,50},{99,67},{INT_MAX,100},{14,-33}}) {
if(!firstDamage)std::cout<<',';firstDamage=false;
ufo._damage=row.first;ufo._stats.damageMax=row.second;
int maximum=0;getDamageMaxScript(&ufo,maximum);
array({row.first,maximum,ufo.getDamagePercentage()});
}
std::cout<<"]}\n";
}

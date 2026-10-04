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
enum BattleType { BT_NONE=0 };
struct ScriptText { std::string value; static const ScriptText empty; ScriptText()=default; ScriptText(const char* s):value(s){} };
const ScriptText ScriptText::empty;
struct RuleItem {
std::string _type="ITEM_METADATA"; int _weight=-7; BattleType _battleType=(BattleType)3;
const std::string& getType() const { return _type; }
int getWeight() const;
BattleType getBattleType() const;
};
struct Armor {
static const std::string NONE; std::string _type="ARMOR_METADATA"; int _size=2;
const std::string& getType() const { return _type; }
int getSize() const;
};
const std::string Armor::NONE="STR_NONE";
struct RuleSoldier {};
struct RuleSkill { std::string _type="SKILL_METADATA"; const std::string& getType() const { return _type; } };
struct RuleCraft { std::string _type="CRAFT_METADATA"; const std::string& getType() const { return _type; } };
// RULE_METADATA
struct RuleResearch {};
bool researchLess(const RuleResearch* a, const RuleResearch* b) { return std::less<const RuleResearch*>{}(a,b); }
// RESEARCH_FIND
struct RuleCountry {
int _fundingCap=123;
int getFundingCap() const;
};
struct Country {
RuleCountry* _rules=nullptr;
const RuleCountry* getRules() const;
std::vector<int> _funding={10,2000},_activityAlien={1,7},_activityXcom={2,9};
bool _pact=false;
int getCurrentFunding() const;
int getCurrentActivityAlien() const;
int getCurrentActivityXcom() const;
bool getPact() const;
};
// COUNTRY_METHODS
// COUNTRY_RULE_METHODS
struct SavedGame {
std::vector<Country*> _countries;
bool _debug=false;
std::vector<const RuleResearch*> _discovered;
bool isResearched(const RuleResearch*,bool considerDebugMode) const;
};
enum RetEnum { RetContinue };
struct ScriptArgSeparator {};
template<auto X> struct BindMemberInvoke {
template<typename T> static auto& f(T* t) { return t->*X; }
};
template<typename T,auto Func,auto... X> struct BindListInitImpl;
template<typename T,auto Func,auto... X> struct BindListLoopImpl;
// COUNTRY_FILTER
// COUNTRY_LIST
// RESEARCH_METHOD
// RESEARCH_WRAPPER
struct Mod {
int _maxViewDistance=20,_maxDarknessToSeeUnits=9;
int getMaxViewDistance() const;
int getMaxDarknessToSeeUnits() const;
std::map<std::string,RuleResearch*> _research;
std::map<std::string,RuleItem*> _items;
std::map<std::string,Armor*> _armors;
std::map<std::string,RuleSoldier*> _soldiers;
std::map<std::string,RuleSkill*> _skills;
static const std::string STR_NULL;
// MOD_EMPTY
template<typename T> T* getRule(const std::string&,const std::string&,const std::map<std::string,T*>&,bool) const;
RuleResearch* getResearch(const std::string&,bool error=false) const;
RuleItem* getItem(const std::string&,bool error=false) const;
Armor* getArmor(const std::string&,bool error=false) const;
RuleSoldier* getSoldier(const std::string&,bool error=false) const;
RuleSkill* getSkill(const std::string&,bool error=false) const;
};
const std::string Mod::STR_NULL={ '\0' };
// MOD_SETTINGS
// MOD_METHODS
// RESEARCH_LOOKUP
// RULE_LOOKUPS
class ScriptValuesBase {
std::vector<int> values;
public:
void setBase(size_t t, int i);
int getBase(size_t t) const;
};
// TAG_METHODS
struct RuleCraftStats {
int fuelMax=0,damageMax=0,speedMax=0,accel=0,radarRange=0,radarChance=0,sightRange=0;
int hitBonus=0,avoidBonus=0,avoidBonus2=0,powerBonus=0,armor=0;
int shieldCapacity=0,shieldRecharge=0,shieldRechargeInGeoscape=0,shieldBleedThrough=0;
int soldiers=0,vehicles=0,maxItems=0; double maxStorageSpace=0;
// STATS_ADD
// STATS_BIND
};
struct StatsOwner { RuleCraftStats stats; };
struct StatsBinding {
StatsOwner* owner;
std::map<std::string,int> values;
template<auto Root,auto Field> void addField(const std::string& name) { values[name]=((owner->*Root).*Field); }
};
struct RuleUfo {
int _radius=-1,_range=0,_power=0,_reload=0;
std::string _size;
int getRadius() const;
int getWeaponRange() const;
int getWeaponPower() const;
int getWeaponReload() const;
template<class T> int getScript() const { return 0; }
};
// UFO_RULE_METHODS
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
std::cout<<"],\"stats\":[";
StatsOwner stats;
stats.stats.fuelMax=100;stats.stats.damageMax=50;stats.stats.speedMax=2200;stats.stats.accel=5;
stats.stats.radarRange=300;stats.stats.radarChance=20;stats.stats.sightRange=120;
stats.stats.hitBonus=1;stats.stats.avoidBonus=2;stats.stats.avoidBonus2=3;stats.stats.powerBonus=4;stats.stats.armor=5;
stats.stats.shieldCapacity=10;stats.stats.shieldRecharge=6;stats.stats.shieldBleedThrough=7;
stats.stats.soldiers=8;stats.stats.vehicles=9;stats.stats.maxItems=11;
RuleCraftStats bonus;
bonus.fuelMax=1;bonus.damageMax=5;bonus.speedMax=100;bonus.accel=2;bonus.radarRange=25;bonus.radarChance=3;bonus.sightRange=10;
bonus.hitBonus=-1;bonus.avoidBonus=1;bonus.avoidBonus2=2;bonus.powerBonus=3;bonus.armor=4;
bonus.shieldCapacity=5;bonus.shieldRecharge=1;bonus.shieldBleedThrough=2;
bonus.soldiers=3;bonus.vehicles=4;bonus.maxItems=5;
RuleCraftStats rawStats=stats.stats;
for(int stage=0;stage<2;++stage) {
if(stage) { std::cout<<',';stats.stats+=bonus; }
StatsBinding bindings{&stats};
RuleCraftStats::addGetStatsScript<&StatsOwner::stats>(bindings,"");
std::cout<<'{';bool firstStat=true;
for(const auto& [name,value]: bindings.values) {
if(!firstStat)std::cout<<',';firstStat=false;std::cout<<'\"'<<name<<"\":"<<value;
}
std::cout<<'}';
}
std::cout<<"],\"ruleLookup\":[";
RuleItem item;Armor armor;RuleSoldier soldier;RuleSkill skill;
mod._items["KNOWN"]=&item;mod._items["STR_NONE"]=&item;
mod._armors["KNOWN"]=&armor;mod._soldiers["KNOWN"]=&soldier;mod._skills["KNOWN"]=&skill;
for(int i=0;i<4;++i) {
if(i)std::cout<<',';
const auto name=(i&1)?"MISSING":"KNOWN";
const auto source=(i&2)?nullptr:&mod;
const RuleItem* gotItem=nullptr;const Armor* gotArmor=nullptr;const RuleSoldier* gotSoldier=nullptr;const RuleSkill* gotSkill=nullptr;
getItemScript(source,gotItem,name);getArmorScript(source,gotArmor,name);
getSoldierScript(source,gotSoldier,name);getSkillScript(source,gotSkill,name);
array({(i&2)?0:1,(i&1)?0:1,gotItem?1:0,gotArmor?1:0,gotSoldier?1:0,gotSkill?1:0});
}
const RuleItem* none=&item;getItemScript(&mod,none,"STR_NONE");
std::cout<<"],\"noneItem\":"<<(none?1:0)<<",\"countryList\":[";
Country countryA,countryB;countryB._pact=true;countryB._funding={3000};countryB._activityAlien={11};countryB._activityXcom={13};
save._countries={&countryB,&countryA};
using CountryInit=BindListInitImpl<decltype(&filterCountryConstScript),&filterCountryConstScript,&SavedGame::_countries>;
using CountryLoop=BindListLoopImpl<decltype(&filterCountryConstScript),&filterCountryConstScript,&SavedGame::_countries>;
int curr=-1,limit=-1;const Country* country=nullptr;
CountryInit::func(&save,{},curr,limit);
for(int i=0;i<3;++i) {
if(i)std::cout<<',';
CountryLoop::func(&save,{},curr,limit,{},country);
array({curr,limit,country?country->getCurrentFunding():0,country?country->getCurrentActivityAlien():0,
country?country->getCurrentActivityXcom():0,country?(int)country->getPact():0});
}
CountryInit::func(nullptr,{},curr,limit);CountryLoop::func(nullptr,{},curr,limit,{},country);
std::cout<<',';array({curr,limit,country?1:0});
std::cout<<"],\"countryRules\":[";
RuleCountry countryRule;countryA._rules=&countryRule;
for(int cap: {-10,0,123,INT_MAX}) {
if(cap!=-10)std::cout<<',';
countryRule._fundingCap=cap;
array({countryA.getRules()->getFundingCap(),countryA.getRules()==&countryRule?1:0});
}
std::cout<<"],\"ufoRules\":[";
RuleUfo ruleRead;
const char* sizes[]={"STR_VERY_SMALL","STR_SMALL","STR_MEDIUM_UC","STR_LARGE","STR_VERY_LARGE","UNKNOWN"};
for(int i=0;i<6;++i) {
if(i)std::cout<<',';
ruleRead._size=sizes[i];ruleRead._radius=-1;
array({i,-1,ruleRead.getRadius()});
}
for(int radius: {-2,0,17,INT_MAX}) {
std::cout<<',';ruleRead._radius=radius;
array({5,radius,ruleRead.getRadius()});
}
ruleRead._range=-7;ruleRead._power=INT_MAX;ruleRead._reload=11;
std::cout<<"],\"ufoWeapons\":";array({ruleRead.getWeaponRange(),ruleRead.getWeaponPower(),ruleRead.getWeaponReload()});
stats.stats=rawStats;
StatsBinding craftStats{&stats};
RuleCraftStats::addGetStatsScript<&StatsOwner::stats>(craftStats,"Stats.");
std::cout<<",\"craftStats\":{";bool firstCraftStat=true;
for(const auto& [name,value]: craftStats.values) {
if(!firstCraftStat)std::cout<<',';firstCraftStat=false;std::cout<<'\"'<<name<<"\":"<<value;
}
std::cout<<"},\"modSettings\":[";
array({mod.getMaxViewDistance(),mod.getMaxDarknessToSeeUnits()});
for(int value: {-7,0,31,INT_MAX}) {
mod._maxViewDistance=value;mod._maxDarknessToSeeUnits=value;
std::cout<<',';array({mod.getMaxViewDistance(),mod.getMaxDarknessToSeeUnits()});
}
std::cout<<"],\"ruleMetadata\":[";
RuleCraft craftMetadata;
ScriptText typeText;
getTypeScript(&item,typeText);std::cout<<'\"'<<typeText.value<<"\",";
getTypeScript(&armor,typeText);std::cout<<'\"'<<typeText.value<<"\",";
getTypeScript(&skill,typeText);std::cout<<'\"'<<typeText.value<<"\",";
getTypeScript(&craftMetadata,typeText);std::cout<<'\"'<<typeText.value<<"\",";
getTypeScript((const RuleItem*)nullptr,typeText);std::cout<<'\"'<<typeText.value<<"\",";
getTypeScript((const Armor*)nullptr,typeText);std::cout<<'\"'<<typeText.value<<"\",";
getTypeScript((const RuleSkill*)nullptr,typeText);std::cout<<'\"'<<typeText.value<<"\",";
getTypeScript((const RuleCraft*)nullptr,typeText);std::cout<<'\"'<<typeText.value<<"\"";
int battleType=99;getBattleTypeScript(&item,battleType);
std::cout<<"],\"ruleScalars\":";array({item.getWeight(),battleType,armor.getSize()});
getBattleTypeScript(nullptr,battleType);std::cout<<",\"nullBattleType\":"<<battleType;
std::cout<<"}\n";
}

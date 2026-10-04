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
#include <cstdint>
#include <cassert>
#include <type_traits>
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
struct Action { int accuracy=0; };
Action _confAimed,_confAuto,_confSnap,_confMelee;
int _accuracyUse=0,_accuracyMind=0,_accuracyPanic=20,_accuracyThrow=100;
int _power=0,_meleePower=0,_armor=20,_waypoints=0;
bool _twoHanded=false,_blockBothHands=false,_underwaterOnly=false;
std::vector<std::string> _categories;
int getAccuracyAimed() const;int getAccuracyAuto() const;int getAccuracySnap() const;int getAccuracyMelee() const;
int getAccuracyUse() const;int getAccuracyMind() const;int getAccuracyPanic() const;int getAccuracyThrow() const;
int getPower() const;int getMeleePower() const;int getArmor() const;int getWaypoints() const;
bool isTwoHanded() const;bool isBlockingBothHands() const;bool isWaterOnly() const;
const std::vector<std::string>& getCategories() const;
void updateCategories(std::map<std::string,std::string>*);
int getWeight() const;
BattleType getBattleType() const;
};
// ITEM_READERS
struct Armor {
static const std::string NONE; std::string _type="ARMOR_METADATA"; int _size=2;
const std::string& getType() const { return _type; }
int getSize() const;
int _visibilityAtDark=0,_visibilityAtDay=0,_camouflageAtDark=0,_camouflageAtDay=0;
int _antiCamouflageAtDark=0,_antiCamouflageAtDay=0,_visibilityThroughSmoke=0,_visibilityThroughFire=100;
int _personalLightFriend=15,_personalLightHostile=0,_personalLightNeutral=0;
int getVisibilityAtDark() const;int getVisibilityAtDay() const;
int getCamouflageAtDark() const;int getCamouflageAtDay() const;
int getAntiCamouflageAtDark() const;int getAntiCamouflageAtDay() const;
int getVisibilityThroughSmoke() const;int getVisibilityThroughFire() const;
int getPersonalLightFriend() const;int getPersonalLightHostile() const;int getPersonalLightNeutral() const;
};
// ARMOR_VISION
const std::string Armor::NONE="STR_NONE";
struct RuleSoldier {};
struct RuleSkill { std::string _type="SKILL_METADATA"; const std::string& getType() const { return _type; } };
struct RuleCraft { std::string _type="CRAFT_METADATA"; const std::string& getType() const { return _type; } };
enum InventoryType { INV_SLOT=0,INV_HAND=1,INV_GROUND=2 };
struct RuleInventory {
std::string _id;int _x,_y,_listOrder,_hand;InventoryType _type;
RuleInventory(const std::string&,int);
const std::string& getId() const;
InventoryType getType() const;
bool isRightHand() const;bool isLeftHand() const;
};
// INVENTORY_METHODS
struct Mod;
struct Unit {
std::string _type="UNIT_METADATA";const std::string& getType() const { return _type; }
std::string _armorName="KNOWN_ARMOR",_spawnUnitName,_liveAlienName={ '\0' },_civilianRecoveryTypeName;
const Armor* _armor=nullptr;const Unit* _spawnUnit=nullptr;const RuleItem* _liveAlien=nullptr;
const RuleSoldier* _civilianRecoverySoldierType=nullptr;const RuleItem* _civilianRecoveryItemType=nullptr;
std::vector<const RuleItem*> _builtInWeapons;std::vector<std::string> _builtInWeaponsNames;
void afterLoad(const Mod*);
const Unit* getSpawnUnit() const;
const RuleItem* getLiveAlienGeoscape() const;
const RuleItem* getCivilianRecoveryItemType() const;
bool isRecoverableAsEngineer() const { return _civilianRecoveryTypeName=="STR_ENGINEER"; }
bool isRecoverableAsScientist() const { return _civilianRecoveryTypeName=="STR_SCIENTIST"; }
bool isRecoverableAsCivilian() const { return !_civilianRecoveryTypeName.empty()||_civilianRecoverySoldierType||_civilianRecoveryItemType; }
};
// UNIT_METADATA
// RULE_METADATA
struct RuleResearch {};
struct UnitStats {
using Type=int16_t;using Ptr=Type UnitStats::*;
Type tu=0,stamina=0,health=0,bravery=0,reactions=0,firing=0,throwing=0,strength=0,psiStrength=0,psiSkill=0,melee=0,mana=0;
// UNIT_STAT_GET
// UNIT_STAT_BIND
};
struct UnitStatsOwner { UnitStats stats; };
struct UnitStatsBinding {
using Type=UnitStatsOwner;
UnitStatsOwner* owner;std::map<std::string,int> values;
template<auto Func> void add(const std::string& name) { int value=99;Func(owner,value);values[name]=value; }
};
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
std::map<std::string,RuleInventory*> _invs;
std::map<std::string,Unit*> _units;
std::map<std::string,RuleResearch*> _research;
std::map<std::string,RuleItem*> _items;
std::map<std::string,Armor*> _armors;
std::map<std::string,RuleSoldier*> _soldiers;
std::map<std::string,RuleSkill*> _skills;
static const std::string STR_NULL;
template<typename T> void linkRule(const T*& rule,std::string& name) const {
if constexpr(std::is_same_v<T,Unit>)rule=getUnit(name,true);
if constexpr(std::is_same_v<T,Armor>)rule=getArmor(name,true);
if constexpr(std::is_same_v<T,RuleItem>)rule=getItem(name,true);
name.clear();
}
void linkRule(std::vector<const RuleItem*>& rules,std::vector<std::string>& names) const {
for(auto& name:names){const RuleItem* item=nullptr;linkRule(item,name);rules.push_back(item);}names.clear();
}
// MOD_EMPTY
template<typename T> T* getRule(const std::string&,const std::string&,const std::map<std::string,T*>&,bool) const;
RuleInventory* getInventory(const std::string&,bool error=false) const;
RuleInventory* getInventoryRightHand() const;
RuleInventory* getInventoryLeftHand() const;
RuleInventory* getInventoryBackpack() const;
RuleInventory* getInventoryBelt() const;
RuleInventory* getInventoryGround() const;
Unit* getUnit(const std::string&,bool error=false) const;
RuleResearch* getResearch(const std::string&,bool error=false) const;
RuleItem* getItem(const std::string&,bool error=false) const;
Armor* getArmor(const std::string&,bool error=false) const;
RuleSoldier* getSoldier(const std::string&,bool error=false) const;
RuleSkill* getSkill(const std::string&,bool error=false) const;
};
const std::string Mod::STR_NULL={ '\0' };
// MOD_SMOKE
// MOD_SETTINGS
// MOD_METHODS
// RESEARCH_LOOKUP
// INVENTORY_LOOKUP
// UNIT_LINKS
// UNIT_LOOKUP
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
RuleCraftStats _stats;
int _damage=0,_shield=0;
int getDamage() const;int getDamagePercentage() const;int getDamageMax() const;
int getShield() const;int getShieldPercentage() const;int getShieldCapacity() const;
double getDistance(const Ufo*) const { return distance; }
UfoDetection detect(const Ufo*,const SavedGame*,bool) const;
};
// CRAFT_METHODS
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
Craft craft; craft.distance=100; craft._stats.radarRange=100; craft._stats.radarChance=75;
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
std::cout<<"],\"craftConditions\":[";
bool firstCraftCondition=true;
for(auto row: std::vector<std::vector<int>>{{7,50,3,10},{29,100,2,7},{14,67,0,0},{0,50,97,100}}) {
if(!firstCraftCondition)std::cout<<',';firstCraftCondition=false;
craft._damage=row[0];craft._stats.damageMax=row[1];craft._shield=row[2];craft._stats.shieldCapacity=row[3];
array({craft.getDamage(),craft.getDamageMax(),craft.getDamagePercentage(),craft.getShield(),craft.getShieldCapacity(),craft.getShieldPercentage()});
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
std::cout<<"],\"inventoryMetadata\":[";
std::vector<RuleInventory*> inventoryRules;
for(const char* id: {"STR_RIGHT_HAND","STR_LEFT_HAND","STR_BACK_PACK","STR_BELT","STR_GROUND","STR_NONE"}){
auto* inv=new RuleInventory(id,10);inv->_hand=inv->_id=="STR_RIGHT_HAND"?2:inv->_id=="STR_LEFT_HAND"?1:0;
inv->_type=inv->_id=="STR_GROUND"?INV_GROUND:inv->_hand?INV_HAND:INV_SLOT;
inventoryRules.push_back(inv);mod._invs[id]=inv;
}
for(size_t i=0;i<inventoryRules.size();++i){if(i)std::cout<<',';auto* inv=inventoryRules[i];
ScriptText id;getIdScript(inv,id);int type=-1;getTypeScript(inv,type);
std::cout<<"{\"id\":\""<<id.value<<"\",\"type\":"<<type<<",\"right\":"<<(inv->isRightHand()?"true":"false")<<",\"left\":"<<(inv->isLeftHand()?"true":"false")<<"}";
}
std::cout<<"],\"inventoryLookup\":[";
for(int i=0;i<5;++i){if(i)std::cout<<',';const char* name=i==0||i==2?"STR_RIGHT_HAND":i==1?"MISSING":i==3?"STR_NONE":"";
const RuleInventory* inv=inventoryRules[0];getInventoryScript(i==2?nullptr:&mod,inv,name);ScriptText text;getIdScript(inv,text);
std::cout<<"{\"source\":"<<(i==2?"false":"true")<<",\"name\":\""<<name<<"\",\"id\":\""<<text.value<<"\"}";
}
std::cout<<"],\"inventoryConventional\":[";
for(auto* inv: {mod.getInventoryRightHand(),mod.getInventoryLeftHand(),mod.getInventoryBackpack(),mod.getInventoryBelt(),mod.getInventoryGround()})
{if(inv!=inventoryRules[0])std::cout<<',';std::cout<<'\"'<<inv->getId()<<'\"';}
mod._invs.erase("STR_GROUND");bool missingInventoryThrows=false;try{(void)mod.getInventoryGround();}catch(const Exception&){missingInventoryThrows=true;}
std::cout<<"],\"missingInventoryThrows\":"<<(missingInventoryThrows?"true":"false");
std::cout<<",\"unitLookup\":[";
Unit unitRule, noneUnit;noneUnit._type="STR_NONE";mod._units["UNIT_METADATA"]=&unitRule;
mod._units["STR_NONE"]=&noneUnit;
for(int i=0;i<6;++i) {
if(i)std::cout<<',';
const std::string name=i==0||i==3?"UNIT_METADATA":i==1?"MISSING":i==2?"STR_NONE":i==4?"":std::string(1,'\0');
const Unit* got=&unitRule;getUnitScript(i==3?nullptr:&mod,got,name);
ScriptText txt;getTypeScript(got,txt);
std::cout<<"{\"source\":"<<(i==3?"false":"true")<<",\"name\":\""<<(i==5?"NUL":name)<<"\",\"found\":"<<(got?"true":"false")<<",\"type\":\""<<txt.value<<"\"}";
}
std::cout<<"],\"unitLinks\":[";
RuleItem unitItem,liveItem,literalNullItem,recoveryItem;RuleSoldier recoverySoldier;Armor unitArmor;
mod._items["UNIT_METADATA"]=&unitItem;unitItem._type="UNIT_METADATA";
mod._items["LIVE_ITEM"]=&liveItem;liveItem._type="LIVE_ITEM";
mod._items["STR_NULL"]=&literalNullItem;literalNullItem._type="STR_NULL";
mod._items["RECOVERY"]=&recoveryItem;recoveryItem._type="RECOVERY";
mod._items["COLLISION"]=&recoveryItem;mod._soldiers["COLLISION"]=&recoverySoldier;
mod._armors["KNOWN_ARMOR"]=&unitArmor;
std::vector<std::vector<std::string>> linkCases={
{"default","UNIT_METADATA",std::string(1,'\0'),"",""},
{"null","UNIT_METADATA",std::string(1,'\0'),std::string(1,'\0'),""},
{"empty","UNIT_METADATA","","",""},
{"none","UNIT_METADATA","STR_NONE","",""},
{"explicit","UNIT_METADATA","LIVE_ITEM","RECOVERY","UNIT_METADATA"},
{"literal","UNIT_METADATA","STR_NULL","STR_NULL",""},
{"engineer","UNIT_METADATA","","STR_ENGINEER",""},
{"scientist","UNIT_METADATA","","STR_SCIENTIST",""},
{"soldier","UNIT_METADATA","","COLLISION",""},
{"missing-default","UNIT_NO_ITEM",std::string(1,'\0'),"",""}
};
for(size_t i=0;i<linkCases.size();++i){
if(i)std::cout<<',';auto& row=linkCases[i];Unit linked;linked._type=row[1];linked._liveAlienName=row[2];linked._civilianRecoveryTypeName=row[3];linked._spawnUnitName=row[4];linked.afterLoad(&mod);
auto* live=linked.getLiveAlienGeoscape();auto* recovery=linked.getCivilianRecoveryItemType();auto* spawn=linked.getSpawnUnit();
std::cout<<"{\"case\":\""<<row[0]<<"\",\"type\":\""<<linked.getType()<<"\",\"live\":\""<<(live?live->getType():"")<<"\",\"recovery\":\""<<(recovery?recovery->getType():"")<<"\",\"spawn\":\""<<(spawn?spawn->getType():"")<<"\"}";
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
std::cout<<"},\"smokeReduction\":[";
bool firstSmoke=true;
for(int view:{20,31,-7,0,INT_MAX})for(int density:{0,15,-15,INT_MAX,INT_MIN}){
if(!firstSmoke)std::cout<<',';firstSmoke=false;
mod._maxViewDistance=view;int smoke=density;getSmokeReduction(&mod,smoke);array({view,density,smoke});
}
mod._maxViewDistance=20;
std::cout<<"],\"modSettings\":[";
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
std::cout<<",\"unitStats\":[";
const char* prefixes[]={"StatsMin.","StatsMax.","StatsCap.","Stats."};
for(int group=0;group<4;++group) {
if(group)std::cout<<',';
UnitStatsOwner owner;
owner.stats={ (int16_t)(-100+group),(int16_t)(200+group), (int16_t)(50+group),
(int16_t)(32767-group),(int16_t)(-32768+group),(int16_t)(7+group),(int16_t)(8+group),
(int16_t)(9+group),(int16_t)(10+group),(int16_t)(11+group),(int16_t)(12+group),(int16_t)(-7+group) };
UnitStatsBinding binding{&owner};
UnitStats::addGetStatsScript<&UnitStatsOwner::stats>(binding,prefixes[group]);
std::cout<<'{';bool first=true;
for(const auto& [name,value]: binding.values) {if(!first)std::cout<<',';first=false;std::cout<<'\"'<<name<<"\":"<<value;}
std::cout<<'}';
}
UnitStatsBinding nullStats{nullptr};UnitStats::addGetStatsScript<&UnitStatsOwner::stats>(nullStats,"");
std::cout<<"],\"unitStatsNull\":{";bool firstNull=true;
for(const auto& [name,value]: nullStats.values) {if(!firstNull)std::cout<<',';firstNull=false;std::cout<<'\"'<<name<<"\":"<<value;}
std::cout<<"},\"armorVision\":[";
Armor vision;
for(int stage=0;stage<2;++stage) {
if(stage) {
std::cout<<',';
vision._visibilityAtDark=-7;vision._visibilityAtDay=0;vision._camouflageAtDark=31;vision._camouflageAtDay=INT_MAX;
vision._antiCamouflageAtDark=INT_MIN;vision._antiCamouflageAtDay=14;vision._visibilityThroughSmoke=56;vision._visibilityThroughFire=-11;
vision._personalLightFriend=8;vision._personalLightHostile=9;vision._personalLightNeutral=10;
}
std::map<std::string,int> fields={
{"getVisibilityAtDark",vision.getVisibilityAtDark()},{"getVisibilityAtDay",vision.getVisibilityAtDay()},
{"getCamouflageAtDark",vision.getCamouflageAtDark()},{"getCamouflageAtDay",vision.getCamouflageAtDay()},
{"getAntiCamouflageAtDark",vision.getAntiCamouflageAtDark()},{"getAntiCamouflageAtDay",vision.getAntiCamouflageAtDay()},
{"getHeatVision",vision.getVisibilityThroughSmoke()},{"getVisibilityThroughFire",vision.getVisibilityThroughFire()},
{"getPersonalLight",vision.getPersonalLightFriend()},{"getPersonalLightHostile",vision.getPersonalLightHostile()},
{"getPersonalLightNeutral",vision.getPersonalLightNeutral()}};
std::cout<<'{';bool firstVision=true;
for(const auto& [name,value]:fields) {if(!firstVision)std::cout<<',';firstVision=false;std::cout<<'\"'<<name<<"\":"<<value;}
std::cout<<'}';
}
std::cout<<"],\"itemReaders\":[";
RuleItem itemReader;
for(int stage=0;stage<2;++stage) {
if(stage) {
std::cout<<',';
itemReader._confAimed.accuracy=-7;itemReader._confAuto.accuracy=INT_MAX;itemReader._confSnap.accuracy=83;itemReader._confMelee.accuracy=41;
itemReader._accuracyUse=27;itemReader._accuracyMind=-11;itemReader._accuracyPanic=INT_MIN;itemReader._accuracyThrow=120;
itemReader._power=-9;itemReader._meleePower=INT_MAX;itemReader._armor=0;itemReader._waypoints=-1;
itemReader._twoHanded=true;itemReader._blockBothHands=true;itemReader._underwaterOnly=true;
}
std::map<std::string,int> fields={
{"getAccuracyAimed",itemReader.getAccuracyAimed()},{"getAccuracyAuto",itemReader.getAccuracyAuto()},
{"getAccuracySnap",itemReader.getAccuracySnap()},{"getAccuracyMelee",itemReader.getAccuracyMelee()},
{"getAccuracyUse",itemReader.getAccuracyUse()},{"getAccuracyMind",itemReader.getAccuracyMind()},
{"getAccuracyPanic",itemReader.getAccuracyPanic()},{"getAccuracyThrow",itemReader.getAccuracyThrow()},
{"getPower",itemReader.getPower()},{"getMeleePower",itemReader.getMeleePower()},
{"getArmorValue",itemReader.getArmor()},{"getWaypoints",itemReader.getWaypoints()},
{"isTwoHanded",itemReader.isTwoHanded()},{"isBlockingBothHands",itemReader.isBlockingBothHands()},{"isWaterOnly",itemReader.isWaterOnly()}};
std::cout<<'{';bool firstItem=true;
for(const auto& [name,value]:fields){if(!firstItem)std::cout<<',';firstItem=false;std::cout<<'\"'<<name<<"\":"<<value;}
std::cout<<'}';
}
std::cout<<"],\"itemCategoryReplacement\":[";
for(int stage=0;stage<3;++stage){
if(stage)std::cout<<',';
itemReader._categories={"A","B","C","FREE","A"};
std::map<std::string,std::string> replacements=stage==0?
std::map<std::string,std::string>{{"A","B"},{"B","C"},{"C","A"}}:
std::map<std::string,std::string>{{"C","B"},{"B","A"}};
if(stage==2){itemReader._categories={"\xF0\x90\x80\x80"};replacements={{"\xF0\x90\x80\x80","\xEE\x80\x80"},{"\xEE\x80\x80","A"}};}
itemReader.updateCategories(&replacements);
std::cout<<'[';bool firstCategory=true;
for(const char* cat: {"A","B","C","FREE","MISSING","free","\xF0\x90\x80\x80","\xEE\x80\x80"}){if(!firstCategory)std::cout<<',';firstCategory=false;int result=-1;hasCategoryScript(&itemReader,result,cat);std::cout<<"[\""<<cat<<"\","<<result<<"]";}
int result=-1;hasCategoryScript(nullptr,result,"A");std::cout<<",[\"NULL\","<<result<<"]]";
}
std::cout<<"]}\n";
}

// Asset-free target selection collaborators; inserted pinned methods remain verbatim.
#include <algorithm>
#include <climits>
#include <cmath>
#include <iostream>
#include <vector>
#include <stdexcept>
#include <string>
#include <map>
namespace OpenXcom {
using Exception = std::runtime_error;
struct RNG { static inline int roll=0, calls=0, suppliedChoice=-1;
static bool percent(int value) { ++calls; return roll < value; }
static int generate(int,int) { ++calls; return suppliedChoice >= 0 ? suppliedChoice : calls % 2 == 0 ? 1 : 0; } };
double Nautical(double x) { return x / 60.0 * 3.14159265358979323846 / 180.0; }
struct Production {
struct Rule { int time=4; int getManufactureTime() const { return time; } } rule;
const Rule* _rules=&rule; int _timeSpent=0,_engineers=0,_amount=20; bool infinite=false;
int getAmountProduced() const; bool getInfiniteAmount() const { return infinite; }
int requested() {
// PRODUCTION_COUNT
}
};
// PRODUCTION_AMOUNT
struct Target { double distance = 0, lon = 0, lat = 0; virtual ~Target() = default;
double getLongitude() const { return lon; } double getLatitude() const { return lat; }
void setLongitude(double value) { lon = value; } void setLatitude(double value) { lat = value; } };
struct Waypoint : Target { int id=0; int getId() const { return id; } void setId(int value) { id=value; } };
struct Item { int clipSize=1; std::string getType() const { return "SUPPLY"; } int getClipSize() const { return clipSize; } };
using RuleItem=Item;
struct Storage { int count=0; int getItem(const Item*) const { return count; }
void removeItem(const Item*,int qty=1) { count-=qty; } void addItem(const Item*,int qty=1) { count+=qty; } };
struct Craft; struct Transfer; struct Soldier {};
struct Base : Target { Storage storage; std::vector<Craft*> crafts; std::vector<Soldier*> soldiers; std::vector<Transfer*> transfers; std::vector<Transfer*>* getTransfers() { return &transfers; } int scientists=0,engineers=0;
Storage* getStorageItems() { return &storage; } std::vector<Craft*>* getCrafts() { return &crafts; }
std::vector<Soldier*>* getSoldiers() { return &soldiers; }
int getScientists() const { return scientists; } void setScientists(int value) { scientists=value; }
int getEngineers() const { return engineers; } void setEngineers(int value) { engineers=value; } };
struct CraftWeapon { bool rearming=false; bool isRearming() const { return rearming; }
struct Rule { Item* clip=nullptr; int ammoMax=0,rate=4; bool saving=false; int getRearmRate() const { return rate; } bool useStatisticalBulletSaving() const { return saving; } Item* getClipItem() const { return clip; } int getAmmoMax() const { return ammoMax; } } rule;
const Rule* getRules() const { return &rule; } int _ammo=0; bool& _rearming=rearming; const Rule* _rules=&rule; int rearm(const int,const int); void setAmmo(int value) { _ammo=std::clamp(value,0,rule.ammoMax); }
int getAmmo() const { return _ammo; } bool isDisabled() const { return false; }
void setRearming(bool value) { rearming=value; } };
// WEAPON_REARM
struct ResearchRule {
static const int RESEARCH_STATUS_DISABLED=2,RESEARCH_STATUS_NEW=0;
std::string name="RESEARCH"; int cost=1,points=1; bool repeatable=false,sequential=false;
std::vector<const ResearchRule*> disables,reenables,unlocks,free,dependencies,requirements;
std::vector<std::pair<const ResearchRule*,std::vector<const ResearchRule*>>> protectedFree;
const auto& getDisabled() const { return disables; } const auto& getReenabled() const { return reenables; }
const auto& getUnlocked() const { return unlocks; } const auto& getGetOneFree() const { return free; }
const auto& getGetOneFreeProtected() const { return protectedFree; } const auto& getRequirements() const { return requirements; }
int getCost() const { return cost; } int getPoints() const { return points; }
bool isRepeatable() const { return repeatable; } bool sequentialGetOneFree() const { return sequential; }

bool holding=false,returns=false,destroy=false; Item* needed=nullptr; std::string spawned=""; int count=0; std::vector<std::string> list;
bool isHoldingNeededItem() const { return holding; } bool returnItem() const { return returns; }
bool destroyItem() const { return destroy; } Item* getNeededItem() const { return needed; }
std::string getName() const { return name; } std::string getSpawnedItem() const { return spawned; }
int getSpawnedItemCount() const { return count; } const auto& getSpawnedItemList() const { return list; }
};
using RuleResearch=ResearchRule;
struct ResearchProject { const RuleResearch* rule=nullptr; bool finished=false;
const RuleResearch* getRules() const { return rule; } bool isFinished() const { return finished; } };
struct ResearchCorpse : Item { bool isRecoverable() const { return true; } bool isCorpseRecoverable() const { return true; } };
struct ResearchArmor { ResearchCorpse* getCorpseGeoscape() const { return nullptr; } };
struct ResearchUnit { const ResearchArmor* getArmor() const { return nullptr; } };
struct ResearchMod { std::vector<ResearchRule*> rules; mutable Item supply; Item* getItem(const std::string& id) const { return id=="SUPPLY" ? &supply : nullptr; } ResearchUnit* getUnit(const std::string&,bool) const { return nullptr; } };
using Mod=ResearchMod;
enum class DiscoverySourceType { FREE_AFTER };
struct ResearchDiaryEntry { explicit ResearchDiaryEntry(const RuleResearch*) {} void setDate(int) {}
struct { DiscoverySourceType type; const RuleResearch* research; std::string name; } source; };
struct Options { static inline bool retainCorpses=true; };
struct ResearchCaller {
Storage storage; ResearchMod mod; const ResearchMod* _mod=&mod;
Storage* getStorageItems() { return &storage; }
void spawn(const RuleResearch*,const ResearchMod*,Base*);
static std::vector<const RuleResearch*> primary(const RuleResearch* research,const RuleResearch* bonus) {
std::vector<const RuleResearch*> topicsToCheck;
// RESEARCH_PRIMARY_TOPICS
return topicsToCheck;
}
void refund(ResearchProject* project) { const RuleResearch* ruleResearch=project->getRules();
// RESEARCH_REFUND
}
};
struct MovingTarget : Target { Target* _dest = nullptr; void setDestination(Target* target) { _dest = target; } };
struct Craft : MovingTarget {
struct Rule { bool landing = false, hidden = false, autoPatrol=true; Item* fuelItem=nullptr; int refuelRate=40;
bool getAllowLanding() const { return landing; } bool isUndetectable() const { return hidden; }
const Item* getRefuelItem() const { return fuelItem; } int getRefuelRate() const { return refuelRate; }
int getRepairRate() const { return 10; }
bool patrolWithoutFuel() const { return false; }
bool canAutoPatrol() const { return autoPatrol; } } rule;
const Rule* _rules = &rule;
struct Stats { int speedMax = 2000, damageMax = 100, fuelMax=100; } _stats;
Base base; Base* _base=&base; int _fuel=0, _excessFuel=0, _takeoff=0, speed=0; int& _speed=speed;
double& _lon=lon; double& _lat=lat;
std::string _status="STR_REFUELLING"; bool autoPatrol=true;
std::vector<CraftWeapon*> _weapons;
void setFuel(int); std::string refuel(); void setDestination(Target*); void repair(); const RuleItem* rearm();
int getFuelConsumption(int,int) const; void consumeFuel(int); void returnToBase();
void checkup(); void setBase(Base*,bool move=true);
void reuseItem(const RuleItem*);
int getFuel() const { return _fuel; } int getFuelLimit() const { return 0; }
int getSpeed() const { return speed; } void setLowFuel(bool value) { _lowFuel=value; }
Target* getDestination() const { return _dest; } double getDistance(Target*) const { return 0; }
void setDamage(int value) { _damage=std::max(0,value); }
void setSpeed(int value) { speed=value; }
std::string getStatus() const { return _status; } void setStatus(const std::string& value) { _status=value; }
bool getIsAutoPatrolling() const { return autoPatrol; }
double getLongitudeAuto() const { return 0.8; } double getLatitudeAuto() const { return 0.3; }
int _damage = 0, units = 0, id = 0; bool ignored = false, _lowFuel = false, _missionComplete = false;
int getMaxUnitsClamped() const { return units; }
bool isIgnoredByHK() const;
bool getLowFuel() const { return _lowFuel; }
bool getMissionComplete() const { return _missionComplete; }
const Rule* getRules() const { return _rules; }
int getHunterKillerAttraction(int huntMode) const;
};
// REFUEL_METHODS
struct Transfer {
explicit Transfer(int hours=1):_hours(hours) {}
void setItems(const Item* item,int qty=1) { _itemId=item; _itemQty=qty; }
int _hours=1,_itemQty=0,_scientists=0,_engineers=0; Soldier* _soldier=nullptr; Craft* _craft=nullptr;
const Item* _itemId=nullptr; bool _delivered=false;
void advance(Base*);
int getHours() const;
};
// TRANSFER_METHOD
void ResearchCaller::spawn(const RuleResearch* myResearchRule,const ResearchMod* mod,Base* base) {
// RESEARCH_SPAWN
}
struct BaseFacility {
struct Rule { Item* item=nullptr; int getAmmoMax() const { return 2; } int getRearmRate() const { return 1; }
const Item* getAmmoItem() const { return item; } } rule;
Rule* _rules=&rule; Base* _base=nullptr; int _buildTime=0,_ammo=0; bool _ammoMissingReported=false;
void resetAmmoMissingReported() { _ammoMissingReported=false; }
const RuleItem* rearm();
};
// FACILITY_REARM
// IGNORED_METHOD
// ATTRACTION
struct Mission { int id=4; int getId() const { return id; } };
struct Ufo : MovingTarget {
Mission mission; int uniqueId=0, status=0; bool _isEscort=false;
Mission* getMission() { return &mission; }
bool isHunterKiller() const { return _isHunterKiller; }
bool isEscort() const { return _isEscort; }
bool isEscorting() const { return _isEscorting; }
void setEscort(bool value) { _isEscort=value; }
bool _isHunterKiller=false; int _huntMode=0, _huntBehavior=0;
Ufo()=default;
Ufo(int hunterKillerPercentage,int huntMode,int huntBehavior) {
// CONSTRUCTOR_FLAGS
}
struct Stats { int radarRange = 300, speedMax = 2200; } _stats;
struct Rule { int huntSpeed = 100; int getHuntSpeed() const { return huntSpeed; } } rule;
const Rule* _rules = &rule; Waypoint* _origWaypoint = nullptr; bool _isHunting = false, _isEscorting = false; int speed = 0;
void setSpeed(int value) { speed = value; }
void backupOriginalDestination(); void setTargetedXcomCraft(Craft*); void setEscortedUfo(Ufo*);
void resetOriginalDestination(bool); void setDestination(Target*);
Craft* original = nullptr; int mode = 0; bool hunting = false;
bool isHunting() const { return hunting; }
Craft* getTargetedXcomCraft() const { return original; }
int getHuntMode() const { return mode; }
double getDistance(Target* target) const { return target->distance; }
bool insideRadarRange(Target* target) const;
};
// RADAR
// RETARGET_METHODS
struct SavedGame { std::vector<Ufo*> ufos; std::vector<Waypoint*> waypoints;
std::vector<const RuleResearch*> _discovered; std::map<std::string,int> statuses; int _time=0,researchScore=0;
bool isResearchRuleStatusDisabled(const std::string& id) const { auto found=statuses.find(id); return found!=statuses.end()&&found->second==2; }
void setResearchRuleStatus(const std::string& id,int value) { statuses[id]=value; }
bool isResearched(const RuleResearch* rule,bool) const { return std::find(_discovered.begin(),_discovered.end(),rule)!=_discovered.end(); }
bool isResearched(const std::vector<const RuleResearch*>& topics,bool,bool disabled) const { return std::all_of(topics.begin(),topics.end(),[&](auto* r){ return isResearched(r,false)||(disabled&&isResearchRuleStatusDisabled(r->getName())); }); }
void sortReserchVector(std::vector<const RuleResearch*>&) {} void removePoppedResearch(const RuleResearch*) {}
void addResearchDiaryEntry(ResearchDiaryEntry* entry) { delete entry; } void addResearchScore(int value) { researchScore+=value; }
void removeDiscoveredResearch(const RuleResearch* rule) { std::erase(_discovered,rule); }
void getAvailableResearchProjects(std::vector<RuleResearch*>& available,const Mod* mod,Base*) const {
std::map<std::string,ResearchRule*> ordered; for(auto* rule:mod->rules) ordered[rule->name]=rule;
for(const auto& pair:ordered) { auto* rule=pair.second; if(!isResearchRuleStatusDisabled(rule->name)&&!isResearched(rule,false)&&
std::all_of(rule->dependencies.begin(),rule->dependencies.end(),[&](auto* r){return isResearched(r,false);})&&
std::all_of(rule->requirements.begin(),rule->requirements.end(),[&](auto* r){return isResearched(r,false);})) available.push_back(rule); }
}
void addFinishedResearch(const RuleResearch*,const Mod*,Base*,bool score=true);
const RuleResearch* selectGetOneFree(const RuleResearch*);
bool hasUndiscoveredGetOneFree(const RuleResearch*,bool) const;
bool hasUndiscoveredProtectedUnlock(const RuleResearch*) const;

std::vector<Ufo*>* getUfos() { return &ufos; }
std::vector<Waypoint*>* getWaypoints() { return &waypoints; }
int getId(const std::string&) { return 8+(int)waypoints.size(); } };
// RESEARCH_DISCOVERY_METHODS
struct ServiceGame { SavedGame save; SavedGame* getSavedGame() { return &save; } };
void autoPatrol(Craft* xcraft, ServiceGame* _game) {
// AUTO_PATROL
}
struct LowFuelState { LowFuelState(Craft*,void*) {} };
struct FuelCaller {
struct Mod { int getEscortRange() const { return 30; } } mod;
struct Game { Mod* mod; Mod* getMod() { return mod; } } game{&mod}; Game* _game=&game;
void popup(LowFuelState* state) { delete state; }
void fuel(Craft* xcraft) {
// FUEL_CALLER
}
};
void spawnEscort(Ufo* ufo,SavedGame& game,bool requested) {
struct { bool escort; } wave{requested};
// SPAWN_ESCORT
}
void retargetEscort(Ufo* ufo,SavedGame& saved) {
struct Game { SavedGame* saved; SavedGame* getSavedGame() { return saved; } } game{&saved};
Game* _game=&game;
// RETARGET_ESCORT
}
struct FastSchedule {
struct Mod { bool fast = true; bool getHunterKillerFastRetarget() const { return fast; } } mod;
struct Game { const Mod* mod; const Mod* getMod() const { return mod; } } game{&mod};
const Game* _game = &game; int _timeSpeed = 0, _btn5Secs = 0, _btn1Min = 1;
bool due() const { return // FAST_CONDITION
; }
};
Craft* select(Ufo* ufo, const std::vector<Craft*>& crafts) {
const auto* activeCrafts = &crafts;
// SELECTION
return newTarget;
}
}
int main() {
using namespace OpenXcom;
std::cout << "{\"maintenanceRows\":[";
bool serviceFirst=true;
for(bool itemFuel:{false,true}) for(int fuel:{0,60,100}) for(int stock:{0,1}) for(bool low:{false,true}) for(bool rearming:{false,true}) {
if(!serviceFirst) std::cout << ','; serviceFirst=false;
Craft craft; Item item; CraftWeapon weapon; ServiceGame game; Ufo hunter;
craft.id=1; craft.distance=Nautical(100); craft.rule.fuelItem=itemFuel?&item:nullptr;
craft._fuel=fuel; craft.base.storage.count=stock; craft._lowFuel=low;
weapon.rearming=rearming; craft._weapons={&weapon};
const auto missing=craft.refuel();
if(missing.empty()) autoPatrol(&craft,&game);
std::vector<Craft*> active;
if(craft._status=="STR_OUT" && craft._damage<craft._stats.damageMax) active.push_back(&craft);
craft.ignored=craft.isIgnoredByHK();
Craft* target=select(&hunter,active);
std::cout << '[' << (itemFuel?"true":"false") << ',' << fuel << ',' << stock << ',' << (low?"true":"false") << ','
<< (rearming?"true":"false") << ',' << craft._fuel << ",\"" << craft._status << "\"," << craft.base.storage.count << ','
<< (craft._lowFuel?"true":"false") << ',' << (!missing.empty()?"true":"false") << ',' << craft._takeoff << ',' << (target?target->id:0) << ']';
for(auto* waypoint:game.save.waypoints) delete waypoint;
}
std::cout << "],\"researchUnicodeRows\":[";
{
ResearchRule source,disabler,late; source.name="UNICODE_SOURCE";
disabler.name="\xEE\x80\x80"; late.name="\xF0\x90\x80\x80";
disabler.cost=late.cost=0; disabler.dependencies=late.dependencies={&source}; disabler.disables={&late};
ResearchMod mod; mod.rules={&source,&late,&disabler}; SavedGame saved; Base base;
saved.addFinishedResearch(&source,&mod,&base);
std::cout << '[' << (saved.isResearched(&late,false)?"true":"false") << ',' << saved.statuses[late.name] << ']';
}
std::cout << "],\"researchQueuedRows\":[";
serviceFirst=true;
for(bool lateFirst:{false,true}) {
if(!serviceFirst) std::cout << ','; serviceFirst=false;
ResearchRule source,disabler,late,held; source.name="QUEUED_SOURCE"; disabler.name="QUEUED_DISABLER"; late.name=lateFirst?"QUEUED_A_LATE":"QUEUED_Z_LATE"; held.name="QUEUED_HELD";
disabler.cost=late.cost=0; disabler.dependencies=late.dependencies={&source}; disabler.disables={&late}; late.disables={&held};
Item supply; held.holding=true; held.destroy=true; held.needed=&supply;
ResearchMod mod; mod.rules=lateFirst?std::vector<ResearchRule*>{&source,&late,&disabler,&held}:std::vector<ResearchRule*>{&source,&disabler,&late,&held};
SavedGame saved; Base base; saved.addFinishedResearch(&source,&mod,&base);
ResearchCaller caller; ResearchProject project{&held,false}; if(saved.isResearchRuleStatusDisabled(held.name)) caller.refund(&project);
std::cout << '[' << (lateFirst?"true":"false") << ',' << (saved.isResearched(&late,false)?"true":"false") << ',' << saved.statuses[late.name] << ',' << caller.storage.count << ']';
}
std::cout << "],\"researchRewardRows\":[";
serviceFirst=true;
for(bool sequential:{false,true}) for(bool duplicate:{false,true}) for(int aState:{0,1,2}) for(int bState:{0,1,2}) for(bool gate:{false,true}) {
if(!serviceFirst) std::cout << ','; serviceFirst=false;
ResearchRule source,a,b,prerequisite; source.name="REWARD_SOURCE"; a.name="REWARD_A"; b.name="REWARD_B"; prerequisite.name="REWARD_GATE";
source.sequential=sequential; source.free={&a}; if(duplicate) source.free.push_back(&a);
source.protectedFree={{&prerequisite,{&b}}}; a.spawned="SUPPLY"; b.spawned="SUPPLY"; b.count=2;
SavedGame saved; ResearchMod mod; mod.rules={&source,&a,&b,&prerequisite}; Base base; ResearchCaller caller;
if(aState==1) saved._discovered.push_back(&a); if(aState==2) saved.statuses[a.name]=2;
if(bState==1) saved._discovered.push_back(&b); if(bState==2) saved.statuses[b.name]=2;
if(gate) saved._discovered.push_back(&prerequisite);
RNG::suppliedChoice=0; const auto* bonus=saved.selectGetOneFree(&source); RNG::suppliedChoice=-1;
if(bonus) saved.addFinishedResearch(bonus,&mod,&base); saved.addFinishedResearch(&source,&mod,&base);
for(auto* topic:ResearchCaller::primary(&source,bonus)) caller.spawn(topic,&mod,&base);
int count=0; for(auto* transfer:base.transfers) { transfer->advance(&base); ++count; delete transfer; }
std::cout << '[' << (sequential?"true":"false") << ',' << (duplicate?"true":"false") << ',' << aState << ',' << bState << ',' << (gate?"true":"false") << ','
<< (bonus==&a?1:bonus==&b?2:0) << ',' << base.storage.count << ',' << (saved.hasUndiscoveredGetOneFree(&source,true)?"true":"false") << ']';
}
std::cout << "],\"researchDiscoveryRows\":[";
serviceFirst=true;
for(int mode:{0,1,2,3}) for(bool complete:{false,true}) for(bool finished:{false,true}) {
if(!serviceFirst) std::cout << ','; serviceFirst=false;
ResearchRule source,lookup,automatic,held; source.name="DISCOVERY_SOURCE"; lookup.name="DISCOVERY_LOOKUP"; automatic.name="DISCOVERY_AUTO"; held.name="DISCOVERY_HELD";
automatic.cost=0; automatic.dependencies={&source}; held.cost=100;
Item supply; held.holding=true; held.destroy=true; held.needed=&supply;
if(mode==0) source.disables={&held};
if(mode==1) lookup.disables={&held};
if(mode==2) automatic.disables={&held};
if(mode==3) { source.disables={&held}; automatic.reenables={&held}; }
ResearchMod mod; mod.rules={&source,&lookup,&automatic,&held}; Base base; SavedGame saved;
if(complete) saved._discovered={&source};
saved.addFinishedResearch(&source,&mod,&base); if(mode==1) saved.addFinishedResearch(&lookup,&mod,&base);
if(finished) saved.addFinishedResearch(&held,&mod,&base);
ResearchCaller caller; ResearchProject project{&held,finished};
if(saved.isResearchRuleStatusDisabled(held.name)) caller.refund(&project);
std::cout << '[' << mode << ',' << (complete?"true":"false") << ',' << (finished?"true":"false") << ',' << caller.storage.count << ','
<< saved.statuses[held.name] << ',' << (saved.isResearched(&automatic,false)?"true":"false") << ']';
}
std::cout << "],\"researchPrimaryRows\":[";
serviceFirst=true;
for(bool direct:{false,true}) for(bool bonus:{false,true}) {
if(!serviceFirst) std::cout << ','; serviceFirst=false;
ResearchRule topic,freeTopic,lookup; topic.spawned=direct?"SUPPLY":""; freeTopic.spawned="SUPPLY"; lookup.spawned="SUPPLY";
ResearchCaller caller; ResearchMod mod; Base base;
for(auto* source:ResearchCaller::primary(&topic,bonus?&freeTopic:nullptr)) caller.spawn(source,&mod,&base);
int count=0; for(auto* transfer:base.transfers) { transfer->advance(&base); ++count; delete transfer; }
std::cout << '[' << (direct?"true":"false") << ',' << (bonus?"true":"false") << ',' << count << ',' << base.storage.count << ']';
}
std::cout << "],\"researchSpawnRows\":[";
serviceFirst=true;
for(bool itemFuel:{false,true}) for(bool main:{false,true}) for(int count:{-2,0,2}) for(int duplicates:{0,1,2}) for(int stock:{0,1}) {
if(!serviceFirst) std::cout << ','; serviceFirst=false;
ResearchRule rule; rule.spawned=main?"SUPPLY":"MISSING"; rule.count=count; rule.list={"MISSING"};
for(int i=0;i<duplicates;i++) rule.list.push_back("SUPPLY");
ResearchMod mod; Base owner; owner.storage.count=stock; ResearchCaller research; research.spawn(&rule,&mod,&owner);
Craft craft; craft.id=1; craft._base=&owner; craft._fuel=60; craft.rule.fuelItem=itemFuel?&mod.supply:nullptr;
CraftWeapon weapon; weapon.rule.clip=&mod.supply; weapon.rule.ammoMax=10; craft._weapons=itemFuel?std::vector<CraftWeapon*>{}:std::vector<CraftWeapon*>{&weapon};
for(auto* transfer:owner.transfers) transfer->advance(&owner);
for(auto* transfer:owner.transfers) craft.reuseItem(transfer->_itemId);
ServiceGame game; if(craft._status=="STR_REFUELLING") { const auto missing=craft.refuel(); if(missing.empty()) autoPatrol(&craft,&game); }
if(craft._status=="STR_OUT") { FuelCaller caller; caller.fuel(&craft); }
Ufo ufo; auto* selected=craft._status=="STR_OUT"?select(&ufo,{&craft}):nullptr;
std::cout << '[' << (itemFuel?"true":"false") << ',' << (main?"true":"false") << ',' << count << ',' << duplicates << ',' << stock << ','
<< owner.transfers.size() << ',' << owner.storage.count << ',' << '"' << craft._status << '"' << ',' << craft._fuel << ','
<< (weapon.rearming?"true":"false") << ',' << (selected?selected->id:0) << ']';
for(auto* waypoint:game.save.waypoints) delete waypoint;
for(auto* transfer:owner.transfers) delete transfer;
}
std::cout << "],\"researchRefundRows\":[";
serviceFirst=true;
for(bool holding:{false,true}) for(bool returns:{false,true}) for(bool finished:{false,true}) for(int stock:{0,1}) {
if(!serviceFirst) std::cout << ','; serviceFirst=false;
Item supply; ResearchRule rule; rule.holding=holding; rule.returns=returns; rule.destroy=holding&&!returns; rule.needed=&supply;
ResearchProject project; project.rule=&rule; project.finished=finished;
ResearchCaller caller; caller.storage.count=stock; caller.refund(&project);
std::cout << '[' << (holding?"true":"false") << ',' << (returns?"true":"false") << ',' << (finished?"true":"false") << ',' << stock << ',' << caller.storage.count << ']';
}
std::cout << "],\"productionProgressRows\":[";
serviceFirst=true;
for(int time:{0,4,100}) for(int spent:{0,3,4,11}) for(int assigned:{0,1,3,8}) for(bool infinite:{false,true}) {
if(!serviceFirst) std::cout << ','; serviceFirst=false;
Production production; production.rule.time=time; production._timeSpent=spent; production._engineers=assigned; production.infinite=infinite;
auto count=production.requested();
std::cout << '[' << time << ',' << spent << ',' << assigned << ',' << (infinite?"true":"false") << ',' << count << ']';
}
std::cout << "],\"ammunitionFuelRows\":[";
serviceFirst=true;
for(int clipSize:{1,4}) for(bool saving:{false,true}) for(int ammo:{0,9}) for(int stock:{0,1,2}) for(int choice:{0,clipSize-1}) {
if(!serviceFirst) std::cout << ','; serviceFirst=false;
RNG::calls=0; RNG::suppliedChoice=choice;
Item supply; supply.clipSize=clipSize; Base owner; owner.storage.count=stock;
Craft gun,fuel; gun._base=&owner; gun._status="STR_REARMING";
CraftWeapon weapon; weapon.rule.clip=&supply; weapon.rule.ammoMax=10; weapon.rule.saving=saving; weapon._ammo=ammo; weapon.rearming=true;
gun._weapons={&weapon}; gun.rearm();
fuel._base=&owner; fuel.rule.fuelItem=&supply; fuel._fuel=60; fuel.id=1;
const auto missing=fuel.refuel(); ServiceGame game; if(missing.empty()) autoPatrol(&fuel,&game);
if(fuel._status=="STR_OUT") { FuelCaller caller; caller.fuel(&fuel); }
Ufo ufo; Craft* selected=fuel._status=="STR_OUT" ? select(&ufo,{&fuel}) : nullptr;
std::cout << '[' << clipSize << ',' << (saving?"true":"false") << ',' << ammo << ',' << stock << ',' << choice << ','
<< weapon.getAmmo() << ',' << (weapon.isRearming()?"true":"false") << ',' << owner.storage.count << ','
<< '"' << fuel._status << '"' << ',' << fuel._fuel << ',' << (selected?selected->id:0) << ',' << RNG::calls << ']';
for(auto* waypoint:game.save.waypoints) delete waypoint;
}
RNG::suppliedChoice=-1;
std::cout << "],\"facilityFuelRows\":[";
serviceFirst=true;
for(int stock:{0,1,2}) for(int ammo:{0,1,2}) for(int build:{0,1}) for(bool reported:{false,true}) {
if(!serviceFirst) std::cout << ','; serviceFirst=false;
Craft craft; Item item; Base base; BaseFacility facility; ServiceGame game; FuelCaller caller; Ufo hunter;
base.storage.count=stock; facility._base=&base; facility.rule.item=&item; facility._ammo=ammo; facility._buildTime=build; facility._ammoMissingReported=reported;
craft._base=&base; craft.rule.fuelItem=&item; craft._fuel=60; craft.id=1; craft.distance=Nautical(100);
const auto* missing=facility.rearm();
const auto fuelMissing=craft.refuel(); if(fuelMissing.empty()) autoPatrol(&craft,&game);
std::vector<Craft*> active;
if(craft._status=="STR_OUT") { caller.fuel(&craft); active.push_back(&craft); }
Craft* target=select(&hunter,active);
std::cout << '[' << stock << ',' << ammo << ',' << build << ',' << (reported?"true":"false") << ','
<< facility._ammo << ',' << (facility._ammoMissingReported?"true":"false") << ',' << (missing?"true":"false") << ','
<< base.storage.count << ",\"" << craft._status << "\"," << craft._fuel << ',' << (target?target->id:0) << ']';
for(auto* waypoint:game.save.waypoints) delete waypoint;
}
std::cout << "],\"invalidArrivalRows\":[";
serviceFirst=true;
for(int hours:{0,-1}) {
if(!serviceFirst) std::cout << ','; serviceFirst=false;
Transfer transfer; Item item; Base base;
transfer._hours=hours; transfer._itemId=&item; transfer._itemQty=1; transfer.advance(&base);
std::cout << '[' << hours << ',' << transfer.getHours() << ',' << (transfer._delivered?"true":"false") << ','
<< (transfer.getHours()==0?"true":"false") << ']';
}
std::cout << "],\"combinedTransferRows\":[";
serviceFirst=true;
for(bool itemFirst:{false,true}) for(bool armed:{false,true}) for(int fuel:{0,60,100}) {
if(!serviceFirst) std::cout << ','; serviceFirst=false;
Craft craft; Item item; Base base; Transfer itemTransfer,craftTransfer; ServiceGame game; FuelCaller caller; Ufo hunter; CraftWeapon weapon;
craft.id=2; craft.distance=Nautical(100); craft.rule.fuelItem=&item; craft._fuel=fuel;
if(armed) { weapon.rule.clip=&item; weapon.rule.ammoMax=10; craft._weapons={&weapon}; }
craftTransfer._craft=&craft; itemTransfer._itemId=&item; itemTransfer._itemQty=1;
if(itemFirst) { itemTransfer.advance(&base); craftTransfer.advance(&base); }
else { craftTransfer.advance(&base); itemTransfer.advance(&base); }
// ItemsArrivingState is constructed only after every due Transfer::advance.
craft.reuseItem(&item);
if(craft._status=="STR_REFUELLING") { const auto missing=craft.refuel(); if(missing.empty()) autoPatrol(&craft,&game); }
std::vector<Craft*> active;
if(craft._status=="STR_OUT") { caller.fuel(&craft); active.push_back(&craft); }
Craft* target=select(&hunter,active);
std::cout << '[' << (itemFirst?"true":"false") << ',' << (armed?"true":"false") << ',' << fuel << ",\"" << craft._status << "\"," << craft._fuel << ',' << base.storage.count << ',' << (weapon.rearming?"true":"false") << ',' << (target?target->id:0) << ']';
for(auto* waypoint:game.save.waypoints) delete waypoint;
}
std::cout << "],\"suppliedFuelRows\":[";
serviceFirst=true;
for(int hours:{1,2}) for(const std::string status:{"STR_REFUELLING","STR_REARMING","STR_READY"}) for(int fuel:{0,60,100}) for(int stock:{0,1}) for(bool armed:{false,true}) {
if(!serviceFirst) std::cout << ','; serviceFirst=false;
Craft craft; Item item; Base base; Transfer transfer; ServiceGame game; FuelCaller caller; Ufo hunter; CraftWeapon weapon;
craft.id=1; craft._base=&base; base.storage.count=stock; craft.distance=Nautical(100);
craft.rule.fuelItem=&item; craft._status=status; craft._fuel=fuel;
if(armed) { weapon.rule.clip=&item; weapon.rule.ammoMax=10; craft._weapons={&weapon}; }
if(status=="STR_REARMING") craft.rearm();
transfer._hours=hours; transfer._itemId=&item; transfer._itemQty=1; transfer.advance(&base);
if(transfer._delivered) craft.reuseItem(&item);
if(craft._status=="STR_REFUELLING") { const auto missing=craft.refuel(); if(missing.empty()) autoPatrol(&craft,&game); }
std::vector<Craft*> active;
if(craft._status=="STR_OUT") { caller.fuel(&craft); active.push_back(&craft); }
Craft* target=select(&hunter,active);
std::cout << '[' << hours << ",\"" << status << "\"," << fuel << ',' << stock << ',' << (armed?"true":"false") << ','
<< (transfer._delivered?"true":"false") << ",\"" << craft._status << "\"," << craft._fuel << ',' << base.storage.count << ',' << (target?target->id:0) << ']';
for(auto* waypoint:game.save.waypoints) delete waypoint;
}
std::cout << "],\"incomingCraftRows\":[";
serviceFirst=true;
for(int hours:{1,2}) for(const std::string status:{"STR_OUT","STR_REFUELLING","STR_REARMING","STR_READY"}) for(int fuel:{0,60,100}) for(int damage:{0,15}) {
if(!serviceFirst) std::cout << ','; serviceFirst=false;
Craft craft; Base base; Transfer transfer; ServiceGame game; FuelCaller caller; Ufo hunter;
craft.id=2; craft.distance=Nautical(100); craft._status=status; craft._fuel=fuel; craft._damage=damage;
craft.lon=0.9; craft.lat=0.3; base.lon=0.45; base.lat=0.21;
transfer._hours=hours; transfer._craft=&craft; transfer.advance(&base);
std::vector<Craft*> active;
for(Craft* arrived:base.crafts) {
if(arrived->_status=="STR_REFUELLING") { const auto missing=arrived->refuel(); if(missing.empty()) autoPatrol(arrived,&game); }
if(arrived->_status=="STR_OUT") { caller.fuel(arrived); if(arrived->_damage<arrived->_stats.damageMax) active.push_back(arrived); }
}
Craft* target=select(&hunter,active);
std::cout << '[' << hours << ",\"" << status << "\"," << fuel << ',' << damage << ',' << (transfer._delivered?"true":"false")
<< ",\"" << craft._status << "\"," << craft._fuel << ',' << craft._damage << ',' << craft.lon << ',' << craft.lat << ',' << craft._takeoff << ',' << (target?target->id:0) << ']';
for(auto* waypoint:game.save.waypoints) delete waypoint;
}
std::cout << "],\"escortFuelRows\":[";
serviceFirst=true;
for(bool relaunch:{false,true}) for(bool leaderFirst:{false,true}) {
if(!serviceFirst) std::cout << ','; serviceFirst=false;
Craft leader,escort; ServiceGame game; FuelCaller caller; Ufo hunter;
leader.id=2; escort.id=1; leader.distance=escort.distance=Nautical(100);
escort._stats.speedMax=6000; escort._fuel=50; escort._status="STR_OUT"; escort.speed=6000; escort._dest=&leader;
leader._status=relaunch?"STR_REFUELLING":"STR_OUT"; leader._fuel=relaunch?60:0;
if(relaunch) { leader.refuel(); autoPatrol(&leader,&game); }
std::vector<Craft*> ordered=leaderFirst?std::vector<Craft*>{&leader,&escort}:std::vector<Craft*>{&escort,&leader};
for(Craft* craft:ordered) caller.fuel(craft);
Craft* target=select(&hunter,ordered);
std::cout << '[' << (relaunch?"true":"false") << ',' << (leaderFirst?"true":"false") << ','
<< escort._fuel << ',' << (escort._lowFuel?"true":"false") << ',' << leader._fuel << ',' << (target?target->id:0) << ']';
for(auto* waypoint:game.save.waypoints) delete waypoint;
}
std::cout << "],\"hourlyMaintenanceRows\":[";
serviceFirst=true;
for(const std::string status:{"STR_REFUELLING","STR_REARMING","STR_REPAIRS","STR_READY"}) for(int fuel:{0,60,100}) for(int damage:{0,15}) {
if(!serviceFirst) std::cout << ','; serviceFirst=false;
Craft craft; ServiceGame game; Ufo hunter;
craft.id=1; craft.distance=Nautical(100); craft._status=status; craft._fuel=fuel; craft._damage=damage;
if(status=="STR_REPAIRS") craft.repair(); else if(status=="STR_REARMING") craft.rearm();
if(craft._status=="STR_REFUELLING") { const auto missing=craft.refuel(); if(missing.empty()) autoPatrol(&craft,&game); }
std::vector<Craft*> active;
if(craft._status=="STR_OUT" && craft._damage<craft._stats.damageMax) active.push_back(&craft);
Craft* target=select(&hunter,active);
std::cout << "[\"" << status << "\"," << fuel << ',' << damage << ',' << craft._fuel << ",\"" << craft._status << "\"," << craft._damage << ',' << (target?target->id:0) << ']';
for(auto* waypoint:game.save.waypoints) delete waypoint;
}
std::cout << "],\"sharedFuelRows\":[";
serviceFirst=true;
for(int firstFuel:{0,60,100}) for(int stock:{0,1,2}) for(bool firstAuto:{false,true}) {
if(!serviceFirst) std::cout << ','; serviceFirst=false;
Craft firstCraft,secondCraft; Item item; Base base; ServiceGame game; Ufo hunter;
base.storage.count=stock;
firstCraft._base=secondCraft._base=&base; firstCraft.rule.fuelItem=secondCraft.rule.fuelItem=&item;
firstCraft.id=1; secondCraft.id=2; firstCraft._fuel=firstFuel; secondCraft._fuel=60;
firstCraft.autoPatrol=firstAuto;
firstCraft.distance=secondCraft.distance=Nautical(100);
std::vector<Craft*> active;
for(Craft* craft:{&firstCraft,&secondCraft}) {
const auto missing=craft->refuel();
if(missing.empty()) autoPatrol(craft,&game);
if(craft->_status=="STR_OUT" && craft->_damage<craft->_stats.damageMax) active.push_back(craft);
}
Craft* target=select(&hunter,active);
std::cout << '[' << firstFuel << ',' << stock << ',' << (firstAuto?"true":"false") << ','
<< firstCraft._fuel << ",\"" << firstCraft._status << "\"," << secondCraft._fuel << ",\"" << secondCraft._status
<< "\"," << base.storage.count << ',' << (target?target->id:0) << ']';
for(auto* waypoint:game.save.waypoints) delete waypoint;
}
std::cout << "],\"attractionRows\":[";
bool first = true;
for (int mode : {0,1}) for (bool landing : {false,true}) for (int capacity : {0,2,8}) for (int speed : {2000,6000}) for (int damage : {0,50}) {
if (!first) std::cout << ','; first = false;
Craft craft; craft.rule.landing = landing; craft.units = capacity; craft._stats.speedMax = speed; craft._damage = damage;
std::cout << '[' << mode << ',' << (landing ? "true" : "false") << ',' << capacity << ',' << speed << ',' << damage
<< ',' << craft.getHunterKillerAttraction(mode) << ']';
}
std::cout << "],\"selectionRows\":[";
first = true;
for (int mode : {0,1}) for (int variant : {0,1,2,3,4,5,6}) {
if (!first) std::cout << ','; first = false;
Craft a,b; a.id = 1; b.id = 2; a._stats.speedMax = 2000; b._stats.speedMax = 6000;
a.distance = Nautical(100); b.distance = Nautical(100);
Ufo ufo; ufo.mode = mode; std::vector<Craft*> crafts{&a,&b};
if (variant == 1) { ufo.original = &a; ufo.hunting = true; b._stats.speedMax = a._stats.speedMax; }
if (variant == 2) { ufo.original = &a; ufo.hunting = true; a.rule.hidden = true; b._missionComplete = true; }
if (variant == 3) { a._lowFuel = true; b.rule.hidden = true; }
if (variant == 4) { a.distance = Nautical(301); b.distance = Nautical(300); }
if (variant == 5) { ufo._stats.radarRange = 0; }
if (variant == 6) { ufo.original = &a; ufo.hunting = true; a._lowFuel = true; }
auto* selected = select(&ufo,crafts);
std::cout << '[' << mode << ',' << variant << ',' << (selected ? selected->id : 0) << ']';
}
std::cout << "],\"retargetRows\":[";
first = true;
for (int percent : {0,50,100,150}) {
if (!first) std::cout << ','; first = false;
Ufo ufo; ufo.rule.huntSpeed = percent; ufo.speed = 900;
auto* waypoint = new Waypoint(); waypoint->lon = 0.4; waypoint->lat = 0.2; ufo._dest = waypoint;
Craft craft; craft.lon = 0.8; craft.lat = 0.3;
ufo.setTargetedXcomCraft(&craft); int huntSpeed = ufo.speed;
Ufo escorted; escorted.lon = 1; escorted.lat = 0.5;
ufo.setEscortedUfo(&escorted);
int escortSpeed = ufo.speed;
ufo.resetOriginalDestination(true);
std::cout << '[' << percent << ',' << huntSpeed << ',' << escortSpeed << ',' << ufo._dest->getLongitude()
<< ',' << ufo._dest->getLatitude() << ',' << (ufo._isHunting ? "true" : "false") << ',' << (ufo._isEscorting ? "true" : "false") << ']';
delete ufo._dest;
}
std::cout << "],\"fastRows\":[";
first = true;
for (bool fast : {false,true}) for (int speed = 0; speed < 6; ++speed) {
if (!first) std::cout << ','; first = false;
FastSchedule schedule; schedule.mod.fast = fast; schedule._timeSpeed = speed;
std::cout << '[' << (fast ? "true" : "false") << ',' << speed << ',' << (schedule.due() ? "true" : "false") << ']';
}
std::cout << "],\"constructorRows\":[";
first=true;
for(int percent:{-1,0,1,50,100,200}) for(int mode:{-1,0,1,2}) for(int behavior:{-1,0,1,2}) for(int roll:{0,99}) {
if(!first) std::cout << ','; first=false; RNG::roll=roll; RNG::calls=0;
Ufo ufo(percent,mode,behavior);
std::cout << '[' << percent << ',' << mode << ',' << behavior << ',' << roll << ','
<< (ufo._isHunterKiller?"true":"false") << ',' << ufo._huntMode << ',' << ufo._huntBehavior << ',' << RNG::calls << ']';
}
std::cout << "],\"ignoredRows\":[";
first=true;
for(bool fuel:{false,true}) for(bool complete:{false,true}) {
if(!first) std::cout << ','; first=false; Craft craft; craft._lowFuel=fuel; craft._missionComplete=complete;
std::cout << '[' << (fuel?"true":"false") << ',' << (complete?"true":"false") << ',' << (craft.isIgnoredByHK()?"true":"false") << ']';
}
std::cout << "],\"spawnEscortRows\":[";
first=true;
for(bool hunter:{false,true}) for(bool requested:{false,true}) for(int variant:{0,1,2,3}) for(int status:{0,1,2,3,4}) for(int sourceStatus:{0,1}) {
if(!first) std::cout << ','; first=false;
Ufo ufo,firstTarget,secondTarget;
ufo.status=sourceStatus; ufo._isHunterKiller=hunter; ufo.lon=0.3; ufo.lat=0.1; ufo.speed=900;
ufo._dest=new Waypoint(); ufo._dest->lon=0.4; ufo._dest->lat=0.2;
firstTarget.uniqueId=10; firstTarget.status=status; firstTarget.lon=0.7; firstTarget.lat=0.3;
secondTarget.uniqueId=11; secondTarget.lon=0.9; secondTarget.lat=0.4;
SavedGame game;
if(variant!=0) game.ufos={&firstTarget,&secondTarget};
if(variant==1) firstTarget.mission.id=5;
if(variant==2) firstTarget._isHunterKiller=true;
spawnEscort(&ufo,game,requested);
auto* followed=dynamic_cast<Ufo*>(ufo._dest);
std::cout << '[' << (hunter?"true":"false") << ',' << (requested?"true":"false") << ',' << variant << ',' << status << ','
<< (ufo._isEscort?"true":"false") << ',' << (ufo._isEscorting?"true":"false") << ',' << (followed?followed->uniqueId:0) << ','
<< ufo.lon << ',' << ufo.lat << ',' << ufo.speed << ',';
if(ufo._origWaypoint) std::cout << ufo._origWaypoint->lon << ',' << ufo._origWaypoint->lat;
else std::cout << "null,null";
std::cout << ',' << ufo.status << ']';
if(ufo._origWaypoint) delete ufo._origWaypoint; else delete ufo._dest;
}
std::cout << "],\"spawnEscortSequenceRows\":[";
first=true;
for(bool ordinaryFirst:{false,true}) for(bool retarget:{false,true}) {
if(!first) std::cout << ','; first=false;
Ufo ufo,leader;
ufo._isHunterKiller=true; ufo.lon=0.3; ufo.lat=0.1; ufo.speed=550;
ufo._dest=new Waypoint(); ufo._dest->lon=0.4; ufo._dest->lat=0.2;
leader.uniqueId=10; leader.lon=0.7; leader.lat=0.3;
SavedGame game;
if(ordinaryFirst) game.ufos.push_back(&leader);
spawnEscort(&ufo,game,true);
game.ufos.push_back(&ufo);
if(!ordinaryFirst) game.ufos.push_back(&leader);
if(retarget) retargetEscort(&ufo,game);
auto* followed=dynamic_cast<Ufo*>(ufo._dest);
std::cout << '[' << (ordinaryFirst?"true":"false") << ',' << (retarget?"true":"false") << ','
<< (ufo._isEscorting?"true":"false") << ',' << (followed?followed->uniqueId:0) << ','
<< ufo.lon << ',' << ufo.lat << ',' << ufo.speed << ',';
if(ufo._origWaypoint) std::cout << ufo._origWaypoint->lon << ',' << ufo._origWaypoint->lat;
else std::cout << "null,null";
std::cout << ']';
if(ufo._origWaypoint) delete ufo._origWaypoint; else delete ufo._dest;
}
std::cout << "]}\n";
}

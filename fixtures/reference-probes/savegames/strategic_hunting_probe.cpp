// Asset-free target selection collaborators; inserted pinned methods remain verbatim.
#include <algorithm>
#include <climits>
#include <cmath>
#include <iostream>
#include <vector>
#include <stdexcept>
#include <string>
#include <map>
#include <cstdint>
namespace OpenXcom {
using Exception = std::runtime_error; using RuleCraft=void;
struct Craft; struct Ufo; struct Base; struct SavedGame; struct Item; struct ResearchMod; struct ProductionDeliveryCaller; struct ResearchProject;
// PRODUCTION_ENUM
const int BT_NONE=0;
struct RNG { static inline int roll=0, calls=0, suppliedChoice=-1;
static bool percent(int value) { ++calls; return roll < value; }
static int generate(int,int) { ++calls; return suppliedChoice >= 0 ? suppliedChoice : calls % 2 == 0 ? 1 : 0; } };
double Nautical(double x) { return x / 60.0 * 3.14159265358979323846 / 180.0; }
struct Production {
struct Rule { int time=4,cost=0,space=0; int& _cost=cost; std::map<const Item*,int> requiredItems; std::map<const void*,int> requiredCrafts;
std::string getName() const { return "PRODUCT"; }
int getRequiredSpace() const { return space; }
int getManufactureTime() const { return time; } int getManufactureCost() const { return cost; }
bool haveEnoughMoneyForOneMoreUnit(int64_t funds) const;
const auto& getRequiredItems() const { return requiredItems; } const auto& getRequiredCrafts() const { return requiredCrafts; }
std::string person=""; std::string getSpawnedPersonType() const { return person; }
} rule;
const Rule* _rules=&rule; int _timeSpent=0,_engineers=0,_amount=20; bool infinite=false;
int completedUnits=0; bool _isFallback=false;
bool isQueuedOnly() const { return _timeSpent==0&&_engineers==0; }
int getAssignedEngineers() const { return _engineers; } void setAssignedEngineers(int value) { _engineers=value; }
int getAmountProduced() const; bool haveEnoughMoneyForOneMoreUnit(SavedGame*) const;
bool haveEnoughLivingSpaceForOneMoreUnit(Base*); bool haveEnoughMaterialsForOneMoreUnit(Base*,const ResearchMod*) const;
void startItem(Base*,SavedGame*,const ResearchMod*) const;
int scalarStep(Base*,SavedGame*,const ResearchMod*,ProductionDeliveryCaller&);
ProductionDeliveryCaller* delivery=nullptr; productionProgress_e step(Base*,SavedGame*,const ResearchMod*,void*);
const Rule* getRules() const { return _rules; }
bool getInfiniteAmount() const { return infinite; }
int requested() {
// PRODUCTION_COUNT
}
};
using RuleManufacture=Production::Rule;
// PRODUCTION_MONEY_RULE
// PRODUCTION_AMOUNT
struct Target { double distance = 0, lon = 0, lat = 0; virtual ~Target();
std::vector<Craft*> craftFollowers; std::vector<Ufo*> ufoFollowers; const auto& getCraftFollowers() const { return craftFollowers; } const auto& getUfoFollowers() const { return ufoFollowers; }
double getLongitude() const { return lon; } double getLatitude() const { return lat; }
void setLongitude(double value) { lon = value; } void setLatitude(double value) { lat = value; } };
struct Waypoint : Target { int id=0; int getId() const { return id; } void setId(int value) { id=value; } };
struct Item { std::string type="SUPPLY"; int battle=0; int clipSize=1,sellCost=3; bool _recover=true,_recoverCorpse=true,corpse=false; bool isRecoverable() const; bool isCorpseRecoverable() const; std::string getType() const { return type; } int getClipSize() const { return clipSize; } int getBattleType() const { return battle; } int getSellCost() const { return sellCost; } int getSellCostAdjusted(const Base*,const SavedGame*) const; };
using RuleItem=Item;
// CORPSE_RECOVERY_FLAGS
namespace ModScript { struct SellCostItem {}; int price(const Item*,int,int,const Item*,const SavedGame*,int); template<class Tag> int scriptFunc2(const Item* owner,int adjusted,int cost,const Item* item,const SavedGame* save,int coefficient) { return price(owner,adjusted,cost,item,save,coefficient); } }
struct Storage { int count=0,corpses=0; std::map<std::string,int> extra; int getItem(const Item* item) const { if(item->type!="SUPPLY"&&!item->corpse) { auto it=extra.find(item->type); return it==extra.end()?0:it->second; } return item->corpse?corpses:count; }
void removeItem(const Item* item,int qty=1) { if(item->type!="SUPPLY"&&!item->corpse) extra[item->type]-=qty; else (item->corpse?corpses:count)-=qty; } void addItem(const Item* item,int qty=1) { if(item->type!="SUPPLY"&&!item->corpse) extra[item->type]+=qty; else (item->corpse?corpses:count)+=qty; } };
struct Craft; struct Transfer; struct RuleSoldier { void* getDefaultArmor() const { return nullptr; } };
namespace YAML { struct YamlRootNodeReader { bool overlay; YamlRootNodeReader(bool value,const char*):overlay(value) {} }; }
struct Soldier { static inline int generated=0; std::string name="Alex Example"; int nationality=0,id=0;
Soldier()=default; Soldier(RuleSoldier*,void*,int value,int identity):nationality(value),id(identity) { ++generated; }
std::string getName() const { return name; } void setName(const std::string& value) { name=value; }
void genName() { name="Alex Example"; } void calcStatString(int,bool) {}
template<class...T> void load(const YAML::YamlRootNodeReader& reader,T...) { if(reader.overlay) name="TEMPLATE NAME"; }
};
struct Base : Target { Storage storage; std::vector<Craft*> crafts; std::vector<Craft*>& _crafts=crafts; std::vector<Soldier*> soldiers; std::vector<Transfer*> transfers; std::vector<Transfer*>* getTransfers() { return &transfers; } int scientists=0,engineers=0; int& _engineers=engineers;
std::vector<Production*> _productions; auto& getProductions() { return _productions; }
std::vector<ResearchProject*> research; auto& getResearch() { return research; } void removeResearch(ResearchProject* project) { std::erase(research,project); }
int freeWorkshops=0; int getAvailableWorkshops() const { return freeWorkshops; } int getUsedWorkshops() const; int getFreeWorkshops() const; void removeProduction(Production*); int getAvailableEngineers() const { return engineers; }
int quarters=100; int getAvailableQuarters() const { return quarters; } int getUsedQuarters() const;
int getTotalSoldiers() const; int getTotalScientists() const; int getTotalEngineers() const;
int getCraftCountForProduction(const RuleCraft*) const;
std::vector<int> consumedOrder; void removeCraft(Craft* craft,bool);
Storage* getStorageItems() { return &storage; } std::vector<Craft*>* getCrafts() { return &crafts; }
std::vector<Soldier*>* getSoldiers() { return &soldiers; }
int getScientists() const { return scientists; } void setScientists(int value) { scientists=value; }
int getEngineers() const { return engineers; } void setEngineers(int value) { engineers=value; } };
namespace Collections { template<class T,class P> void deleteIf(std::vector<T*>& values,int,P predicate) { for(auto it=values.begin();it!=values.end();) { if(predicate(*it)) { delete *it; it=values.erase(it); } else ++it; } } }
// PRODUCTION_BASE_METHODS
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
std::vector<std::string> increases,decreases;
const auto& getIncreaseCounter() const { return increases; } const auto& getDecreaseCounter() const { return decreases; }
std::vector<std::pair<const ResearchRule*,std::vector<const ResearchRule*>>> protectedFree;
const auto& getDisabled() const { return disables; } const auto& getReenabled() const { return reenables; }
const auto& getUnlocked() const { return unlocks; } const auto& getGetOneFree() const { return free; }
const auto& getGetOneFreeProtected() const { return protectedFree; } const auto& getRequirements() const { return requirements; }
int getCost() const; int getPoints() const;
const ResearchRule* lookup=nullptr; const ResearchRule* getLookup() const;
bool isRepeatable() const { return repeatable; } bool sequentialGetOneFree() const { return sequential; }

bool holding=false,returns=false,destroy=false,needs=false; Item* needed=nullptr; std::string spawned=""; int count=0; std::vector<std::string> list;
bool needItem() const { return needs; }
bool isHoldingNeededItem() const { return holding; } bool returnItem() const { return returns; }
bool destroyItem() const { return destroy; } const RuleItem* getNeededItem() const;
std::string getName() const { return name; } std::string getSpawnedItem() const { return spawned; }
int getSpawnedItemCount() const { return count; } const auto& getSpawnedItemList() const { return list; }
};
using RuleResearch=ResearchRule;
// RESEARCH_RULE_GETTERS
struct ResearchProject { const RuleResearch* rule=nullptr; bool finished=false;
const RuleResearch* getRules() const { return rule; } bool isFinished() const { return finished; } };
struct ResearchCorpse : Item {};
struct ResearchArmor { const RuleItem* _corpseGeo=nullptr; const RuleItem* getCorpseGeoscape() const; };
using Armor=ResearchArmor;
// ARMOR_CORPSE
struct ResearchUnit { const ResearchArmor* armor=nullptr; const ResearchArmor* getArmor() const { return armor; } };
struct ResearchMod { std::vector<ResearchRule*> rules; mutable Item supply, clip; bool extraItems=false; ResearchUnit* matchedUnit=nullptr; std::string matchName="CORPSE_TOPIC"; Item* getItem(const std::string& id) const { return id=="SUPPLY" ? &supply : extraItems && id=="CLIP" ? &clip : nullptr; } ResearchUnit* getUnit(const std::string& name,bool) const { return name==matchName?matchedUnit:nullptr; } mutable RuleSoldier soldierRule; RuleSoldier* getSoldier(const std::string&) const { return &soldierRule; } Soldier* genSoldier(SavedGame*,const RuleSoldier*,int) const; int getScriptGlobal() const { return 0; } int getStatStrings() const { return 0; } int getPsiRequirements() const { return 0; } };
using Mod=ResearchMod;
enum class DiscoverySourceType { FREE_AFTER };
struct ResearchDiaryEntry { explicit ResearchDiaryEntry(const RuleResearch*) {} void setDate(int) {}
struct { DiscoverySourceType type; const RuleResearch* research; std::string name; } source; };
struct Options { static inline bool psiStrengthEval=false; static inline bool retainCorpses=true; };
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
void dailyCorpse(const RuleResearch* research,Base* xbase) { const Mod* mod=_mod;
// RESEARCH_DAILY_CORPSE
}
};
struct MovingTarget : Target { Target* _dest = nullptr; void setDestination(Target* target) { _dest = target; } };
struct CraftBirthRule { bool fixed=false; int fuelMax=100; Item* fuelItem=nullptr; std::string type="PRODUCED_CRAFT"; std::string getType() const { return type; } };
struct Cargo { std::map<const RuleItem*,int> values; const auto* getContents() const { return &values; } };
struct Craft : MovingTarget {
Craft()=default;
Craft(const CraftBirthRule* rules,Base* owner,int identity) { id=identity; birthFixed=rules->fixed; rule.fuelItem=rules->fuelItem; _stats.fuelMax=rules->fuelMax; autoPatrol=false; setBase(owner); }
void initFixedWeapons(const Mod*) { if(birthFixed) { auto* weapon=new CraftWeapon(); weapon->rule.ammoMax=10; _weapons.push_back(weapon); } }
bool birthFixed=false;
struct Rule { bool landing = false, hidden = false, autoPatrol=true; Item* fuelItem=nullptr; int refuelRate=40;
bool getAllowLanding() const { return landing; } bool isUndetectable() const { return hidden; }
const Item* getRefuelItem() const { return fuelItem; } int getRefuelRate() const { return refuelRate; }
int getRepairRate() const { return 10; }
bool patrolFree=false; bool patrolWithoutFuel() const { return patrolFree; }
bool canAutoPatrol() const { return autoPatrol; } } rule;
const Rule* _rules = &rule;
struct Stats { int speedMax = 2000, damageMax = 100, fuelMax=100; } _stats;
Base base; Base* _base=&base; int _fuel=0, _excessFuel=0, _takeoff=0, speed=0; int& _speed=speed;
double& _lon=lon; double& _lat=lat;
std::string _status="STR_REFUELLING"; bool autoPatrol=true;
std::vector<CraftWeapon*> _weapons;
void setFuel(int); std::string refuel(); void setDestination(Target*); void repair(); const RuleItem* rearm();
int getFuelConsumption(int,int) const; void consumeFuel(int); void returnToBase();
void checkup(); bool think(); void setBase(Base*,bool move=true);
// Controlled at-home movement collaborators; globe equality has its own capture.
bool atDestination=true; bool reachedDestination() const { return atDestination; }
void move() {} void resetMeetPoint() {} void setInterceptionOrder(int) {}
void reuseItem(const RuleItem*);
Cargo cargo; Cargo* _items=&cargo; void unloadItems() {
// PRODUCTION_CRAFT_UNLOAD_ITEMS
}
int _fuelLimit=0; int getFuel() const { return _fuel; } int getFuelLimit() const { return _fuelLimit; }
int getSpeed() const { return speed; } void setLowFuel(bool value) { _lowFuel=value; }
Target* getDestination() const { return _dest; } double getDistance(Target*) const { return 0; }
void setDamage(int value) { _damage=std::max(0,value); }
void setSpeed(int value) { speed=value; }
std::string getStatus() const { return _status; } void setStatus(const std::string& value) { _status=value; }
bool getIsAutoPatrolling() const { return autoPatrol; }
double getLongitudeAuto() const { return 0.8; } double getLatitudeAuto() const { return 0.3; }
int _damage = 0, units = 0, id = 0; bool ignored = false, _lowFuel = false, _missionComplete = false;
int getMaxUnitsClamped() const { return units; } int getNumTotalUnits() const { return units; }
bool isIgnoredByHK() const; bool isDestroyed() const { return false; }
bool getLowFuel() const { return _lowFuel; }
bool& _mission=_missionComplete;
bool getMissionComplete() const { return _missionComplete; }
const Rule* getRules() const { return _rules; }
int getHunterKillerAttraction(int huntMode) const;
};
void Base::removeCraft(Craft* craft,bool unload) { consumedOrder.push_back(craft->id); if(unload) craft->unloadItems(); std::erase(crafts,craft); }
// PRODUCTION_CRAFT_COUNT
// REFUEL_METHODS
struct Transfer {
explicit Transfer(int hours=1):_hours(hours) {}
void setItems(const Item* item,int qty=1) { _itemId=item; _itemQty=qty; }
void setCraft(Craft* craft) { _craft=craft; }
void setScientists(int qty) { _scientists=qty; } void setEngineers(int qty) { _engineers=qty; }
void setSoldier(Soldier* soldier) { _soldier=soldier; } Soldier* getSoldier() const { return _soldier; }
int getType() const { return _soldier?1:0; }
int _hours=1,_itemQty=0,_scientists=0,_engineers=0; Soldier* _soldier=nullptr; Craft* _craft=nullptr;
const Item* _itemId=nullptr; bool _delivered=false;
void advance(Base*);
int getHours() const;
};
int Base::getTotalScientists() const { int count=scientists; for(auto* t:transfers) count+=t->_scientists; return count; }
int Base::getTotalSoldiers() const { int count=(int)soldiers.size(); for(auto* t:transfers) if(t->_soldier) ++count; return count; }
int Base::getTotalEngineers() const { int count=engineers; for(auto* t:transfers) count+=t->_engineers; for(auto* p:_productions) count+=p->getAssignedEngineers(); return count; }
// PRODUCTION_QUARTERS
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
void resetOriginalDestination(bool); void resetOriginalDestination(Craft*); void setDestination(Target*);
Craft* original = nullptr; int mode = 0; bool hunting = false;
bool isHunting() const { return hunting; }
Craft* getTargetedXcomCraft() const;
int getHuntMode() const { return mode; }
double getDistance(Target* target) const { return target->distance; }
bool insideRadarRange(Target* target) const;
};
// RADAR
// TARGET_DESTRUCTOR
// RETARGET_METHODS
struct GameTime {};
struct SavedGame { std::vector<Ufo*> ufos; std::vector<Ufo*>& _ufos=ufos; std::vector<Waypoint*> waypoints;
void stopHuntingXcomCraft(Craft*);
GameTime clockValue; GameTime* clock=&clockValue; GameTime* getTime() const;
std::vector<Base*> bases; auto* getBases() { return &bases; }
int selectSoldierNationalityByLocation(const Mod*,const RuleSoldier*,const Target*) const { return 0; }
bool isResearched(int) const { return false; }
std::map<std::string,int> _ids; void increaseCustomCounter(const std::string&); void decreaseCustomCounter(const std::string&);
void applyPrimaryCounters(const RuleResearch* myResearchRule) {
// RESEARCH_PRIMARY_COUNTERS
}
std::vector<int64_t> _funds={100},_incomes={0},_expenditures={0}; int64_t& funds=_funds.back(); int sellCoefficient=100;
int priceScript=-1,priceSecond=0,priceHour=0,priceDays=0,priceMonths=0,priceDifficulty=0; bool priceResearched=false;
int64_t getFunds() const { return funds; } void setFunds(int64_t); int getSellPriceCoefficient() const { return sellCoefficient; }
std::vector<const RuleResearch*> _discovered; std::map<std::string,int> statuses; int _time=0; std::vector<int> _researchScores={0}; int& researchScore=_researchScores.back();
bool isResearchRuleStatusDisabled(const std::string& id) const { auto found=statuses.find(id); return found!=statuses.end()&&found->second==2; }
void setResearchRuleStatus(const std::string& id,int value) { statuses[id]=value; }
bool isResearched(const RuleResearch* rule,bool) const { return std::find(_discovered.begin(),_discovered.end(),rule)!=_discovered.end(); }
bool isResearched(const std::vector<const RuleResearch*>& topics,bool,bool disabled) const { return std::all_of(topics.begin(),topics.end(),[&](auto* r){ return isResearched(r,false)||(disabled&&isResearchRuleStatusDisabled(r->getName())); }); }
void sortReserchVector(std::vector<const RuleResearch*>&) {} void removePoppedResearch(const RuleResearch*) {}
void addResearchDiaryEntry(ResearchDiaryEntry* entry) { delete entry; } void addResearchScore(int);
void removeDiscoveredResearch(const RuleResearch* rule) { std::erase(_discovered,rule); }
void getAvailableResearchProjects(std::vector<RuleResearch*>& available,const Mod* mod,Base* base) const {
std::map<std::string,ResearchRule*> ordered; for(auto* rule:mod->rules) ordered[rule->name]=rule;
for(const auto& pair:ordered) { auto* rule=pair.second; if(base) {
// RESEARCH_ACTIVE_AVAILABILITY
// RESEARCH_NEED_ITEM_AVAILABILITY
} if(!isResearchRuleStatusDisabled(rule->name)&&!isResearched(rule,false)&&
std::all_of(rule->dependencies.begin(),rule->dependencies.end(),[&](auto* r){return isResearched(r,false);})&&
std::all_of(rule->requirements.begin(),rule->requirements.end(),[&](auto* r){return isResearched(r,false);})) available.push_back(rule); }
}
void addFinishedResearch(const RuleResearch*,const Mod*,Base*,bool score=true);
const RuleResearch* selectGetOneFree(const RuleResearch*);
bool hasUndiscoveredGetOneFree(const RuleResearch*,bool) const;
bool hasUndiscoveredProtectedUnlock(const RuleResearch*) const;

std::vector<Ufo*>* getUfos() { return &ufos; }
std::vector<Waypoint*>* getWaypoints() { return &waypoints; }
int craftCounter=10,soldierCounter=20; int getId(const std::string& type) { return type=="STR_SOLDIER"?soldierCounter++:type=="PRODUCED_CRAFT"?craftCounter++:8+(int)waypoints.size(); } };
// STOP_HUNTING_CRAFT
// PRICE_TIME_POINTER
const int TRANSFER_SOLDIER=1;
// PRODUCTION_SOLDIER_GENERATION
struct ProductionCraftRule { CraftBirthRule craft; std::vector<int> times; const CraftBirthRule* getProducedCraft() const { return &craft; } const auto& getTransferTimes() const { return times; } };
struct ProductionCraftCaller { const ProductionCraftRule* _rules; void complete(Base* b,SavedGame* g,const Mod* m) {
// PRODUCTION_CRAFT_DELIVERY
} };
int ModScript::price(const Item*,int adjusted,int,const Item*,const SavedGame* save,int) {
switch(save->priceScript) { case 0:return save->priceSecond; case 1:return save->priceHour+save->priceDays; case 2:return save->priceDays; case 3:return save->priceDifficulty+save->priceMonths; case 4:return save->priceResearched?save->priceDays:save->priceHour; default:return adjusted; }
}
// PRODUCTION_SALE_PRICE
// RESEARCH_COUNTER_METHODS
// PRODUCTION_ACCOUNTING
// RESEARCH_SCORE
// RESEARCH_DISCOVERY_METHODS
struct ProductionItemRule {
std::vector<int> times; std::map<const RuleItem*,int> produced; std::vector<std::pair<int,std::map<const RuleItem*,int>>> random; int points=0;
int getPoints() const { return points; } std::string person=""; std::string getSpawnedPersonType() const { return person; }
bool soldierTemplate=false; std::string personName="";
bool getSpawnedSoldierTemplate() const { return soldierTemplate; } const std::string& getSpawnedPersonName() const { return personName; }
const auto& getTransferTimes() const { return times; } const auto& getProducedItems() const { return produced; }
const auto& getRandomProducedItems() const { return random; }
};
struct ProductionDeliveryCaller {
const ProductionItemRule* _rules; bool sell=false; std::map<std::string,int> _randomProductionInfo;
const ProductionCraftRule* craftBirth=nullptr;
bool getSellItems() const { return sell; }
void deliver(Base* b,SavedGame* g) {
ResearchMod soldierMod; const Mod* m=&soldierMod; struct Language { std::string getString(const std::string& value) const { return value; } } language; auto* lang=&language;
if(craftBirth) ProductionCraftCaller{craftBirth}.complete(b,g,nullptr);
else {
// PRODUCTION_DELIVERY
}
// PRODUCTION_RANDOM_DELIVERY
// PRODUCTION_PERSONNEL
// PRODUCTION_SCORE
}
};
// PRODUCTION_INPUT_METHODS
int Production::scalarStep(Base* b,SavedGame* g,const Mod* m,ProductionDeliveryCaller& delivery) {
// PRODUCTION_STAFFING
const int done=getAmountProduced(); const int produced=requested(); int count=0;
if(produced>0) { do { delivery.deliver(b,g); ++completedUnits;
// PRODUCTION_CONTINUATION
}
productionProgress_e Production::step(Base* b,SavedGame* g,const ResearchMod* m,void*) { return static_cast<productionProgress_e>(scalarStep(b,g,m,*delivery)); }
struct ProductionHourlyCaller {
struct Game { SavedGame* save; Mod* mod; SavedGame* getSavedGame() { return save; } Mod* getMod() { return mod; } void* getLanguage() { return nullptr; } } game; Game* _game=&game;
struct ProductionCompleteState { template<class...T> ProductionCompleteState(T...) {} };
std::string tr(const std::string& name) { return name; } void popup(ProductionCompleteState* state) { delete state; }
void advance(Base* xbase) {
// PRODUCTION_HOURLY_CALLER
}
};
struct ServiceGame { SavedGame save; SavedGame* getSavedGame() { return &save; } };
void removeDisabled(ServiceGame* _game) {
// DAILY_DISABLED_RESEARCH
}
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
namespace OpenXcom {
struct AlienBase : Target { bool discovered=false; bool isDiscovered() const { return discovered; } };
struct Texture {};
struct ArrivalGlobe { Texture* getTexture(int) { return nullptr; }
void getPolygonTextureAndShade(double,double,int* texture,int* shade) { *texture=0; *shade=0; } };
struct ArrivalMod { ArrivalGlobe globe; ArrivalGlobe* getGlobe() { return &globe; } };
struct ArrivalGame { ArrivalMod mod; ArrivalMod* getMod() { return &mod; } };
struct ConfirmLandingState { ConfirmLandingState(Craft*,Texture*,Texture*,int) {} };
struct AlienBaseArrivalCaller {
ArrivalGlobe globe; ArrivalGlobe* _globe=&globe; ArrivalGame game; ArrivalGame* _game=&game;
bool landing=false; void timerReset() {} void popup(ConfirmLandingState* value) { landing=true; delete value; }
void arrive(Craft* xcraft,AlienBase* b,bool suppressLanding) {
// ALIEN_BASE_ARRIVAL
}
void arriveEscort(Craft* xcraft,Craft* x) {
// CRAFT_ESCORT_ARRIVAL
}
};
}
int main() {
using namespace OpenXcom;
std::cout << "{\"productionScoreRows\":[";
bool scoreFirst=true;
for(int points:{-7,0,7}) for(int assigned:{1,6}) for(bool infinite:{false,true}) for(int stock:{0,1}) {
if(!scoreFirst) std::cout << ','; scoreFirst=false;
Base base; ServiceGame game; ResearchMod mod; Item item; Production progress; Craft craft; Ufo hunter; FuelCaller fuelCaller;
craft.id=1; craft._base=&base; base.crafts={&craft}; craft.rule.fuelItem=&item; craft._fuel=60; craft.distance=Nautical(100);
ProductionItemRule rule; rule.produced={{&item,1}}; rule.points=points; ProductionDeliveryCaller delivery{&rule};
progress.rule.time=2; progress.rule.requiredItems={{&item,2}}; progress._timeSpent=1; progress._engineers=assigned; progress._amount=2; progress.infinite=infinite;
base.storage.count=stock; game.save.researchScore=13;
int stop=progress.scalarStep(&base,&game.save,&mod,delivery);
auto missing=craft.refuel(); if(missing.empty()) autoPatrol(&craft,&game);
std::vector<Craft*> active; if(craft._status=="STR_OUT") { fuelCaller.fuel(&craft); active.push_back(&craft); }
auto* target=select(&hunter,active);
std::cout << '[' << points << ',' << assigned << ',' << (infinite?"true":"false") << ',' << stock << ',' << progress.completedUnits << ',' << game.save.researchScore << ',' << base.storage.count << ',' << stop << ",\"" << craft._status << "\"," << craft._fuel << ',' << (target?target->id:0) << ']';
for(auto* waypoint:game.save.waypoints) delete waypoint;
}
std::cout << "],\"productionDeterministicRandomRows\":[";
scoreFirst=true;
for(bool sell:{false,true}) for(int delay:{0,2}) for(int qty:{0,2}) for(bool duplicate:{false,true}) for(int assigned:{1,6}) for(bool infinite:{false,true}) {
if(!scoreFirst) std::cout << ','; scoreFirst=false;
Base base; ServiceGame game; ResearchMod mod; Item item; Production progress; Craft craft; Ufo hunter; FuelCaller fuelCaller;
craft.id=1; craft._base=&base; base.crafts={&craft}; craft.rule.fuelItem=&item; craft._fuel=60; craft.distance=Nautical(100);
ProductionItemRule rule; rule.times={delay}; rule.produced={{&item,1}}; rule.random={{1,{{&item,qty}}}}; if(duplicate) rule.random.push_back({1,{{&item,qty}}});
ProductionDeliveryCaller delivery{&rule}; delivery.sell=sell;
progress.rule.time=2; progress.rule.requiredItems={{&item,2}}; progress._timeSpent=1; progress._engineers=assigned; progress._amount=2; progress.infinite=infinite;
base.storage.count=1; RNG::suppliedChoice=duplicate?2:1;
int stop=progress.scalarStep(&base,&game.save,&mod,delivery); RNG::suppliedChoice=-1;
auto missing=craft.refuel(); if(missing.empty()) autoPatrol(&craft,&game);
std::vector<Craft*> active; if(craft._status=="STR_OUT") { fuelCaller.fuel(&craft); active.push_back(&craft); }
auto* target=select(&hunter,active);
std::cout << '[' << (sell?"true":"false") << ',' << delay << ',' << qty << ',' << (duplicate?"true":"false") << ',' << assigned << ',' << (infinite?"true":"false") << ',' << progress.completedUnits << ',' << stop << ',' << base.storage.count << ',' << base.transfers.size() << ',' << delivery._randomProductionInfo["SUPPLY"] << ",\"" << craft._status << "\"," << craft._fuel << ',' << (target?target->id:0) << ']';
for(auto* waypoint:game.save.waypoints) delete waypoint; for(auto* transfer:base.transfers) delete transfer;
}
std::cout << "],\"productionVariantRows\":[";
scoreFirst=true;
for(bool sell:{false,true}) for(int delay:{0,2}) for(int variant:{0,1,2}) for(int choice:{1,2}) {
if(!scoreFirst) std::cout << ','; scoreFirst=false;
Base base; ServiceGame game; ResearchMod mod; Item item,a,b; a.type="INERT_A"; b.type="INERT_B"; a.battle=b.battle=1;
Production progress; Craft craft; Ufo hunter; FuelCaller fuelCaller;
craft.id=1; craft._base=&base; base.crafts={&craft}; craft.rule.fuelItem=&item; craft._fuel=60; craft.distance=Nautical(100);
ProductionItemRule rule; rule.times={delay}; rule.produced={{&item,1}};
rule.random={{1,{{&item,2},{&a,1}}},{1,{{&item,2},{variant==0?&b:&a,variant==2?0:2}}}};
ProductionDeliveryCaller delivery{&rule}; delivery.sell=sell;
progress.rule.time=2; progress.rule.requiredItems={{&item,2}}; progress._timeSpent=1; progress._engineers=1; progress._amount=2; progress.infinite=true;
base.storage.count=1; RNG::suppliedChoice=choice;
int stop=progress.scalarStep(&base,&game.save,&mod,delivery); RNG::suppliedChoice=-1;
auto missing=craft.refuel(); if(missing.empty()) autoPatrol(&craft,&game);
std::vector<Craft*> active; if(craft._status=="STR_OUT") { fuelCaller.fuel(&craft); active.push_back(&craft); }
auto* target=select(&hunter,active);
std::cout << '[' << (sell?"true":"false") << ',' << delay << ',' << variant << ',' << choice << ',' << progress.completedUnits << ',' << stop << ',' << base.storage.count << ',' << base.storage.getItem(&a) << ',' << base.storage.getItem(&b) << ',' << base.transfers.size() << ',' << delivery._randomProductionInfo["SUPPLY"] << ',' << delivery._randomProductionInfo["INERT_A"] << ',' << delivery._randomProductionInfo["INERT_B"] << ",\"" << craft._status << "\"," << craft._fuel << ',' << (target?target->id:0) << ']';
for(auto* waypoint:game.save.waypoints) delete waypoint; for(auto* transfer:base.transfers) delete transfer;
}
std::cout << "],\"productionScopedMaterialRows\":[";
scoreFirst=true;
for(int mode:{0,1,2,3,4}) for(int choice:{1,2}) {
if(!scoreFirst)std::cout<<',';scoreFirst=false;
Base base; ServiceGame game; ResearchMod mod; Item item,a,b; a.type="INERT_A"; b.type="INERT_B"; a.battle=b.battle=1;
Production progress,other; Craft craft; Ufo hunter; FuelCaller fuelCaller;
craft.id=1; craft._base=&base; base.crafts={&craft}; craft.rule.fuelItem=&item; craft._fuel=60; craft.distance=Nautical(100);
ProductionItemRule rule; rule.produced={{&item,1}}; rule.random={{1,{{&item,2},{&a,1}}},{1,{{&item,2},{&b,2}}}}; ProductionDeliveryCaller delivery{&rule};
progress.rule.time=2; progress.rule.requiredItems=mode==0?std::map<const Item*,int>{{&a,1}}:std::map<const Item*,int>{{&item,2}};
progress._timeSpent=1;progress._engineers=1;progress._amount=mode==0?1:2;progress.infinite=mode!=0;
ProductionItemRule otherRule;otherRule.produced={{&item,0}};ProductionDeliveryCaller otherDelivery{&otherRule};other.rule.time=2;other.rule.requiredItems={{&a,1}};
other._timeSpent=1;other._engineers=mode==1||mode==4?0:1;other._amount=mode==2?1:3;other._isFallback=mode==4;
base.freeWorkshops=6;base.engineers=mode==4?1:0;base._productions={&progress};if(mode!=0)base._productions.push_back(&other);
base.storage.count=1;RNG::suppliedChoice=choice;int stop=progress.scalarStep(&base,&game.save,&mod,delivery);RNG::suppliedChoice=-1;
int otherStop=mode==0?-1:other.scalarStep(&base,&game.save,&mod,otherDelivery);
auto missing=craft.refuel();if(missing.empty())autoPatrol(&craft,&game);
std::vector<Craft*> active;if(craft._status=="STR_OUT"){fuelCaller.fuel(&craft);active.push_back(&craft);}auto* target=select(&hunter,active);
std::cout << '[' << mode << ',' << choice << ',' << progress.completedUnits << ',' << stop << ',' << other.completedUnits << ',' << otherStop
<< ',' << base.storage.count << ',' << base.storage.getItem(&a) << ',' << base.storage.getItem(&b) << ',' << other._engineers << ",\"" << craft._status << "\"," << craft._fuel << ',' << (target?target->id:0) << ']';
for(auto* waypoint:game.save.waypoints)delete waypoint;
}
std::cout << "],\"productionScopedCraftRows\":[";
scoreFirst=true;
for(int mode:{0,1,2}) for(int choice:{1,2}) {
if(!scoreFirst)std::cout<<',';scoreFirst=false;
Base base;ServiceGame game;ResearchMod mod;Item item,a,b;a.type="INERT_A";b.type="INERT_B";a.battle=b.battle=1;
Production progress,other;Craft craft;Ufo hunter;FuelCaller fuelCaller;
craft.id=1;craft._base=&base;base.crafts={&craft};craft.rule.fuelItem=&item;craft._fuel=60;craft.distance=Nautical(100);
ProductionItemRule rule;rule.produced={{&item,1}};rule.random={{1,{{&item,2},{&a,1}}},{1,{{&item,2},{&b,2}}}};ProductionDeliveryCaller delivery{&rule};
progress.rule.time=2;progress.rule.requiredItems={{&item,2}};progress._timeSpent=1;progress._engineers=1;progress._amount=2;progress.infinite=true;
ProductionCraftRule birth;birth.craft.fuelItem=&a;birth.times={0,0,mode==1?2:0};ProductionItemRule otherRule;ProductionDeliveryCaller otherDelivery{&otherRule};otherDelivery.craftBirth=&birth;
other.rule.time=mode==1?1:100;other._timeSpent=0;other._engineers=mode==2?0:1;other._amount=1;other._isFallback=mode==2;
base.freeWorkshops=6;base.engineers=mode==2?2:0;base._productions={&progress,&other};base.storage.count=1;
RNG::suppliedChoice=choice;int stop=progress.scalarStep(&base,&game.save,&mod,delivery);RNG::suppliedChoice=-1;int otherStop=other.scalarStep(&base,&game.save,&mod,otherDelivery);
auto missing=craft.refuel();if(missing.empty())autoPatrol(&craft,&game);
std::vector<Craft*> active;if(craft._status=="STR_OUT"){fuelCaller.fuel(&craft);active.push_back(&craft);}auto* target=select(&hunter,active);
std::cout << '[' << mode << ',' << choice << ',' << progress.completedUnits << ',' << stop << ',' << other.completedUnits << ',' << otherStop
<< ',' << base.storage.count << ',' << base.storage.getItem(&a) << ',' << base.storage.getItem(&b) << ',' << other._engineers << ',' << base.crafts.size()-1 << ',' << base.transfers.size()
<< ',' << (base.transfers.empty()?0:base.transfers.front()->getHours()) << ",\"" << craft._status << "\"," << craft._fuel << ',' << (target?target->id:0) << ']';
for(auto* transfer:base.transfers){delete transfer->_craft;delete transfer;}for(auto* waypoint:game.save.waypoints)delete waypoint;
}
std::cout << "],\"productionFuelQuantityRows\":[";
scoreFirst=true;
for(int minimum:{0,1}) for(int stock:{0,1}) for(int choice:{1,2}) {
if(!scoreFirst)std::cout<<',';scoreFirst=false;
Base base;ServiceGame game;ResearchMod mod;Item item;Production progress;Craft craft;Ufo hunter;FuelCaller fuelCaller;
craft.id=1;craft._base=&base;base.crafts={&craft};craft.rule.fuelItem=&item;craft._fuel=60;craft.distance=Nautical(100);
ProductionItemRule rule;rule.produced={{&item,0}};rule.random={{1,{{&item,minimum}}},{1,{{&item,2}}}};ProductionDeliveryCaller delivery{&rule};
progress.rule.time=1;progress._timeSpent=0;progress._engineers=1;progress._amount=3;progress.infinite=true;base.storage.count=stock;
RNG::suppliedChoice=choice;int stop=progress.scalarStep(&base,&game.save,&mod,delivery);RNG::suppliedChoice=-1;
auto missing=craft.refuel();if(missing.empty())autoPatrol(&craft,&game);
std::vector<Craft*> active;if(craft._status=="STR_OUT"){fuelCaller.fuel(&craft);active.push_back(&craft);}auto* target=select(&hunter,active);
std::cout << '[' << minimum << ',' << stock << ',' << choice << ',' << progress.completedUnits << ',' << stop << ',' << base.storage.count
<< ',' << delivery._randomProductionInfo["SUPPLY"] << ",\"" << craft._status << "\"," << craft._fuel << ',' << (target?target->id:0) << ']';
for(auto* waypoint:game.save.waypoints)delete waypoint;
}
std::cout << "],\"productionDelayedFuelQuantityRows\":[";
scoreFirst=true;
for(int minimum:{0,1}) for(int stock:{1,2}) for(int choice:{1,2}) {
if(!scoreFirst)std::cout<<',';scoreFirst=false;
Base base;ServiceGame game;ResearchMod mod;Item item;Production progress;Craft craft;Ufo hunter;FuelCaller fuelCaller;
craft.id=1;craft._base=&base;base.crafts={&craft};craft.rule.fuelItem=&item;craft._fuel=60;craft.distance=Nautical(100);
ProductionItemRule rule;rule.times={2};rule.produced={{&item,0}};rule.random={{1,{{&item,minimum}}},{1,{{&item,2}}}};ProductionDeliveryCaller delivery{&rule};
progress.rule.time=1;progress._timeSpent=0;progress._engineers=1;progress._amount=3;progress.infinite=true;base.storage.count=stock;
RNG::suppliedChoice=choice;int stop=progress.scalarStep(&base,&game.save,&mod,delivery);RNG::suppliedChoice=-1;
auto missing=craft.refuel();if(missing.empty())autoPatrol(&craft,&game);
std::vector<Craft*> active;if(craft._status=="STR_OUT"){fuelCaller.fuel(&craft);active.push_back(&craft);}auto* target=select(&hunter,active);
std::cout << '[' << minimum << ',' << stock << ',' << choice << ',' << progress.completedUnits << ',' << stop << ',' << base.storage.count
<< ',' << delivery._randomProductionInfo["SUPPLY"] << ",\"" << craft._status << "\"," << craft._fuel << ',' << (target?target->id:0)
<< ',' << base.transfers.size() << ',' << base.transfers[0]->_itemQty << ',' << base.transfers[1]->_itemQty << ',' << base.transfers[0]->getHours();
for(auto* transfer:base.transfers)transfer->advance(&base);
std::cout << ',' << base.storage.count << ',' << base.transfers[0]->getHours();
for(auto* transfer:base.transfers)transfer->advance(&base);
std::cout << ',' << base.storage.count << ',' << base.transfers[0]->getHours() << ']';
for(auto* transfer:base.transfers)delete transfer;for(auto* waypoint:game.save.waypoints)delete waypoint;
}
std::cout << "],\"productionSharedFuelQuantityRows\":[";
scoreFirst=true;
for(bool reverse:{false,true}) for(int stock:{0,1}) for(int choice:{1,2}) {
if(!scoreFirst)std::cout<<',';scoreFirst=false;
Base base;ServiceGame game;ResearchMod mod;Item item;Production progress;Craft first,second;Ufo hunter;FuelCaller fuelCaller;
first.id=reverse?2:1;second.id=reverse?1:2;first._base=second._base=&base;base.crafts={&first,&second};
for(auto* craft:base.crafts){craft->rule.fuelItem=&item;craft->_fuel=60;craft->distance=Nautical(100);}
ProductionItemRule rule;rule.produced={{&item,0}};rule.random={{1,{{&item,1}}},{1,{{&item,2}}}};ProductionDeliveryCaller delivery{&rule};
progress.rule.time=1;progress._timeSpent=0;progress._engineers=1;progress._amount=3;progress.infinite=true;base.storage.count=stock;
RNG::suppliedChoice=choice;int stop=progress.scalarStep(&base,&game.save,&mod,delivery);RNG::suppliedChoice=-1;
for(auto* craft:base.crafts){auto missing=craft->refuel();if(missing.empty())autoPatrol(craft,&game);}
std::vector<Craft*> active;for(auto* craft:base.crafts)if(craft->_status=="STR_OUT"){fuelCaller.fuel(craft);active.push_back(craft);}auto* target=select(&hunter,active);
std::cout << '[' << (reverse?1:0) << ',' << stock << ',' << choice << ',' << progress.completedUnits << ',' << stop << ',' << base.storage.count
<< ',' << delivery._randomProductionInfo["SUPPLY"] << ',' << first.id << ",\"" << first._status << "\"," << first._fuel
<< ',' << second.id << ",\"" << second._status << "\"," << second._fuel << ',' << (target?target->id:0) << ']';
for(auto* waypoint:game.save.waypoints)delete waypoint;
}
std::cout << "],\"productionSharedMaterialQuantityRows\":[";
scoreFirst=true;
for(int required:{1,2}) for(int stock:{0,1,2}) for(int choice:{1,2}) {
if(!scoreFirst)std::cout<<',';scoreFirst=false;
Base base;ServiceGame game;ResearchMod mod;Item item,output;output.type="INERT_A";output.battle=1;Production progress,other;Craft craft;Ufo hunter;FuelCaller fuelCaller;
craft.id=1;craft._base=&base;base.crafts={&craft};craft.rule.fuelItem=&item;craft._fuel=60;craft.distance=Nautical(100);
ProductionItemRule rule;rule.produced={{&item,0}};rule.random={{1,{{&item,1}}},{1,{{&item,2}}}};ProductionDeliveryCaller delivery{&rule};
progress.rule.time=1;progress._timeSpent=0;progress._engineers=1;progress._amount=3;progress.infinite=true;base.storage.count=stock;
ProductionItemRule otherRule;otherRule.produced={{&output,1}};ProductionDeliveryCaller otherDelivery{&otherRule};other.rule.time=2;other.rule.requiredItems={{&item,required}};
other._timeSpent=1;other._engineers=1;other._amount=3;other.infinite=true;base.freeWorkshops=6;base._productions={&progress,&other};
RNG::suppliedChoice=choice;int stop=progress.scalarStep(&base,&game.save,&mod,delivery);RNG::suppliedChoice=-1;
int otherStop=other.scalarStep(&base,&game.save,&mod,otherDelivery);
auto missing=craft.refuel();if(missing.empty())autoPatrol(&craft,&game);
std::vector<Craft*> active;if(craft._status=="STR_OUT"){fuelCaller.fuel(&craft);active.push_back(&craft);}auto* target=select(&hunter,active);
std::cout << '[' << required << ',' << stock << ',' << choice << ',' << progress.completedUnits << ',' << stop << ',' << other.completedUnits << ',' << otherStop
<< ',' << base.storage.count << ',' << base.storage.getItem(&output) << ',' << delivery._randomProductionInfo["SUPPLY"] << ",\"" << craft._status << "\"," << craft._fuel << ',' << (target?target->id:0) << ']';
for(auto* waypoint:game.save.waypoints)delete waypoint;
}
std::cout << "],\"productionInertSiteResearchRows\":[";
scoreFirst=true;
for(bool presence:{false,true}) for(int stock:{0,1}) for(int choice:{1,2}) {
if(!scoreFirst)std::cout<<',';scoreFirst=false;
Base base;ServiceGame game;ResearchMod mod;Item item,token;token.type="TOKEN";token.battle=1;Production progress;Craft craft;
craft.id=1;craft._base=&base;base.crafts={&craft};craft.rule.fuelItem=&item;craft._fuel=60;
ProductionItemRule rule;rule.produced={{&item,1}};rule.random={{1,presence?std::map<const Item*,int>{}:std::map<const Item*,int>{{&token,0}}},{1,{{&token,2}}}};ProductionDeliveryCaller delivery{&rule};
progress.rule.time=1;progress._timeSpent=0;progress._engineers=1;progress._amount=3;progress.infinite=true;base.storage.extra["TOKEN"]=stock;
RNG::suppliedChoice=choice;int stop=progress.scalarStep(&base,&game.save,&mod,delivery);RNG::suppliedChoice=-1;craft.refuel();
ResearchRule core,automatic;core.name="CORE";core.points=automatic.points=0;automatic.name="AUTO";automatic.cost=0;automatic.dependencies={&core};automatic.needs=true;automatic.needed=&token;mod.rules={&core,&automatic};
game.save.addFinishedResearch(&core,&mod,&base,true);
std::cout << '[' << (presence?1:0) << ',' << stock << ',' << choice << ',' << progress.completedUnits << ',' << stop << ',' << base.storage.getItem(&token)
<< ',' << delivery._randomProductionInfo["TOKEN"] << ',' << (game.save.isResearched(&automatic,false)?1:0) << ',' << craft._fuel << ']';
}
std::cout << "],\"productionPersonnelRows\":[";
scoreFirst=true;
for(bool scientist:{false,true}) for(int delay:{-1,0,3}) for(int assigned:{1,6}) for(int headroom:{0,1,2}) for(bool infinite:{false,true}) {
if(!scoreFirst) std::cout << ','; scoreFirst=false;
Base base; ServiceGame game; ResearchMod mod; Item item; Production progress; Craft craft; Ufo hunter; FuelCaller fuelCaller;
craft.id=1; craft._base=&base; base.crafts={&craft}; craft.rule.fuelItem=&item; craft._fuel=60; craft.distance=Nautical(100);
ProductionItemRule rule; rule.times=delay<0?std::vector<int>{0}:std::vector<int>{0,delay}; rule.produced={{&item,1}}; rule.person=scientist?"STR_SCIENTIST":"STR_ENGINEER"; ProductionDeliveryCaller delivery{&rule};
progress.rule.time=2; progress.rule.cost=3; progress.rule.person=rule.person; progress._timeSpent=1; progress._engineers=assigned; progress._amount=2; progress.infinite=infinite; base._productions={&progress}; game.save.funds=10;
base.quarters=assigned+1+headroom; int usedBefore=base.getUsedQuarters();
int stop=progress.scalarStep(&base,&game.save,&mod,delivery);
int usedAfter=base.getUsedQuarters();
auto missing=craft.refuel(); if(missing.empty()) autoPatrol(&craft,&game);
std::vector<Craft*> active; if(craft._status=="STR_OUT") { fuelCaller.fuel(&craft); active.push_back(&craft); }
auto* target=select(&hunter,active);
std::cout << '[' << (scientist?"true":"false") << ',' << delay << ',' << assigned << ',' << headroom << ',' << (infinite?"true":"false") << ',' << progress.completedUnits << ',' << stop << ',' << usedBefore << ',' << usedAfter << ',' << base.transfers.size() << ',' << base.transfers.back()->_hours << ',' << game.save.funds << ',' << base.storage.count << ",\"" << craft._status << "\"," << craft._fuel << ',' << (target?target->id:0) << ']';
for(auto* waypoint:game.save.waypoints) delete waypoint; for(auto* transfer:base.transfers) delete transfer;
}
std::cout << "],";
std::cout << "\"productionSoldierRows\":[";
scoreFirst=true;
for(int delay:{-1,0,3}) for(bool fixed:{false,true}) for(bool overlay:{false,true}) for(bool pending:{false,true}) {
if(!scoreFirst) std::cout << ','; scoreFirst=false;
Base base; SavedGame save; save.bases={&base}; Soldier existing; Transfer incoming(2); incoming.setSoldier(&existing); if(pending) base.transfers.push_back(&incoming);
ProductionItemRule rule; rule.person="RECRUIT"; rule.times=delay<0?std::vector<int>{0}:std::vector<int>{0,delay}; rule.personName=fixed?"FORCED":""; rule.soldierTemplate=overlay;
Soldier::generated=0; ProductionDeliveryCaller delivery{&rule}; delivery.deliver(&base,&save);
auto* transfer=base.transfers.back(); auto* soldier=transfer->_soldier;
std::cout << '[' << delay << ',' << (fixed?"true":"false") << ',' << (overlay?"true":"false") << ',' << (pending?"true":"false") << ',' << transfer->_hours << ',' << soldier->id << ',' << save.soldierCounter << ',' << soldier->nationality << ",\"" << soldier->name << "\"," << Soldier::generated << ']';
delete soldier; delete transfer;
}
std::cout << "],\"productionSoldierHuntingRows\":[";
scoreFirst=true;
for(int delay:{-1,0,3}) for(int assigned:{1,6}) for(int headroom:{0,1,2}) for(bool infinite:{false,true}) {
if(!scoreFirst) std::cout << ','; scoreFirst=false;
Base base; ServiceGame game; game.save.bases={&base}; ResearchMod mod; Item item; Production progress; Craft craft; Ufo hunter; FuelCaller fuelCaller;
craft.id=1; craft._base=&base; base.crafts={&craft}; craft.rule.fuelItem=&item; craft._fuel=60; craft.distance=Nautical(100);
ProductionItemRule rule; rule.times=delay<0?std::vector<int>{0}:std::vector<int>{0,delay}; rule.produced={{&item,1}}; rule.person="RECRUIT"; ProductionDeliveryCaller delivery{&rule};
progress.rule.time=2; progress.rule.cost=3; progress.rule.person=rule.person; progress._timeSpent=1; progress._engineers=assigned; progress._amount=2; progress.infinite=infinite; base._productions={&progress}; game.save.funds=10;
base.quarters=assigned+1+headroom; int usedBefore=base.getUsedQuarters();
int stop=progress.scalarStep(&base,&game.save,&mod,delivery); int usedAfter=base.getUsedQuarters();
auto missing=craft.refuel(); if(missing.empty()) autoPatrol(&craft,&game);
std::vector<Craft*> active; if(craft._status=="STR_OUT") { fuelCaller.fuel(&craft); active.push_back(&craft); }
auto* target=select(&hunter,active);
std::cout << "[false," << delay << ',' << assigned << ',' << headroom << ',' << (infinite?"true":"false") << ',' << progress.completedUnits << ',' << stop << ',' << usedBefore << ',' << usedAfter << ',' << base.transfers.size() << ',' << base.transfers.back()->_hours << ',' << game.save.funds << ',' << base.storage.count << ",\"" << craft._status << "\"," << craft._fuel << ',' << (target?target->id:0) << ',' << game.save.soldierCounter << ']';
for(auto* waypoint:game.save.waypoints) delete waypoint; for(auto* transfer:base.transfers) { delete transfer->_soldier; delete transfer; }
}
std::cout << "],\"researchCorpseRows\":[";
scoreFirst=true;
for(bool retain:{false,true}) for(bool needs:{false,true}) for(bool destroy:{false,true}) for(bool returns:{false,true}) for(bool finished:{false,true}) for(bool unitExists:{false,true}) for(bool recover:{false,true}) for(bool recoverCorpse:{false,true}) for(bool daily:{false,true}) {
if(!scoreFirst) std::cout << ','; scoreFirst=false;
ResearchCaller caller; ResearchRule rule; ResearchProject project; ResearchCorpse corpse; ResearchArmor armor; ResearchUnit unit; Base base;
rule.name="CORPSE_TOPIC"; rule.needs=needs; rule.destroy=destroy; rule.returns=returns; rule.holding=needs&&(destroy||returns); rule.needed=&caller.mod.supply;
project.rule=&rule; project.finished=finished; corpse.corpse=true; corpse._recover=recover; corpse._recoverCorpse=recoverCorpse; armor._corpseGeo=&corpse; unit.armor=&armor; caller.mod.matchedUnit=unitExists?&unit:nullptr;
Options::retainCorpses=retain; caller.refund(&project);
if(daily&&finished) caller.dailyCorpse(&rule,&base);
std::cout << '[' << (retain?"true":"false") << ',' << (needs?"true":"false") << ',' << (destroy?"true":"false") << ',' << (returns?"true":"false") << ',' << (finished?"true":"false") << ',' << (unitExists?"true":"false") << ',' << (recover?"true":"false") << ',' << (recoverCorpse?"true":"false") << ',' << (daily?"true":"false") << ',' << caller.storage.count+base.storage.count << ',' << caller.storage.corpses+base.storage.corpses << ']';
}
Options::retainCorpses=true;
std::cout << "],\"researchCorpseHuntingRows\":[";
scoreFirst=true;
for(bool retain:{false,true}) for(bool needs:{false,true}) for(bool destroy:{false,true}) for(bool returns:{false,true}) for(bool unitExists:{false,true}) for(bool recover:{false,true}) for(bool recoverCorpse:{false,true}) {
if(!scoreFirst) std::cout << ','; scoreFirst=false;
ResearchCaller caller; ResearchRule rule; ResearchProject project; ResearchCorpse corpse; ResearchArmor armor; ResearchUnit unit; Base base; Craft craft; ServiceGame game; Ufo hunter; FuelCaller fuelCaller;
rule.name="CORPSE_TOPIC"; rule.needs=needs; rule.destroy=destroy; rule.returns=returns; rule.holding=needs&&(destroy||returns); rule.needed=&caller.mod.supply;
project.rule=&rule; project.finished=true; corpse.corpse=true; corpse._recover=recover; corpse._recoverCorpse=recoverCorpse; armor._corpseGeo=&corpse; unit.armor=&armor; caller.mod.matchedUnit=unitExists?&unit:nullptr;
Options::retainCorpses=retain; caller.refund(&project); caller.dailyCorpse(&rule,&base);
base.storage.count+=caller.storage.count; base.storage.corpses+=caller.storage.corpses;
craft.id=1; craft._base=&base; base.crafts={&craft}; craft.rule.fuelItem=&corpse; craft._fuel=60; craft.distance=Nautical(100);
auto missing=craft.refuel(); if(missing.empty()) autoPatrol(&craft,&game);
std::vector<Craft*> active; if(craft._status=="STR_OUT") { fuelCaller.fuel(&craft); active.push_back(&craft); }
auto* target=select(&hunter,active);
std::cout << '[' << (retain?"true":"false") << ',' << (needs?"true":"false") << ',' << (destroy?"true":"false") << ',' << (returns?"true":"false") << ',' << (unitExists?"true":"false") << ',' << (recover?"true":"false") << ',' << (recoverCorpse?"true":"false") << ',' << base.storage.count << ',' << base.storage.corpses << ",\"" << craft._status << "\"," << craft._fuel << ',' << (target?target->id:0) << ']';
for(auto* waypoint:game.save.waypoints) delete waypoint;
}
Options::retainCorpses=true;
std::cout << "],\"maintenanceRows\":[";
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
std::cout << "],\"productionCraftOrderRows\":[";
{
Base base; SavedGame game; ResearchMod mod; Production progress; Craft::Rule types[18];
for(int i=0;i<18;++i) { auto* craft=new Craft(); craft->id=i+1; craft->_rules=&types[i]; craft->_status="STR_READY"; base.crafts.push_back(craft); progress.rule.requiredCrafts[&types[i]]=0; }
progress.startItem(&base,&game,&mod);
std::cout << '['; bool first=true; for(int id:base.consumedOrder) { if(!first) std::cout << ','; first=false; std::cout << id; } std::cout << ']';
}
std::cout << "],\"deletedCraftFollowerRows\":[";
serviceFirst=true;
for(bool craftFollows:{false,true}) for(bool ufoFollows:{false,true}) {
if(!serviceFirst) std::cout << ','; serviceFirst=false;
auto* leader=new Craft(); Craft follower; follower._fuel=100; Ufo hunter; auto* original=new Waypoint(); original->lon=0.9; original->lat=0.5;
hunter.setDestination(original); hunter.setSpeed(1000);
if(craftFollows) { follower._status="STR_OUT"; follower.setDestination(leader); leader->craftFollowers={&follower}; }
if(ufoFollows) { hunter.setTargetedXcomCraft(leader); leader->ufoFollowers={&hunter}; }
delete leader;
std::cout << '[' << (craftFollows?"true":"false") << ',' << (ufoFollows?"true":"false") << ",\"" << follower._status << "\"," << follower.speed << ',' << (follower._dest==&follower.base?"true":"false") << ','
<< (hunter._isHunting?"true":"false") << ',' << (hunter._isEscorting?"true":"false") << ',' << (hunter._origWaypoint?"true":"false") << ',' << hunter._dest->lon << ',' << hunter._dest->lat << ',' << hunter.speed << ',';
FuelCaller fuelCaller; if(craftFollows) fuelCaller.fuel(&follower); std::cout << follower._fuel << ']';
delete hunter._dest;
}
std::cout << "],\"deletedUfoChainRows\":[";
{
Ufo* actors[4];
for(int index=0;index<4;++index) {
actors[index]=new Ufo(); actors[index]->uniqueId=9+index; actors[index]->lon=0.4; actors[index]->lat=0.2;
auto* waypoint=new Waypoint(); waypoint->lon=0.400001; waypoint->lat=0.2;
actors[index]->setDestination(waypoint); actors[index]->setSpeed(2200);
if(index>0) { actors[index]->setEscortedUfo(actors[index-1]); actors[index-1]->ufoFollowers={actors[index]}; }
}
for(int removed=0;removed<3;++removed) {
if(removed) std::cout << ',';
// Ufo's own reset precedes Target::~Target's direct-follower repair.
actors[removed]->resetOriginalDestination(false); actors[removed]->ufoFollowers={actors[removed+1]};
delete actors[removed]; actors[removed]=nullptr;
std::cout << '[' << removed+1;
for(int index=removed+1;index<4;++index) {
auto* actor=actors[index]; auto* target=dynamic_cast<Ufo*>(actor->_dest);
std::cout << ",[" << actor->uniqueId << ',' << (actor->_isEscorting?"true":"false") << ','
<< (actor->_origWaypoint?"true":"false") << ',' << (target?target->uniqueId:0) << ',' << actor->speed << ','
<< (target?0:actor->_dest->lon) << ',' << (target?0:actor->_dest->lat) << ']';
}
std::cout << ']';
}
actors[3]->resetOriginalDestination(false); delete actors[3]->_dest; actors[3]->_dest=nullptr; delete actors[3];
}
std::cout << "],\"productionCraftUnloadRows\":[";
serviceFirst=true;
for(int stock:{0,1,3}) for(int input:{0,1,2}) for(int loaded:{0,1,2,INT_MAX}) {
if(loaded==INT_MAX && (stock!=1 || input!=1)) continue;
if(!serviceFirst) std::cout << ','; serviceFirst=false;
Craft craft; Item item; Base base; ServiceGame game; FuelCaller fuelCaller; Ufo hunter; ResearchMod mod;
craft.id=1; craft._base=&base; base.crafts={&craft}; base.storage.count=stock; craft.rule.fuelItem=&item; craft._fuel=60; craft.distance=Nautical(100);
auto* consumed=new Craft(); consumed->id=2; consumed->_base=&base; consumed->_status="STR_READY"; consumed->cargo.values={{&item,loaded}}; base.crafts.push_back(consumed);
Production progress; progress.rule.requiredItems={{&item,input}}; progress.rule.requiredCrafts={{consumed->getRules(),1}};
const bool enough=progress.haveEnoughMaterialsForOneMoreUnit(&base,&mod);
if(enough) progress.startItem(&base,&game.save,&mod);
if(craft._status=="STR_REFUELLING") { auto missing=craft.refuel(); if(missing.empty()) autoPatrol(&craft,&game); }
std::vector<Craft*> active; if(craft._status=="STR_OUT") { fuelCaller.fuel(&craft); active.push_back(&craft); }
auto* target=select(&hunter,active);
std::cout << '[' << stock << ',' << input << ',' << loaded << ',' << (enough?"true":"false") << ',' << base.storage.count << ",\"" << craft._status << "\"," << craft._fuel << ',' << base.crafts.size() << ',' << (target?target->id:0) << ']';
if(!enough) delete consumed; for(auto* waypoint:game.save.waypoints) delete waypoint;
}
std::cout << "],\"productionCraftInputRows\":[";
serviceFirst=true;
for(int quantity:{0,1,2}) for(int grounded:{0,1,2,3}) for(bool outFirst:{false,true}) {
if(!serviceFirst) std::cout << ','; serviceFirst=false;
Base base; SavedGame game; ResearchMod mod; Craft::Rule type; Production progress; progress.rule.requiredCrafts={{&type,quantity}};
if(outFirst) { auto* craft=new Craft(); craft->id=1; craft->_rules=&type; craft->_status="STR_OUT"; base.crafts.push_back(craft); }
for(int i=0;i<grounded;++i) { auto* craft=new Craft(); craft->id=2+i; craft->_rules=&type; craft->_status="STR_READY"; base.crafts.push_back(craft); }
const bool enough=progress.haveEnoughMaterialsForOneMoreUnit(&base,&mod);
if(enough) progress.startItem(&base,&game,&mod);
std::cout << '[' << quantity << ',' << grounded << ',' << (outFirst?"true":"false") << ',' << (enough?"true":"false") << ",[";
bool firstCraft=true; for(auto* craft:base.crafts) { if(!firstCraft) std::cout << ','; firstCraft=false; std::cout << craft->id; }
std::cout << "]]"; for(auto* craft:base.crafts) delete craft;
}
std::cout << "],\"productionStaffReleaseRows\":[";
serviceFirst=true;
for(bool fails:{false,true}) for(int freeEngineers:{0,1}) {
if(!serviceFirst) std::cout << ','; serviceFirst=false;
Craft craft; Item item; Base base; ServiceGame game; FuelCaller fuelCaller; Ufo hunter; ResearchMod mod;
craft.id=1; craft._base=&base; base.crafts={&craft}; base.storage.count=0; craft.rule.fuelItem=&item; craft._fuel=60; craft.distance=Nautical(100);
base.engineers=freeEngineers; base.freeWorkshops=3;
ProductionItemRule firstRule; firstRule.produced={{&item,0}}; ProductionDeliveryCaller firstDelivery{&firstRule};
ProductionItemRule secondRule; secondRule.produced={{&item,1}}; ProductionDeliveryCaller secondDelivery{&secondRule};
auto* first=new Production(); first->rule.time=1; first->_engineers=2; first->_amount=fails?3:1; first->rule.cost=fails?1000:0; first->delivery=&firstDelivery;
auto* second=new Production(); second->rule.time=1; second->_amount=4; second->_isFallback=true; second->delivery=&secondDelivery;
base._productions={first,second}; ProductionHourlyCaller caller; caller.game={&game.save,&mod}; caller.advance(&base);
const int spent=second->_timeSpent,assigned=second->_engineers;
if(craft._status=="STR_REFUELLING") { auto missing=craft.refuel(); if(missing.empty()) autoPatrol(&craft,&game); }
std::vector<Craft*> active; if(craft._status=="STR_OUT") { fuelCaller.fuel(&craft); active.push_back(&craft); }
auto* target=select(&hunter,active);
std::cout << '[' << (fails?"true":"false") << ',' << freeEngineers << ',' << spent << ',' << assigned << ',' << base.engineers << ',' << base.storage.count << ",\"" << craft._status << "\"," << craft._fuel << ',' << (target?target->id:0) << ']';
for(auto* project:base._productions) delete project; for(auto* waypoint:game.save.waypoints) delete waypoint;
}
std::cout << "],\"productionFallbackRows\":[";
serviceFirst=true;
for(int engineers:{0,1,4}) for(int freeSpace:{0,1,4}) for(int space:{0,2}) for(int spent:{0,1}) for(int assigned:{0,1}) {
if(!serviceFirst) std::cout << ','; serviceFirst=false;
Craft craft; Item item; Base base; ServiceGame game; FuelCaller fuelCaller; Ufo hunter;
craft.id=1; craft._base=&base; base.crafts={&craft}; base.storage.count=0; craft.rule.fuelItem=&item; craft._fuel=60; craft.distance=Nautical(100);
base.engineers=engineers; base.freeWorkshops=freeSpace;
Production progress; progress.rule.time=2; progress.rule.space=space; progress._timeSpent=spent; progress._engineers=assigned; progress._amount=4; progress._isFallback=true;
ProductionItemRule rule; rule.produced={{&item,1}}; ProductionDeliveryCaller delivery{&rule}; ResearchMod mod;
const auto result=progress.scalarStep(&base,&game.save,&mod,delivery);
if(craft._status=="STR_REFUELLING") { auto missing=craft.refuel(); if(missing.empty()) autoPatrol(&craft,&game); }
std::vector<Craft*> active; if(craft._status=="STR_OUT") { fuelCaller.fuel(&craft); active.push_back(&craft); }
auto* target=select(&hunter,active);
std::cout << '[' << engineers << ',' << freeSpace << ',' << space << ',' << spent << ',' << assigned << ',' << progress._engineers << ',' << base.engineers << ',' << progress._timeSpent << ',' << progress.completedUnits << ',' << result << ',' << base.storage.count << ",\"" << craft._status << "\"," << craft._fuel << ',' << (target?target->id:0) << ']';
for(auto* waypoint:game.save.waypoints) delete waypoint;
}
std::cout << "],\"productionMaterialRows\":[";
serviceFirst=true;
for(int input:{1,2}) for(int output:{0,1,2}) for(int stock:{0,1,3}) for(int delay:{0,2}) for(int units:{1,3}) {
if(!serviceFirst) std::cout << ','; serviceFirst=false;
Craft craft; Item item; Base base; ServiceGame game; FuelCaller fuelCaller; Ufo hunter;
craft.id=1; craft._base=&base; base.crafts={&craft}; base.storage.count=stock; craft.rule.fuelItem=&item; craft._fuel=60; craft.distance=Nautical(100);
Production progress; progress.rule.time=2; progress.rule.cost=3; progress.rule.requiredItems={{&item,input}};
progress._timeSpent=1; progress._engineers=units*2; progress._amount=4;
ProductionItemRule rule; rule.times={delay}; rule.produced={{&item,output}}; ProductionDeliveryCaller delivery{&rule}; ResearchMod mod; game.save.funds=12;
const auto result=progress.scalarStep(&base,&game.save,&mod,delivery);
if(craft._status=="STR_REFUELLING") { auto missing=craft.refuel(); if(missing.empty()) autoPatrol(&craft,&game); }
std::vector<Craft*> active; if(craft._status=="STR_OUT") { fuelCaller.fuel(&craft); active.push_back(&craft); }
auto* target=select(&hunter,active);
std::cout << '[' << input << ',' << output << ',' << stock << ',' << delay << ',' << units << ',' << progress.completedUnits << ',' << game.save.funds << ',' << result << ',' << base.storage.count << ",\"" << craft._status << "\"," << craft._fuel << ',' << (target?target->id:0) << ',' << base.transfers.size() << ']';
for(auto* transfer:base.transfers) delete transfer; for(auto* waypoint:game.save.waypoints) delete waypoint;
}
std::cout << "],\"productionMixedSaleRows\":[";
serviceFirst=true;
for(int firstPrice:{-7,7}) {
if(!serviceFirst) std::cout << ','; serviceFirst=false;
Item items[2]; items[0].sellCost=firstPrice; items[1].sellCost=-firstPrice;
Base base; SavedGame game; ResearchMod mod; game.funds=10;
Production progress; progress.rule.time=1; progress._engineers=1; progress._amount=1;
ProductionItemRule rule; rule.produced={{&items[0],1},{&items[1],1}}; ProductionDeliveryCaller delivery{&rule}; delivery.sell=true;
progress.scalarStep(&base,&game,&mod,delivery);
std::cout << '[' << firstPrice << ',' << -firstPrice << ',' << game.funds << ',' << game._incomes.back() << ',' << game._expenditures.back() << ']';
}
std::cout << "],\"productionSaleRows\":[";
serviceFirst=true;
for(int cost:{0,3}) for(int funds:{-1,0}) for(int assignedUnits:{1,3}) for(bool infinite:{false,true}) for(int qty:{0,2}) for(int coefficient:{50,150}) for(int delay:{0,2}) {
if(!serviceFirst) std::cout << ','; serviceFirst=false;
Craft craft; Item item; Base base; ServiceGame game; FuelCaller fuelCaller; Ufo hunter;
craft.id=1; craft._base=&base; base.crafts={&craft}; base.storage.count=0; craft.rule.fuelItem=&item; craft._fuel=60; craft.distance=Nautical(100); item.sellCost=7;
Production sale; sale.rule.time=2; sale.rule.cost=cost; sale._timeSpent=1; sale._engineers=assignedUnits*2; sale._amount=2; sale.infinite=infinite;
ProductionItemRule saleRule; saleRule.times={delay}; saleRule.produced={{&item,qty}}; ProductionDeliveryCaller saleDelivery{&saleRule}; saleDelivery.sell=true;
ResearchMod mod; game.save.funds=funds; game.save.sellCoefficient=coefficient;
const auto saleResult=sale.scalarStep(&base,&game.save,&mod,saleDelivery);
Production later; later.rule.time=1; later.rule.cost=3; later._engineers=2; later._amount=3;
ProductionItemRule laterRule; laterRule.produced={{&item,1}}; ProductionDeliveryCaller laterDelivery{&laterRule};
const auto laterResult=later.scalarStep(&base,&game.save,&mod,laterDelivery);
if(craft._status=="STR_REFUELLING") { auto missing=craft.refuel(); if(missing.empty()) autoPatrol(&craft,&game); }
std::vector<Craft*> active; if(craft._status=="STR_OUT") { fuelCaller.fuel(&craft); active.push_back(&craft); }
auto* target=select(&hunter,active);
std::cout << '[' << cost << ',' << funds << ',' << assignedUnits << ',' << (infinite?"true":"false") << ',' << qty << ',' << coefficient << ',' << delay << ','
<< sale.completedUnits << ',' << saleResult << ',' << later.completedUnits << ',' << laterResult << ',' << game.save.funds << ',' << game.save._incomes.back() << ',' << game.save._expenditures.back() << ','
<< base.storage.count << ',' << base.transfers.size() << ",\"" << craft._status << "\"," << craft._fuel << ',' << (target?target->id:0) << ']';
for(auto* transfer:base.transfers) delete transfer; for(auto* waypoint:game.save.waypoints) delete waypoint;
}
std::cout << "],\"productionMoneyRows\":[";
serviceFirst=true;
for(int cost:{0,3}) for(int funds:{-1,0,3,12}) for(int assignedUnits:{1,3}) for(int amount:{2,4}) for(bool infinite:{false,true}) {
if(!serviceFirst) std::cout << ','; serviceFirst=false;
Craft craft; Item item; Base base; ServiceGame game; FuelCaller fuelCaller; Ufo hunter;
craft.id=1; craft._base=&base; base.crafts={&craft}; base.storage.count=0; craft.rule.fuelItem=&item; craft._fuel=60; craft.distance=Nautical(100);
Production progress; progress.rule.time=2; progress.rule.cost=cost; progress._timeSpent=1; progress._engineers=assignedUnits*2; progress._amount=amount; progress.infinite=infinite;
ProductionItemRule rule; rule.produced={{&item,1}}; ProductionDeliveryCaller delivery{&rule}; ResearchMod mod; game.save.funds=funds;
const auto result=progress.scalarStep(&base,&game.save,&mod,delivery);
if(craft._status=="STR_REFUELLING") { auto missing=craft.refuel(); if(missing.empty()) autoPatrol(&craft,&game); }
std::vector<Craft*> active; if(craft._status=="STR_OUT") { fuelCaller.fuel(&craft); active.push_back(&craft); }
auto* target=select(&hunter,active);
std::cout << '[' << cost << ',' << funds << ',' << assignedUnits << ',' << amount << ',' << (infinite?"true":"false") << ','
<< progress.completedUnits << ',' << game.save.funds << ',' << result << ',' << base.storage.count << ",\"" << craft._status << "\"," << craft._fuel << ',' << (target?target->id:0) << ']';
for(auto* waypoint:game.save.waypoints) delete waypoint;
}
std::cout << "],\"productionFreeRows\":[";
serviceFirst=true;
for(bool armed:{false,true}) for(int assignedUnits:{0,1,3}) for(int amount:{1,2}) for(bool infinite:{false,true}) for(int qty:{0,2}) for(int delay:{0,1}) for(int stock:{0,1}) {
if(!serviceFirst) std::cout << ','; serviceFirst=false;
Craft craft; Item item; Base base; CraftWeapon weapon; ServiceGame game; FuelCaller fuelCaller; Ufo hunter;
craft.id=1; craft._base=&base; base.crafts={&craft}; base.storage.count=stock; craft.rule.fuelItem=&item;
craft._fuel=60; craft.distance=Nautical(100); if(armed) { weapon.rule.clip=&item; weapon.rule.ammoMax=10; craft._weapons={&weapon}; }
Production progress; progress.rule.time=2; progress._timeSpent=1; progress._engineers=assignedUnits*2; progress._amount=amount; progress.infinite=infinite;
const int requested=progress.requested();
ProductionItemRule rule; rule.times={delay}; rule.produced={{&item,qty}};
ProductionDeliveryCaller caller{&rule}; for(int unit=0;unit<requested;++unit) caller.deliver(&base,&game.save);
if(craft._status=="STR_REFUELLING") { auto missing=craft.refuel(); if(missing.empty()) autoPatrol(&craft,&game); }
std::vector<Craft*> active; if(craft._status=="STR_OUT") { fuelCaller.fuel(&craft); active.push_back(&craft); }
auto* target=select(&hunter,active);
std::cout << '[' << (armed?"true":"false") << ',' << assignedUnits << ',' << amount << ',' << (infinite?"true":"false") << ',' << qty << ',' << delay << ',' << stock << ','
<< requested << ',' << base.storage.count << ",\"" << craft._status << "\"," << craft._fuel << ',' << (weapon.rearming?"true":"false") << ',' << base.transfers.size() << ',' << (target?target->id:0) << ']';
for(auto* transfer:base.transfers) delete transfer; for(auto* waypoint:game.save.waypoints) delete waypoint;
}
std::cout << "],\"productionDeliveryRows\":[";
serviceFirst=true;
for(bool fuel:{false,true}) for(bool armed:{false,true}) for(int delay:{0,1,2}) for(int qty:{0,1,2}) for(int stock:{0,1}) {
if(!serviceFirst) std::cout << ','; serviceFirst=false;
Craft craft; Item item; Base base; CraftWeapon weapon; ServiceGame game; FuelCaller fuelCaller; Ufo hunter;
craft.id=1; craft._base=&base; base.crafts={&craft}; base.storage.count=stock; craft.rule.fuelItem=fuel?&item:nullptr;
craft._fuel=60; craft.distance=Nautical(100); if(armed) { weapon.rule.clip=&item; weapon.rule.ammoMax=10; craft._weapons={&weapon}; }
ProductionItemRule rule; rule.times={delay}; rule.produced={{&item,qty}};
ProductionDeliveryCaller caller{&rule}; caller.deliver(&base,&game.save);
if(craft._status=="STR_REFUELLING") { auto missing=craft.refuel(); if(missing.empty()) autoPatrol(&craft,&game); }
std::vector<Craft*> active; if(craft._status=="STR_OUT") { fuelCaller.fuel(&craft); active.push_back(&craft); }
auto* target=select(&hunter,active);
std::cout << '[' << (fuel?"true":"false") << ',' << (armed?"true":"false") << ',' << delay << ',' << qty << ',' << stock << ',' << base.storage.count << ",\"" << craft._status << "\"," << craft._fuel << ',' << (weapon.rearming?"true":"false") << ',' << base.transfers.size() << ',' << (target?target->id:0) << ']';
for(auto* transfer:base.transfers) delete transfer; for(auto* waypoint:game.save.waypoints) delete waypoint;
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
std::cout << "],\"researchEquivalentRewardRows\":[";
serviceFirst=true;
for(int choice:{0,1}) for(bool duplicate:{false,true}) for(int count:{0,2}) {
if(!serviceFirst) std::cout << ','; serviceFirst=false;
ResearchRule topics[3]; auto* source=&topics[0]; auto* a=&topics[1]; auto* b=&topics[2];
source->name="EQUIV_SOURCE"; a->name="EQUIV_A"; b->name="EQUIV_B";
source->free=duplicate?std::vector<const ResearchRule*>{a,a,b}:std::vector<const ResearchRule*>{a,b};
source->spawned=a->spawned=b->spawned="SUPPLY"; source->count=1; a->count=b->count=count;
source->increases=a->increases=b->increases={"EQUIV_COUNTER"};
SavedGame saved; Mod mod; mod.rules={source,a,b}; Base base; ResearchCaller caller;
saved._ids["EQUIV_COUNTER"]=1;
RNG::suppliedChoice=duplicate?choice*2:choice; auto* bonus=saved.selectGetOneFree(source); RNG::suppliedChoice=-1;
saved.addFinishedResearch(bonus,&mod,&base); saved.addFinishedResearch(source,&mod,&base);
auto primary=ResearchCaller::primary(source,bonus); std::sort(primary.begin(),primary.end()); primary.erase(std::unique(primary.begin(),primary.end()),primary.end());
for(auto* topic:primary) { caller.spawn(topic,&mod,&base); saved.applyPrimaryCounters(topic); }
const auto transfers=base.transfers.size(); for(auto* transfer:base.transfers) { transfer->advance(&base); delete transfer; }
std::cout << '[' << choice << ',' << (duplicate?"true":"false") << ',' << count << ',' << (bonus==a?1:2)
<< ',' << base.storage.count << ',' << transfers << ',' << saved._ids["EQUIV_COUNTER"] << ',' << saved.researchScore << ']';
}
std::cout << "],\"researchConvergentRewardRows\":[";
serviceFirst=true;
for(int choice:{0,1}) for(bool duplicate:{false,true}) {
if(!serviceFirst) std::cout << ','; serviceFirst=false;
ResearchRule topics[3]; auto* source=&topics[0]; auto* a=&topics[1]; auto* b=&topics[2];
source->name="CORE"; a->name="EQUIV_A"; b->name="EQUIV_B";
a->cost=b->cost=0; a->dependencies=b->dependencies={source};
source->free=duplicate?std::vector<const ResearchRule*>{a,a,b}:std::vector<const ResearchRule*>{a,b};
source->spawned=a->spawned=b->spawned="SUPPLY"; source->count=1; a->count=b->count=2;
source->increases=a->increases=b->increases={"EQUIV_COUNTER"};
SavedGame saved; Mod mod; mod.rules={source,a,b}; Base base; ResearchCaller caller;
saved._ids["EQUIV_COUNTER"]=1;
RNG::suppliedChoice=duplicate?choice*2:choice; auto* bonus=saved.selectGetOneFree(source); RNG::suppliedChoice=-1;
saved.addFinishedResearch(bonus,&mod,&base); saved.addFinishedResearch(source,&mod,&base);
auto primary=ResearchCaller::primary(source,bonus); std::sort(primary.begin(),primary.end()); primary.erase(std::unique(primary.begin(),primary.end()),primary.end());
for(auto* topic:primary) { caller.spawn(topic,&mod,&base); saved.applyPrimaryCounters(topic); }
const auto transfers=base.transfers.size(); for(auto* transfer:base.transfers) { transfer->advance(&base); delete transfer; }
std::cout << '[' << choice << ',' << (duplicate?"true":"false") << ',' << (saved.isResearched(source,false)?"true":"false")
<< ',' << (saved.isResearched(a,false)?"true":"false") << ',' << (saved.isResearched(b,false)?"true":"false")
<< ',' << base.storage.count << ',' << transfers << ',' << saved._ids["EQUIV_COUNTER"] << ',' << saved.researchScore << ']';
}
std::cout << "],\"researchConvergentStatusRows\":[";
serviceFirst=true;
for(int choice:{0,1}) {
if(!serviceFirst) std::cout << ','; serviceFirst=false;
ResearchRule topics[4]; auto* source=&topics[0]; auto* a=&topics[1]; auto* b=&topics[2]; auto* z=&topics[3];
source->name="CORE"; a->name="EQUIV_A"; b->name="EQUIV_B"; z->name="EQUIV_Z";
a->cost=b->cost=0; a->dependencies=b->dependencies={source}; source->free={a,b}; a->disables={z}; b->reenables={z};
SavedGame saved; Mod mod; mod.rules={source,a,b,z}; Base base;
RNG::suppliedChoice=choice; auto* bonus=saved.selectGetOneFree(source); RNG::suppliedChoice=-1;
saved.addFinishedResearch(bonus,&mod,&base); saved.addFinishedResearch(source,&mod,&base);
std::cout << '[' << choice << ',' << (saved.isResearched(a,false)?"true":"false") << ',' << (saved.isResearched(b,false)?"true":"false")
<< ',' << saved.statuses[z->name] << ',' << saved.researchScore << ']';
}
std::cout << "],\"researchWeightedEquivalentRows\":[";
serviceFirst=true;
for(int repeats:{3,4096}) for(int choice:{0,1}) {
if(!serviceFirst) std::cout << ','; serviceFirst=false;
ResearchRule topics[3]; auto* source=&topics[0]; auto* a=&topics[1]; auto* b=&topics[2];
source->name="CORE"; a->name="EQUIV_A"; b->name="EQUIV_B";
source->free=std::vector<const ResearchRule*>(repeats,a); source->free.push_back(b);
source->spawned=a->spawned=b->spawned="SUPPLY"; source->count=1; a->count=b->count=2;
SavedGame saved; Mod mod; mod.rules={source,a,b}; Base base; ResearchCaller caller;
RNG::suppliedChoice=choice?repeats:0; auto* bonus=saved.selectGetOneFree(source); RNG::suppliedChoice=-1;
saved.addFinishedResearch(bonus,&mod,&base); saved.addFinishedResearch(source,&mod,&base);
auto primary=ResearchCaller::primary(source,bonus); std::sort(primary.begin(),primary.end()); primary.erase(std::unique(primary.begin(),primary.end()),primary.end());
for(auto* topic:primary) caller.spawn(topic,&mod,&base);
const auto transfers=base.transfers.size(); for(auto* transfer:base.transfers) { transfer->advance(&base); delete transfer; }
std::cout << '[' << repeats << ',' << choice << ',' << (bonus==a?1:2) << ',' << base.storage.count << ',' << transfers << ',' << saved.researchScore << ']';
}
std::cout << "],\"researchScopedRewardRows\":[";
serviceFirst=true;
for(int mode:{0,1}) for(int choice:{0,1}) {
if(!serviceFirst) std::cout << ','; serviceFirst=false;
ResearchRule topics[4]; auto* source=&topics[0]; auto* a=&topics[1]; auto* b=&topics[2]; auto* z=&topics[3];
source->name="CORE"; a->name="EQUIV_A"; b->name="EQUIV_B"; z->name="EQUIV_Z"; z->points=0;
b->points=mode==0?2:1; if(mode==1) a->lookup=z;
source->free={a,b}; source->spawned=a->spawned=b->spawned="SUPPLY"; source->count=1; a->count=b->count=2;
source->increases=a->increases=b->increases={"EQUIV_COUNTER"};
SavedGame saved; Mod mod; mod.rules={source,a,b,z}; Base base; ResearchCaller caller; saved._ids["EQUIV_COUNTER"]=1;
RNG::suppliedChoice=choice; auto* bonus=saved.selectGetOneFree(source); RNG::suppliedChoice=-1;
saved.addFinishedResearch(bonus,&mod,&base); if(bonus->lookup) saved.addFinishedResearch(bonus->lookup,&mod,&base); saved.addFinishedResearch(source,&mod,&base);
auto primary=ResearchCaller::primary(source,bonus); std::sort(primary.begin(),primary.end()); primary.erase(std::unique(primary.begin(),primary.end()),primary.end());
for(auto* topic:primary) { caller.spawn(topic,&mod,&base); saved.applyPrimaryCounters(topic); }
const auto transfers=base.transfers.size(); for(auto* transfer:base.transfers) { transfer->advance(&base); delete transfer; }
std::cout << '[' << mode << ',' << choice << ',' << (saved.isResearched(z,false)?"true":"false") << ',' << base.storage.count
<< ',' << transfers << ',' << saved._ids["EQUIV_COUNTER"] << ',' << saved.researchScore << ']';
}
std::cout << "],\"researchDailySiteAlternativeRows\":[";
serviceFirst=true;
for(int mode:{0,1}) for(int choice:{0,1}) {
if(!serviceFirst)std::cout<<',';serviceFirst=false;
ResearchRule topics[6];auto* source=&topics[0];auto* a=&topics[1];auto* b=&topics[2];auto* z=&topics[3];auto* site=&topics[4];auto* automatic=&topics[5];
source->name="CORE";a->name="EQUIV_A";b->name="EQUIV_B";z->name="EQUIV_Z";z->points=0;site->name="SITE";site->points=3;
automatic->name="SITE_AUTO";automatic->cost=0;automatic->points=0;automatic->dependencies={site,a};
b->points=mode==0?2:1;if(mode==1)a->lookup=z;source->free={a,b};
source->spawned=a->spawned=b->spawned="SUPPLY";source->count=1;a->count=b->count=2;source->increases=a->increases=b->increases={"EQUIV_COUNTER"};
SavedGame saved;Mod mod;mod.rules={source,a,b,z,site,automatic};Base base;ResearchCaller caller;saved._ids["EQUIV_COUNTER"]=1;
RNG::suppliedChoice=choice;auto* bonus=saved.selectGetOneFree(source);RNG::suppliedChoice=-1;
saved.addFinishedResearch(bonus,&mod,&base);if(bonus->lookup)saved.addFinishedResearch(bonus->lookup,&mod,&base);saved.addFinishedResearch(source,&mod,&base);
auto primary=ResearchCaller::primary(source,bonus);std::sort(primary.begin(),primary.end());primary.erase(std::unique(primary.begin(),primary.end()),primary.end());
for(auto* topic:primary){caller.spawn(topic,&mod,&base);saved.applyPrimaryCounters(topic);}for(auto* transfer:base.transfers){transfer->advance(&base);delete transfer;}base.transfers.clear();
Craft craft;craft._base=&base;craft.rule.fuelItem=&mod.supply;craft._fuel=960;craft._stats.fuelMax=1000;craft.refuel();
saved.addFinishedResearch(site,&mod,&base,true);
std::cout << '[' << mode << ',' << choice << ',' << base.storage.count << ',' << saved._ids["EQUIV_COUNTER"] << ',' << saved.researchScore
<< ',' << (saved.isResearched(automatic,false)?1:0) << ',' << (saved.isResearched(a,false)?1:0) << ',' << (saved.isResearched(b,false)?1:0) << ',' << (saved.isResearched(z,false)?1:0) << ']';
}
std::cout << "],\"researchDailyQuantityRows\":[";
serviceFirst=true;
for(int choice:{0,1}) for(bool withSite:{false,true}) {
if(!serviceFirst)std::cout<<',';serviceFirst=false;
ResearchRule topics[5];auto* source=&topics[0];auto* a=&topics[1];auto* b=&topics[2];auto* site=&topics[3];auto* automatic=&topics[4];
source->name="CORE";a->name="EQUIV_A";b->name="EQUIV_B";site->name="SITE";site->points=3;automatic->name="SITE_AUTO";automatic->cost=0;automatic->points=0;automatic->dependencies={site};
SavedGame saved;Mod mod;mod.rules={source,a,b,site,automatic};Base base;ResearchCaller caller;saved._ids["EQUIV_COUNTER"]=1;automatic->needed=&mod.supply;automatic->needs=true;
source->free={a,b};source->spawned=a->spawned=b->spawned="SUPPLY";source->count=1;a->count=2;b->count=3;source->increases=a->increases=b->increases={"EQUIV_COUNTER"};
RNG::suppliedChoice=choice;auto* bonus=saved.selectGetOneFree(source);RNG::suppliedChoice=-1;saved.addFinishedResearch(bonus,&mod,&base);saved.addFinishedResearch(source,&mod,&base);
auto primary=ResearchCaller::primary(source,bonus);std::sort(primary.begin(),primary.end());primary.erase(std::unique(primary.begin(),primary.end()),primary.end());
for(auto* topic:primary){caller.spawn(topic,&mod,&base);saved.applyPrimaryCounters(topic);}const auto transfers=base.transfers.size();
for(auto* transfer:base.transfers){transfer->advance(&base);delete transfer;}base.transfers.clear();
Craft craft;craft._base=&base;craft.rule.fuelItem=&mod.supply;craft._fuel=960;craft._stats.fuelMax=1000;craft.refuel();
if(withSite)saved.addFinishedResearch(site,&mod,&base,true);
std::cout<<'['<<choice<<','<<(withSite?1:0)<<','<<base.storage.count<<','<<transfers<<','<<saved._ids["EQUIV_COUNTER"]<<','<<saved.researchScore<<','<<(saved.isResearched(automatic,false)?1:0)<<']';
}
std::cout << "],\"researchDailyQuantityContextRows\":[";
serviceFirst=true;
for(int dependency:{0,1}) for(int choice:{0,1}) {
if(!serviceFirst)std::cout<<',';serviceFirst=false;
ResearchRule topics[5];auto* source=&topics[0];auto* a=&topics[1];auto* b=&topics[2];auto* site=&topics[3];auto* automatic=&topics[4];
source->name="CORE";a->name="EQUIV_A";b->name="EQUIV_B";site->name="SITE";site->points=3;automatic->name="SITE_AUTO";automatic->cost=0;automatic->points=2;automatic->dependencies={site,dependency==0?a:b};
SavedGame saved;Mod mod;mod.rules={source,a,b,site,automatic};Base base;ResearchCaller caller;saved._ids["EQUIV_COUNTER"]=1;automatic->needed=&mod.supply;automatic->needs=true;
source->free={a,b};a->spawned=b->spawned="SUPPLY";a->count=1;b->count=2;source->increases=a->increases=b->increases={"EQUIV_COUNTER"};
RNG::suppliedChoice=choice;auto* bonus=saved.selectGetOneFree(source);RNG::suppliedChoice=-1;saved.addFinishedResearch(bonus,&mod,&base);saved.addFinishedResearch(source,&mod,&base);
auto primary=ResearchCaller::primary(source,bonus);std::sort(primary.begin(),primary.end());primary.erase(std::unique(primary.begin(),primary.end()),primary.end());
for(auto* topic:primary){caller.spawn(topic,&mod,&base);saved.applyPrimaryCounters(topic);}const auto transfers=base.transfers.size();
for(auto* transfer:base.transfers){transfer->advance(&base);delete transfer;}base.transfers.clear();
Craft craft;craft._base=&base;craft.rule.fuelItem=&mod.supply;craft._fuel=960;craft._stats.fuelMax=1000;craft.refuel();saved.addFinishedResearch(site,&mod,&base,true);
std::cout<<'['<<dependency<<','<<choice<<','<<base.storage.count<<','<<saved.researchScore<<','<<(saved.isResearched(automatic,false)?1:0)<<','<<transfers<<','<<saved._ids["EQUIV_COUNTER"]<<']';
}
std::cout << "],\"researchDailyScorePrefixRows\":[";serviceFirst=true;
for(int mode:{0,1}) for(int choice:{0,1}) {
if(!serviceFirst)std::cout<<',';serviceFirst=false;
ResearchRule topics[5];auto* source=&topics[0];auto* a=&topics[1];auto* b=&topics[2];auto* automatic=&topics[3];auto* gate=&topics[4];
source->name="CORE";a->name="EQUIV_A";b->name="EQUIV_B";automatic->name="UNREACHED_AUTO";gate->name="FUTURE_GATE";automatic->cost=0;automatic->points=99;automatic->dependencies={gate};
source->points=mode==0?-4:4;a->points=mode==0?3:-3;b->points=mode==0?2:-2;source->free={a,b};source->spawned=a->spawned=b->spawned="SUPPLY";source->count=1;a->count=2;b->count=3;source->increases=a->increases=b->increases={"EQUIV_COUNTER"};
SavedGame saved;Mod mod;mod.rules={source,a,b,automatic,gate};Base base;ResearchCaller caller;saved._ids["EQUIV_COUNTER"]=1;
RNG::suppliedChoice=choice;auto* bonus=saved.selectGetOneFree(source);RNG::suppliedChoice=-1;saved.addFinishedResearch(bonus,&mod,&base);int minimum=std::min(0,saved.researchScore),maximum=std::max(0,saved.researchScore);
saved.addFinishedResearch(source,&mod,&base);minimum=std::min(minimum,saved.researchScore);maximum=std::max(maximum,saved.researchScore);
auto primary=ResearchCaller::primary(source,bonus);std::sort(primary.begin(),primary.end());primary.erase(std::unique(primary.begin(),primary.end()),primary.end());for(auto* topic:primary){caller.spawn(topic,&mod,&base);saved.applyPrimaryCounters(topic);}
const auto transfers=base.transfers.size();for(auto* transfer:base.transfers){transfer->advance(&base);delete transfer;}
std::cout<<'['<<mode<<','<<choice<<','<<minimum<<','<<maximum<<','<<saved.researchScore<<','<<base.storage.count<<','<<transfers<<','<<saved._ids["EQUIV_COUNTER"]<<','<<(saved.isResearched(automatic,false)?1:0)<<']';
}
std::cout << "],\"researchDailyProductionScoreRows\":[";serviceFirst=true;
for(int mode:{0,1}) for(int choice:{0,1}) for(int requested:{4,5}) for(bool stopped:{false,true}) {
if(!serviceFirst)std::cout<<',';serviceFirst=false;
ResearchRule topics[5];auto* source=&topics[0];auto* a=&topics[1];auto* b=&topics[2];auto* automatic=&topics[3];auto* gate=&topics[4];
source->name="CORE";a->name="EQUIV_A";b->name="EQUIV_B";automatic->name="UNREACHED_AUTO";gate->name="FUTURE_GATE";
source->points=mode==0?-4:4;a->points=mode==0?3:-3;b->points=mode==0?2:-2;automatic->cost=0;automatic->points=99;automatic->dependencies={gate};source->free={a,b};
source->spawned=a->spawned=b->spawned="SUPPLY";source->count=1;a->count=2;b->count=3;
SavedGame saved;Mod mod;mod.rules={source,a,b,automatic,gate};Base base;saved.bases={&base};ResearchCaller caller;
RNG::suppliedChoice=choice;auto* bonus=saved.selectGetOneFree(source);RNG::suppliedChoice=-1;saved.addFinishedResearch(bonus,&mod,&base);int minimum=std::min(0,saved.researchScore),maximum=std::max(0,saved.researchScore);
saved.addFinishedResearch(source,&mod,&base);minimum=std::min(minimum,saved.researchScore);maximum=std::max(maximum,saved.researchScore);const int daily=saved.researchScore;
auto primary=ResearchCaller::primary(source,bonus);std::sort(primary.begin(),primary.end());primary.erase(std::unique(primary.begin(),primary.end()),primary.end());for(auto* topic:primary)caller.spawn(topic,&mod,&base);
for(auto* transfer:base.transfers){transfer->advance(&base);delete transfer;}base.transfers.clear();
Production progress;progress.rule.time=1;progress.rule.cost=stopped?2000:0;progress._engineers=progress._amount=requested;saved.funds=0;
ProductionItemRule output;output.produced={{&mod.supply,0}};output.points=mode==0?1:-1;ProductionDeliveryCaller delivery{&output};const int stop=progress.scalarStep(&base,&saved,&mod,delivery);
// Production applies a single-sign score once per actual completion, so its endpoints bound its prefixes.
minimum=std::min(minimum,saved.researchScore);maximum=std::max(maximum,saved.researchScore);
Craft craft;craft._base=&base;craft.rule.fuelItem=&mod.supply;craft._fuel=960;craft._stats.fuelMax=1000;craft.refuel();
std::cout<<'['<<mode<<','<<choice<<','<<requested<<','<<(stopped?1:0)<<','<<progress.completedUnits<<','<<daily<<','<<saved.researchScore<<','<<minimum<<','<<maximum<<','<<base.storage.count<<','<<stop<<']';
}
std::cout << "],\"researchDailyProductionSiteScoreRows\":[";serviceFirst=true;
for(int mode:{0,1}) for(int choice:{0,1}) for(int requested:{4,5}) for(bool stopped:{false,true}) {
if(!serviceFirst)std::cout<<',';serviceFirst=false;
ResearchRule topics[6];auto* source=&topics[0];auto* a=&topics[1];auto* b=&topics[2];auto* automatic=&topics[3];auto* gate=&topics[4];auto* site=&topics[5];site->name="SITE";site->points=mode==0?1:-1;
source->name="CORE";a->name="EQUIV_A";b->name="EQUIV_B";automatic->name="UNREACHED_AUTO";gate->name="FUTURE_GATE";
source->points=mode==0?-4:4;a->points=mode==0?3:-3;b->points=mode==0?2:-2;automatic->cost=0;automatic->points=99;automatic->dependencies={gate};source->free={a,b};
source->spawned=a->spawned=b->spawned="SUPPLY";source->count=1;a->count=2;b->count=3;
SavedGame saved;Mod mod;mod.rules={source,a,b,automatic,gate,site};Base base;saved.bases={&base};ResearchCaller caller;
RNG::suppliedChoice=choice;auto* bonus=saved.selectGetOneFree(source);RNG::suppliedChoice=-1;saved.addFinishedResearch(bonus,&mod,&base);int minimum=std::min(0,saved.researchScore),maximum=std::max(0,saved.researchScore);
saved.addFinishedResearch(source,&mod,&base);minimum=std::min(minimum,saved.researchScore);maximum=std::max(maximum,saved.researchScore);const int daily=saved.researchScore;
auto primary=ResearchCaller::primary(source,bonus);std::sort(primary.begin(),primary.end());primary.erase(std::unique(primary.begin(),primary.end()),primary.end());for(auto* topic:primary)caller.spawn(topic,&mod,&base);
for(auto* transfer:base.transfers){transfer->advance(&base);delete transfer;}base.transfers.clear();
Production progress;progress.rule.time=1;progress.rule.cost=stopped?2000:0;progress._engineers=progress._amount=requested;saved.funds=0;
ProductionItemRule output;output.produced={{&mod.supply,0}};output.points=mode==0?1:-1;ProductionDeliveryCaller delivery{&output};const int stop=progress.scalarStep(&base,&saved,&mod,delivery);
// Production applies a single-sign score once per actual completion, so its endpoints bound its prefixes.
minimum=std::min(minimum,saved.researchScore);maximum=std::max(maximum,saved.researchScore);
Craft craft;craft._base=&base;craft.rule.fuelItem=&mod.supply;craft._fuel=960;craft._stats.fuelMax=1000;craft.refuel();
saved.addFinishedResearch(site,&mod,&base,true);minimum=std::min(minimum,saved.researchScore);maximum=std::max(maximum,saved.researchScore);
std::cout<<'['<<mode<<','<<choice<<','<<requested<<','<<(stopped?1:0)<<','<<progress.completedUnits<<','<<daily<<','<<saved.researchScore<<','<<minimum<<','<<maximum<<','<<base.storage.count<<','<<stop<<']';
}
std::cout << "],\"researchDailySiteActiveRows\":[";
serviceFirst=true;
for(int choice:{0,1}) {
if(!serviceFirst)std::cout<<',';serviceFirst=false;
ResearchRule topics[5];auto* source=&topics[0];auto* a=&topics[1];auto* b=&topics[2];auto* site=&topics[3];auto* active=&topics[4];
source->name="CORE";a->name="EQUIV_A";b->name="EQUIV_B";site->name="SITE";site->points=3;active->name="ACTIVE";active->cost=0;active->points=2;
active->dependencies={site};b->points=2;source->free={a,b};a->disables={active};site->reenables={active};
source->spawned=a->spawned=b->spawned="SUPPLY";source->count=1;a->count=b->count=2;source->increases=a->increases=b->increases={"EQUIV_COUNTER"};
ServiceGame game;auto& saved=game.save;Mod mod;mod.rules={source,a,b,site,active};Base base;ResearchCaller caller;ResearchProject project;project.rule=active;base.research={&project};saved.bases={&base};saved._ids["EQUIV_COUNTER"]=1;
RNG::suppliedChoice=choice;auto* bonus=saved.selectGetOneFree(source);RNG::suppliedChoice=-1;saved.addFinishedResearch(bonus,&mod,&base);saved.addFinishedResearch(source,&mod,&base);
auto primary=ResearchCaller::primary(source,bonus);std::sort(primary.begin(),primary.end());primary.erase(std::unique(primary.begin(),primary.end()),primary.end());
for(auto* topic:primary){caller.spawn(topic,&mod,&base);saved.applyPrimaryCounters(topic);}removeDisabled(&game);
for(auto* transfer:base.transfers){transfer->advance(&base);delete transfer;}base.transfers.clear();
saved.addFinishedResearch(site,&mod,&base,true);
std::cout << '[' << choice << ',' << base.research.size() << ',' << (saved.isResearched(active,false)?1:0) << ',' << saved.statuses["ACTIVE"] << ',' << saved.researchScore << ',' << saved._ids["EQUIV_COUNTER"] << ']';
}
std::cout << "],\"researchEmptyCounterRewardRows\":[";
serviceFirst=true;
for(int mode:{0,1,2}) for(int choice:{0,1}) {
if(!serviceFirst) std::cout << ','; serviceFirst=false;
ResearchRule topics[3]; auto* source=&topics[0]; auto* a=&topics[1]; auto* b=&topics[2];
source->name="CORE"; a->name="EQUIV_A"; b->name="EQUIV_B"; source->free={a,b};
source->spawned=a->spawned=b->spawned="SUPPLY"; source->count=1; a->count=b->count=2;
source->increases={"EQUIV_COUNTER"}; a->increases=mode==1?std::vector<std::string>{"EQUIV_COUNTER"}:std::vector<std::string>{""};
b->increases=mode==1?std::vector<std::string>{"EQUIV_COUNTER"}:std::vector<std::string>{}; if(mode!=0)a->decreases={""};
SavedGame saved; Mod mod; mod.rules={source,a,b}; Base base; ResearchCaller caller; saved._ids["EQUIV_COUNTER"]=1; saved._ids["UNKNOWN_COUNTER"]=11;
RNG::suppliedChoice=choice; auto* bonus=saved.selectGetOneFree(source); RNG::suppliedChoice=-1;
saved.addFinishedResearch(bonus,&mod,&base); saved.addFinishedResearch(source,&mod,&base);
auto primary=ResearchCaller::primary(source,bonus); std::sort(primary.begin(),primary.end()); primary.erase(std::unique(primary.begin(),primary.end()),primary.end());
for(auto* topic:primary) { caller.spawn(topic,&mod,&base); saved.applyPrimaryCounters(topic); }
const auto transfers=base.transfers.size(); for(auto* transfer:base.transfers) { transfer->advance(&base); delete transfer; }
std::cout << '[' << mode << ',' << choice << ',' << base.storage.count << ',' << transfers << ',' << saved._ids["EQUIV_COUNTER"]
<< ',' << (saved._ids.contains("")?"true":"false") << ',' << saved._ids["UNKNOWN_COUNTER"] << ',' << saved.researchScore << ']';
}
std::cout << "],\"researchMultiBaseRewardRows\":[";
serviceFirst=true;
for(int count:{2,3}) for(bool incoming:{false,true}) for(int firstChoice:{0,1,2}) for(int secondChoice:{0,1}) {
if(!serviceFirst) std::cout << ','; serviceFirst=false;
ResearchRule topics[4]; auto* source=&topics[0]; source->name="CORE"; source->count=1;
for(int i=1;i<4;i++) { topics[i].name="EQUIV_"+std::string(1,char('A'+i-1)); topics[i].count=2; source->free.push_back(&topics[i]); }
for(auto& topic:topics) { topic.spawned="SUPPLY"; topic.increases={"EQUIV_COUNTER"}; }
SavedGame saved; Mod mod; mod.rules={&topics[0],&topics[1],&topics[2],&topics[3]}; Base bases[3]; ResearchCaller caller; saved._ids["EQUIV_COUNTER"]=1;
for(int index=0;index<count;index++) { saved.bases.push_back(&bases[index]); if(incoming) { auto* transfer=new Transfer(1); transfer->setItems(&mod.supply); bases[index].transfers.push_back(transfer); } }
for(int index=0;index<count;index++) {
RNG::suppliedChoice=index==0?firstChoice:index==1?secondChoice:0; auto* bonus=saved.selectGetOneFree(source); RNG::suppliedChoice=-1;
saved.addFinishedResearch(bonus,&mod,&bases[index]); saved.addFinishedResearch(source,&mod,&bases[index]);
auto primary=ResearchCaller::primary(source,bonus); std::sort(primary.begin(),primary.end()); primary.erase(std::unique(primary.begin(),primary.end()),primary.end());
for(auto* topic:primary) { caller.spawn(topic,&mod,&bases[index]); saved.applyPrimaryCounters(topic); }
}
int mask=0,transfers=0; for(int i=1;i<4;i++) if(saved.isResearched(&topics[i],false)) mask|=1<<(i-1);
std::cout << '[' << count << ',' << (incoming?"true":"false") << ',' << firstChoice << ',' << secondChoice << ',' << mask << ",[";
for(int index=0;index<count;index++) { auto& base=bases[index]; transfers+=(int)base.transfers.size(); for(auto* transfer:base.transfers) { transfer->advance(&base); delete transfer; } if(index)std::cout<<',';std::cout<<base.storage.count; }
std::cout << "]," << transfers << ',' << saved._ids["EQUIV_COUNTER"] << ',' << saved.researchScore << ']';
}
std::cout << "],\"researchEmptyOnlyRewardRows\":[";
serviceFirst=true;
for(int labels:{0,10000}) for(int choice:{0,1}) {
if(!serviceFirst) std::cout << ','; serviceFirst=false;
ResearchRule topics[3]; auto* source=&topics[0]; auto* a=&topics[1]; auto* b=&topics[2];
source->name="CORE"; a->name="EQUIV_A"; b->name="EQUIV_B"; b->points=2;
source->free={a,b}; source->increases=std::vector<std::string>(labels,"");
SavedGame saved; Mod mod; mod.rules={source,a,b}; Base base; ResearchCaller caller; base.storage.count=1; saved._ids["UNKNOWN_COUNTER"]=11;
RNG::suppliedChoice=choice; auto* bonus=saved.selectGetOneFree(source); RNG::suppliedChoice=-1;
saved.addFinishedResearch(bonus,&mod,&base); saved.addFinishedResearch(source,&mod,&base);
auto primary=ResearchCaller::primary(source,bonus); std::sort(primary.begin(),primary.end()); primary.erase(std::unique(primary.begin(),primary.end()),primary.end());
for(auto* topic:primary) { caller.spawn(topic,&mod,&base); saved.applyPrimaryCounters(topic); }
std::cout << '[' << labels << ',' << choice << ',' << base.storage.count << ',' << base.transfers.size()
<< ',' << saved._ids["UNKNOWN_COUNTER"] << ',' << saved.researchScore << ']';
}
std::cout << "],\"researchOrderedArrivalRows\":[";
serviceFirst=true;
for(bool duplicate:{false,true}) for(int choice:{0,1}) {
if(!serviceFirst) std::cout << ','; serviceFirst=false;
ResearchRule topics[3]; auto* source=&topics[0]; auto* a=&topics[1]; auto* b=&topics[2];
source->name="CORE"; a->name="EQUIV_A"; b->name="EQUIV_B"; source->free={a,b};
source->spawned="SUPPLY"; source->count=1;
a->list={"CLIP","SUPPLY"}; b->list={"SUPPLY","CLIP"}; if(duplicate) { a->list.push_back("CLIP"); b->list.insert(b->list.begin(),"CLIP"); }
SavedGame saved; Mod mod; mod.extraItems=true; mod.clip.type="CLIP"; mod.clip.battle=2; mod.rules={source,a,b}; Base base; ResearchCaller caller;
RNG::suppliedChoice=choice; auto* bonus=saved.selectGetOneFree(source); RNG::suppliedChoice=-1;
saved.addFinishedResearch(bonus,&mod,&base); saved.addFinishedResearch(source,&mod,&base);
auto primary=ResearchCaller::primary(source,bonus); std::sort(primary.begin(),primary.end()); primary.erase(std::unique(primary.begin(),primary.end()),primary.end());
for(auto* topic:primary) caller.spawn(topic,&mod,&base);
std::cout << '[' << (duplicate?"true":"false") << ',' << choice << ",[";
for(size_t i=0;i<base.transfers.size();i++) { if(i)std::cout<<','; std::cout<<'"'<<base.transfers[i]->_itemId->type<<'"'; }
const auto count=base.transfers.size(); for(auto* transfer:base.transfers) { transfer->advance(&base); delete transfer; }
std::cout << "]," << base.storage.count << ',' << base.storage.getItem(&mod.clip) << ',' << count << ',' << saved.researchScore << ']';
}
std::cout << "],\"researchReusableArrivalRows\":[";
serviceFirst=true;
for(int choice:{0,1}) {
if(!serviceFirst) std::cout << ','; serviceFirst=false;
ResearchRule topics[3]; auto* source=&topics[0]; auto* a=&topics[1]; auto* b=&topics[2];
source->name="CORE"; a->name="EQUIV_A"; b->name="EQUIV_B"; source->free={a,b}; a->list={"CLIP","SUPPLY"}; b->list={"SUPPLY","CLIP"};
SavedGame saved; Mod mod; mod.extraItems=true; mod.clip.type="CLIP"; mod.clip.battle=0; mod.rules={source,a,b}; Base base; ResearchCaller caller;
Craft craft; CraftWeapon first,second; first.rule.clip=&mod.clip; second.rule.clip=&mod.supply; first.rule.ammoMax=second.rule.ammoMax=10; craft._weapons={&first,&second};
craft._status="STR_REFUELLING"; craft.rule.fuelItem=&mod.supply; craft._fuel=960; craft._stats.fuelMax=1000;
RNG::suppliedChoice=choice; auto* bonus=saved.selectGetOneFree(source); RNG::suppliedChoice=-1;
saved.addFinishedResearch(bonus,&mod,&base); saved.addFinishedResearch(source,&mod,&base);
auto primary=ResearchCaller::primary(source,bonus); std::sort(primary.begin(),primary.end()); primary.erase(std::unique(primary.begin(),primary.end()),primary.end());
for(auto* topic:primary) caller.spawn(topic,&mod,&base);
std::cout << '[' << choice << ",["; for(size_t i=0;i<base.transfers.size();i++) { if(i)std::cout<<','; std::cout<<'"'<<base.transfers[i]->_itemId->type<<'"'; }
for(auto* transfer:base.transfers) transfer->advance(&base);
for(auto* transfer:base.transfers) { if(transfer->_itemId->getBattleType()==0) craft.reuseItem(transfer->_itemId); delete transfer; }
std::cout << "]," << base.storage.count << ',' << base.storage.getItem(&mod.clip) << ',' << (first.rearming?"true":"false") << ',' << (second.rearming?"true":"false") << ",\"" << craft._status << "\"]";
}
std::cout << "],\"researchLargeReusableRows\":[";
serviceFirst=true;
for(int count:{1000,10000}) {
if(!serviceFirst) std::cout << ','; serviceFirst=false;
ResearchRule source,reward; source.name="CORE"; reward.name="EQUIV_A"; source.spawned="SUPPLY"; source.count=1; reward.list=std::vector<std::string>(count,"SUPPLY"); source.free={&reward};
SavedGame saved; Mod mod; mod.rules={&source,&reward}; Base base; ResearchCaller caller;
RNG::suppliedChoice=0; auto* bonus=saved.selectGetOneFree(&source); RNG::suppliedChoice=-1; saved.addFinishedResearch(bonus,&mod,&base); saved.addFinishedResearch(&source,&mod,&base);
for(auto* topic:ResearchCaller::primary(&source,bonus)) caller.spawn(topic,&mod,&base);
const auto transfers=base.transfers.size(); for(auto* transfer:base.transfers) { transfer->advance(&base); delete transfer; }
std::cout << '[' << count << ',' << base.storage.count << ',' << transfers << ',' << saved.researchScore << ']';
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
std::cout << "],\"productionAmmunitionMaterialRows\":[";
serviceFirst=true;
for(int stock:{0,1,2,4}) for(int input:{1,2}) for(int amount:{1,3}) for(int assigned:{0,1,2}) for(int choice:{0,3}) {
if(!serviceFirst) std::cout << ','; serviceFirst=false;
RNG::calls=0; RNG::suppliedChoice=choice;
Item item; item.clipSize=4; Base base; base.storage.count=stock; ServiceGame game; ResearchMod mod;
Craft gun; gun._base=&base; gun._status="STR_REARMING"; gun._fuel=100;
CraftWeapon weapon; weapon.rule.clip=&item; weapon.rule.ammoMax=10; weapon.rule.saving=true; weapon._ammo=9; weapon.rearming=true;
gun._weapons={&weapon}; base.crafts={&gun}; gun.rearm();
Production progress; progress.rule.time=1; progress.rule.requiredItems={{&item,input}}; progress._engineers=assigned; progress._amount=amount;
ProductionItemRule rule; rule.produced={{&item,1}}; ProductionDeliveryCaller delivery{&rule};
const auto result=progress.scalarStep(&base,&game.save,&mod,delivery);
std::cout << '[' << stock << ',' << input << ',' << amount << ',' << assigned << ',' << choice << ','
<< weapon.getAmmo() << ',' << (weapon.isRearming()?"true":"false") << ',' << progress.completedUnits << ','
<< progress._timeSpent << ',' << result << ',' << base.storage.count << ',' << RNG::calls << ']';
}
RNG::suppliedChoice=-1;
std::cout << "],\"productionAmmunitionSequenceRows\":[";
serviceFirst=true;
for(int stock:{1,2,4,8}) for(int input:{1,2}) for(int nextInput:{1,2}) for(int choice:{0,3}) {
if(!serviceFirst) std::cout << ','; serviceFirst=false;
RNG::calls=0; RNG::suppliedChoice=choice;
Item item; item.clipSize=4; Base base; base.storage.count=stock; ServiceGame game; ResearchMod mod;
Craft gun; gun._base=&base; gun._status="STR_REARMING"; gun._fuel=100;
CraftWeapon weapon; weapon.rule.clip=&item; weapon.rule.ammoMax=10; weapon.rule.saving=true; weapon._ammo=9; weapon.rearming=true;
gun._weapons={&weapon}; base.crafts={&gun}; gun.rearm();
Production first,second; first.rule.time=second.rule.time=1;
first.rule.requiredItems={{&item,input}}; second.rule.requiredItems={{&item,nextInput}};
first._engineers=second._engineers=2; first._amount=second._amount=3;
ProductionItemRule rule; rule.produced={{&item,1}}; ProductionDeliveryCaller delivery{&rule};
const auto firstResult=first.scalarStep(&base,&game.save,&mod,delivery);
const auto secondResult=second.scalarStep(&base,&game.save,&mod,delivery);
std::cout << '[' << stock << ',' << input << ',' << nextInput << ',' << choice << ','
<< first.completedUnits << ',' << firstResult << ',' << first._timeSpent << ','
<< second.completedUnits << ',' << secondResult << ',' << second._timeSpent << ','
<< base.storage.count << ',' << weapon.getAmmo() << ',' << (weapon.isRearming()?"true":"false") << ',' << RNG::calls << ']';
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
std::cout << "],\"flightFuelCapRows\":[";
serviceFirst=true;
for(bool itemFuel:{false,true}) for(bool destination:{false,true}) for(bool patrolFree:{false,true})
for(int speed:{0,500,6000}) for(int fuel:{0,100,101,245}) for(int excess:{0,7}) {
if(!serviceFirst) std::cout << ','; serviceFirst=false;
Craft craft; Base base; Item item; Target target;
craft._base=&base; craft._stats.speedMax=6000; craft._stats.fuelMax=100;
craft._status="STR_OUT"; craft.speed=speed; craft._fuel=fuel; craft._excessFuel=excess;
craft.rule.fuelItem=itemFuel?&item:nullptr; craft.rule.patrolFree=patrolFree;
craft._dest=destination?&target:nullptr;
craft.consumeFuel(0);
std::cout << '[' << (itemFuel?"true":"false") << ',' << (destination?"true":"false") << ',' << (patrolFree?"true":"false")
<< ',' << speed << ',' << fuel << ',' << excess << ',' << craft._fuel << ',' << craft._excessFuel << ',' << base.storage.count << ']';
}
std::cout << "],\"optionalCraftArrivalRows\":[";
serviceFirst=true;
for(bool deleted:{false,true}) for(int takeoff:{0,5}) for(bool leaderFirst:{false,true}) for(bool itemFuel:{false,true}) {
if(!serviceFirst) std::cout << ','; serviceFirst=false;
Craft leader,escort; Base home; AlienBase target; SavedGame game; Ufo hunter; Item item;
FuelCaller fuelCaller; AlienBaseArrivalCaller arrival;
leader._base=&home; leader._dest=&target; leader._status="STR_OUT"; leader.speed=1000;
leader._stats.speedMax=2100; leader._stats.fuelMax=1000; leader._fuel=1000; leader._takeoff=takeoff;
leader.autoPatrol=false; leader.rule.fuelItem=itemFuel?&item:nullptr;
target.discovered=true;
escort._base=&home; escort._dest=&leader; escort._status="STR_OUT"; escort.speed=2100;
escort._stats.speedMax=2100; escort._stats.fuelMax=1000; escort._fuel=1000; escort.autoPatrol=false; escort._missionComplete=true;
hunter._dest=&leader; hunter._isHunting=true; hunter.speed=3300;
hunter._origWaypoint=new Waypoint(); hunter._origWaypoint->lon=1; hunter._origWaypoint->lat=0.6;
game.ufos={&hunter};
if(deleted) leader.returnToBase();
std::vector<Craft*> ordered=leaderFirst?std::vector<Craft*>{&leader,&escort}:std::vector<Craft*>{&escort,&leader};
for(auto* craft:ordered) fuelCaller.fuel(craft);
for(auto* craft:ordered) {
if(craft->think()) game.stopHuntingXcomCraft(craft);
else if(craft==&leader) arrival.arrive(craft,&target,false);
else arrival.arriveEscort(craft,&leader);
}
auto destinationKind=[&](Craft& craft) { return !craft._dest?0:craft._dest==&home?1:craft._dest==&target?2:3; };
std::cout << '[' << (deleted?"true":"false") << ',' << takeoff << ',' << (leaderFirst?"true":"false") << ',' << (itemFuel?"true":"false")
<< ',' << leader._fuel << ',' << '\"' << leader._status << '\"' << ',' << leader.speed << ',' << leader._takeoff << ',' << destinationKind(leader)
<< ',' << escort._fuel << ',' << '\"' << escort._status << '\"' << ',' << escort.speed << ',' << escort._takeoff << ',' << destinationKind(escort)
<< ',' << (hunter._isHunting?"true":"false") << ',' << (hunter._origWaypoint!=nullptr?"true":"false") << ']';
if(hunter._origWaypoint) { delete hunter._origWaypoint; hunter._origWaypoint=nullptr; }
if(dynamic_cast<Waypoint*>(hunter._dest)) { delete hunter._dest; hunter._dest=nullptr; }
}
std::cout << "],\"alienBaseArrivalRows\":[";
serviceFirst=true;
for(bool discovered:{false,true}) for(int units:{0,1}) for(bool allow:{false,true})
for(bool suppress:{false,true}) for(int takeoff:{0,5}) {
if(!serviceFirst) std::cout << ','; serviceFirst=false;
Craft craft; AlienBase target; Base home; AlienBaseArrivalCaller caller;
craft._base=&home; craft._dest=&target; craft._status="STR_OUT"; craft.speed=1000;
craft._stats.speedMax=6000; craft._stats.fuelMax=1000; craft._fuel=1000; craft.autoPatrol=false;
craft.units=units; craft.rule.landing=allow; craft._takeoff=takeoff; target.discovered=discovered;
craft.think(); caller.arrive(&craft,&target,suppress);
std::cout << '[' << (discovered?"true":"false") << ',' << units << ',' << (allow?"true":"false")
<< ',' << (suppress?"true":"false") << ',' << takeoff << ',' << (caller.landing?"true":"false")
<< ',' << (craft._dest==&home?"true":"false") << ',' << craft.speed << ',' << craft._takeoff << ',' << '\"' << craft._status << '\"' << ']';
}
std::cout << "],\"returnedCraftHuntRows\":[";
serviceFirst=true;
for(bool match:{false,true}) for(bool original:{false,true}) {
if(!serviceFirst) std::cout << ','; serviceFirst=false;
Craft craft,other; Ufo hunter; SavedGame game;
hunter._dest=match?static_cast<Target*>(&craft):static_cast<Target*>(&other);
hunter._isHunting=true; hunter.speed=3300;
if(original) { hunter._origWaypoint=new Waypoint(); hunter._origWaypoint->lon=1; hunter._origWaypoint->lat=0.6; }
game.ufos={&hunter}; bool threw=false;
try { game.stopHuntingXcomCraft(&craft); } catch(const Exception&) { threw=true; }
std::cout << '[' << (match?"true":"false") << ',' << (original?"true":"false") << ',' << (threw?"true":"false")
<< ',' << (hunter._isHunting?"true":"false") << ',' << (hunter._isEscorting?"true":"false")
<< ',' << (hunter._origWaypoint!=nullptr?"true":"false") << ',' << (dynamic_cast<Craft*>(hunter._dest)!=nullptr?"true":"false") << ',' << hunter.speed << ']';
if(hunter._origWaypoint) { delete hunter._origWaypoint; hunter._origWaypoint=nullptr; }
if(dynamic_cast<Waypoint*>(hunter._dest)) { delete hunter._dest; hunter._dest=nullptr; }
}
std::cout << "],\"optionalReturnFuelRows\":[";
serviceFirst=true;
for(int limit:{0,139}) for(int fuel:{0,1,10,21,100,139,149,150,160,200,1000,1500})
for(bool low:{false,true}) for(bool deleted:{false,true}) {
if(!serviceFirst) std::cout << ','; serviceFirst=false;
Craft craft; Base base; Target target; FuelCaller caller;
craft._base=&base; craft._stats.speedMax=2100; craft._stats.fuelMax=1000;
craft._status="STR_OUT"; craft.speed=1000; craft._fuel=fuel; craft._fuelLimit=limit;
craft._lowFuel=low; craft._dest=&target; craft.autoPatrol=false;
if(deleted) craft.returnToBase();
caller.fuel(&craft);
std::cout << '[' << limit << ',' << fuel << ',' << (low?"true":"false") << ',' << (deleted?"true":"false")
<< ',' << craft._fuel << ',' << (craft._lowFuel?"true":"false") << ',' << craft.speed << ',' << (craft._dest==&base?"true":"false") << ']';
}
std::cout << "],\"optionalRecallArrivalRows\":[";
serviceFirst=true;
for(int fuel:{0,1,10}) for(int takeoff:{0,5}) for(bool deleted:{false,true}) {
if(!serviceFirst) std::cout << ','; serviceFirst=false;
Craft craft; Base base; Target target; FuelCaller caller;
craft._base=&base; craft._stats.speedMax=2100; craft._stats.fuelMax=1000;
craft._status="STR_OUT"; craft.speed=1000; craft._fuel=fuel; craft._takeoff=takeoff;
craft._dest=&target; craft.autoPatrol=false;
if(deleted) craft.returnToBase();
caller.fuel(&craft); const bool arrived=craft.think();
std::cout << '[' << fuel << ',' << takeoff << ',' << (deleted?"true":"false") << ',' << (arrived?"true":"false")
<< ',' << craft._fuel << ',' << '\"' << craft._status << '\"' << ',' << craft.speed << ',' << craft._takeoff
<< ',' << (craft._lowFuel?"true":"false") << ',' << (craft._missionComplete?"true":"false") << ',' << (craft._dest==nullptr?"true":"false") << ']';
}
std::cout << "],\"optionalEscortFuelRows\":[";
serviceFirst=true;
for(int fuel:{100,149,150,160,200,1500}) for(bool low:{false,true}) for(bool deleted:{false,true}) for(bool leaderFirst:{false,true}) {
if(!serviceFirst) std::cout << ','; serviceFirst=false;
Craft leader,follower; Base base; Target target; Item item; FuelCaller caller;
leader._base=follower._base=&base; leader._stats.speedMax=6000; leader._stats.fuelMax=1000;
leader._status="STR_OUT"; leader.speed=1000; leader._fuel=1000; leader.rule.fuelItem=&item; leader._dest=&target;
follower._stats.speedMax=2100; follower._stats.fuelMax=1000; follower._fuelLimit=139;
follower._status="STR_OUT"; follower.speed=2100; follower._fuel=fuel; follower._lowFuel=low; follower._dest=&leader;
if(deleted) leader.returnToBase();
const std::vector<Craft*> ordered=leaderFirst?std::vector<Craft*>{&leader,&follower}:std::vector<Craft*>{&follower,&leader};
for(Craft* craft:ordered) caller.fuel(craft);
std::cout << '[' << fuel << ',' << (low?"true":"false") << ',' << (deleted?"true":"false") << ',' << (leaderFirst?"true":"false")
<< ',' << leader._fuel << ',' << (leader._lowFuel?"true":"false") << ',' << leader.speed
<< ',' << follower._fuel << ',' << (follower._lowFuel?"true":"false") << ',' << follower.speed << ',' << (follower._dest==&base?"true":"false") << ']';
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
for (int mode : {0,1}) for (int variant : {0,1,2,3,4,5,6,7,8}) {
if (!first) std::cout << ','; first = false;
Craft a,b; a.id = 1; b.id = 2; a._stats.speedMax = 2000; b._stats.speedMax = 6000;
a.distance = Nautical(100); b.distance = Nautical(100);
Ufo ufo; ufo.mode = mode; std::vector<Craft*> crafts{&a,&b};
if (variant == 1) { ufo._dest = &a; ufo.hunting = true; b._stats.speedMax = a._stats.speedMax; }
if (variant == 2) { ufo._dest = &a; ufo.hunting = true; a.rule.hidden = true; b._missionComplete = true; }
if (variant == 3) { a._lowFuel = true; b.rule.hidden = true; }
if (variant == 4) { a.distance = Nautical(301); b.distance = Nautical(300); }
if (variant == 5) { ufo._stats.radarRange = 0; }
if (variant == 6) { ufo._dest = &a; ufo.hunting = true; a._lowFuel = true; }
if (variant == 7) { a.distance = Nautical(301); b.distance = Nautical(302); }
if (variant == 8) { ufo._stats.radarRange = -1; a.distance = 0; b.distance = 0; }
auto* selected = select(&ufo,crafts);
std::cout << '[' << mode << ',' << variant << ',' << (selected ? selected->id : 0) << ']';
}
std::cout << "],\"retargetRows\":[";
first = true;
for (int percent : {-1,0,50,100,150}) {
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
std::cout << "],\"spawnEscortChainRows\":["; first=true;
for(int flags:{0,1,2,3}) for(int sourceStatus:{0,1}) {
if(!first) std::cout << ','; first=false;
Ufo ufo,leader,other;
ufo.status=sourceStatus; ufo._isHunterKiller=true; ufo.lon=0.3; ufo.lat=0.1; ufo.speed=550;
ufo._dest=new Waypoint(); ufo._dest->lon=0.4; ufo._dest->lat=0.2;
leader.uniqueId=9; leader.lon=0.7; leader.lat=0.3;
leader._isHunting=flags==1; leader._isEscorting=flags>=2;
leader._dest=flags==3?&leader:&other;
SavedGame game; game.ufos={&leader,&other}; spawnEscort(&ufo,game,true);
auto* followed=dynamic_cast<Ufo*>(ufo._dest);
std::cout << '[' << flags << ',' << sourceStatus << ',' << (ufo._isEscorting?"true":"false") << ','
<< (followed?followed->uniqueId:0) << ',' << ufo.lon << ',' << ufo.lat << ',' << ufo.speed << ','
<< ufo._origWaypoint->lon << ',' << ufo._origWaypoint->lat << ']';
delete ufo._origWaypoint;
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
std::cout << "],\"productionCraftBirthRows\":["; first=true;
for(bool fixed:{false,true}) for(int fuel:{0,100}) for(int delay:{-1,0,1,3}) {
if(!first) std::cout << ','; first=false;
ProductionCraftRule rule; rule.craft.fixed=fixed; rule.craft.fuelMax=fuel; rule.times={0,0,delay};
ProductionCraftCaller caller{&rule}; Base owner; owner.lon=0.45; owner.lat=0.21; SavedGame saved; Mod mod;
caller.complete(&owner,&saved,&mod);
auto* craft=owner.crafts.empty()?owner.transfers.front()->_craft:owner.crafts.front();
if(owner.transfers.empty()&&craft->_status=="STR_REFUELLING") craft->refuel();
std::cout << '[' << (fixed?"true":"false") << ',' << fuel << ',' << delay << ',' << craft->id << ',' << owner.crafts.size() << ',' << owner.transfers.size() << ','
<< (owner.transfers.empty()?0:owner.transfers.front()->getHours()) << ',' << '"' << craft->_status << '"' << ',' << craft->_fuel << ',' << craft->lon << ',' << craft->lat << ','
<< (craft->_weapons.empty()?0:craft->_weapons.front()->getAmmo()) << ',' << (craft->_weapons.empty()?"false":craft->_weapons.front()->isRearming()?"true":"false") << ',' << (craft->autoPatrol?"true":"false") << ']';
for(auto* weapon:craft->_weapons) delete weapon; for(auto* transfer:owner.transfers) delete transfer; delete craft;
}
std::cout << "],\"productionCraftHuntingRows\":["; first=true;
for(int delay:{-1,0,1,3}) for(bool fixed:{false,true}) for(int stock:{0,1,2,3,4,5,6,7}) for(int requested:{1,2}) for(int cost:{0,3}) {
if(!first) std::cout << ','; first=false;
Base owner; owner.lon=0.45; owner.lat=0.21; owner.storage.count=stock; SavedGame saved; saved.funds=5; Mod mod;
Craft existing; existing.id=1; existing._base=&owner; existing._fuel=60; existing.rule.fuelItem=&mod.supply; owner.crafts.push_back(&existing);
ProductionCraftRule birth; birth.craft.fixed=fixed; birth.craft.fuelItem=&mod.supply; birth.times={0,0,delay};
ProductionItemRule output; ProductionDeliveryCaller delivery{&output}; delivery.craftBirth=&birth;
Production project; project.rule.time=2; project.rule.cost=cost; project.rule.requiredItems={{&mod.supply,1}}; project._timeSpent=1; project._engineers=requested*2; project._amount=2;
int stop=project.scalarStep(&owner,&saved,&mod,delivery);
ServiceGame game; for(auto* craft:owner.crafts) if(craft->_status=="STR_REFUELLING") { auto missing=craft->refuel(); if(missing.empty()&&craft->_status=="STR_READY"&&craft->autoPatrol) autoPatrol(craft,&game); }
FuelCaller fuelCaller; for(auto* craft:owner.crafts) if(craft->_status=="STR_OUT") fuelCaller.fuel(craft);
Ufo hunter; std::vector<Craft*> active; for(auto* craft:owner.crafts) if(craft->_status=="STR_OUT") active.push_back(craft);
auto* target=select(&hunter,active);
std::cout << '[' << delay << ',' << (fixed?"true":"false") << ',' << stock << ',' << requested << ',' << cost << ',' << project.completedUnits << ',' << owner.storage.count << ',' << saved.funds << ',' << stop << ',' << owner.crafts.size()-1 << ',' << owner.transfers.size() << ",[";
bool bornFirst=true; auto emit=[&](Craft* craft) { if(!bornFirst) std::cout << ','; bornFirst=false; std::cout << '[' << craft->id << ',' << '"' << craft->_status << '"' << ',' << craft->_fuel << ',' << (craft->_weapons.empty()?"false":craft->_weapons.front()->isRearming()?"true":"false") << ']'; };
for(auto* craft:owner.crafts) if(craft!=&existing) emit(craft); for(auto* transfer:owner.transfers) emit(transfer->_craft);
std::cout << "]," << '"' << existing._status << '"' << ',' << existing._fuel << ',' << (target?target->id:0) << ']';
for(auto* craft:owner.crafts) if(craft!=&existing) { for(auto* weapon:craft->_weapons) delete weapon; delete craft; }
for(auto* transfer:owner.transfers) { for(auto* weapon:transfer->_craft->_weapons) delete weapon; delete transfer->_craft; delete transfer; }
for(auto* waypoint:game.save.waypoints) delete waypoint;
}
std::cout << "],\"researchOrderedCounterRows\":["; first=true;
for(int initial:{-1,1,3,20,INT_MAX-1}) for(bool bonus:{false,true}) for(bool duplicate:{false,true}) {
if(!first) std::cout << ','; first=false;
ResearchRule topics[2]; auto* source=&topics[0]; auto* reward=&topics[1]; source->increases={"COUNTER_TRACE"}; source->decreases={"COUNTER_TRACE","COUNTER_TRACE"}; reward->increases={"COUNTER_TRACE","COUNTER_TRACE"}; reward->decreases={"COUNTER_TRACE"};
SavedGame saved; if(initial!=-1) saved._ids["COUNTER_TRACE"]=initial;
auto primary=ResearchCaller::primary(source,bonus?reward:nullptr); if(duplicate) primary.push_back(source); std::sort(primary.begin(),primary.end()); primary.erase(std::unique(primary.begin(),primary.end()),primary.end());
for(auto* topic:primary) saved.applyPrimaryCounters(topic);
std::cout << '[' << initial << ',' << (bonus?"true":"false") << ',' << (duplicate?"true":"false") << ',' << saved._ids["COUNTER_TRACE"] << ",[";
bool changeFirst=true; for(auto* topic:primary) { for(auto& name:topic->increases) { if(!changeFirst) std::cout << ','; changeFirst=false; std::cout << "true"; } for(auto& name:topic->decreases) { if(!changeFirst) std::cout << ','; changeFirst=false; std::cout << "false"; } }
std::cout << "]]";
}
std::cout << "],\"researchQuantityCounterBirthRows\":[";first=true;
for(bool person:{false,true}) for(int initial:{1,20,INT_MAX-1}) for(int choice:{0,1}) {
if(!first)std::cout<<',';first=false;
ResearchRule topics[3];auto* source=&topics[0];auto* a=&topics[1];auto* b=&topics[2];source->name="CORE";a->name="EQUIV_A";b->name="EQUIV_B";source->free={a,b};
const std::string counter=person?"STR_SOLDIER":"PRODUCED_CRAFT";source->increases={counter};source->decreases={counter,counter};a->increases=b->increases={counter,counter};a->decreases=b->decreases={counter};
source->spawned=a->spawned=b->spawned="SUPPLY";source->count=1;a->count=2;b->count=3;
SavedGame saved;Mod mod;mod.rules={source,a,b};Base base;ResearchCaller caller;saved._ids[counter]=initial;saved.bases={&base};
RNG::suppliedChoice=choice;auto* bonus=saved.selectGetOneFree(source);RNG::suppliedChoice=-1;saved.addFinishedResearch(bonus,&mod,&base);saved.addFinishedResearch(source,&mod,&base);
auto primary=ResearchCaller::primary(source,bonus);std::sort(primary.begin(),primary.end());primary.erase(std::unique(primary.begin(),primary.end()),primary.end());for(auto* topic:primary){caller.spawn(topic,&mod,&base);saved.applyPrimaryCounters(topic);}
for(auto* transfer:base.transfers){transfer->advance(&base);delete transfer;}base.transfers.clear();const auto before=saved._ids[counter];saved.craftCounter=saved.soldierCounter=before;
ProductionCraftRule birth;birth.craft.fuelMax=0;ProductionItemRule output;output.person=person?"RECRUIT":"";ProductionDeliveryCaller delivery{&output};if(!person)delivery.craftBirth=&birth;delivery.deliver(&base,&saved);
Craft craft;craft._base=&base;craft.rule.fuelItem=&mod.supply;craft._fuel=960;craft._stats.fuelMax=1000;craft.refuel();
const auto id=person?base.transfers.back()->_soldier->id:base.crafts.back()->id;
std::cout<<'['<<(person?1:0)<<','<<initial<<','<<choice<<','<<before<<','<<id<<','<<(person?saved.soldierCounter:saved.craftCounter)<<','<<base.storage.count<<','<<base.transfers.size()<<','<<saved.researchScore<<']';
if(!person)delete base.crafts.back();for(auto* transfer:base.transfers){delete transfer->_soldier;delete transfer;}
}
std::cout << "],\"researchDailyCounterAlternativeRows\":[";first=true;
for(int mode:{0,1}) for(int initial:{1,20,INT_MAX-1}) for(int choice:{0,1}) {
if(!first)std::cout<<',';first=false;
ResearchRule topics[3];auto* source=&topics[0];auto* a=&topics[1];auto* b=&topics[2];source->name="CORE";a->name="EQUIV_A";b->name="EQUIV_B";source->free={a,b};
source->increases={"EQUIV_COUNTER"};a->increases={mode==0?"LEFT_COUNTER":"TRACE_COUNTER"};b->increases={mode==0?"RIGHT_COUNTER":"TRACE_COUNTER"};
if(mode==1){source->decreases={"TRACE_COUNTER","TRACE_COUNTER"};b->increases.push_back("TRACE_COUNTER");b->decreases={"TRACE_COUNTER"};}
source->spawned=a->spawned=b->spawned="SUPPLY";source->count=1;a->count=2;b->count=3;
SavedGame saved;Mod mod;mod.rules={source,a,b};Base base;ResearchCaller caller;saved._ids={{"EQUIV_COUNTER",1},{"LEFT_COUNTER",initial},{"RIGHT_COUNTER",initial},{"TRACE_COUNTER",initial}};saved.bases={&base};
RNG::suppliedChoice=choice;auto* bonus=saved.selectGetOneFree(source);RNG::suppliedChoice=-1;saved.addFinishedResearch(bonus,&mod,&base);saved.addFinishedResearch(source,&mod,&base);
auto primary=ResearchCaller::primary(source,bonus);std::sort(primary.begin(),primary.end());primary.erase(std::unique(primary.begin(),primary.end()),primary.end());for(auto* topic:primary){caller.spawn(topic,&mod,&base);saved.applyPrimaryCounters(topic);}const auto transfers=base.transfers.size();
for(auto* transfer:base.transfers){transfer->advance(&base);delete transfer;}base.transfers.clear();
Craft craft;craft._base=&base;craft.rule.fuelItem=&mod.supply;craft._fuel=960;craft._stats.fuelMax=1000;craft.refuel();
std::cout<<'['<<mode<<','<<initial<<','<<choice<<','<<saved._ids["EQUIV_COUNTER"]<<','<<saved._ids["LEFT_COUNTER"]<<','<<saved._ids["RIGHT_COUNTER"]<<','<<saved._ids["TRACE_COUNTER"]<<','<<base.storage.count<<','<<saved.researchScore<<','<<transfers<<']';
}
std::cout << "],\"researchCounterSequentialBaseRows\":["; first=true;
for(int initial:{-1,1,3,20,INT_MAX-1}) for(int bases:{1,2}) {
if(!first) std::cout << ','; first=false;
ResearchRule topics[3]; auto* source=&topics[0]; auto* reward=&topics[1]; auto* other=&topics[2]; source->name="A_COUNTER_SOURCE"; reward->name="B_COUNTER_REWARD"; other->name="C_COUNTER_OTHER";
source->sequential=true; source->free={reward,other}; source->increases={"COUNTER_TRACE"}; source->decreases={"COUNTER_TRACE","COUNTER_TRACE"}; reward->increases={"COUNTER_TRACE","COUNTER_TRACE"}; reward->decreases={"COUNTER_TRACE"};
SavedGame saved; Mod mod; mod.rules={source,reward,other}; Base base; if(initial!=-1) saved._ids["COUNTER_TRACE"]=initial;
for(int index=0;index<bases;index++) { auto* bonus=saved.selectGetOneFree(source); if(bonus) saved.addFinishedResearch(bonus,&mod,&base); saved.addFinishedResearch(source,&mod,&base);
auto primary=ResearchCaller::primary(source,bonus); std::sort(primary.begin(),primary.end()); primary.erase(std::unique(primary.begin(),primary.end()),primary.end()); for(auto* topic:primary) saved.applyPrimaryCounters(topic); }
std::cout << '[' << initial << ',' << bases << ',' << saved._ids["COUNTER_TRACE"] << ']';
}
std::cout << "],\"productionSharedSoldierCraftCounterRows\":["; first=true;
for(int order:{0,1,2}) {
if(!first) std::cout << ','; first=false;
Base base; SavedGame save; save.bases={&base}; ProductionCraftRule birth; birth.craft.type="STR_SOLDIER"; birth.craft.fuelMax=0;
ProductionItemRule person; person.person="RECRUIT"; ProductionDeliveryCaller personCaller{&person}; ProductionItemRule craftOutput; ProductionDeliveryCaller craftCaller{&craftOutput}; craftCaller.craftBirth=&birth;
if(order==0) { personCaller.deliver(&base,&save); craftCaller.deliver(&base,&save); }
else if(order==1) { craftCaller.deliver(&base,&save); personCaller.deliver(&base,&save); }
else { craftCaller._rules=&person; craftCaller.deliver(&base,&save); }
std::cout << '[' << order << ',' << base.crafts.back()->id << ',' << base.transfers.back()->_soldier->id << ',' << save.soldierCounter << ']';
delete base.crafts.back(); for(auto* transfer:base.transfers) { delete transfer->_soldier; delete transfer; }
}
std::cout << "],\"productionScriptSaleContextRows\":["; first=true;
for(int mode:{0,1,2,3,4}) for(int daily:{0,1}) for(int researched:{0,1}) {
if(!first) std::cout << ','; first=false;
Base base; SavedGame save; save.priceScript=mode; save.priceSecond=0; save.priceHour=daily?0:2; save.priceDays=daily?13:12; save.priceMonths=3; save.priceDifficulty=4; save.priceResearched=researched!=0; save.funds=0;
Item item; item.sellCost=7; ProductionItemRule rule; rule.produced={{&item,2}}; ProductionDeliveryCaller delivery{&rule}; delivery.sell=true;
const auto price=item.getSellCostAdjusted(&base,&save); delivery.deliver(&base,&save);
std::cout << '[' << mode << ',' << daily << ',' << researched << ',' << save.priceSecond << ',' << save.priceHour << ',' << save.priceDays << ',' << save.priceMonths << ',' << save.priceDifficulty << ',' << price << ',' << save.funds << ']';
}
std::cout << "],\"priceTimePointerRows\":["; first=true;
for(bool present:{false,true}) {
if(!first) std::cout << ','; first=false;
SavedGame save; const SavedGame* receiver=present?&save:nullptr;
const GameTime* a=&save.clockValue; const GameTime* b=&save.clockValue;
getTimeScript(receiver,a); getTimeScript(receiver,b);
std::cout << '[' << (present?"true":"false") << ',' << (a?"true":"false") << ',' << (a==b?"true":"false") << ']';
}
std::cout << "],\"researchPriceGetterRows\":["; first=true;
for(bool present:{false,true}) for(bool linked:{false,true}) for(bool needed:{false,true}) for(int mode:{0,1,2}) {
if(!first) std::cout << ','; first=false;
ResearchRule rule,lookup; Item item; rule.cost=mode==2?INT_MAX:mode; rule.points=mode==0?-7:mode==1?0:17;
lookup.points=23; rule.lookup=linked?&lookup:nullptr; rule.needs=needed; rule.needed=needed?&item:nullptr;
const RuleResearch* receiver=present?&rule:nullptr;
// The script member dispatcher supplies zero/null for a null receiver; the VM is excluded.
const auto* lookupValue=receiver?receiver->getLookup():nullptr;
const auto* itemValue=receiver?receiver->getNeededItem():nullptr;
std::cout << '[' << (present?"true":"false") << ',' << (linked?"true":"false") << ',' << (needed?"true":"false") << ',' << rule.cost << ',' << rule.points << ',' << (receiver?receiver->getCost():0) << ',' << (receiver?receiver->getPoints():0) << ',' << (lookupValue?lookupValue->getPoints():0) << ',' << (itemValue?1:0) << ']';
}
std::cout << "],\"correlatedLeaderOrderRows\":["; first=true;
for(bool firstHunter:{false,true}) for(bool same:{false,true}) {
if(!first) std::cout << ','; first=false;
Ufo source,early,late; source._isHunterKiller=true; source._isEscort=true; source.speed=550;
source._dest=new Waypoint(); source._dest->lon=1; source._dest->lat=0.6;
early._isHunterKiller=firstHunter; early.uniqueId=10; early.lon=0.7; early.lat=0.3;
late.uniqueId=11; late.lon=same?0.7:0.8; late.lat=0.3;
SavedGame game; game.ufos={&source,&early,&late}; retargetEscort(&source,game);
auto* target=dynamic_cast<Ufo*>(source._dest);
std::cout << '[' << (firstHunter?"true":"false") << ',' << (same?"true":"false") << ',' << target->uniqueId << ',' << target->lon << ',' << target->lat << ',' << source.speed << ']';
delete source._origWaypoint;
}
std::cout << "]}\n";
}

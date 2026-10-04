// Asset-free target selection collaborators; inserted pinned methods remain verbatim.
#include <algorithm>
#include <climits>
#include <cmath>
#include <iostream>
#include <vector>
#include <stdexcept>
#include <string>
namespace OpenXcom {
using Exception = std::runtime_error;
struct RNG { static inline int roll=0, calls=0;
static bool percent(int value) { ++calls; return roll < value; }
static int generate(int,int) { ++calls; return calls % 2 == 0 ? 1 : 0; } };
double Nautical(double x) { return x / 60.0 * 3.14159265358979323846 / 180.0; }
struct Target { double distance = 0, lon = 0, lat = 0; virtual ~Target() = default;
double getLongitude() const { return lon; } double getLatitude() const { return lat; }
void setLongitude(double value) { lon = value; } void setLatitude(double value) { lat = value; } };
struct Waypoint : Target { int id=0; int getId() const { return id; } void setId(int value) { id=value; } };
struct Item { std::string getType() const { return "SUPPLY"; } int getClipSize() const { return 1; } };
using RuleItem=Item;
struct Storage { int count=0; int getItem(const Item*) const { return count; }
void removeItem(const Item*,int qty=1) { count-=qty; } void addItem(const Item*,int qty) { count+=qty; } };
struct Base { Storage storage; Storage* getStorageItems() { return &storage; } };
struct CraftWeapon { bool rearming=false; bool isRearming() const { return rearming; }
struct Rule { Item* getClipItem() const { return nullptr; } } rule;
const Rule* getRules() const { return &rule; } int rearm(int,int) { return 0; }
void setRearming(bool value) { rearming=value; } };
struct MovingTarget : Target { Target* _dest = nullptr; void setDestination(Target* target) { _dest = target; } };
struct Craft : MovingTarget {
struct Rule { bool landing = false, hidden = false, autoPatrol=true; Item* fuelItem=nullptr; int refuelRate=40;
bool getAllowLanding() const { return landing; } bool isUndetectable() const { return hidden; }
const Item* getRefuelItem() const { return fuelItem; } int getRefuelRate() const { return refuelRate; }
int getRepairRate() const { return 10; }
bool canAutoPatrol() const { return autoPatrol; } } rule;
const Rule* _rules = &rule;
struct Stats { int speedMax = 2000, damageMax = 100, fuelMax=100; } _stats;
Base base; Base* _base=&base; int _fuel=0, _excessFuel=0, _takeoff=0, speed=0;
std::string _status="STR_REFUELLING"; bool autoPatrol=true;
std::vector<CraftWeapon*> _weapons;
void setFuel(int); std::string refuel(); void setDestination(Target*); void repair(); const RuleItem* rearm();
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
std::vector<Ufo*>* getUfos() { return &ufos; }
std::vector<Waypoint*>* getWaypoints() { return &waypoints; }
int getId(const std::string&) { return 8+(int)waypoints.size(); } };
struct ServiceGame { SavedGame save; SavedGame* getSavedGame() { return &save; } };
void autoPatrol(Craft* xcraft, ServiceGame* _game) {
// AUTO_PATROL
}
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

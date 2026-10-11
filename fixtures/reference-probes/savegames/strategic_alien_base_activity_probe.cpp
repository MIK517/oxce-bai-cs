#include <cmath>
#include <iostream>
#include <vector>
#include <map>
#include <string>
constexpr double M_PI = 3.14159265358979323846;
double Nautical(double units) { return units / 60.0 * M_PI / 180.0; }
// XCOM_DISTANCE
struct RNG {
 static inline std::vector<int> chances;
 static bool percent(int chance) { chances.push_back(chance); return chance > 0; }
};
namespace YAML {
struct WeightEntry {
 std::string key;size_t value;
 template<class T>T readKey()const{return key;}
 template<class T>T readVal()const{return value;}
};
struct YamlNodeReader {
 std::vector<WeightEntry> values;
 const auto& children()const{return values;}
};
}
struct WeightedOptions {
 std::map<std::string,size_t> _choices;size_t _totalWeight=0;
 void set(const std::string&,size_t);
 void load(const YAML::YamlNodeReader&);
};
// WEIGHT_SET
// WEIGHT_LOAD
struct Deployment { int points=7; int getPoints() const{return points;} };
struct AlienBase {
 bool discovered=false; Deployment deployment;
 bool isDiscovered() const{return discovered;} void setDiscovered(bool value){discovered=value;}
 Deployment* getDeployment(){return &deployment;}
 double getLongitude() const{return 0;} double getLatitude() const{return 0;}
};
struct FacilityRule {int range,chance;int getSightRange()const{return range;} int getSightChance()const{return chance;}};
struct Facility {FacilityRule rule;int build;int getBuildTime()const{return build;} FacilityRule* getRules(){return &rule;}};
struct Base {double distance;std::vector<Facility*> facilities;double getDistance(AlienBase*)const{return distance;}
 std::vector<Facility*>* getFacilities(){return &facilities;}};
struct AreaRule {bool inside;bool insideRegion(double,double)const{return inside;} bool insideCountry(double,double)const{return inside;}};
struct Area {AreaRule rule;int activity=0;AreaRule* getRules(){return &rule;} void addActivityAlien(int value){activity+=value;}};
struct Save {std::vector<AlienBase*> alienBases;std::vector<Base*> bases;std::vector<Area*> regions,countries;
 auto* getAlienBases(){return &alienBases;} auto* getBases(){return &bases;} auto* getRegions(){return &regions;} auto* getCountries(){return &countries;}};
struct Game {Save save;Save* getSavedGame(){return &save;}};
struct Stats {int sightRange;};
struct Craft {Stats stats;double distance;bool patrol=true;void* getDestination()const{return patrol?nullptr:(void*)this;}
 const Stats& getCraftStats()const{return stats;} double getDistance(AlienBase*)const{return distance;}};
struct AlienBaseState {AlienBaseState(AlienBase*,void*){}};
struct Driver {Game* _game;int alerts=0;void popup(AlienBaseState* state){++alerts;delete state;}
 void activity(){auto* saveGame=_game->getSavedGame();
 // BASE_ACTIVITY
 }
 void facilities(){
 // FACILITY_DETECTION
 }
 void craft(Craft* xcraft){
 // CRAFT_DISCOVERY
 }
};
void chances(){std::cout<<'[';bool first=true;for(int c:RNG::chances){if(!first)std::cout<<',';first=false;std::cout<<c;}std::cout<<']';}
int main(){
 std::cout<<"{\"facilityRows\":[";bool first=true;
 struct Case{int distance,range,chance,build,count;bool known;};
 for(auto c:std::vector<Case>{{0,100,0,0,1,false},{50,100,0,0,1,false},{99,100,0,0,1,false},{100,100,80,0,1,false},{10,100,80,1,1,false},{10,100,80,0,1,true},{10,100,80,0,2,false},{0,100,100,0,1,false}}){
  if(!first)std::cout<<',';first=false;RNG::chances.clear();Game game;AlienBase ab;ab.discovered=c.known;
  Facility f{{c.range,c.chance},c.build};Base base{Nautical(c.distance),{}};for(int n=0;n<c.count;++n)base.facilities.push_back(&f);
  game.save.alienBases={&ab};game.save.bases={&base};Driver driver{&game};driver.facilities();
  std::cout<<'['<<c.distance<<','<<c.range<<','<<c.chance<<','<<c.build<<','<<c.count<<','<<c.known<<','<<ab.discovered<<','<<driver.alerts<<',';chances();std::cout<<']';
 }
 std::cout<<"],\"craftRows\":[";first=true;
 for(auto c:std::vector<Case>{{0,100,0,0,1,false},{50,100,0,0,1,false},{100,100,0,0,1,false},{101,100,0,0,1,false},{50,100,0,0,1,true},{0,0,0,0,1,false}}){
  if(!first)std::cout<<',';first=false;RNG::chances.clear();Game game;AlienBase ab;ab.discovered=c.known;game.save.alienBases={&ab};
  Craft craft{{c.range},Nautical(c.distance)};Driver driver{&game};driver.craft(&craft);
  std::cout<<'['<<c.distance<<','<<c.range<<','<<c.known<<','<<ab.discovered<<',';chances();std::cout<<']';
 }
 std::cout<<"],\"activityRows\":[";first=true;
 for(int points:{7,-3}){if(!first)std::cout<<',';first=false;Game game;AlienBase ab;ab.deployment.points=points;game.save.alienBases={&ab};
  Area r0{{false}},r1{{true}},r2{{true}},country{{true}};game.save.regions={&r0,&r1,&r2};game.save.countries={&country};Driver driver{&game};driver.activity();
  std::cout<<'['<<points<<','<<r0.activity<<','<<r1.activity<<','<<r2.activity<<','<<country.activity<<']';}
 std::cout<<"],\"weightRows\":[";first=true;
 using Patch=std::vector<YAML::WeightEntry>;
 for(auto patches:std::vector<std::vector<Patch>>{
  {{{"MISSION_SUPPLY",1}},{{"",1}}},
  {{{"MISSION_SUPPLY",3},{"MISSING_MISSION",4}},{{"MISSION_SUPPLY",7},{"MISSING_MISSION",0},{"MISSION_SCOUT",5}}},
  {{},{{"MISSION_SUPPLY",0}}},
  {{{"MISSION_SUPPLY",3}},{}},
  {{{"MISSION_SUPPLY",3}},{{"",1}},{{"MISSION_SUPPLY",0},{"",0}}}
 }){
  if(!first)std::cout<<',';first=false;WeightedOptions options;std::cout<<"[[";bool firstPatch=true;
  for(auto patch:patches){
   if(!firstPatch)std::cout<<',';firstPatch=false;std::cout<<'[';bool firstEntry=true;
   for(auto entry:patch){if(!firstEntry)std::cout<<',';firstEntry=false;std::cout<<"[\""<<entry.key<<"\","<<entry.value<<']';}
   std::cout<<']';options.load(YAML::YamlNodeReader{patch});
  }
  std::cout<<"],[";bool firstEntry=true;
  for(auto entry:options._choices){if(!firstEntry)std::cout<<',';firstEntry=false;std::cout<<"[\""<<entry.first<<"\","<<entry.second<<']';}
  std::cout<<"],"<<options._totalWeight<<']';
 }
 std::cout<<"]}\n";
}

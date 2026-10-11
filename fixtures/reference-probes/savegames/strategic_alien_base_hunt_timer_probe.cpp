#include <iostream>
#include <map>
#include <string>
#include <vector>
#include <stdexcept>
#include <cmath>
struct Exception:std::runtime_error{using std::runtime_error::runtime_error;};
struct RNG{static inline std::vector<std::pair<int,int>> calls;static int generate(int a,int b){calls.emplace_back(a,b);return a;}
 static bool percent(int p){return generate(0,99)<p;}};
struct WeightedOptions{std::map<std::string,size_t> _choices;size_t _totalWeight=0;
 std::string choose()const;void set(const std::string&,size_t);};
// CHOOSE
// SET
struct AlienDeployment{int range=100,chance=100,frequency=60;std::vector<std::pair<size_t,WeightedOptions*>> _huntMissionDistribution;
 int getBaseDetectionRange()const{return range;}int getBaseDetectionChance()const{return chance;}
 int getHuntMissionMaxFrequency()const{return frequency;}bool isHuntMissionRaceFromAlienBase()const{return true;}
 std::string generateHuntMission(size_t)const;};
// HUNT_MISSION
struct AlienBase{AlienDeployment* dep;int timer=50;AlienDeployment* getDeployment(){return dep;}
 int getMinutesSinceLastHuntMissionGeneration()const{return timer;}void setMinutesSinceLastHuntMissionGeneration(int v){timer=v;}
 std::string getAlienRace()const{return "RACE_A";}};
struct RuleCraft{bool undetectable=false;bool isUndetectable()const{return undetectable;}};
struct Craft{std::string status="STR_OUT";RuleCraft rule;int _damage=0;struct{int damageMax=100;} _stats;
 bool low=false,complete=false;double distance=0;const std::string& getStatus()const{return status;}
 RuleCraft* getRules(){return &rule;}bool getLowFuel()const{return low;}bool getMissionComplete()const{return complete;}
 bool isDestroyed()const;bool isIgnoredByHK()const;double getDistance(AlienBase*)const{return distance;}};
// DESTROYED
// IGNORED
struct Base{std::vector<Craft*> crafts;auto* getCrafts(){return &crafts;}};
struct MissionArea{};struct Zone{std::vector<MissionArea> areas;};struct RuleRegion{std::string type="REGION";std::vector<Zone> zones;
 const std::string& getType()const{return type;}const auto& getMissionZones()const{return zones;}};
struct Region{RuleRegion rule;RuleRegion* getRules(){return &rule;}};
constexpr int OBJECTIVE_SITE=3;
struct RuleAlienMission{bool hasRaceWeights()const{return false;}std::string generateRace(int)const{return "";}
 int getObjective()const{return 0;}int getSpawnZone()const{return 0;}};
struct Mod{RuleAlienMission* getAlienMission(const std::string&)const{return nullptr;}
 RuleRegion* getRegion(const std::string&){static RuleRegion r;return &r;}};
struct Game;struct Globe{};
struct AlienMission{RuleAlienMission rule;AlienMission(const RuleAlienMission&){}
 void setRegion(const std::string&,Mod&){}void setId(int){}void setRace(std::string){}void setAlienBase(AlienBase*){}
 const RuleAlienMission& getRules()const{return rule;}std::string getRegion()const{return "REGION";}
 void setMissionSiteZoneArea(int){}void start(Game&,Globe&){}void think(Game&,Globe&){} };
struct SavedGame{std::vector<Base*> bases;std::vector<AlienBase*> alienBases;std::vector<AlienMission*> missions;
 auto* getBases(){return &bases;}auto* getAlienBases(){return &alienBases;}auto& getAlienMissions(){return missions;}
 int getMonthsPassed(){return 0;}int getId(const std::string&){return 1;}Region* locateRegion(AlienBase&){static Region r;return &r;}};
struct Game{SavedGame save;Mod mod;auto* getSavedGame(){return &save;}auto* getMod(){return &mod;}};
double Nautical(double d){return d*3.14159265358979323846/180.0/60.0;}
struct GeoscapeState{Game* _game;Globe* _globe;std::vector<Craft*> _activeCrafts;
 const std::vector<Craft*>* updateActiveCrafts();void baseHunting();};
// ACTIVE
// HUNTING
int main(){std::cout<<"{\"rows\":[";bool first=true;
 for(std::string name:{"disabled","waiting","grounded","undetectable","lowFuel","complete","outside","destroyed","emptyChoice","zeroChance","zeroTotal","multiple"}){
 if(!first)std::cout<<',';first=false;RNG::calls.clear();
 WeightedOptions w;w.set("",1);AlienDeployment dep;dep._huntMissionDistribution={{0,&w}};
 AlienBase ab{&dep};Craft c,c2;Base b{{&c}};Game game;game.save.bases={&b};game.save.alienBases={&ab};Globe globe;
 if(name=="disabled")dep.range=0;if(name=="waiting")ab.timer=40;if(name=="grounded")c.status="STR_READY";
 if(name=="undetectable")c.rule.undetectable=true;if(name=="lowFuel")c.low=true;if(name=="complete")c.complete=true;
 if(name=="outside")c.distance=Nautical(dep.range*2);if(name=="destroyed")c._damage=100;
 if(name=="zeroChance")dep.chance=0;if(name=="zeroTotal")w.set("",0);if(name=="multiple")b.crafts.push_back(&c2);
 GeoscapeState state{&game,&globe};state.baseHunting();
 std::cout<<"[\""<<name<<"\","<<ab.timer<<",[";bool fc=true;for(auto p:RNG::calls){if(!fc)std::cout<<',';fc=false;std::cout<<'['<<p.first<<','<<p.second<<']';}std::cout<<"]]";
 }std::cout<<"]}\n";}

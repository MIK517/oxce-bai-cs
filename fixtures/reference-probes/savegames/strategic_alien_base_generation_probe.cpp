#include <iostream>
#include <map>
#include <string>
#include <vector>
#include <stdexcept>
struct Exception : std::runtime_error { using std::runtime_error::runtime_error; };
struct RNG {
 static inline std::vector<std::pair<int,int>> calls;
 static int generate(int low,int high){calls.emplace_back(low,high);return low;}
 static bool percent(int chance){return generate(0,99)<chance;}
};
struct WeightedOptions {
 std::map<std::string,size_t> choices;
 std::string choose()const{size_t total=0;for(auto& p:choices)total+=p.second;if(!total)return "";
 auto roll=RNG::generate(1,(int)total);for(auto& p:choices){if(roll<=(int)p.second)return p.first;roll-=(int)p.second;}throw Exception("roll");}
};
constexpr int AMOT_SPACE=0, OBJECTIVE_SITE=3, OBJECTIVE_INFILTRATION=1;
struct RuleResearch{};
struct Wave {size_t spawnTimer=9000;};
struct RuleAlienMission {
 std::string id,interrupt="UNLOCK";int target=0,objective=5,operation=0;Wave wave;
 std::vector<std::pair<size_t,WeightedOptions*>> _raceDistribution,_regionWeights;
 int getTargetBaseOdds()const{return target;} bool hasRegionWeights()const{return !_regionWeights.empty();}
 bool hasRaceWeights()const{return !_raceDistribution.empty();} int getObjective()const{return objective;}
 int getSpawnZone()const{return 0;} int getOperationType()const{return operation;}
 const Wave& getWave(int)const{return wave;}
 const std::string& getInterruptResearch()const{return interrupt;}bool isEndlessInfiltration()const{return false;}
 size_t getWaveCount()const{return 1;}
 std::string generateRace(size_t)const;std::string generateRegion(size_t)const;
};
// GENERATE_RACE
// GENERATE_REGION
struct MissionArea{};struct Zone{std::vector<MissionArea> areas;};
struct RuleRegion {std::string id,fallback;std::vector<Zone> zones={Zone{{MissionArea{},MissionArea{}}}};
 const std::string& getType()const{return id;} const std::string& getMissionRegion()const{return fallback;}
 const auto& getMissionZones()const{return zones;}};
struct Region {RuleRegion* rule;RuleRegion* getRules(){return rule;}};
struct Base {Region* region;};
struct Deployment {WeightedOptions choices;int frequency=100,limit=3;bool fromBase=true;
 std::string chooseGenMissionType()const{return choices.choose();}int getGenMissionLimit()const{return limit;}
 int getGenMissionFrequency()const{return frequency;}bool isGenMissionRaceFromAlienBase()const{return fromBase;}};
struct AlienBase {Deployment* deployment;Region* region;int count=0;std::string race="RACE_A";
 Deployment* getDeployment(){return deployment;}int getGenMissionCount()const{return count;}
 void setGenMissionCount(int v){count=v;}std::string getAlienRace()const{return race;}};
struct Mod {RuleAlienMission* rule;std::map<std::string,RuleRegion*> regions;
 RuleResearch research;const RuleResearch* getResearch(const std::string& id,bool)const{if(id!="UNLOCK")throw Exception("research");return &research;}
 RuleAlienMission* getAlienMission(const std::string& name)const{return name==rule->id?rule:nullptr;}
 RuleRegion* getRegion(const std::string& name,bool=false)const{return regions.at(name);}};
struct Game;struct Globe{};
struct AlienMission {
 const RuleAlienMission& _rule;std::string _region,race;int id=0,area=-1;
 size_t _nextWave=0,_nextUfoCounter=0,_liveUfos=0,_spawnCountdown=0;AlienBase* _base=nullptr;bool _interrupted=false;
 AlienMission(const RuleAlienMission& rule):_rule(rule){}
 void setRegion(const std::string&,const Mod&);void setId(int v){id=v;}void setRace(std::string v){race=v;}
 void setAlienBase(AlienBase* v){_base=v;}const RuleAlienMission& getRules()const{return _rule;}
 const std::string& getRegion()const{return _region;}void setMissionSiteZoneArea(int v){area=v;}
 void setInterrupted(bool v){_interrupted=v;}bool isOver()const;
 void start(Game&,const Globe&,size_t initialCount=0){
 // START_TIMER
 // Linked bases bypass the remaining base acquisition block in AlienMission::start.
 if(_rule.getOperationType()!=AMOT_SPACE && !_base)throw Exception("unlinked operation");
 }
};
// SET_REGION
// IS_OVER
struct SavedGame {std::vector<Base*> bases;std::vector<AlienMission*> missions;int month=0,nextId=1;
 bool researched=false;bool isResearched(const RuleResearch*,bool)const{return researched;}
 auto* getBases(){return &bases;}Region* locateRegion(Base& b){return b.region;}Region* locateRegion(AlienBase& b){return b.region;}
 int getMonthsPassed(){return month;}int getId(const std::string&){return nextId++;}auto& getAlienMissions(){return missions;}};
struct Game {Mod mod;SavedGame save;Mod* getMod(){return &mod;}SavedGame* getSavedGame(){return &save;}};
struct GenerateSupplyMission {Game& _engine;const Globe& _globe;void operator()(AlienBase*)const;};
// GENERATE_SUPPLY
void interruptDaily(Game& game){auto* saveGame=game.getSavedGame();auto* mod=game.getMod();
 // INTERRUPT_DAILY
}
int main(){
 std::cout<<"{\"rows\":[";bool first=true;
 for(std::string name:{"local","weighted","target","baseRace","noRaceWeights","site","futureRace","exhausted","zeroFrequency","empty","operation1","operation2","operation3","operation4","operation5","operation6","objective0","objective1","objective2","objective3","objective4","objective5","objective6","immediate","immediateZero"}){
  if(name=="immediate"){std::cout<<"],\"immediateRows\":[";first=true;}
  if(!first)std::cout<<',';first=false;RNG::calls.clear();
  RuleAlienMission rule;rule.id="GEN_"+name;Deployment dep;dep.choices.choices={{rule.id,1}};
  WeightedOptions regions{{{"REGION_SECOND",1}}},races{{{"RACE_B",1}}};
  RuleRegion r0{"REGION",""},r1{"REGION_SECOND","REGION"};Region region{&r0},second{&r1};
  Game game;game.mod.rule=&rule;game.mod.regions={{r0.id,&r0},{r1.id,&r1}};
  Base b0{&region},b1{&region},b2{&second};game.save.bases={&b0,&b1,&b2};
  AlienBase base{&dep,&region};Globe globe;
  if(name=="immediate")rule.wave.spawnTimer=90;
  if(name=="immediateZero")rule.wave.spawnTimer=0;
  if(name=="weighted"){rule.objective=0;rule._regionWeights={{0,&regions}};rule._raceDistribution={{0,&races}};dep.fromBase=false;}
  if(name=="target"){rule.target=100;rule._regionWeights={{0,&regions}};}
  if(name=="baseRace"){rule._raceDistribution={{0,&races}};}
  if(name=="noRaceWeights")dep.fromBase=false;
  if(name=="site")rule.objective=OBJECTIVE_SITE;
  if(name=="futureRace"){rule._raceDistribution={{2,&races}};dep.fromBase=false;}
  if(name=="exhausted")base.count=3;
  if(name=="zeroFrequency")dep.frequency=0;
  if(name=="empty")dep.choices.choices={{"",1}};
  if(name.starts_with("operation"))rule.operation=name.back()-'0';
  if(name.starts_with("objective"))rule.objective=name.back()-'0';
  GenerateSupplyMission{game,globe}(&base);
  std::cout<<"[\""<<name<<"\","<<base.count<<','<<game.save.missions.size()<<',';
  if(game.save.missions.empty())std::cout<<"null";
  else{auto* m=game.save.missions[0];std::cout<<"["<<m->id<<",\""<<m->_region<<"\",\""<<m->race<<"\","<<m->area<<','<<m->_spawnCountdown<<','<<(m->_base==&base)<<']';}
  std::cout<<",[";bool firstCall=true;for(auto& c:RNG::calls){if(!firstCall)std::cout<<',';firstCall=false;std::cout<<'['<<c.first<<','<<c.second<<']';}std::cout<<"]]";
  for(auto* m:game.save.missions)delete m;
 }
 std::cout<<"],\"interruptionRows\":[";first=true;
 for(bool researched:{false,true})for(bool interrupted:{false,true}){
  if(!first)std::cout<<',';first=false;RuleAlienMission rule;rule.objective=0;
  AlienMission mission{rule};mission._interrupted=interrupted;Game game;game.save.researched=researched;game.save.missions={&mission};
  interruptDaily(game);
  std::cout<<'['<<researched<<','<<interrupted<<','<<mission._interrupted<<','<<mission.isOver()<<']';
 }
 std::cout<<"],\"liveInterruptionRows\":[";first=true;
 for(bool researched:{false,true})for(bool interrupted:{false,true}){
  if(!first)std::cout<<',';first=false;RuleAlienMission rule;rule.objective=0;
  AlienMission mission{rule};mission._interrupted=interrupted;mission._liveUfos=1;Game game;game.save.researched=researched;game.save.missions={&mission};
  interruptDaily(game);
  std::cout<<'['<<researched<<','<<interrupted<<','<<mission._interrupted<<','<<mission.isOver()<<']';
 }
 std::cout<<"]}\n";
}

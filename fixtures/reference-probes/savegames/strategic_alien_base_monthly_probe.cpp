#include <iostream>
#include <map>
#include <sstream>
#include <stdexcept>
#include <string>
#include <tuple>
#include <vector>
using Exception = std::runtime_error;
struct RNG {
 static inline std::vector<std::pair<int,int>> calls;
 static int generate(int low,size_t high){calls.emplace_back(low,(int)high);return low;}
};
struct WeightedOptions {
 size_t _totalWeight=0;std::map<std::string,size_t> _choices;
 WeightedOptions(std::map<std::string,size_t> choices):_choices(choices){for(auto& p:choices)_totalWeight+=p.second;}
 std::string choose()const;
};
// WEIGHTED_CHOOSE
struct AlienDeployment {
 std::string id,marker="STR_ALIEN_BASE",upgradeRace;bool resetSource=false,resetTarget=false;int hunt=90;
 std::vector<std::pair<size_t,WeightedOptions*>> _alienBaseUpgrades;
 std::vector<std::tuple<size_t,std::string,std::string>> evolution;
 std::string generateAlienBaseUpgrade(size_t)const;
 bool resetAlienBaseAgeAfterUpgrade()const{return resetSource;}bool resetAlienBaseAge()const{return resetTarget;}
 const std::string& getType()const{return id;}const std::string& getUpgradeRace()const{return upgradeRace;}
 const auto& getAlienRaceEvolution()const{return evolution;}int getHuntMissionMaxFrequency()const{return hunt;}
};
// GENERATE_UPGRADE
struct AlienBase {
 AlienDeployment* _deployment;int start=1,id=2,_minutesSinceLastHuntMissionGeneration=17;std::string race="RACE_A";
 int getStartMonth()const{return start;}void setStartMonth(int v){start=v;}int getId()const{return id;}
 const std::string& getType()const{return _deployment->marker;}AlienDeployment* getDeployment()const{return _deployment;}
 const std::string& getAlienRace()const{return race;}void setAlienRace(const std::string& v){race=v;}
 void setDeployment(AlienDeployment*);
};
// SET_DEPLOYMENT
struct RuleAlienRace {std::string id;const std::string& getId()const{return id;}};
struct Mod {
 std::map<std::string,AlienDeployment*> deployments;std::map<std::string,RuleAlienRace*> races;
 AlienDeployment* getDeployment(const std::string& id,bool)const{auto p=deployments.find(id);return p==deployments.end()?nullptr:p->second;}
 RuleAlienRace* getAlienRace(const std::string& id,bool)const{auto p=races.find(id);return p==races.end()?nullptr:p->second;}
};
struct SavedGame {std::vector<AlienBase*> bases;std::vector<std::string> logs;auto* getAlienBases(){return &bases;}auto& getGeoscapeDebugLog(){return logs;}};
struct Game {Mod mod;SavedGame save;Mod* getMod(){return &mod;}SavedGame* getSavedGame(){return &save;}};
struct Options {static inline int oxceGeoscapeDebugLogMaxEntries=0;};
struct GeoscapeState {Game* _game;bool attemptAlienRaceEvolution(int,AlienBase*)const;
 void monthly(bool isNewMonth,int month){auto* mod=_game->getMod();auto* save=_game->getSavedGame();
 // MONTHLY_BLOCK
 }
};
// EVOLUTION
int main(){std::cout<<"{\"rows\":[";bool first=true;
 for(std::string name:{"monthZero","notNewMonth","noWeights","upgrade","sourceReset","targetReset","unknownUpgrade","sameUpgrade","emptyUpgrade","zeroWeights","unknownRace","upgradeEvolution","evolutionChain","priority","stableTie","missingEvolution","futureEvolution","selfLoop","cycle","timelineEarly","timelineBoundary","futureStart"}){
  if(!first)std::cout<<',';first=false;RNG::calls.clear();
  AlienDeployment oldRule,newRule;oldRule.id="MONTHLY_"+name;newRule.id="MONTHLY_NEW";newRule.hunt=120;
  if(name=="targetReset")newRule.id="MONTHLY_RESET";
  if(name=="unknownRace")newRule.id="MONTHLY_UNKNOWN_RACE";
  if(name=="upgradeEvolution")newRule.id="MONTHLY_EVOLVED";
  WeightedOptions weights{{{newRule.id,1}}},empty{{{"",1}}},zero{{{newRule.id,0}}},missing{{{"MISSING",1}}},same{{{oldRule.id,1}}};
  oldRule._alienBaseUpgrades={{0,&weights}};
  AlienBase base{&oldRule};Game game;game.save.bases={&base};game.mod.deployments={{oldRule.id,&oldRule},{newRule.id,&newRule}};
  RuleAlienRace a{"RACE_A"},b{"RACE_B"},c{"RACE_C"};game.mod.races={{a.id,&a},{b.id,&b},{c.id,&c}};
  int month=3;bool isNewMonth=true;
  if(name=="monthZero")month=0;
  if(name=="notNewMonth")isNewMonth=false;
  if(name=="sourceReset")oldRule.resetSource=true;
  if(name=="targetReset")newRule.resetTarget=true;
  if(name=="unknownUpgrade")oldRule._alienBaseUpgrades={{0,&missing}};
  if(name=="sameUpgrade")oldRule._alienBaseUpgrades={{0,&same}};
  if(name=="emptyUpgrade")oldRule._alienBaseUpgrades={{0,&empty}};
  if(name=="zeroWeights")oldRule._alienBaseUpgrades={{0,&zero}};
  if(name=="unknownRace")newRule.upgradeRace="MISSING";
  if(name=="upgradeEvolution"){newRule.upgradeRace="RACE_B";newRule.evolution={{3,"RACE_B","RACE_C"}};}
  if(name=="noWeights" || name=="evolutionChain" || name=="priority" || name=="stableTie" || name=="missingEvolution" || name=="futureEvolution" || name=="selfLoop" || name=="cycle")oldRule._alienBaseUpgrades.clear();
  if(name=="evolutionChain")oldRule.evolution={{2,"RACE_B","RACE_C"},{1,"RACE_A","RACE_B"}};
  if(name=="priority")oldRule.evolution={{3,"RACE_A","RACE_C"},{1,"RACE_A","RACE_B"}};
  if(name=="stableTie")oldRule.evolution={{2,"RACE_A","RACE_B"},{2,"RACE_A","RACE_C"}};
  if(name=="missingEvolution")oldRule.evolution={{2,"RACE_A","MISSING"},{1,"RACE_A","RACE_B"}};
  if(name=="futureEvolution")oldRule.evolution={{4,"RACE_A","RACE_B"}};
  if(name=="selfLoop")oldRule.evolution={{1,"RACE_A","RACE_A"}};
  if(name=="cycle")oldRule.evolution={{1,"RACE_A","RACE_B"},{1,"RACE_B","RACE_A"}};
  if(name=="timelineEarly" || name=="timelineBoundary" || name=="futureStart"){
   oldRule._alienBaseUpgrades={{0,&same},{2,&weights}};
   if(name=="timelineEarly")base.start=2;
   if(name=="futureStart")base.start=4;
  }
  bool error=false;try{GeoscapeState{&game}.monthly(isNewMonth,month);}catch(const Exception&){error=true;}
  std::cout<<"[\""<<name<<"\","<<month<<','<<isNewMonth<<",\""<<base.getDeployment()->id<<"\",\""<<base.race<<"\","<<base.start<<','<<base._minutesSinceLastHuntMissionGeneration<<','<<error<<",[";
  bool firstCall=true;for(auto& p:RNG::calls){if(!firstCall)std::cout<<',';firstCall=false;std::cout<<'['<<p.first<<','<<p.second<<']';}std::cout<<"]]";
 }
 std::cout<<"],\"evolutionLimitRows\":[";first=true;
 for(int count:{99,100}){
  if(!first)std::cout<<',';first=false;AlienDeployment deployment;AlienBase base{&deployment};Game game;
  std::vector<RuleAlienRace> races;races.reserve(count);
  for(int i=1;i<=count;i++){
   std::string id="CHAIN_"+std::to_string(i);races.push_back(RuleAlienRace{id});game.mod.races[id]=&races.back();
   deployment.evolution.emplace_back(1,i==1?"RACE_A":"CHAIN_"+std::to_string(i-1),id);
  }
  game.save.bases={&base};bool error=false;
  try{GeoscapeState{&game}.monthly(true,3);}catch(const Exception&){error=true;}
  std::cout<<'['<<count<<','<<error<<",\""<<base.race<<"\"]";
 }
 std::cout<<"]}\n";
}

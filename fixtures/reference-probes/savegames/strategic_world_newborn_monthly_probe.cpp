#include <iostream>
#include <map>
#include <sstream>
#include <stdexcept>
#include <string>
#include <tuple>
#include <type_traits>
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
 const std::string& getMarkerName()const{return marker;}
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
struct Node {
 std::string type;int id;int slot=0;
 explicit operator bool()const{return true;}
 Node operator[](const std::string& key)const{Node result=*this;result.slot=key=="type"?1:key=="id"?2:0;return result;}
 template<class T>T readVal()const{if constexpr(std::is_same_v<T,std::string>)return type;else return id;}
};
namespace YAML{using YamlNodeReader=Node;}
struct SavedGame;
struct RuleCraft {std::string type="SHIP";const std::string& getType()const{return type;}};
struct Site {AlienDeployment* deployment;int getId()const{return 2;}AlienDeployment* getDeployment()const{return deployment;}};
struct OtherTarget{int getId()const{return 2;}};
struct Craft {
 RuleCraft* rule;int id=1,destinationKind=0;
 int getId()const{return id;}RuleCraft* getRules()const{return rule;}
 void setDestination(AlienBase*){destinationKind=1;}void setDestination(Craft*){destinationKind=2;}
 void setDestination(Site*){destinationKind=3;}void setDestination(OtherTarget*){destinationKind=4;}
 void returnToBase(){destinationKind=5;}
 void loadDestination(const YAML::YamlNodeReader&,SavedGame*);void finishLoading(const YAML::YamlNodeReader&,SavedGame*);
};
struct PlayerBase{std::vector<Craft*> crafts;auto* getCrafts(){return &crafts;}};
struct SavedGame {std::vector<AlienBase*> bases;std::vector<std::string> logs;
 std::vector<PlayerBase*> playerBases;std::vector<Site*> sites;std::vector<OtherTarget*> other;
 auto* getAlienBases(){return &bases;}auto& getGeoscapeDebugLog(){return logs;}
 auto* getBases(){return &playerBases;}auto* getMissionSites(){return &sites;}auto* getUfos(){return &other;}auto* getWaypoints(){return &other;}};
void Craft::loadDestination(const YAML::YamlNodeReader& reader,SavedGame* save){
 // CRAFT_DESTINATION
}
// CRAFT_FINISH_LOADING
struct Game {Mod mod;SavedGame save;Mod* getMod(){return &mod;}SavedGame* getSavedGame(){return &save;}};
struct Options {static inline int oxceGeoscapeDebugLogMaxEntries=0;};
struct GeoscapeState {Game* _game;bool attemptAlienRaceEvolution(int,AlienBase*)const;
 void monthly(bool isNewMonth,int month){auto* mod=_game->getMod();auto* save=_game->getSavedGame();
 // MONTHLY_BLOCK
 }
};
// EVOLUTION
int main(){std::cout<<"{\"rows\":[";bool first=true;
for(std::string name:{"upgrade","evolution","loop","marker"}){
 if(!first)std::cout<<',';first=false;RNG::calls.clear();
 AlienDeployment source,target;source.id="NEWBORN_"+name;target.id="NEWBORN_TARGET";target.hunt=120;
 if(name=="marker")target.id="NEWBORN_MARKER_TARGET";
 WeightedOptions weights{{{target.id,1}}};
 source._alienBaseUpgrades={{0,&weights}};
 AlienBase base{&source};base.start=3;
 if(name=="evolution"){target.upgradeRace="RACE_B";target.evolution={{3,"RACE_B","RACE_C"}};}
 if(name=="loop"){target.evolution={{3,"RACE_A","RACE_A"}};}
 if(name=="marker"){source.marker="NEWBORN_SOURCE";target.marker="NEWBORN_DESTINATION";}
 Game game;game.save.bases={&base};game.mod.deployments={{source.id,&source},{target.id,&target}};
 RuleAlienRace a{"RACE_A"},b{"RACE_B"},c{"RACE_C"};game.mod.races={{a.id,&a},{b.id,&b},{c.id,&c}};
 bool error=false;try{GeoscapeState{&game}.monthly(true,3);}catch(const Exception&){error=true;}
 std::cout<<"[\""<<name<<"\",\""<<base.getDeployment()->id<<"\",\""<<base.race<<"\","<<base.start<<','<<base._minutesSinceLastHuntMissionGeneration<<','<<error<<",\""<<base.getType()<<"\"]";
}
std::cout<<"]}\n";
}

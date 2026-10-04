// Exact determineAlienMissions arc/mission/event caller; controlled state, rules and effects.
// Base upgrades/evolution are covered by their separate pinned subsystem fixtures.
#include <algorithm>
#include <iostream>
#include <map>
#include <string>
#include <vector>
#include <set>
#include <unordered_map>
#include <unordered_set>
#include <sstream>
#include <stdexcept>
#include <climits>
#include <cstdint>
std::vector<std::string> trace;
namespace RNG {
bool percent(int odds) { trace.push_back("roll:"+std::to_string(odds)); return odds>=100; }
int generate(int lower,int) { return lower; }
}
namespace Options { bool verboseLogging=false; int oxceGeoscapeDebugLogMaxEntries=0; }
using Exception=std::runtime_error;
struct WeightedOptions {
std::map<std::string,size_t> _choices;size_t _totalWeight=0;
std::string choose() const;void set(const std::string&,size_t);std::vector<std::string> getNames();
bool empty() const { return _choices.empty(); }
};
// WEIGHT_METHODS
struct RuleType { std::string type="REGION"; const std::string& getType() const { return type; } };
struct Base { std::string name="HQ"; const std::string& getName()const{return name;} int getPersonnelMaintenance(){return 7;} };
struct Region { RuleType rule;RuleType* getRules(){return &rule;} };
struct Country { RuleType rule;RuleType* getRules(){return &rule;} bool getPact(){return false;} };
struct Criteria {
std::string type,variable,marker;int first=0,last=-1,odds=100,minimum=0,maximum=-1;
int minimumScore=INT_MIN,maximumScore=INT_MAX,minimumDifficulty=0,maximumDifficulty=4;
int64_t minimumFunds=INT64_MIN,maximumFunds=INT64_MAX;
std::map<std::string,bool> research,items,facilities,soldiers,regions,countries,pacts;
const std::string& getType(){return type;}
const std::string& getMissionVarName(){return variable;}
const std::string& getMissionMarkerName(){return marker;}
int getFirstMonth(){return first;}int getLastMonth(){return last;}int getExecutionOdds(){return odds;}
int getMinScore(){return minimumScore;}int getMaxScore(){return maximumScore;}
int64_t getMinFunds(){return minimumFunds;}int64_t getMaxFunds(){return maximumFunds;}
int getMinDifficulty(){return minimumDifficulty;}int getMaxDifficulty(){return maximumDifficulty;}
int getCounterMin(){return minimum;}int getCounterMax(){return maximum;}
const auto& getResearchTriggers(){return research;}const auto& getItemTriggers(){return items;}
const auto& getFacilityTriggers(){return facilities;}const auto& getSoldierTypeTriggers(){return soldiers;}
const auto& getXcomBaseInRegionTriggers(){return regions;}const auto& getXcomBaseInCountryTriggers(){return countries;}
const auto& getPactCountryTriggers(){return pacts;}
};
struct RuleArcScript:Criteria {
int maxArcs=-1;std::vector<std::string> sequential;WeightedOptions random;
int getMaxArcs(){return maxArcs;}const auto& getSequentialArcs(){return sequential;}
const auto& getRandomArcs(){return random;}
};
struct RuleMissionScript:Criteria {
std::string runVariable="RUN";int maxRuns=-1,label=0;std::vector<int> conditions;std::vector<std::string> tags;
int getMaxRuns(){return maxRuns;}const std::string& getVarName(){return runVariable;}
int getLabel(){return label;}const auto& getConditionals(){return conditions;}const auto& getAdhocMissionScriptTags(){return tags;}
};
struct RuleEvent {std::string id;std::vector<std::string> tags;const auto& getAdhocMissionScriptTags()const{return tags;}};
struct RuleResearch {std::string id;RuleResearch* lookup=nullptr;std::vector<std::string> disables;
const auto& getName()const{return id;}RuleResearch* getLookup()const{return lookup;}};
struct RuleEventScript:Criteria {
std::vector<std::string> sequential;WeightedOptions random;
std::vector<std::pair<size_t,WeightedOptions*>> _eventWeights;
const auto& getOneTimeSequentialEvents(){return sequential;}const auto& getOneTimeRandomEvents(){return random;}
std::string generate(size_t) const;
};
struct AlienStrategy {std::map<std::string,int> runs;int getMissionsRun(const std::string& id){return runs[id];}};
struct Mod;
struct SavedGame {
int month=0,days=0,score=0;int64_t funds=1000;int maintenance=16;
Base hq;std::vector<Base*> bases{&hq};std::vector<Country*> countries;
AlienStrategy strategy;std::set<std::string> research;std::map<std::string,int> nextIds;
std::map<std::string,int> _generatedEvents;std::vector<std::string> debug;
AlienStrategy& getAlienStrategy(){return strategy;}int getMonthsPassed(){return month;}int getCurrentScore(int){return score;}
int64_t getFunds(){return funds;}void setFunds(int64_t amount){funds=amount;}int getCountryFunding(){return 0;}
int getBaseMaintenance(){return maintenance;}void addMonth(){month++;}void increaseDaysPassed(){days++;}
auto* getBases(){return &bases;}auto* getCountries(){return &countries;}auto& getGeoscapeDebugLog(){return debug;}
Region* locateRegion(const Base&){return nullptr;}Country* locateCountry(const Base&){return nullptr;}
bool isResearched(const std::string& id){return research.count(id)!=0;}
bool isItemObtained(const std::string&,Mod*){return false;}bool isFacilityBuilt(const std::string&){return false;}
bool isSoldierTypeHired(const std::string&){return false;}int getDifficulty(){return 0;}
int getLastId(const std::string& id){auto it=nextIds.find(id);return it==nextIds.end()?0:std::max(1,it->second-1);}
void addFinishedResearch(RuleResearch* rule,Mod*,Base*,bool) {
if(research.insert(rule->id).second){score+=100;for(const auto& id:rule->disables)research.erase(id);trace.push_back("research:"+rule->id);}
}
bool wasEventGenerated(const std::string&);
void spawnEvent(const RuleEvent* rule){_generatedEvents[rule->id]++;trace.push_back("event:"+rule->id);}
};
struct Mod {
std::vector<std::string> arcs,missions,adhoc,events;
std::map<std::string,RuleArcScript*> arcRules;std::map<std::string,RuleMissionScript*> missionRules;
std::map<std::string,RuleEventScript*> eventScripts;std::map<std::string,RuleResearch*> research;std::map<std::string,RuleEvent*> eventRules;
int getPerformanceBonus(int){return 0;}auto* getArcScriptList(){return &arcs;}auto* getMissionScriptList(){return &missions;}
auto* getAdhocScriptList(){return &adhoc;}auto* getEventScriptList(){return &events;}
auto* getArcScript(const std::string& id){return arcRules.at(id);}auto* getMissionScript(const std::string& id){return missionRules.at(id);}
auto* getAdhocScript(const std::string& id){return missionRules.at(id);}auto* getEventScript(const std::string& id){return eventScripts.at(id);}
RuleResearch* getResearch(const std::string& id,bool required){auto it=research.find(id);if(it!=research.end())return it->second;if(required)throw Exception("missing research");return nullptr;}
RuleEvent* getEvent(const std::string& id,bool required){auto it=eventRules.find(id);if(it!=eventRules.end())return it->second;if(required)throw Exception("missing event");return nullptr;}
};
struct Game {SavedGame* save;Mod* mod;auto* getSavedGame(){return save;}auto* getMod(){return mod;}};
namespace Ufopaedia {void openArticle(Game*,const std::string& id){trace.push_back("article:"+id);}}
struct GeoscapeState {
Game* _game;void determineAlienMissions(bool isNewMonth=true,const RuleEvent* eventRules=nullptr);
void initWorld(){
// INITIAL_CALLER
}
bool processCommand(RuleMissionScript* rule){
_game->save->strategy.runs[rule->runVariable]++;
auto& next=_game->save->nextIds["ALIEN_MISSIONS"];if(next==0)next=1;next++;
trace.push_back("mission:"+rule->type);return true;}
};
// METHODS
// SCHEDULING_CALLER
int main(){
std::cout<<"{\"rows\":[";
for(int scenario=0;scenario<5;scenario++){
if(scenario)std::cout<<',';
trace.clear();SavedGame save;Mod mod;Game game{&save,&mod};GeoscapeState geo{&game};
RuleResearch first{"R1"},lookup{"R2"},second{"R3"},disabled{"OLD"};first.lookup=&lookup;
mod.research={{"R1",&first},{"R2",&lookup},{"R3",&second},{"OLD",&disabled}};
RuleArcScript arc1,arc2;arc1.type="A1";arc1.sequential={"R1"};arc2.type="A2";arc2.research={{"R1",false}};arc2.sequential={"R3"};
mod.arcs={"A1","A2"};mod.arcRules={{"A1",&arc1},{"A2",&arc2}};
RuleMissionScript mission1,mission2;mission1.type="M1";mission1.research={{"R2",true}};
mission2.type="M2";mission2.research={{"R3",true}};mission2.maxRuns=1;
mod.missions={"M1","M2"};mod.missionRules={{"M1",&mission1},{"M2",&mission2}};
RuleEvent event{"E"};mod.eventRules={{"E",&event}};
RuleEventScript event1,event2;event1.type="E1";event1.research={{"R3",true}};event1.variable="RUN";event1.minimum=2;event1.sequential={"E"};
event2.type="E2";event2.minimumScore=50;event2.sequential={"E"};
mod.events={"E1","E2"};mod.eventScripts={{"E1",&event1},{"E2",&event2}};
const char* name=scenario==0?"month":scenario==1?"zero-odds":scenario==2?"disable":scenario==3?"missing-event":"initial";
if(scenario==1)arc1.odds=arc2.odds=0;
if(scenario==2){save.research.insert("OLD");first.disables={"OLD"};mission1.research={{"OLD",false}};}
if(scenario==3){mod.eventRules.clear();}
save.month=scenario==4?-1:1;
bool failed=false;try{if(scenario==4){geo.initWorld();geo.initWorld();}else geo.determineAlienMissions();}catch(const Exception&){failed=true;}
std::cout<<"{\"case\":\""<<name<<"\",\"trace\":[";
for(size_t i=0;i<trace.size();i++){if(i)std::cout<<',';std::cout<<'\"'<<trace[i]<<'\"';}
std::cout<<"],\"failed\":"<<(failed?"true":"false")<<",\"runs\":"<<save.strategy.runs["RUN"]
<<",\"events\":"<<save._generatedEvents["E"]<<",\"month\":"<<save.month<<",\"days\":"<<save.days<<",\"funds\":"<<save.funds<<"}";
}
std::cout<<"]}\n";
}

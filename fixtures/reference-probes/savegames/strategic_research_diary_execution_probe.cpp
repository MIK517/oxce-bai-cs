#include <algorithm>
#include <cstdint>
#include <iomanip>
#include <iostream>
#include <set>
#include <stdexcept>
#include <string>
#include <string_view>
#include <vector>
using Uint8=std::uint8_t;
using Uint32=std::uint32_t;
using Exception=std::runtime_error;
struct RuleResearch {
 std::string name; const RuleResearch *lookup=nullptr,*bonus=nullptr,*automatic=nullptr;
 const RuleResearch *other=nullptr,*protectedTopic=nullptr;
 bool repeatable=false; const std::string& getName() const{return name;}
 const RuleResearch* getLookup()const{return lookup;}
 std::vector<const RuleResearch*> getGetOneFree()const{if(other)return {bonus,other,bonus};return bonus?std::vector<const RuleResearch*>{bonus}:std::vector<const RuleResearch*>{};}
 std::vector<std::pair<const RuleResearch*,std::vector<const RuleResearch*>>> getGetOneFreeProtected()const{return protectedTopic?std::vector<std::pair<const RuleResearch*,std::vector<const RuleResearch*>>>{{this,{protectedTopic}}}:std::vector<std::pair<const RuleResearch*,std::vector<const RuleResearch*>>>{};}
 bool sequentialGetOneFree()const{return false;}
};
struct RNG {static inline int pick=0;static int generate(int low,int high){return std::clamp(pick,low,high);}};
struct RuleEvent {};
struct AlienDeployment {const std::string& getType()const{static const std::string type="DEPLOY_DIARY";return type;}};
struct Mod {
 std::vector<const RuleResearch*> rules;
 const RuleResearch* getResearch(const std::string& id,bool=false)const{for(auto*r:rules)if(r->name==id)return r;return nullptr;}
 const RuleEvent* getEvent(const std::string&,bool){return nullptr;}
 const AlienDeployment* getDeployment(const std::string&,bool){return nullptr;}
};
struct GameTime {int getYear(){return 1999;}int getMonth(){return 1;}int getDay(){return 2;}};
// DISCOVERY_SOURCE
namespace YAML {
 struct YamlNodeReader {
  const YamlNodeReader& operator[](const char*)const{return *this;}const YamlNodeReader& operator[](int)const{return *this;}
  template<typename T>T readVal()const{return {};}
  template<typename T>void tryRead(const char*,T&)const{}
 };
 struct YamlNodeWriter {
  void setAsMap(){}void setAsSeq(){}void setFlowStyle(){}
  YamlNodeWriter operator[](const char*)const{return *this;}
  template<typename T>void write(T){}template<typename T>void write(const char*,T){}
 };
}
// DIARY_ENTRY
struct Base {const std::string& getName()const{static const std::string name="Alpha";return name;}};
struct MissionRule {std::string interrupt;const std::string& getInterruptResearch()const{return interrupt;}};
struct Mission {MissionRule rule;bool interrupted=false;const MissionRule& getRules()const{return rule;}void setInterrupted(bool value){interrupted=value;}};
struct SavedGame {
 GameTime time;GameTime* _time=&time;bool debug=false;
 std::vector<Base*> _bases;std::vector<Mission*> _activeMissions;
 std::set<std::string> completed,disabled;std::vector<ResearchDiaryEntry*> diary;
 ~SavedGame(){for(auto*entry:diary)delete entry;}
 GameTime* getTime(){return _time;}
 bool isResearched(const RuleResearch* research,bool considerDebug=true){return research&&(completed.contains(research->name)||(considerDebug&&debug));}
 bool isResearchRuleStatusDisabled(const std::string&id){return disabled.contains(id);}
 void addResearchDiaryEntry(ResearchDiaryEntry*entry){diary.push_back(entry);}
 const RuleResearch* selectGetOneFree(const RuleResearch*);
 // Queue eligibility is a collaborator: these scenarios contain one explicit automatic link.
 // The diary block itself is inserted verbatim from SavedGame::addFinishedResearch.
 void addFinishedResearch(const RuleResearch*research,const Mod*,Base*,bool=true){
  if(disabled.contains(research->name))return;
  for(auto*currentQueueItem=research;currentQueueItem;currentQueueItem=currentQueueItem->automatic){
   if(isResearched(currentQueueItem,false))continue;
   if(!research->repeatable)completed.insert(currentQueueItem->name);
   // AUTOMATIC_DIARY
  }
 }
 bool handleResearchUnlockedByMissions(const RuleResearch*,const Mod*,const AlienDeployment*);
 void daily(const RuleResearch*research,const Mod*mod,Base*xbase){
  auto*saveGame=this;
  // BASE_HELPER
  const RuleResearch*bonus=nullptr;
  // DAILY_BONUS
  // DAILY_CORE
 }
};
// MISSION_RESEARCH
// SELECT_REWARD
int main(){
 bool first=true;std::cout<<"{\"executionRows\":[";
 for(bool mission:{false,true})for(bool debug:{false,true})for(int scenario=0;scenario<4;scenario++){
  RuleResearch core{"CORE"},lookup{"LOOKUP"},bonus{"BONUS"},bonusLookup{"BONUS_LOOKUP"},automatic{"AUTO"};
  core.lookup=&lookup;core.bonus=&bonus;bonus.lookup=&bonusLookup;
  if(scenario==1)bonus.automatic=&bonusLookup;
  else if(scenario==2){core.repeatable=true;bonus.automatic=&automatic;}
  Mod mod{{&core,&lookup,&bonus,&bonusLookup,&automatic}};Base base;AlienDeployment deployment;
  Mission coreMission{{"CORE"}},bonusMission{{"BONUS"}},autoMission{{"AUTO"}};
  SavedGame saved;saved.debug=debug;saved._bases={&base};saved._activeMissions={&coreMission,&bonusMission,&autoMission};
  if(scenario==3)saved.disabled.insert("CORE");
  if(mission)saved.handleResearchUnlockedByMissions(&core,&mod,&deployment);else saved.daily(&core,&mod,&base);
  if(!first)std::cout<<',';first=false;
  std::cout<<"{\"mission\":"<<(mission?"true":"false")<<",\"debug\":"<<(debug?"true":"false")<<",\"scenario\":"<<scenario<<",\"diary\":[";
  bool firstEntry=true;for(auto*entry:saved.diary){if(!firstEntry)std::cout<<',';firstEntry=false;
   std::cout<<"{\"name\":"<<std::quoted(entry->research->name)<<",\"date\":["<<entry->year<<','<<entry->month<<','<<entry->day<<"],\"sourceType\":"<<unsigned(entry->source.type)<<",\"sourceName\":"<<std::quoted(entry->source.name)<<'}';}
  std::cout<<"],\"interrupted\":["<<(coreMission.interrupted?"true":"false")<<','<<(bonusMission.interrupted?"true":"false")<<','<<(autoMission.interrupted?"true":"false")<<"]}";
 }
 std::cout<<"],\"randomRows\":[";
 for(int scenario=0;scenario<4;scenario++){
  RuleResearch core{"CORE"},bonus{"BONUS"},other{"OTHER"},protectedTopic{"PROTECTED"};
  core.bonus=&bonus;core.other=&other;core.protectedTopic=&protectedTopic;core.repeatable=scenario==3;
  Mod mod{{&core,&bonus,&other,&protectedTopic}};Base base;SavedGame saved;
  if(scenario==1)saved.disabled.insert("BONUS");if(scenario==2)saved.completed.insert("BONUS");
  saved.addFinishedResearch(&core,&mod,&base);
  if(scenario)std::cout<<',';std::cout<<"{\"scenario\":"<<scenario<<",\"choices\":[";
  const int count=scenario==1||scenario==2?2:scenario==3?3:4;
  for(int pick=0;pick<count;pick++){RNG::pick=pick;auto*choice=saved.selectGetOneFree(&core);if(pick)std::cout<<',';std::cout<<std::quoted(choice->name);}
  std::cout<<"]}";
 }
 std::cout<<"],\"stockScopeRows\":[";
 for(int mode:{0,1})for(int choice:{0,1}){
  RuleResearch core{"CORE"},a{"EQUIV_A"},b{"EQUIV_B"},lookup{"EQUIV_Z"};
  core.bonus=&a;core.other=&b;if(mode==1)a.lookup=&lookup;
  Mod mod{{&core,&a,&b,&lookup}};Base base;SavedGame saved;saved._bases={&base};RNG::pick=choice;
  saved.daily(&core,&mod,&base);
  if(mode||choice)std::cout<<',';std::cout<<'['<<mode<<','<<choice<<",[";
  bool firstEntry=true;for(auto*entry:saved.diary){if(!firstEntry)std::cout<<',';firstEntry=false;std::cout<<std::quoted(entry->research->name);}
  std::cout<<"]]";
 }
 std::cout<<"]}\n";
}

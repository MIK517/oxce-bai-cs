#include <algorithm>
#include <iostream>
#include <map>
#include <stdexcept>
#include <string>
#include <vector>
using Exception = std::runtime_error;
struct MissionArea { bool point; int texture; bool isPoint() const { return point; } };
struct Zone { std::vector<MissionArea> areas; };
struct RuleRegion { std::string type; std::vector<Zone> zones;
 const auto& getType() const { return type; } const auto& getMissionZones() const { return zones; } };
struct MissionRule { std::string type="MISSION"; const auto& getType() const { return type; } };
struct Mission { MissionRule rule; std::string region; const auto& getRules() const { return rule; }
 const auto& getRegion() const { return region; } };
struct Save { std::vector<Mission*> missions; auto& getAlienMissions() { return missions; } };
struct Mod { std::map<std::string,RuleRegion> regions; RuleRegion* getRegion(const std::string& id,bool) { return &regions.at(id); } };
struct Command { std::string var="RECENT",type="COMMAND"; const auto& getVarName() const { return var; }
 const auto& getType() const { return type; } };
struct Strategy { int repeat; bool validMissionLocation(const std::string&,const std::string& r,int n) const {
 return !((repeat&1) && r=="REGION" && n==0) && !((repeat&2) && r=="REGION_SECOND" && n==0);
} };
std::vector<std::pair<std::string,int>> candidates(Save* save,Mod* mod,Command* command,Strategy& strategy,
 int targetZoneNumber,bool spawnMissionSiteDirectly,bool spawnMissionSiteBySiteType) {
 MissionRule rule; const auto* missionRules=&rule;
 std::vector<std::string> regions={"REGION","REGION_SECOND"};
 std::vector<std::pair<std::string,int>> validAreas;
 // CANDIDATES
 return validAreas;
}
int main() {
 std::cout << "{\"rows\":["; bool first=true;
 for(bool direct:{false,true}) for(bool site:{false,true}) for(int occupied:{0,1,2,3})
 for(int repeat:{0,1,2,3}) for(int zone:{-1,0,1,2}) {
 if(!first) std::cout << ','; first=false;
 Mod mod; mod.regions.emplace("REGION",RuleRegion{"REGION",{{{{true,0},{false,0},{false,-1},{true,0}}},{}}});
 mod.regions.emplace("REGION_SECOND",RuleRegion{"REGION_SECOND",{{{{true,0},{false,0}}}}});
 Save save; Mission a{{},"REGION"},b{{},"REGION_SECOND"};
 if(occupied&1) save.missions.push_back(&a); if(occupied&2) save.missions.push_back(&b);
 Command command; Strategy strategy{repeat}; bool invalid=false;
 std::vector<std::pair<std::string,int>> result;
 try { result=candidates(&save,&mod,&command,strategy,zone,direct,site); } catch(const Exception&) { invalid=true; }
 std::cout << '[' << (direct?"true":"false") << ',' << (site?"true":"false") << ',' << occupied << ',' << repeat << ',' << zone << ',' << (invalid?"true":"false") << ",[";
 bool fc=true;for(auto& v:result){if(!fc)std::cout<<',';fc=false;std::cout<<"[\""<<v.first<<"\","<<v.second<<']';}
 std::cout<<"]]";
 }
 std::cout<<"]}\n";
}

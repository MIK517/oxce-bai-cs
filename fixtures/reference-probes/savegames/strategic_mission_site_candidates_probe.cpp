#include <algorithm>
#include <iostream>
#include <map>
#include <stdexcept>
#include <string>
#include <vector>
using Exception = std::runtime_error;
enum Generation { GEN_MISSION, GEN_REGION };
struct RNG { static inline std::vector<std::pair<int,int>> calls;
 static int generate(int a,int b) { calls.emplace_back(a,b); return a; }
 static bool percent(int p) { return generate(0,99)<p; } };
struct MissionArea { bool point; int texture; bool isPoint() const { return point; } };
struct Zone { std::vector<MissionArea> areas; };
struct RuleRegion { std::string type; std::vector<Zone> zones;
 const auto& getType() const { return type; } const auto& getMissionZones() const { return zones; } };
struct Wave { std::string ufoType="UFO"; };
using MissionWave = Wave;
struct MissionRule { std::string type="MISSION"; int zone=0; std::string site; Wave wave;
 const auto& getType() const { return type; } int getSpawnZone() const { return zone; }
 const auto& getWave(int) const { return wave; } const auto& getSiteType() const { return site; } };
struct Mission { MissionRule rule; std::string region; const auto& getRules() const { return rule; }
 const auto& getRegion() const { return region; } };
struct Base { double longitude=0; double getLongitude()const{return longitude;} double getLatitude()const{return 0;} };
struct LocatedRegion { RuleRegion* rule; auto* getRules(){return rule;} };
struct Save { std::vector<Mission*> missions; std::vector<Base*> bases; std::vector<LocatedRegion> baseRegions;
 auto& getAlienMissions() { return missions; } auto* getBases(){return &bases;}
 LocatedRegion* locateRegion(double lon,double){return &baseRegions.at((int)lon);} };
struct Deployment { std::string marker="SITE"; const auto& getMarkerName()const{return marker;} };
struct Mod { std::map<std::string,RuleRegion> regions; std::map<std::string,MissionRule> missions;
 std::vector<std::string> names={"REGION","REGION_SECOND"}; Deployment deployment;
 RuleRegion* getRegion(const std::string& id,bool) { return &regions.at(id); }
 const auto& getRegionsList()const{return names;} MissionRule* getAlienMission(const std::string& id,bool){return &missions.at(id);}
 Deployment* getDeployment(const std::string& id){return id=="DEPLOY"?&deployment:nullptr;}
 void* getUfo(const std::string& id){return id=="UFO"?this:nullptr;} };
struct Command { std::string var="RECENT",type="COMMAND"; const auto& getVarName() const { return var; }
 std::vector<std::string> missions={"MISSION_A","MISSION_B"},regions={"REGION"};
 int baseOdds=0; bool weighted=true,preferSecond=false;
 const auto& getType() const { return type; } bool getSiteType()const{return true;}
 std::string generate(int,Generation g)const{RNG::generate(1,g==GEN_MISSION?3:1);return g==GEN_MISSION?missions.front():regions[preferSecond?regions.size()-1:0];}
 auto getMissionTypes(int)const{return missions;} bool hasRegionWeights()const{return weighted;}
 auto getRegions(int)const{return regions;} int getTargetBaseOdds()const{return baseOdds;} int getRepeatAvoidance()const{return 2;} };
struct Strategy { int repeat; bool validMissionLocation(const std::string&,const std::string& r,int n) const {
 return !((repeat&1) && r=="REGION" && n==0) && !((repeat&2) && r=="REGION_SECOND" && n==0);
} void addMissionLocation(const std::string&,const std::string&,int,int){} };
struct Result {bool success=false;std::string mission,region;int area=-1;
 Result(bool){} Result(std::string m,std::string r,int a):success(true),mission(m),region(r),area(a){} };
Result select(Save* save,Mod* mod,Command* command,Strategy& strategy){
 int month=0;std::string targetRegion,missionType;const MissionRule* missionRules=nullptr;
 int targetZoneNumber=-1,targetAreaNumber=-1;
 // SITE_COMMAND
 return {missionType,targetRegion,targetAreaNumber};
}
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
 std::cout<<"],\"selectionRows\":[";first=true;
 for(std::string name:{"normal","firstOccupied","allOccupied","repeatPoints","rectangle","noBase","twoRegions","uniform","secondRegion"}){
 if(!first)std::cout<<',';first=false;RNG::calls.clear();
 Mod mod;mod.regions.emplace("REGION",RuleRegion{"REGION",{{{{true,0},{true,0}}}}});
 mod.regions.emplace("REGION_SECOND",RuleRegion{"REGION_SECOND",{{{{true,0}}}}});
 mod.missions.emplace("MISSION_A",MissionRule{"MISSION_A"});mod.missions.emplace("MISSION_B",MissionRule{"MISSION_B"});
 Save save;Command command;Strategy strategy{0};
 Mission a{{"MISSION_A"},"REGION"},b{{"MISSION_B"},"REGION"};
 if(name=="firstOccupied"||name=="allOccupied")save.missions.push_back(&a);
 if(name=="allOccupied")save.missions.push_back(&b);
 if(name=="repeatPoints")strategy.repeat=1;
 if(name=="rectangle"){mod.regions.at("REGION").zones[0].areas={{{false,0}}};mod.missions.at("MISSION_A").wave.ufoType="DEPLOY";}
 if(name=="noBase")command.baseOdds=100;
 if(name=="twoRegions"||name=="secondRegion")command.regions={"REGION","REGION_SECOND"};
 if(name=="uniform")command.weighted=false;
 if(name=="secondRegion")command.preferSecond=true;
 auto result=select(&save,&mod,&command,strategy);
 std::cout<<"[\""<<name<<"\","<<(result.success?"true":"false")<<",\""<<result.mission<<"\",\""<<result.region<<"\","<<result.area<<",[";
 bool fc=true;for(auto call:RNG::calls){if(!fc)std::cout<<',';fc=false;std::cout<<'['<<call.first<<','<<call.second<<']';}
 std::cout<<"]]";
 }
 std::cout<<"]}\n";
}

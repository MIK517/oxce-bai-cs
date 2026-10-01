#include <iostream>
#include <string>
#include <vector>
#include <map>
struct RNG {
 static inline std::vector<std::pair<int,int>> calls;
 static int generate(int a,int b){calls.emplace_back(a,b);return a;}
 static bool percent(int p){return generate(0,99)<p;}
};
struct Rule {std::string type;const std::string& getType()const{return type;}};
struct Region {Rule rule;Rule* getRules(){return &rule;}};
struct Base {Region* region;};
struct Mission {Rule rule;std::string region;const Rule& getRules()const{return rule;}
 const std::string& getRegion()const{return region;}};
struct Strategy {int mode=0;bool validMissionRegion(const std::string& r)const{return mode==0 || (mode==1 && r=="REGION_SECOND");}};
struct SavedGame {int mode=0;std::vector<Base*> bases;std::vector<Mission*> missions;
 auto* getBases(){return &bases;}Region* locateRegion(Base& b){return b.region;}
 auto& getAlienMissions(){return missions;}};
struct Command {std::vector<std::string> types={"MISSION_A","MISSION_B"};
 int getTargetBaseOdds()const{return 100;}auto getMissionTypes(int)const{return types;}};
struct Result {std::string region,mission;Result(bool){} Result(std::string r,std::string m):region(r),mission(m){}};
Result select(SavedGame* save,Command* command){
 Strategy strategy{save->mode};int month=0;std::string targetRegion,missionType;
 if(false){}
 // BASE_TARGET
 return {targetRegion,missionType};
}
int main(){std::cout<<"{\"rows\":[";bool first=true;
 for(std::string name:{"duplicates","pruned","wrap","busy","single","implicit","implicitPruned","implicitEmpty"}){
  if(!first)std::cout<<',';first=false;RNG::calls.clear();
  Region r{{"REGION"}},s{{"REGION_SECOND"}};Base a{&r},b{&r},c{&s};
  SavedGame save;save.bases={&a,&b,&c};Command command;
  Mission ar{{"MISSION_A"},"REGION"},as{{"MISSION_A"},"REGION_SECOND"},
   br{{"MISSION_B"},"REGION"},bs{{"MISSION_B"},"REGION_SECOND"};
  if(name=="pruned")save.missions={&ar};
  if(name=="wrap")save.missions={&ar,&as};
  if(name=="busy")save.missions={&ar,&as,&br,&bs};
  if(name=="single")command.types={"MISSION_A"};
  if(name.starts_with("implicit")){command.types.clear();if(name=="implicitPruned")save.mode=1;if(name=="implicitEmpty")save.mode=2;}
  auto v=select(&save,&command);
  std::cout<<"[\""<<name<<"\",\""<<v.mission<<"\",\""<<v.region<<"\",[";
  bool fc=true;for(auto p:RNG::calls){if(!fc)std::cout<<',';fc=false;std::cout<<'['<<p.first<<','<<p.second<<']';}
  std::cout<<"]]";
 }
 std::cout<<"]}\n";
}

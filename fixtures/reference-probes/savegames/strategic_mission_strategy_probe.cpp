#include <cassert>
#include <iostream>
#include <map>
#include <string>
#include <vector>
struct RNG {static inline int roll=1;static inline std::vector<std::pair<int,int>> calls;
 static int generate(int a,int b){calls.emplace_back(a,b);return roll;}};
struct WeightedOptions {std::map<std::string,size_t> _choices;size_t _totalWeight=0;
 std::string choose()const;void set(const std::string&,size_t);bool empty()const{return _totalWeight==0;}};
// CHOOSE
// SET
struct RuleRegion {size_t weight=1;WeightedOptions options;
 size_t getWeight()const{return weight;}const WeightedOptions& getAvailableMissions()const{return options;}};
struct Mod {std::vector<std::string> names={"REGION"};RuleRegion rule;
 const auto& getRegionsList()const{return names;}RuleRegion* getRegion(const std::string&,bool)const{return const_cast<RuleRegion*>(&rule);}};
struct AlienStrategy {WeightedOptions _regionChances;std::map<std::string,WeightedOptions*> _regionMissions;
 void init(const Mod*);std::string chooseRandomRegion(const Mod*);std::string chooseRandomMission(const std::string&)const;
 bool removeMission(const std::string&,const std::string&);
 ~AlienStrategy(){for(auto p:_regionMissions)delete p.second;}};
// INIT
// REGION
// MISSION
// REMOVE
int main(){std::cout<<"{\"rows\":[";bool first=true;
 for(std::string name:{"first","last","empty","removed","refresh"}){
 if(!first)std::cout<<',';first=false;RNG::calls.clear();RNG::roll=name=="last"?4:1;
 Mod mod;mod.rule.options.set("MISSION_A",3);mod.rule.options.set("MISSION_B",1);
 AlienStrategy s;s.init(&mod);
 if(name=="empty"){s._regionMissions["REGION"]->set("MISSION_A",0);s._regionMissions["REGION"]->set("MISSION_B",0);}
 if(name=="removed")s.removeMission("REGION","MISSION_A");
 if(name=="refresh"){s.removeMission("REGION","MISSION_A");s.removeMission("REGION","MISSION_B");s.chooseRandomRegion(&mod);}
 auto mission=s.chooseRandomMission("REGION");
 std::cout<<"[\""<<name<<"\",\""<<mission<<"\",[";bool fc=true;for(auto p:RNG::calls){if(!fc)std::cout<<',';fc=false;std::cout<<'['<<p.first<<','<<p.second<<']';}std::cout<<"]]";
 }std::cout<<"],\"deletionRows\":[";first=true;
 for(std::string name:{"empty","single","remaining"}){
 if(!first)std::cout<<',';first=false;Mod mod;
 if(name!="empty")mod.rule.options.set("MISSION_A",1);
 if(name=="remaining")mod.rule.options.set("MISSION_B",1);
 AlienStrategy s;s.init(&mod);s.removeMission("REGION","MISSION_A");
 std::cout<<"[\""<<name<<"\","<<s._regionMissions.size()<<','<<s._regionChances._totalWeight<<']';
 }std::cout<<"]}\n";}

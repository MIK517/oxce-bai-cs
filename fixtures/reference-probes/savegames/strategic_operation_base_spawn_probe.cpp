#include <algorithm>
#include <iostream>
#include <string>
#include <vector>
#include <stdexcept>
struct Exception : std::runtime_error { using std::runtime_error::runtime_error; };
struct RNG {
 static inline int integerCalls=0, unitCalls=0;
 static int generate(int a,int){++integerCalls;return a;}
 static double generate(double a,double){++unitCalls;return a;}
 static bool percent(int chance){return generate(0,99)<chance;}
};
constexpr int AMOT_SPACE=0, AMOT_REGION_EXISTING_BASE=1, AMOT_REGION_NEW_BASE=2,
 AMOT_REGION_NEW_BASE_IF_NECESSARY=3, AMOT_EARTH_EXISTING_BASE=4, AMOT_EARTH_NEW_BASE=5,
 AMOT_EARTH_NEW_BASE_IF_NECESSARY=6;
struct MissionArea { double lonMin,lonMax,latMin,latMax; };
struct MissionZone { std::vector<MissionArea> areas; };
struct RuleAlienMission {
 int getOperationType()const{return AMOT_REGION_NEW_BASE;}
 int getOperationSpawnZone()const{return 0;}
 std::string getOperationBaseType()const{return "BASE";}
 std::string getType()const{return "MISSION";}
 struct Wave{size_t spawnTimer=9000;} wave;
 const Wave& getWave(int)const{return wave;}
};
struct AlienBase { double x=0.2,y=0.1; double getLongitude()const{return x;} double getLatitude()const{return y;} };
struct AlienDeployment { int fake=0; int getFakeUnderwaterSpawnChance()const{return fake;} };
struct RuleRegion {
 bool technical=false;
 std::vector<MissionZone> zones={{{{0.2,0.2,0.1,0.1}}}};
 bool insideRegion(double x,double,bool ignore=false)const{return technical?ignore:x<1;}
 const auto& getMissionZones()const{return zones;}
};
struct Mod {
 mutable RuleRegion region; mutable AlienDeployment deployment; bool allow=true;
 RuleRegion* getRegion(const std::string&,bool)const{return &region;}
 AlienDeployment* getDeployment(const std::string&,bool)const{return &deployment;}
 bool getAllowAlienBasesOnWrongTextures()const{return allow;}
};
struct SavedGame { std::vector<AlienBase*> bases; auto* getAlienBases(){return &bases;} };
struct Game { Mod mod; SavedGame save; AlienBase created; Mod* getMod(){return &mod;} SavedGame* getSavedGame(){return &save;} };
struct Globe {
 int successAt=1; bool fake=false; mutable int landCalls=0;
 bool insideLand(double,double)const{return ++landCalls>=successAt && successAt!=0;}
 bool insideFakeUnderwaterTexture(double,double)const{return fake;}
};
struct AlienMission {
 RuleAlienMission _rule; std::string _region="REGION";
 size_t _nextWave=99,_nextUfoCounter=99,_liveUfos=99,_spawnCountdown=99;
 AlienBase* _base=nullptr; bool _interrupted=false;
 void start(Game&,const Globe&,size_t=0);
 AlienBase* spawnAlienBase(void*,Game& game,std::pair<double,double> pos,AlienDeployment*) {
  game.created={pos.first,pos.second};game.save.bases.push_back(&game.created);return &game.created;
 }
};
// START
int main(){std::cout<<"{\"rows\":[";bool first=true;
 for(int success:{1,99,0})for(int geometry:{0,1,2})for(int chance:{0,100})for(bool fake:{false,true})for(bool allow:{false,true}){
 if(!first)std::cout<<',';first=false;RNG::integerCalls=RNG::unitCalls=0;
 Game game;Globe globe;AlienMission mission;
 game.mod.allow=allow;game.mod.deployment.fake=chance;globe.successAt=success;globe.fake=fake;
 if(geometry!=0)game.mod.region.zones[0].areas[0]={2,2,0.1,0.1};
 game.mod.region.technical=geometry==2;
 mission.start(game,globe);
 std::cout<<'['<<success<<','<<geometry<<','<<chance<<','<<fake<<','<<allow<<','
 <<(mission._base!=nullptr)<<','<<mission._interrupted<<','<<RNG::integerCalls<<','<<RNG::unitCalls
 <<','<<globe.landCalls<<','<<mission._spawnCountdown<<']';
 }std::cout<<"]}\n";}

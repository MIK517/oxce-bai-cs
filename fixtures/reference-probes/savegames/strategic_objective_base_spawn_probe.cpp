#include <algorithm>
#include <iostream>
#include <string>
#include <map>
#include <vector>
#include <stdexcept>
struct Exception : std::runtime_error { using std::runtime_error::runtime_error; };
struct RNG {
 static inline int integerCalls=0,unitCalls=0;
 static int generate(int a,int){++integerCalls;return a;}
 static double generate(double a,double){++unitCalls;return a;}
 static bool percent(int c){return generate(0,99)<c;}
};
constexpr int OBJECTIVE_BASE=2;
struct MissionArea { double lonMin=0,lonMax=0,latMin=0,latMax=0; int texture=0; };
struct MissionZone { std::vector<MissionArea> areas; };
struct Texture {
 std::map<std::string,int> _deployments;
 const auto& getDeployments()const{return _deployments;}
 std::string getRandomDeployment()const;
};
struct GlobeRules { mutable Texture texture; bool missing=false;
 Texture* getTexture(int)const{return missing?nullptr:&texture;}
};
struct AlienDeployment { int fake=0; std::string id; int getFakeUnderwaterSpawnChance()const{return fake;} };
struct RuleRegion {
 bool technical=false;
 std::vector<MissionZone> zones={{{{0.2,0.2,0.1,0.1,0}}}};
 bool insideRegion(double x,double,bool ignore=false)const{return technical?ignore:x<1;}
 const auto& getMissionZones()const{return zones;}
};
struct Mod {
 mutable RuleRegion region; mutable GlobeRules globe; mutable AlienDeployment explicitBase{0,"EXPLICIT"},textureBase{0,"TEXTURE"},fallback{0,"STR_ALIEN_BASE_ASSAULT"}; bool allow=true;
 RuleRegion* getRegion(const std::string&,bool)const{return &region;}
 GlobeRules* getGlobe()const{return &globe;}
 AlienDeployment* getDeployment(const std::string& id,bool)const{
 if(id=="EXPLICIT")return &explicitBase;if(id=="TEXTURE")return &textureBase;
 if(id=="STR_ALIEN_BASE_ASSAULT")return &fallback;throw Exception("missing deployment");
 }
 bool getAllowAlienBasesOnWrongTextures()const{return allow;}
};
struct RuleAlienMission {
 std::string site;
 int objective=OBJECTIVE_BASE,zone=0;
 int getObjective()const{return objective;} int getWaveCount()const{return 1;}
 int getSpawnZone()const{return zone;} const std::string& getSiteType()const{return site;}
 std::string getType()const{return "BASE_MISSION";}
};
struct Game { Mod mod; int births=0; std::string selected; };
struct Globe { int successAt=1; bool fake=false; mutable int landCalls=0;
 bool insideLand(double,double)const{return ++landCalls>=successAt && successAt!=0;}
 bool insideFakeUnderwaterTexture(double,double)const{return fake;}
};
struct Wave { bool objectiveOnTheLandingSite=false,objective=false; };
struct Ufo { double getLongitude()const{return 0.2;} double getLatitude()const{return 0.1;} };
struct Trajectory { int zone; size_t getZone(int)const{return zone;} };
struct AlienMission {
 RuleAlienMission _rule; std::string _region="REGION"; int _nextWave=1;
 AlienDeployment* chooseAlienBaseType(const Mod&,const MissionArea&);
 void complete(Game& engine,const Globe& globe){const Mod& mod=engine.mod;Wave wave;
 // OBJECTIVE
 }
 void land(Game& engine,bool allowed,bool conversion,bool landingFlag,bool objectiveFlag,int currentZone) {
 const Mod& mod=engine.mod;Wave wave{landingFlag,objectiveFlag};Ufo ufo;Trajectory trajectory{currentZone};int curWaypoint=0;
 if(allowed && !conversion) {
 // LANDING
 }
 }
 void spawnAlienBase(void*,Game& engine,std::pair<double,double>,AlienDeployment* d){++engine.births;engine.selected=d->id;}
};
// CHOOSE
// TEXTURE
int main(){std::cout<<"{\"rows\":[";bool first=true;
 for(int success:{1,99,0})for(int geometry:{0,1,2})for(int kind:{0,1,2,3,4,5})for(int chance:{0,100})for(bool fake:{false,true})for(bool allow:{false,true}){
 if(!first)std::cout<<',';first=false;RNG::integerCalls=RNG::unitCalls=0;
 Game game;Globe globe;AlienMission mission;game.mod.allow=allow;
 game.mod.explicitBase.fake=game.mod.textureBase.fake=game.mod.fallback.fake=chance;
 globe.successAt=success;globe.fake=fake;
 if(geometry!=0)game.mod.region.zones[0].areas[0]={2,2,0.1,0.1,0};
 game.mod.region.technical=geometry==2;
 if(kind==0)mission._rule.site="EXPLICIT";
 if(kind==1)game.mod.globe.texture._deployments={{"TEXTURE",0}};
 if(kind==2)game.mod.globe.texture._deployments={{"TEXTURE",2},{"Z_UNUSED",3}};
 if(kind==3)game.mod.globe.missing=true;
 if(kind==5)game.mod.globe.texture._deployments={{"TEXTURE",0},{"Z_UNUSED",0}};
 bool error=false;try{mission.complete(game,globe);}catch(const Exception&){error=true;}
 std::cout<<'['<<success<<','<<geometry<<','<<kind<<','<<chance<<','<<fake<<','<<allow<<','<<game.births<<','<<error<<','<<RNG::integerCalls<<','<<RNG::unitCalls<<','<<globe.landCalls<<",\""<<game.selected<<"\"]";
 }std::cout<<"],\"landingRows\":[";first=true;
 for(int objective:{0,2})for(bool landing:{false,true})for(bool waveObjective:{false,true})for(int current:{0,1})for(int spawn:{0,1})for(bool allowed:{false,true})for(bool conversion:{false,true})for(int kind:{0,1,2}){
 if(!first)std::cout<<',';first=false;RNG::integerCalls=RNG::unitCalls=0;
 Game game;AlienMission mission;mission._rule.objective=objective;mission._rule.zone=spawn;
 if(kind==0)mission._rule.site="EXPLICIT";
 if(kind==1)game.mod.globe.texture._deployments={{"TEXTURE",2},{"Z_UNUSED",3}};
 mission.land(game,allowed,conversion,landing,waveObjective,current);
 std::cout<<'['<<objective<<','<<landing<<','<<waveObjective<<','<<current<<','<<spawn<<','<<allowed<<','<<conversion<<','<<kind<<','<<game.births<<','<<RNG::integerCalls<<','<<RNG::unitCalls<<",\""<<game.selected<<"\"]";
 }std::cout<<"]}\n";}

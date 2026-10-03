#include <iostream>
#include <vector>
#include <string>
#include <stdexcept>
constexpr int OBJECTIVE_RETALIATION=4,OBJECTIVE_INSTANT_RETALIATION=6,AMOT_SPACE=0;
namespace RNG {int integerCalls=0,unitCalls=0;int generate(int a,int){++integerCalls;return a;}double generate(double a,double){++unitCalls;return a;}}
struct Base {int id;double x;bool known;double getLongitude()const{return x;}double getLatitude()const{return 0.1;}bool getRetaliationTarget()const{return known;}};
struct RuleRegion {bool insideRegion(double x,double)const{return x<1;}std::pair<double,double>getRandomPoint(int)const{RNG::generate(0,0);return {RNG::generate(2.0,2.0),RNG::generate(0.1,0.1)};}};
struct RuleUfo {std::string id;int speed;};
struct UfoTrajectory {static inline std::string RETALIATION_ASSAULT_RUN="STR_RETALIATION_ASSAULT_RUN";bool ground=false;std::string getAltitude(int)const{return ground?"STR_GROUND":"STR_HIGH_UC";}int getZone(int)const{return 0;}int applySpeedPercentage(int,int speed)const{return speed;}};
struct Ufo {const RuleUfo* rule;int id;double x=0,y=0,dx=0,dy=0;std::string altitude;int speed=0;struct Stats{int speedMax;}stats;Ufo(const RuleUfo* r,int i):rule(r),id(i),stats{r->speed}{}template<typename T>void setMissionInfo(T*,const UfoTrajectory*){}const Stats&getCraftStats()const{return stats;}void setAltitude(std::string v){altitude=v;}void setSpeed(int v){speed=v;}void setLongitude(double v){x=v;}void setLatitude(double v){y=v;}template<typename T>void setDestination(T* p){dx=p->x;dy=p->y;delete p;}};
struct Waypoint {double x=0,y=0;void setLongitude(double v){x=v;}void setLatitude(double v){y=v;}};
struct SavedGame {std::vector<Base*>bases;int ids=0;auto*getBases(){return &bases;}int getId(const std::string&){return ++ids;}};
struct Mod {RuleRegion region;RuleUfo overridden{"OVERRIDE",200};UfoTrajectory assault;const RuleRegion*getRegion(const std::string&,bool)const{return &region;}const RuleUfo*getUfo(const std::string&,bool)const{return &overridden;}const UfoTrajectory*getUfoTrajectory(const std::string&,bool)const{return &assault;}};
struct Globe {};
struct Wave {};
struct Rule {int objective,operation=0;bool multi=false,skip=false,overrideUfo=false;int getObjective()const{return objective;}bool isMultiUfoRetaliation()const{return multi;}bool skipScoutingPhase()const{return skip;}std::string getSpawnUfo()const{return overrideUfo?"OVERRIDE":"";}int getOperationType()const{return operation;}};
struct AlienMission {
 Rule _rule;std::string _region="REGION";Base* _base=nullptr;bool _interrupted=false,fallback=false;
 Base* selectXcomBase(SavedGame&,const RuleRegion&);
 std::pair<double,double>getLandPoint(const Globe&,const RuleRegion& region,int zone,const Ufo&)const{return region.getRandomPoint(zone);}
 Ufo* spawn(SavedGame& game,const Mod&mod,const Globe&globe,const UfoTrajectory&trajectory) {
  RuleUfo normal{"NORMAL",100};RuleUfo* ufoRule=&normal;
  auto logUfo=[](Ufo*,SavedGame&,AlienMission*){};
  // SPAWN
  fallback=true;return nullptr;
 }
};
// SELECT
int main(){
 std::cout<<"{\"rows\":[";bool first=true;
 for(int objective:{4,6})for(bool multi:{false,true})for(bool skip:{false,true})for(int mask=0;mask<4;++mask)for(bool empty:{false,true})for(bool source:{false,true})for(bool ground:{false,true})for(bool overridden:{false,true}){
  Base a{1,0.2,(mask&1)!=0},b{2,0.4,(mask&2)!=0},operation{3,2.4,false};
  SavedGame game{empty?std::vector<Base*>{}:std::vector<Base*>{&a,&b}};Mod mod;Globe globe;UfoTrajectory trajectory{ground};
  AlienMission mission{{objective,source?1:0,multi,skip,overridden}};if(source)mission._base=&operation;
  RNG::integerCalls=RNG::unitCalls=0;
  Ufo* ufo=mission.spawn(game,mod,globe,trajectory);
  if(!first)std::cout<<',';first=false;
  std::cout<<'['<<objective<<','<<multi<<','<<skip<<','<<mask<<','<<empty<<','<<source<<','<<ground<<','<<overridden<<','<<(ufo?1:0)<<','<<mission._interrupted<<','<<mission.fallback<<','<<RNG::integerCalls<<','<<RNG::unitCalls;
  if(ufo){std::cout<<','<<ufo->x<<','<<ufo->y<<','<<ufo->dx<<','<<ufo->dy<<','<<ufo->speed;delete ufo;}else std::cout<<",0,0,0,0,0";
  std::cout<<']';
 }
 std::cout<<"]}\n";
}

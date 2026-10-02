#include <iostream>
#include <utility>
constexpr int AMOT_SPACE=0;
struct RNG{static inline int calls=0;};
struct RuleRegion{std::pair<double,double> getRandomPoint(int)const{RNG::calls+=3;return {0.17,0.14};}};
struct Base{double getLongitude()const{return 0.2;}double getLatitude()const{return 0.1;}};
struct Rule{int op;int getOperationType()const{return op;}};
struct Trajectory{const char* getAltitude(int)const{return "STR_HIGH_UC";}int groundTimer()const{return 0;}
 int applySpeedPercentage(int,int speed)const{return speed;}int getZone(int)const{return 4;}};
struct Ufo{double x=0,y=0;struct{int speedMax=2200;}stats;auto getCraftStats(){return stats;}
 void setLongitude(double v){x=v;}void setLatitude(double v){y=v;}void setAltitude(const char*){}void setSpeed(int){}void setSecondsRemaining(int){}};
struct Mission{Rule _rule;Base* _base;struct Wave{}wave;Trajectory trajectory;struct Globe{}globe;RuleRegion regionRules;
 std::pair<double,double> getWaypoint(Wave&,Trajectory&,int,Globe&,RuleRegion&,Ufo&){return regionRules.getRandomPoint(4);}
 std::pair<double,double> getLandPoint(Globe&,RuleRegion&,int,Ufo&){return regionRules.getRandomPoint(4);}
 std::pair<double,double> origin(bool supply){Ufo value;Ufo* ufo=&value;
 if(supply){
 // SUPPLY_ORIGIN
 }else{
 // ORDINARY_ORIGIN
 }return {value.x,value.y};}
};
int main(){std::cout<<"{\"rows\":[";bool first=true;Base base;
for(bool supply:{false,true})for(int op:{0,1})for(bool linked:{false,true}){
 if(!first)std::cout<<',';first=false;RNG::calls=0;Mission m{Rule{op},linked?&base:nullptr};auto p=m.origin(supply);
 std::cout<<'['<<supply<<','<<op<<','<<linked<<','<<p.first<<','<<p.second<<','<<RNG::calls<<']';
}std::cout<<"]}\n";}

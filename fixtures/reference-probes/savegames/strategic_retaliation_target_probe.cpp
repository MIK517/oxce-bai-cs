#include <iostream>
#include <vector>
#include <string>
constexpr int OBJECTIVE_RETALIATION=4;
namespace RNG {int calls=0; int generate(int minimum,int){++calls;return minimum;}}
struct Base {int id;double x;bool known;double getLongitude()const{return x;}double getLatitude()const{return 0.1;}bool getRetaliationTarget()const{return known;}};
struct RuleRegion {bool insideRegion(double x,double)const{return x<1;}};
struct SavedGame {std::vector<Base*>bases;auto* getBases(){return &bases;}};
struct Rule {int objective;bool multi;int getObjective()const{return objective;}bool isMultiUfoRetaliation()const{return multi;}};
struct AlienMission {Rule _rule;Base* selectXcomBase(SavedGame&,const RuleRegion&);};
 // SELECT
int main(){
 std::cout<<"{\"rows\":[";bool first=true;
 for(int objective:{0,4,6})for(bool multi:{false,true})for(bool skip:{false,true})for(int mask=0;mask<8;++mask){
  Base a{1,0.2,(mask&1)!=0},b{2,0.4,(mask&2)!=0},c{3,2,(mask&4)!=0};
  SavedGame game{{&a,&b,&c}};RuleRegion regionRules;AlienMission mission{{objective,multi}};Base* xcombase=nullptr;
  RNG::calls=0;
  if(skip && objective==4) {
   for(auto* xbase:*game.getBases())if(regionRules.insideRegion(xbase->getLongitude(),xbase->getLatitude())){xcombase=xbase;break;}
  }else xcombase=mission.selectXcomBase(game,regionRules);
  if(!first)std::cout<<',';first=false;
  std::cout<<'['<<objective<<','<<multi<<','<<skip<<','<<mask<<','<<(xcombase?xcombase->id:0)<<','<<RNG::calls<<']';
 }
 std::cout<<"]}\n";
}

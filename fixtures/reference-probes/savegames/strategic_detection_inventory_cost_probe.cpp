#include <iostream>
#include <map>
#include <string>
#include <utility>
namespace OpenXcom {
enum RetEnum {RetContinue};
struct RuleInventory {
 std::string _id;
 std::map<std::string,int> _costs;
 const std::string& getId()const{return _id;}
 int getCost(const RuleInventory*)const;
};
// METHODS
template<typename Signature,auto Function>struct BindFuncImpl;
// BINDING
}
using namespace OpenXcom;
int main(){
 RuleInventory right{"RIGHT",{{"LEFT",-7},{"GROUND",2147483647},{"RIGHT",99}}};
 RuleInventory left{"LEFT",{{"RIGHT",-2147483647-1}}},ground{"GROUND",{}};
 const RuleInventory* sources[]={&right,&right,&right,&left,nullptr,nullptr,&ground};
 const RuleInventory* targets[]={&right,&left,&ground,&right,&right,nullptr,&ground};
 std::cout<<"{\"costs\":[";
 for(int i=0;i<7;i++){
  int cost=99;BindFuncImpl<decltype(&RuleInventory::getCost),&RuleInventory::getCost>::func(sources[i],cost,targets[i]);
  if(i)std::cout<<',';std::cout<<'['<<cost<<']';
 }
 std::cout<<"]}\n";
}

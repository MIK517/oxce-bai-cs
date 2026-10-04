#include <cmath>
#include <iostream>
#include <vector>
namespace OpenXcom {
// TYPES
RuleDamageType();
bool isDirect()const;
};
struct Mod {
 std::vector<RuleDamageType*> _damageTypes;
 Mod(){RuleDamageType* dmg=nullptr;
// BUILTINS
 }
 ~Mod(){for(auto* type:_damageTypes)delete type;}
 const RuleDamageType* getDamageType(int type)const{return _damageTypes.at(type);}
};
struct RuleItem {
 RuleDamageType _damageType,_meleeType;
 const RuleDamageType* getDamageType()const;
 const RuleDamageType* getMeleeType()const;
};
// METHODS
}
using namespace OpenXcom;
void conversions(const RuleDamageType* damage,int power){
 int value;
 std::cout<<'[';
 getDamageToScript<&RuleDamageType::ToArmorPre>(damage,value,power);std::cout<<value;
 getDamageToScript<&RuleDamageType::ToArmor>(damage,value,power);std::cout<<','<<value;
 getDamageToScript<&RuleDamageType::ToEnergy>(damage,value,power);std::cout<<','<<value;
 getDamageToScript<&RuleDamageType::ToHealth>(damage,value,power);std::cout<<','<<value;
 getDamageToScript<&RuleDamageType::ToItem>(damage,value,power);std::cout<<','<<value;
 getDamageToScript<&RuleDamageType::ToMana>(damage,value,power);std::cout<<','<<value;
 getDamageToScript<&RuleDamageType::ToMorale>(damage,value,power);std::cout<<','<<value;
 getDamageToScript<&RuleDamageType::ToStun>(damage,value,power);std::cout<<','<<value;
 getDamageToScript<&RuleDamageType::ToTile>(damage,value,power);std::cout<<','<<value;
 getDamageToScript<&RuleDamageType::ToTime>(damage,value,power);std::cout<<','<<value;
 getDamageToScript<&RuleDamageType::ToWound>(damage,value,power);std::cout<<','<<value<<']';
}
int main(){
 Mod mod;std::cout<<"{\"rows\":[";bool first=true;
 for(int type=-1;type<20;type++){
  if(!first)std::cout<<',';first=false;RuleItem item;
  if(type>=0)item._damageType=*mod.getDamageType(type);
  int random,resist,area,ignore,armor,single;
  getRandomTypeScript(item.getDamageType(),random);getResistTypeScript(item.getDamageType(),resist);
  getAoeScript(item.getDamageType(),area);getArmorIgnoreScript(item.getDamageType(),ignore);
  getArmorEffectivenessScript(item.getDamageType(),armor);isSingleTargetScript(&item,single);
  std::cout<<'['<<type<<','<<item._damageType.FixRadius<<','<<random<<','<<resist<<','<<item._damageType.isDirect()<<','<<area<<','<<ignore<<','<<armor<<','<<single<<']';
 }
 RuleItem item,other;int single=99;isSingleTargetScript(nullptr,single);
 std::cout<<"],\"identity\":["<<(item.getDamageType()==item.getDamageType())<<','<<(item.getDamageType()==item.getMeleeType())<<','<<(item.getDamageType()==other.getDamageType())<<"],\"nullSingle\":"<<single<<",\"conversionRows\":[";
 first=true;
 for(int type=-1;type<20;type++)for(int power:{-19,19,16777217}){
  if(!first)std::cout<<',';first=false;RuleDamageType defaults;
  std::cout<<'['<<type<<','<<power<<',';
  conversions(type<0?&defaults:mod.getDamageType(type),power);std::cout<<']';
 }
 RuleDamageType custom;
 custom.ToArmorPre=-0.125f;custom.ToArmor=0.375f;custom.ToEnergy=-1.125f;
 custom.ToHealth=0.5f;custom.ToItem=1.25f;custom.ToMana=-0.75f;custom.ToMorale=2.125f;
 custom.ToStun=0.025f;custom.ToTile=-0.01f;custom.ToTime=0.1f;custom.ToWound=0.33333333f;
 for(int power:{-39,39}){std::cout<<",[20,"<<power<<',';conversions(&custom,power);std::cout<<']';}
 std::cout<<"],\"nullConversions\":";conversions(nullptr,2147483647);
 std::cout<<",\"armorPercent\":[";
 first=true;
 for(float multiplier:{-1.125f,-0.005f,0.005f,1.125f,0.0049f}){
  if(!first)std::cout<<',';first=false;custom.ArmorEffectiveness=multiplier;int percent;
  getArmorEffectivenessScript(&custom,percent);std::cout<<'['<<multiplier<<','<<percent<<']';
 }
 std::cout<<"],\"floatBoundaries\":[";
 for(int power:{2147483520,(-2147483647-1),2147483647}){
  custom.ToHealth=power==2147483647?0.5f:1.0f;int value;
  getDamageToScript<&RuleDamageType::ToHealth>(&custom,value,power);
  if(power!=2147483520)std::cout<<',';std::cout<<'['<<power<<','<<custom.ToHealth<<','<<value<<']';
 }
 std::cout<<"]}\n";
}

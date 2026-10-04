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
 std::cout<<"],\"identity\":["<<(item.getDamageType()==item.getDamageType())<<','<<(item.getDamageType()==item.getMeleeType())<<','<<(item.getDamageType()==other.getDamageType())<<"],\"nullSingle\":"<<single<<"}\n";
}

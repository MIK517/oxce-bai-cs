/* Synthetic OXCE compatibility probe. SPDX-License-Identifier: GPL-3.0-or-later */
#define _C4_YML_STD_MAP_HPP_
#define _C4_YML_STD_VECTOR_HPP_
#include "ryml.hpp"
#include "ryml_std.hpp"
#include <algorithm>
#include <fstream>
#include <iomanip>
#include <iostream>
#include <map>
#include <sstream>
#include <string>
#include <vector>
namespace c4::yml {
template<class V,class A> bool read(ConstNodeRef const& n,std::vector<V,A>* v){v->clear();v->resize((size_t)n.num_children());size_t i=0;for(auto c:n)c>>(*v)[i++];return true;}
}
static ryml::ConstNodeRef child(const ryml::ConstNodeRef& n,const char* k){return n.find_child(ryml::to_csubstr(k));}
static std::string scalar(const ryml::ConstNodeRef& n){auto v=n.val();return {v.str,v.len};}
template<class V>static void read(const ryml::ConstNodeRef& n,const char* k,V& v){auto c=child(n,k);if(c.valid())c>>v;}
static ryml::Tree parse(const char* path){std::ifstream i(path,std::ios::binary);std::ostringstream b;b<<i.rdbuf();auto y=b.str();return ryml::parse_in_arena(ryml::to_csubstr(path),ryml::to_csubstr(y));}
struct Cost{int time=0,energy=0;};
struct Action{int accuracy=0,range=200,shots=1,ammoSlot=0;Cost cost;};
struct Damage{int type=-1,radius=0;double toHealth=1;bool ignoreDirection=false;};
struct Item{std::string id;int order=0,loadOrder=0,battleType=0,clipSize=0,costBuy=0,costSell=0,transferTime=24,dropoff=2,fuse=-3,meleeAmmo=0,targetMatrix=7;bool ignoreEquip=true,psiRequired=false,manaRequired=false;std::vector<std::string>categories,ammo;std::vector<int>fireSound;Action aimed;Damage damage;};
static void action(Action&a,const ryml::ConstNodeRef&n){read(n,"accuracyAimed",a.accuracy);read(n,"aimRange",a.range);read(n,"tuAimed",a.cost.time);auto c=child(n,"costAimed");if(c.valid())read(c,"energy",a.cost.energy);auto x=child(n,"confAimed");if(x.valid()){read(x,"shots",a.shots);int slot=a.ammoSlot;read(x,"ammoSlot",slot);if(slot>=-1&&slot<4)a.ammoSlot=slot;}}
static void load(Item&i,const ryml::ConstNodeRef&n){auto ref=child(n,"refNode");if(ref.valid())load(i,ref);read(n,"costBuy",i.costBuy);read(n,"costSell",i.costSell);read(n,"transferTime",i.transferTime);read(n,"clipSize",i.clipSize);read(n,"loadOrder",i.loadOrder);read(n,"manaRequired",i.manaRequired);auto cats=child(n,"categories");if(cats.valid()){bool add=cats.has_val_tag()&&cats.val_tag()=="!add";std::vector<std::string>v;cats>>v;if(add)i.categories.insert(i.categories.end(),v.begin(),v.end());else i.categories=v;}auto ammo=child(n,"compatibleAmmo");if(ammo.valid())ammo>>i.ammo;auto bt=child(n,"battleType");if(bt.valid()){bt>>i.battleType;i.ignoreEquip=i.battleType==0||i.battleType==11;if(i.battleType==9){i.psiRequired=true;i.dropoff=1;i.aimed.range=0;i.targetMatrix=6;}else i.psiRequired=false;i.fuse=i.battleType==5?-2:i.battleType==4?-1:-3;i.meleeAmmo=i.battleType==3?0:-1;}action(i.aimed,n);auto dt=child(n,"damageType");if(dt.valid())dt>>i.damage.type;read(n,"blastRadius",i.damage.radius);auto da=child(n,"damageAlter");if(da.valid()){read(da,"ToHealth",i.damage.toHealth);read(da,"IgnoreDirection",i.damage.ignoreDirection);}auto fs=child(n,"fireSound");if(fs.valid())fs>>i.fireSound;}
static void apply(std::map<std::string,Item>&items,std::vector<std::string>&order,int&next,const ryml::ConstNodeRef&root){auto all=child(root,"items");if(!all.valid())return;for(auto n:all){auto del=child(n,"delete");if(del.valid()){auto id=scalar(del);items.erase(id);order.erase(std::remove(order.begin(),order.end(),id),order.end());continue;}auto idn=child(n,"type");if(!idn.valid())idn=child(n,"update");auto id=scalar(idn);auto[it,added]=items.emplace(id,Item{});if(added){it->second.id=id;it->second.order=++next*100;order.push_back(id);}load(it->second,n);}}
int main(int argc,char**argv){if(argc!=3)return 2;auto a=parse(argv[1]);auto b=parse(argv[2]);std::map<std::string,Item>items;std::vector<std::string>order;int next=0;apply(items,order,next,a.crootref());apply(items,order,next,b.crootref());std::cout<<std::setprecision(17)<<"{\"items\":[";for(size_t x=0;x<order.size();++x){if(x)std::cout<<',';auto&i=items.at(order[x]);int effective=i.loadOrder<=0?i.order:i.loadOrder;std::cout<<"{\"id\":\""<<i.id<<"\",\"listOrder\":"<<i.order<<",\"effectiveLoadOrder\":"<<effective<<",\"battleType\":"<<i.battleType<<",\"ignoreInCraftEquip\":"<<(i.ignoreEquip?"true":"false")<<",\"dropoff\":"<<i.dropoff<<",\"fuseType\":"<<i.fuse<<",\"meleeAmmoSlot\":"<<i.meleeAmmo<<",\"targetMatrix\":"<<i.targetMatrix<<",\"psiRequired\":"<<(i.psiRequired?"true":"false")<<",\"manaRequired\":"<<(i.manaRequired?"true":"false")<<",\"costBuy\":"<<i.costBuy<<",\"costSell\":"<<i.costSell<<",\"transferTime\":"<<i.transferTime<<",\"categories\":[";for(size_t j=0;j<i.categories.size();++j){if(j)std::cout<<',';std::cout<<'"'<<i.categories[j]<<'"';}std::cout<<"],\"ammoCount\":"<<i.ammo.size()<<",\"aimed\":["<<i.aimed.accuracy<<','<<i.aimed.range<<','<<i.aimed.shots<<','<<i.aimed.ammoSlot<<','<<i.aimed.cost.time<<','<<i.aimed.cost.energy<<"],\"damage\":["<<i.damage.type<<','<<i.damage.radius<<','<<i.damage.toHealth<<','<<(i.damage.ignoreDirection?"true":"false")<<"],\"fireSound\":[";for(size_t j=0;j<i.fireSound.size();++j){if(j)std::cout<<',';std::cout<<i.fireSound[j];}std::cout<<"]}";}std::cout<<"]}\n";}

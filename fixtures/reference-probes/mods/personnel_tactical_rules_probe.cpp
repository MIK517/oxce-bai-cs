/* Synthetic OXCE compatibility probe. SPDX-License-Identifier: GPL-3.0-or-later */
#define _C4_YML_STD_MAP_HPP_
#define _C4_YML_STD_VECTOR_HPP_
#include "ryml.hpp"
#include "ryml_std.hpp"
#include <algorithm>
#include <fstream>
#include <iostream>
#include <map>
#include <sstream>
#include <string>
#include <vector>
namespace c4::yml {
template<class V,class A> bool read(ConstNodeRef const&n,std::vector<V,A>*v){v->clear();v->resize((size_t)n.num_children());size_t i=0;for(auto c:n)c>>(*v)[i++];return true;}
template<class K,class V,class L,class A> bool read(ConstNodeRef const&n,std::map<K,V,L,A>*v){v->clear();for(auto c:n){K k{};V x{};c>>c4::yml::key(k);c>>x;v->emplace(k,x);}return true;}
}
static ryml::ConstNodeRef child(const ryml::ConstNodeRef&n,const char*k){return n.find_child(ryml::to_csubstr(k));}
static std::string scalar(const ryml::ConstNodeRef&n){auto v=n.val();return {v.str,v.len};}
template<class V>static void read(const ryml::ConstNodeRef&n,const char*k,V&v){auto c=child(n,k);if(c.valid())c>>v;}
static ryml::Tree parse(const char*p){std::ifstream i(p,std::ios::binary);std::ostringstream b;b<<i.rdbuf();auto y=b.str();return ryml::parse_in_arena(ryml::to_csubstr(p),ryml::to_csubstr(y));}
struct Stats{short health=0,firing=0;};
static void stats(Stats&v,const ryml::ConstNodeRef&n,bool merge=true){if(!n.valid())return;short x=0;auto h=child(n,"health");if(h.valid()){h>>x;if(!merge||x)v.health=x;}x=0;auto f=child(n,"firing");if(f.valid()){f>>x;if(!merge||x)v.firing=x;}}
struct Inventory{int order=0,x=0,hand=0,slots=0,cost=0;};
struct Armor{int order=0;Stats value;int runTime=75,runEnergy=75;double damage=1;};
struct Skill{int target=0,battle=0,tu=0,energy=0;};
struct Soldier{int order=0;Stats minimum,caps;std::vector<std::string>names;};
struct Unit{Stats value;int stand=0,floatHeight=0,sets=0;size_t weight=0;};
struct Bonus{int order=0;Stats value;};
struct Transform{int order=0;Stats minimum,maximum{9999,9999};std::map<std::string,size_t>events;};
template<class T>static void erase(std::map<std::string,T>&m,const std::string&id){m.erase(id);}
static void apply(const ryml::ConstNodeRef&root,std::map<std::string,Inventory>&invs,int&invNext,std::map<std::string,Armor>&armors,int&armorNext,Skill&skill,std::map<std::string,Soldier>&soldiers,int&soldierNext,Unit&unit,Bonus&bonus,int&bonusNext,Transform&trans,int&transNext,std::string&description,int&criteriaCount){
 auto is=child(root,"invs");if(is.valid())for(auto n:is){auto d=child(n,"delete");if(d.valid()){erase(invs,scalar(d));continue;}auto idn=child(n,"id");if(!idn.valid())idn=child(n,"update");auto id=scalar(idn);auto[it,added]=invs.emplace(id,Inventory{++invNext*10});auto&v=it->second;read(n,"x",v.x);v.hand=id=="STR_RIGHT_HAND"?2:id=="STR_LEFT_HAND"?1:0;auto s=child(n,"slots");if(s.valid())v.slots=(int)s.num_children();auto costs=child(n,"costs");if(costs.valid())read(costs,"STR_LEFT_HAND",v.cost);}
 auto as=child(root,"armors");if(as.valid())for(auto n:as){auto d=child(n,"delete");if(d.valid()){erase(armors,scalar(d));continue;}auto idn=child(n,"type");if(!idn.valid())idn=child(n,"update");auto id=scalar(idn);auto[it,added]=armors.emplace(id,Armor{++armorNext*100});auto&v=it->second;stats(v.value,child(n,"stats"));auto move=child(n,"moveCost");auto run=move.valid()?child(move,"runPercent"):ryml::ConstNodeRef{};if(run.valid()){run[0]>>v.runTime;run[1]>>v.runEnergy;}auto damage=child(n,"damageModifier");if(damage.valid()&&damage.num_children())damage[0]>>v.damage;}
 auto ks=child(root,"skills");if(ks.valid())for(auto n:ks){int target=skill.target;read(n,"targetMode",target);skill.target=target<0||target>16?0:target;read(n,"battleType",skill.battle);read(n,"tuUse",skill.tu);auto cost=child(n,"costUse");if(cost.valid())read(cost,"energy",skill.energy);}
 auto ss=child(root,"soldiers");if(ss.valid())for(auto n:ss){auto d=child(n,"delete");if(d.valid()){erase(soldiers,scalar(d));continue;}auto idn=child(n,"type");if(!idn.valid())idn=child(n,"update");auto id=scalar(idn);auto[it,added]=soldiers.emplace(id,Soldier{++soldierNext});auto&v=it->second;stats(v.minimum,child(n,"minStats"));auto caps=child(n,"statCaps");stats(v.caps,caps);auto names=child(n,"soldierNames");if(names.valid())for(auto x:names){auto name=scalar(x);if(name=="delete")v.names.clear();else v.names.push_back(name);}}
 auto us=child(root,"units");if(us.valid())for(auto n:us){stats(unit.value,child(n,"stats"));read(n,"standHeight",unit.stand);read(n,"floatHeight",unit.floatHeight);auto sets=child(n,"builtInWeaponSets");if(sets.valid())unit.sets=(int)sets.num_children();auto weighted=child(n,"weightedBuiltInWeaponSets");if(weighted.valid()&&weighted.num_children()){size_t x=0;read(weighted[0],"SET_A",x);unit.weight=x;}}
 auto bs=child(root,"soldierBonuses");if(bs.valid())for(auto n:bs){if(!bonus.order)bonus.order=++bonusNext*100;stats(bonus.value,child(n,"stats"));}
 auto ts=child(root,"soldierTransformation");if(ts.valid())for(auto n:ts){if(!trans.order)trans.order=++transNext*100;stats(trans.minimum,child(n,"requiredMinStats"),false);auto e=child(n,"events");if(e.valid())for(auto x:e){std::string k;size_t v;x>>c4::yml::key(k);x>>v;if(v)trans.events[k]=v;else trans.events.erase(k);}}
 auto cs=child(root,"commendations");if(cs.valid())for(auto n:cs){read(n,"description",description);auto c=child(n,"criteria");auto kills=c.valid()?child(c,"kills"):ryml::ConstNodeRef{};if(kills.valid())criteriaCount=(int)kills.num_children();}
}
int main(int argc,char**argv){if(argc!=3)return 2;auto a=parse(argv[1]);auto b=parse(argv[2]);std::map<std::string,Inventory>invs;int invNext=0;std::map<std::string,Armor>armors;int armorNext=0;Skill skill;std::map<std::string,Soldier>soldiers;int soldierNext=0;Unit unit;Bonus bonus;int bonusNext=0;Transform trans;int transNext=0;std::string description;int criteria=0;apply(a.crootref(),invs,invNext,armors,armorNext,skill,soldiers,soldierNext,unit,bonus,bonusNext,trans,transNext,description,criteria);apply(b.crootref(),invs,invNext,armors,armorNext,skill,soldiers,soldierNext,unit,bonus,bonusNext,trans,transNext,description,criteria);auto&i=invs.at("STR_RIGHT_HAND");auto&ar=armors.at("ARMOR");auto&s=soldiers.at("SOLDIER");std::cout<<"{\"inventory\":["<<i.order<<','<<i.hand<<','<<i.x<<','<<i.slots<<','<<i.cost<<"],\"armor\":["<<ar.order<<','<<ar.value.health<<','<<ar.value.firing<<','<<ar.runTime<<','<<ar.runEnergy<<','<<ar.damage<<"],\"skill\":["<<skill.target<<','<<skill.battle<<','<<skill.tu<<','<<skill.energy<<"],\"soldier\":["<<s.order<<','<<s.minimum.health<<','<<s.minimum.firing<<','<<s.caps.health<<','<<s.names.size()<<"],\"unit\":["<<unit.value.health<<','<<unit.stand+unit.floatHeight<<','<<unit.sets<<','<<unit.weight<<"],\"bonus\":["<<bonus.order<<','<<bonus.value.firing<<"],\"transformation\":["<<trans.order<<','<<trans.minimum.health<<','<<trans.maximum.health<<",{\"EVENT_B\":"<<trans.events["EVENT_B"]<<"}],\"commendation\":[\""<<description<<"\","<<criteria<<"]}\n";}

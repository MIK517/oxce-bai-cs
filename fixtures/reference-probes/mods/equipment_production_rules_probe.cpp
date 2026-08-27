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
template<class V,class A> bool read(ConstNodeRef const& n,std::vector<V,A>*v){v->clear();v->resize((size_t)n.num_children());size_t i=0;for(auto c:n)c>>(*v)[i++];return true;}
template<class K,class V,class L,class A> bool read(ConstNodeRef const& n,std::map<K,V,L,A>*v){v->clear();for(auto c:n){K k{};V x{};c>>c4::yml::key(k);c>>x;v->emplace(k,x);}return true;}
}
static ryml::ConstNodeRef child(const ryml::ConstNodeRef&n,const char*k){return n.find_child(ryml::to_csubstr(k));}
static std::string scalar(const ryml::ConstNodeRef&n){auto v=n.val();return {v.str,v.len};}
template<class V>static void read(const ryml::ConstNodeRef&n,const char*k,V&v){auto c=child(n,k);if(c.valid())c>>v;}
static ryml::Tree parse(const char*p){std::ifstream i(p,std::ios::binary);std::ostringstream b;b<<i.rdbuf();auto y=b.str();return ryml::parse_in_arena(ryml::to_csubstr(p),ryml::to_csubstr(y));}
template<class T>static void names(std::vector<T>&out,const ryml::ConstNodeRef&n){bool add=n.has_val_tag()&&n.val_tag()=="!add";std::vector<T>v;n>>v;if(add)out.insert(out.end(),v.begin(),v.end());else out=v;}
template<class T>static void intmap(std::map<std::string,T>&out,const ryml::ConstNodeRef&n){bool add=n.has_val_tag()&&n.val_tag()=="!add";std::map<std::string,T>v;n>>v;if(!add)out.clear();for(auto&p:v)out[p.first]=p.second;}
struct Category{std::string id;int order=0;bool hidden=false;std::vector<std::string>inv;};
struct Weapon{std::string launcher,clip;int damage=0,ammo=0,rearm=1,speed=0;};
struct Craft{std::string id;int order=0,soldiers=0,vehicles=0,radar=672;std::vector<std::vector<int>>weaponTypes;std::vector<std::string>fixed;};
struct Ufo{std::string size="STR_VERY_SMALL";int blob=-1,speed=0;};
struct Research{std::string id;int order=0,cost=0;std::vector<std::string>requirements;std::map<std::string,size_t>events;};
struct Manufacture{std::string id;int order=0,time=0;std::map<std::string,int>required,produced;};
template<class T>static void erase(std::map<std::string,T>&m,std::vector<std::string>&o,const std::string&id){m.erase(id);o.erase(std::remove(o.begin(),o.end(),id),o.end());}
static void apply(std::map<std::string,Category>&cats,std::vector<std::string>&catOrder,int&catNext,std::map<std::string,Weapon>&weapons,std::map<std::string,Craft>&crafts,std::vector<std::string>&craftOrder,int&craftNext,Ufo&ufo,std::map<std::string,Research>&research,std::vector<std::string>&researchOrder,int&researchNext,std::map<std::string,Manufacture>&manufacture,std::vector<std::string>&manufactureOrder,int&manufactureNext,std::vector<std::string>&setWeapons,std::string&shortcut,const ryml::ConstNodeRef&root){
 auto c=child(root,"itemCategories");if(c.valid())for(auto n:c){auto d=child(n,"delete");if(d.valid()){erase(cats,catOrder,scalar(d));continue;}auto idn=child(n,"type");if(!idn.valid())idn=child(n,"update");auto id=scalar(idn);auto[it,added]=cats.emplace(id,Category{id,++catNext*100});if(added)catOrder.push_back(id);read(n,"hidden",it->second.hidden);auto inv=child(n,"invOrder");if(inv.valid())names(it->second.inv,inv);}
 auto sets=child(root,"weaponSets");if(sets.valid())for(auto n:sets){auto w=child(n,"weapons");if(w.valid())names(setWeapons,w);}
 auto ws=child(root,"craftWeapons");if(ws.valid())for(auto n:ws){auto id=scalar(child(n,"type"));auto&w=weapons[id];read(n,"launcher",w.launcher);read(n,"clip",w.clip);read(n,"damage",w.damage);read(n,"ammoMax",w.ammo);read(n,"rearmRate",w.rearm);read(n,"projectileSpeed",w.speed);}
 auto cs=child(root,"crafts");if(cs.valid())for(auto n:cs){auto d=child(n,"delete");if(d.valid()){erase(crafts,craftOrder,scalar(d));continue;}auto idn=child(n,"type");if(!idn.valid())idn=child(n,"update");auto id=scalar(idn);auto[it,added]=crafts.emplace(id,Craft{id,++craftNext*100});if(added)craftOrder.push_back(id);auto&x=it->second;read(n,"soldiers",x.soldiers);read(n,"vehicles",x.vehicles);read(n,"radarRange",x.radar);auto f=child(n,"fixedWeapons");if(f.valid())f>>x.fixed;auto wt=child(n,"weaponTypes");if(wt.valid()){x.weaponTypes.assign(4,std::vector<int>(8));size_t slot=0;for(auto v:wt){if(slot>=4)break;if(v.is_seq()){std::vector<int>a;v>>a;for(size_t j=0;j<8;j++)x.weaponTypes[slot][j]=j<a.size()?a[j]:a[0];}else{int a;v>>a;std::fill(x.weaponTypes[slot].begin(),x.weaponTypes[slot].end(),a);}slot++;}}}
 auto us=child(root,"ufos");if(us.valid())for(auto n:us){read(n,"size",ufo.size);if(ufo.size=="STR_MEDIUM")ufo.size="STR_MEDIUM_UC";read(n,"blobSize",ufo.blob);ufo.blob=std::min(ufo.blob,7);read(n,"speedMax",ufo.speed);}
 auto rs=child(root,"research");if(rs.valid())for(auto n:rs){auto idn=child(n,"name");if(!idn.valid())idn=child(n,"update");auto id=scalar(idn);auto[it,added]=research.emplace(id,Research{id,++researchNext*100});if(added)researchOrder.push_back(id);read(n,"cost",it->second.cost);auto req=child(n,"requires");if(req.valid())names(it->second.requirements,req);auto events=child(n,"events");if(events.valid())for(auto e:events){std::string k;size_t v;e>>c4::yml::key(k);e>>v;if(v)it->second.events[k]=v;else it->second.events.erase(k);}}
 auto ms=child(root,"manufacture");if(ms.valid())for(auto n:ms){auto idn=child(n,"name");if(!idn.valid())idn=child(n,"update");auto id=scalar(idn);auto[it,added]=manufacture.emplace(id,Manufacture{id,++manufactureNext*100});if(added){it->second.produced[id]=1;manufactureOrder.push_back(id);}read(n,"time",it->second.time);auto req=child(n,"requiredItems");if(req.valid())intmap(it->second.required,req);auto prod=child(n,"producedItems");if(prod.valid())intmap(it->second.produced,prod);}
 auto ss=child(root,"manufactureShortcut");if(ss.valid())for(auto n:ss)read(n,"startFrom",shortcut);
}
int main(int argc,char**argv){if(argc!=3)return 2;auto a=parse(argv[1]);auto b=parse(argv[2]);std::map<std::string,Category>cats;std::vector<std::string>catOrder;int catNext=0;std::map<std::string,Weapon>weapons;std::map<std::string,Craft>crafts;std::vector<std::string>craftOrder;int craftNext=0;Ufo ufo;std::map<std::string,Research>research;std::vector<std::string>researchOrder;int researchNext=0;std::map<std::string,Manufacture>manufacture;std::vector<std::string>manufactureOrder;int manufactureNext=0;std::vector<std::string>setWeapons;std::string shortcut;apply(cats,catOrder,catNext,weapons,crafts,craftOrder,craftNext,ufo,research,researchOrder,researchNext,manufacture,manufactureOrder,manufactureNext,setWeapons,shortcut,a.crootref());apply(cats,catOrder,catNext,weapons,crafts,craftOrder,craftNext,ufo,research,researchOrder,researchNext,manufacture,manufactureOrder,manufactureNext,setWeapons,shortcut,b.crootref());auto&cat=cats.at("CATEGORY");auto&w=weapons.at("CRAFT_WEAPON");auto&c=crafts.at("CRAFT");auto&r=research.at("RES_B");auto&m=manufacture.at("PRODUCT");std::cout<<"{\"category\":["<<cat.order<<','<<(cat.hidden?"true":"false")<<",[\""<<cat.inv[0]<<"\",\""<<cat.inv[1]<<"\"]],\"weaponSetCount\":"<<setWeapons.size()<<",\"craftWeapon\":[\""<<w.launcher<<"\",\""<<w.clip<<"\","<<w.damage<<','<<w.ammo<<','<<w.rearm<<','<<w.speed<<"],\"craft\":["<<c.order<<','<<c.soldiers<<','<<c.vehicles<<','<<c.radar<<','<<c.weaponTypes[0][7]<<','<<c.weaponTypes[1][2]<<",\""<<c.fixed[0]<<"\"],\"ufo\":[\""<<ufo.size<<"\","<<ufo.blob<<','<<ufo.speed<<"],\"research\":["<<r.order<<','<<r.cost<<','<<r.requirements.size()<<",{";bool first=true;for(auto const&[k,v]:r.events){if(!first)std::cout<<',';first=false;std::cout<<'"'<<k<<"\":"<<v;}std::cout<<"}],\"manufacture\":["<<m.order<<','<<m.time<<','<<m.required["CLIP"]<<','<<m.required["LAUNCHER"]<<','<<m.produced["PRODUCT"]<<"],\"shortcut\":\""<<shortcut<<"\"}\n";}

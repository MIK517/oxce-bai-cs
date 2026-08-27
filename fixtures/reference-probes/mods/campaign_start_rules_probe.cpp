/* Synthetic OXCE compatibility probe. SPDX-License-Identifier: GPL-3.0-or-later */
#define _C4_YML_STD_MAP_HPP_
#define _C4_YML_STD_VECTOR_HPP_
#include "ryml.hpp"
#include "ryml_std.hpp"
#include <algorithm>
#include <cmath>
#include <fstream>
#include <iomanip>
#include <iostream>
#include <map>
#include <sstream>
#include <string>
#include <vector>
namespace c4::yml {
template<class V,class A> bool read(ConstNodeRef const& n,std::vector<V,A>* v){v->clear();v->resize((size_t)n.num_children());size_t i=0;for(auto c:n)c>>(*v)[i++];return true;}
template<class K,class V,class L,class A> bool read(ConstNodeRef const& n,std::map<K,V,L,A>* v){v->clear();for(auto c:n){K k{};V x{};c>>c4::yml::key(k);c>>x;v->emplace(k,x);}return true;}
}
static ryml::ConstNodeRef child(const ryml::ConstNodeRef& n,const char* k){return n.find_child(ryml::to_csubstr(k));}
static std::string scalar(const ryml::ConstNodeRef& n){auto v=n.val();return {v.str,v.len};}
template<class V>static void read(const ryml::ConstNodeRef& n,const char* k,V& v){auto c=child(n,k);if(c.valid())c>>v;}
static ryml::Tree parse(const char* path){std::ifstream i(path,std::ios::binary);std::ostringstream b;b<<i.rdbuf();auto y=b.str();return ryml::parse_in_arena(ryml::to_csubstr(path),ryml::to_csubstr(y));}
static double rad(double v){return v*3.14159265358979323846/180.0;}
struct Country{int base=0,cap=0;double lon=0;std::vector<std::vector<double>> areas;};
struct Region{std::vector<std::vector<double>> areas;std::map<std::string,size_t> weights;};
struct Facility{std::string id;int order=0,sx=1,sy=1;};
struct Settings{std::map<std::string,std::string> base;int weekday=6,day=1,month=1,year=1999,hour=12,minute=0,second=0,funding=0,mult=1,div=1;};
static void apply(Country& c,Region& r,std::map<std::string,Facility>& fs,std::vector<std::string>& order,int& next,Settings& s,const ryml::ConstNodeRef& root){
 auto cs=child(root,"countries");if(cs.valid())for(auto n:cs){read(n,"fundingBase",c.base);read(n,"fundingCap",c.cap);double d;if(auto x=child(n,"labelLon");x.valid()){x>>d;c.lon=rad(d);}auto a=child(n,"areas");if(a.valid())for(auto x:a){std::vector<double> v;x>>v;if(v[2]>v[3])std::swap(v[2],v[3]);c.areas.push_back(v);}}
 auto rs=child(root,"regions");if(rs.valid())for(auto n:rs){bool clear=false;read(n,"deleteOldAreas",clear);if(clear)r.areas.clear();auto a=child(n,"areas");if(a.valid())for(auto x:a){std::vector<double> v;x>>v;r.areas.push_back(v);}auto w=child(n,"missionWeights");if(w.valid())for(auto x:w){std::string k;size_t v;x>>c4::yml::key(k);x>>v;if(v)r.weights[k]=v;else r.weights.erase(k);}}
 auto facs=child(root,"facilities");if(facs.valid())for(auto n:facs){auto del=child(n,"delete");if(del.valid()){auto id=scalar(del);fs.erase(id);order.erase(std::remove(order.begin(),order.end(),id),order.end());continue;}auto idn=child(n,"type");if(!idn.valid())idn=child(n,"update");auto id=scalar(idn);auto [it,added]=fs.emplace(id,Facility{id,++next*100});if(added)order.push_back(id);auto size=child(n,"size");if(size.valid()){size>>it->second.sx;it->second.sy=it->second.sx;}read(n,"sizeX",it->second.sx);read(n,"sizeY",it->second.sy);}
 auto base=child(root,"startingBase");if(base.valid())for(auto x:base){std::string k,v;x>>c4::yml::key(k);auto first=x.first_child();if(first.valid())v=scalar(first);s.base[k]=v;}
 auto time=child(root,"startingTime");if(time.valid()){read(time,"weekday",s.weekday);read(time,"day",s.day);read(time,"month",s.month);read(time,"year",s.year);read(time,"hour",s.hour);read(time,"minute",s.minute);read(time,"second",s.second);}read(root,"initialFunding",s.funding);auto transfer=child(root,"transferCosts");if(transfer.valid()){read(transfer,"globalCostMult",s.mult);read(transfer,"globalCostDiv",s.div);}}
int main(int argc,char**argv){if(argc!=3)return 2;auto a=parse(argv[1]);auto b=parse(argv[2]);Country c;Region r;std::map<std::string,Facility>fs;std::vector<std::string>order;int next=0;Settings s;apply(c,r,fs,order,next,s,a.crootref());apply(c,r,fs,order,next,s,b.crootref());std::cout<<std::setprecision(17);
 std::cout<<"{\"country\":{\"areaCount\":"<<c.areas.size()<<",\"fundingBase\":"<<c.base<<",\"fundingCap\":"<<c.cap<<",\"labelLongitude\":"<<c.lon<<"},\"facilities\":[";for(size_t i=0;i<order.size();++i){if(i)std::cout<<',';auto&f=fs.at(order[i]);std::cout<<"[\""<<f.id<<"\","<<f.order<<','<<f.sx<<','<<f.sy<<']';}std::cout<<"],\"region\":{\"areaCount\":"<<r.areas.size()<<",\"missionWeights\":{";bool first=true;for(auto const&[k,v]:r.weights){if(!first)std::cout<<',';first=false;std::cout<<'"'<<k<<"\":"<<v;}std::cout<<"}},\"settings\":{\"baseCraft\":\""<<s.base["crafts"]<<"\",\"baseFacility\":\""<<s.base["facilities"]<<"\",\"initialFunding\":"<<s.funding<<",\"startingTime\":["<<s.weekday<<','<<s.day<<','<<s.month<<','<<s.year<<','<<s.hour<<','<<s.minute<<','<<s.second<<"],\"transferCosts\":["<<s.mult<<','<<s.div<<"]}}\n";}

/* Synthetic OXCE compatibility probe. SPDX-License-Identifier: GPL-3.0-or-later */
#include "ryml.hpp"
#include "ryml_std.hpp"
#include <fstream>
#include <iostream>
#include <sstream>
#include <string>
static ryml::ConstNodeRef child(const ryml::ConstNodeRef& n,const char* k){return n.find_child(ryml::to_csubstr(k));}
static std::string scalar(const ryml::ConstNodeRef& n){auto v=n.val();return {v.str,v.len};}
template<class V>static void read(const ryml::ConstNodeRef& n,const char* k,V& v){auto c=child(n,k);if(c.valid())c>>v;}
static ryml::Tree parse(const char* p){std::ifstream i(p,std::ios::binary);std::ostringstream b;b<<i.rdbuf();auto y=b.str();return ryml::parse_in_arena(ryml::to_csubstr(p),ryml::to_csubstr(y));}
struct State{int blocks=0,civilians=2,minDepth=0,maxDepth=0,sizeX=1,sizeY=1,sizeZ=0,freq=1,label=0,replX=0,replY=0,replZ=0,patches=0,bigWall=0,lofts=0,raceOrder=0,raceWeight=0,chance=0,color=29,allowed=0,required=0,deployMin=0,deployMax=0,deployData=0;bool noFloor=false;};
static void apply(const ryml::ConstNodeRef& root,State& s){
 auto terrains=child(root,"terrains");if(terrains.valid())for(auto n:terrains){auto blocks=child(n,"mapBlocks");if(blocks.valid()){bool add=child(n,"addOnly").valid();if(!add)s.blocks=0;s.blocks+=(int)blocks.num_children();}auto civilians=child(n,"civilianTypes");if(civilians.valid()){if(civilians.has_val_tag()&&civilians.val_tag()=="!remove")s.civilians-=(int)civilians.num_children();else if(civilians.has_val_tag()&&civilians.val_tag()=="!add")s.civilians+=(int)civilians.num_children();else s.civilians=(int)civilians.num_children();}auto depth=child(n,"depth");if(depth.valid()){depth[0]>>s.minDepth;depth[1]>>s.maxDepth;}}
 auto scripts=child(root,"mapScripts");if(scripts.valid())for(auto n:scripts){auto type=scalar(child(n,"type"));auto commands=child(n,"commands");if(!commands.valid()||!commands.num_children())continue;auto c=commands[0];auto size=child(c,"size");int x=type=="REPLACED"?0:1,y=x,z=0;if(size.valid()){if(size.is_seq()){if(size.num_children()>0)size[0]>>x;if(size.num_children()>1)size[1]>>y;if(size.num_children()>2)size[2]>>z;}else{size>>x;y=x;}}if(type=="MAP"){s.sizeX=x;s.sizeY=y;s.sizeZ=z;read(c,"freqs",s.freq);read(c,"label",s.label);if(s.label<0)s.label=-s.label;}else if(type=="REPLACED"){s.replX=x;s.replY=y;s.replZ=z;}}
 auto patches=child(root,"MCDPatches");if(patches.valid())for(auto p:patches){auto data=child(p,"data");for(auto d:data){++s.patches;read(d,"bigWall",s.bigWall);read(d,"noFloor",s.noFloor);auto loft=child(d,"LOFTS");if(loft.valid())s.lofts=(int)loft.num_children();}}
 auto races=child(root,"alienRaces");if(races.valid())for(auto n:races){if(!s.raceOrder)s.raceOrder=100;auto timeline=child(n,"retaliationMissionWeights");if(timeline.valid()&&timeline.num_children())read(timeline[0],"MISSION",s.raceWeight);}
 auto effects=child(root,"enviroEffects");if(effects.valid())for(auto n:effects){auto all=child(n,"environmentalConditions");auto hostile=child(all,"STR_HOSTILE");read(hostile,"chancePerTurn",s.chance);read(hostile,"color",s.color);}
 auto starts=child(root,"startingConditions");if(starts.valid())for(auto n:starts){auto a=child(n,"allowedItems");if(a.valid())s.allowed=(int)a.num_children();read(child(n,"requiredItems"),"ITEM",s.required);}
 auto deployments=child(root,"alienDeployments");if(deployments.valid())for(auto n:deployments){auto d=child(n,"depth");if(d.valid()){d[0]>>s.deployMin;d[1]>>s.deployMax;}auto data=child(n,"data");if(data.valid())s.deployData=(int)data.num_children();}
}
int main(int argc,char**argv){if(argc!=3)return 2;auto a=parse(argv[1]);auto b=parse(argv[2]);State s;apply(a.crootref(),s);apply(b.crootref(),s);std::cout<<"{\"terrain\":["<<s.blocks<<','<<s.civilians<<','<<s.minDepth<<','<<s.maxDepth<<"],\"mapCommand\":["<<s.sizeX<<','<<s.sizeY<<','<<s.sizeZ<<','<<s.freq<<','<<s.label<<"],\"replacement\":["<<s.replX<<','<<s.replY<<','<<s.replZ<<"],\"mcdPatch\":["<<s.patches<<','<<s.bigWall<<','<<(s.noFloor?"true":"false")<<','<<s.lofts<<"],\"race\":["<<s.raceOrder<<','<<s.raceWeight<<"],\"environment\":["<<s.chance<<','<<s.color<<"],\"startingCondition\":["<<s.allowed<<','<<s.required<<"],\"deployment\":["<<s.deployMin<<','<<s.deployMax<<','<<s.deployData<<"]}\n";}

/* Synthetic OXCE compatibility probe. SPDX-License-Identifier: GPL-3.0-or-later */
#define _C4_YML_STD_MAP_HPP_
#define _C4_YML_STD_VECTOR_HPP_
#include "ryml.hpp"
#include "ryml_std.hpp"
#include <climits>
#include <fstream>
#include <iostream>
#include <map>
#include <sstream>
#include <string>
#include <vector>

namespace c4::yml {
template<class V, class A> bool read(ConstNodeRef const& n, std::vector<V,A>* v) {
    v->clear(); v->resize(static_cast<size_t>(n.num_children())); size_t i=0;
    for (ConstNodeRef const c:n) c >> (*v)[i++]; return true;
}
template<class K,class V,class L,class A> bool read(ConstNodeRef const& n,std::map<K,V,L,A>* v) {
    v->clear(); for(ConstNodeRef const c:n){K k{};V x{};c>>c4::yml::key(k);c>>x;v->emplace(k,x);} return true;
}
}

static ryml::ConstNodeRef child(const ryml::ConstNodeRef& n,const char* k){return n.find_child(ryml::to_csubstr(k));}
static std::string scalar(const ryml::ConstNodeRef& n){auto v=n.val();return {v.str,v.len};}
template<class V> static void read(const ryml::ConstNodeRef& n,const char* k,V& v){auto c=child(n,k);if(c.valid())c>>v;}
static void pair(const ryml::ConstNodeRef& n,int& first,int& second){auto i=n.begin();auto a=*i;++i;auto b=*i;a>>first;b>>second;}
static void text(std::ostream& o,const std::string& s){o<<'"';for(char c:s){if(c=='"'||c=='\\')o<<'\\';o<<c;}o<<'"';}

struct Element {int x=INT_MAX,y=INT_MAX,w=INT_MAX,h=INT_MAX,color=INT_MAX;bool tftd=false;};
struct Interface {std::string palette;std::map<std::string,Element> elements;};
static void loadInterface(Interface& value,const ryml::ConstNodeRef& n){
    auto p=child(n,"refNode");if(p.valid())loadInterface(value,p);read(n,"palette",value.palette);
    auto es=child(n,"elements");if(!es.valid())return;for(auto e:es){std::string id;read(e,"id",id);auto& x=value.elements[id];
        auto pos=child(e,"pos");if(pos.valid())pair(pos,x.x,x.y);
        auto size=child(e,"size");if(size.valid())pair(size,x.w,x.h);
        read(e,"color",x.color);read(e,"TFTDMode",x.tftd);}}
struct Music {int cat=INT_MAX;float normalization=.76f;std::string name;};
struct Slide {int w=320,h=200,x=0,y=0,color=INT_MAX;};
struct Video {bool use=false,win=false;std::vector<std::string> videos;std::vector<Slide> slides;};
static void loadVideo(Video& v,const ryml::ConstNodeRef& n){v.use=false;read(n,"useUfoAudioSequence",v.use);read(n,"winGame",v.win);
    auto vs=child(n,"videos");if(vs.valid())for(auto x:vs)v.videos.push_back(scalar(x));auto show=child(n,"slideshow");if(!show.valid())return;
    auto slides=child(show,"slides");if(!slides.valid())return;for(auto s:slides){Slide x;auto size=child(s,"captionSize");if(size.valid())pair(size,x.w,x.h);auto pos=child(s,"captionPos");if(pos.valid())pair(pos,x.x,x.y);read(s,"captionColor",x.color);v.slides.push_back(x);}}
struct Sprite {bool single=false;int width=320,height=200;std::map<int,std::string> files;};

int main(int argc,char** argv){if(argc!=2)return 2;std::ifstream input(argv[1],std::ios::binary);std::ostringstream b;b<<input.rdbuf();if(!input)return 3;
    std::string yaml=b.str();auto tree=ryml::parse_in_arena(ryml::to_csubstr(argv[1]),ryml::to_csubstr(yaml));auto root=tree.crootref();
    Interface interfaceRule;auto interfaces=child(root,"interfaces");for(auto n:interfaces)loadInterface(interfaceRule,n);
    Music music;auto musics=child(root,"musics");for(auto n:musics){read(n,"name",music.name);read(n,"catPos",music.cat);read(n,"normalization",music.normalization);}
    Video video;video.win=true;auto videos=child(root,"cutscenes");for(auto n:videos)loadVideo(video,n);
    std::map<std::string,std::string> strings;auto languages=child(root,"extraStrings");for(auto language:languages){auto entries=child(language,"strings");for(auto entry:entries){std::string key;entry>>c4::yml::key(key);if(entry.is_map()){for(auto form:entry){std::string suffix,value;form>>c4::yml::key(suffix);form>>value;strings[key+"_"+suffix]=value;}}else{entry>>strings[key];}}}
    std::vector<Sprite> sprites;auto spriteRules=child(root,"extraSprites");for(auto n:spriteRules){auto del=child(n,"delete");if(del.valid()){sprites.clear();continue;}Sprite s;std::string type;read(n,"type",type);if(type.empty()){read(n,"typeSingle",type);s.single=!type.empty();std::string file;read(n,"fileSingle",file);if(!file.empty())s.files[0]=file;}read(n,"files",s.files);read(n,"width",s.width);read(n,"height",s.height);read(n,"singleImage",s.single);sprites.push_back(s);}
    auto& e=interfaceRule.elements.at("button");auto& s=sprites.back();auto& slide=video.slides.front();
    std::cout<<"{\"interface\":{\"palette\":";text(std::cout,interfaceRule.palette);std::cout<<",\"button\":{\"x\":"<<e.x<<",\"y\":"<<e.y<<",\"width\":"<<e.w<<",\"height\":"<<e.h<<",\"color\":"<<e.color<<",\"tftdMode\":"<<(e.tftd?"true":"false")<<"}},\"music\":{\"catalogPosition\":"<<music.cat<<",\"normalization\":"<<music.normalization<<",\"resolvedName\":\"TRACK\"},\"video\":{\"winGame\":"<<(video.win?"true":"false")<<",\"useUfoAudioSequence\":"<<(video.use?"true":"false")<<",\"videos\":[";
    for(size_t i=0;i<video.videos.size();++i){if(i)std::cout<<',';text(std::cout,video.videos[i]);}std::cout<<"],\"slide\":{\"width\":"<<slide.w<<",\"height\":"<<slide.h<<",\"x\":"<<slide.x<<",\"y\":"<<slide.y<<",\"color\":"<<slide.color<<"}},\"strings\":{";
    bool first=true;for(auto const& [k,v]:strings){if(!first)std::cout<<',';first=false;text(std::cout,k);std::cout<<':';text(std::cout,v);}std::cout<<"},\"sprite\":{\"singleImage\":"<<(s.single?"true":"false")<<",\"width\":"<<s.width<<",\"height\":"<<s.height<<",\"file\":";text(std::cout,s.files.at(0));std::cout<<"}}\n";}

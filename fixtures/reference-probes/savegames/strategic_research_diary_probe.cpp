#include <cstdint>
#include <iomanip>
#include <iostream>
#include <sstream>
#include <stdexcept>
#include <string>
#include <string_view>
#include <vector>
using Uint8=std::uint8_t;
using Uint32=std::uint32_t;
using Exception=std::runtime_error;
struct RuleResearch { std::string name; const std::string& getName() const { return name; } };
struct RuleEvent {};
struct AlienDeployment {};
struct Mod {
 RuleResearch topic{"DIARY_TOPIC"},source{"SOURCE"};RuleEvent event;AlienDeployment deployment;
 const RuleResearch* getResearch(const std::string& name,bool=false) { return name==topic.name?&topic:name==source.name?&source:nullptr; }
 const RuleEvent* getEvent(const std::string& name,bool) { return name=="SOURCE"?&event:nullptr; }
 const AlienDeployment* getDeployment(const std::string& name,bool) { return name=="SOURCE"?&deployment:nullptr; }
};
struct GameTime { int getYear(){return 1999;}int getMonth(){return 1;}int getDay(){return 2;} };
// DISCOVERY_SOURCE
namespace YAML {
 struct YamlNodeReader {
  std::string name,sourceName;std::vector<Uint32> date;DiscoverySourceType type;std::size_t index=0;
  const YamlNodeReader& operator[](const char*) const {return *this;}
  YamlNodeReader operator[](int offset) const {auto result=*this;result.index=offset;return result;}
  template<typename T>T readVal() const {return static_cast<T>(date.at(index));}
  void tryRead(const char* key,std::string& value) const {value=std::string(key)=="name"?name:sourceName;}
  void tryRead(const char*,DiscoverySourceType& value) const {value=type;}
 };
 struct Written {std::vector<Uint32> date;std::string name,sourceName;unsigned type=0;};
 struct YamlNodeWriter {
  Written* result;std::string key;
  void setAsMap(){}void setAsSeq(){}void setFlowStyle(){}
  YamlNodeWriter operator[](const char* field) const {return {result,field};}
  void write(Uint32 value){result->date.push_back(value);}
  void write(const char* field,const std::string& value){(std::string(field)=="name"?result->name:result->sourceName)=value;}
  void write(const char*,DiscoverySourceType value){result->type=static_cast<Uint8>(value);}
 };
}
// DIARY_ENTRY
int main(){
 Mod mod;bool first=true;std::cout<<"{\"rows\":[";
 for(unsigned type:{0,1,2,3,4,255})for(bool missing:{false,true})for(bool maximum:{false,true}){
  YAML::YamlNodeReader reader{"DIARY_TOPIC",missing?"REMOVED_SOURCE":"SOURCE",maximum?std::vector<Uint32>{UINT32_MAX,0,0}:std::vector<Uint32>{1999,1,2},static_cast<DiscoverySourceType>(type)};
  ResearchDiaryEntry entry(nullptr);entry.load(reader,&mod);YAML::Written saved;entry.save({&saved,{}});
  if(!first)std::cout<<',';first=false;
  std::cout<<"{\"name\":"<<std::quoted(saved.name)<<",\"date\":["<<saved.date[0]<<','<<saved.date[1]<<','<<saved.date[2]<<"],\"sourceType\":"<<saved.type<<",\"sourceName\":"<<std::quoted(saved.sourceName)<<'}';
 }
 std::cout<<"],\"skippedUndefined\":"<<(mod.getResearch("REMOVED_TOPIC")?"false":"true")<<"}\n";
}

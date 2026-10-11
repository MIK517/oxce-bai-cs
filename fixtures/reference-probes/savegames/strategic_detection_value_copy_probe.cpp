#include <iostream>
#include <string>
namespace OpenXcom {
enum RetEnum {RetContinue};
struct ScriptWorkerBase {};
struct ScriptText {std::string value;ScriptText()=default;ScriptText(const char* v):value(v){};static const ScriptText empty;};
const ScriptText ScriptText::empty;
struct RuleSkill {std::string type="SKILL_QUERY";const std::string& getType()const{return type;}};
// METHODS
}
using namespace OpenXcom;
int main(){
 ScriptText source("copied"),copy;BindSet<ScriptText>::func(copy,source);
 std::cout<<"{\"text\":[\""<<copy.value<<"\",";
 BindSet<ScriptText>::func(copy,ScriptText::empty);std::cout<<"\""<<copy.value<<"\"],";
 int tag=-7,other=0;BindSet<int>::func(other,tag);std::cout<<"\"tagCopy\":"<<other<<",\"soldierType\":[";
 RuleSkill skill;getTypeScript(&skill,copy);std::cout<<"\""<<copy.value<<"\",";
 getTypeScript(nullptr,copy);std::cout<<"\""<<copy.value<<"\"],";
 ScriptWorkerBase worker;Func_clear_text::func(worker,source);BindSet<ScriptText>::func(copy,source);
 std::cout<<"\"clearedText\":\""<<copy.value<<"\"}\n";
}

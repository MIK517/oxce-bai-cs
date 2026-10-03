#include <iostream>
#include <set>
#include <string>
#include <vector>
struct SavedGame{
 std::set<int> _ignoredUfos;
 void addUfoToIgnoreList(int);
 bool isUfoOnIgnoreList(int);
};
// ADD_IGNORE
// HAS_IGNORE
struct Rule{bool noAlert;bool isNoAlert()const{return noAlert;}};
struct Ufo{int id;Rule rule;int getId()const{return id;}const Rule* getRules()const{return &rule;}};
struct Game{SavedGame save;SavedGame* getSavedGame(){return &save;}};
bool alert(Game* _game,Ufo* ufo){
 // ALERT_CONDITION
 return false;
}
int main(){std::cout<<"{\"rows\":[";bool first=true;
 for(const auto& ids:std::vector<std::vector<int>>{{0},{3},{3,3,0},{3,4},{-1}})
 for(int id:{0,3,4,-1})for(bool noAlert:{false,true}){
  if(!first)std::cout<<',';first=false;Game game;for(int ignored:ids)game.save.addUfoToIgnoreList(ignored);
  Ufo ufo{id,{noAlert}};std::cout<<"[[";bool firstId=true;for(int ignored:ids){if(!firstId)std::cout<<',';firstId=false;std::cout<<ignored;}
  std::cout<<"],"<<id<<','<<noAlert<<','<<game.save._ignoredUfos.size()<<','<<alert(&game,&ufo)<<']';
 }std::cout<<"]}\n";
}

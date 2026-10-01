#include <iostream>
#include <vector>
enum { OBJECTIVE_RETALIATION = 4 };
struct Rule { int objective; int getObjective() { return objective; } };
struct AlienMission { Rule rule; int id; bool over; Rule& getRules() { return rule; } int getId() { return id; } bool isOver() { return over; } };
struct Node { int id; bool present; explicit operator bool() const { return present; } Node operator[](const char*) const { return *this; } template<class T> T readVal() const { return id; } };
struct SavedGame;
struct Base { AlienMission* _retaliationMission = nullptr;
 AlienMission* getRetaliationMission() { return _retaliationMission; }
 void setRetaliationMission(AlienMission* am) { _retaliationMission = am; }
 void loadLink(Node, SavedGame*);
};
struct SavedGame { std::vector<AlienMission*> missions; std::vector<Base*> bases;
 auto& getAlienMissions() { return missions; } auto* getBases() { return &bases; }
};
void Base::loadLink(Node reader, SavedGame* save) { // LOAD_LINK
}
struct Game { SavedGame* save; SavedGame* getSavedGame() { return save; } };
void cleanup(Game* _game, AlienMission* am) { // CLEANUP_LINK
}
int main() {
 std::cout << "{\"rows\":["; bool first = true;
 for(int objective : {4,6}) for(int over : {0,1}) for(int linked : {0,1}) {
  if(!first) std::cout << ','; first=false;
  AlienMission am{{objective},4,bool(over)},other{{4},5,false}; Base base{linked ? &am : &other}; SavedGame save{{&am,&other},{&base}}; Game game{&save};
  cleanup(&game,&am);
  std::cout << '[' << objective << ',' << over << ',' << linked << ',' << (base.getRetaliationMission() ? base.getRetaliationMission()->getId() : 0) << ']';
 }
 std::cout << "],\"loadRows\":["; first=true;
 for(int id : {0,4,99}) {
  if(!first) std::cout << ','; first=false;
  AlienMission am{{4},4,false}; Base base; SavedGame save{{&am},{&base}}; base.loadLink({id,id!=0},&save);
  std::cout << '[' << id << ',' << (base.getRetaliationMission() ? base.getRetaliationMission()->getId() : 0) << ']';
 }
 std::cout << "]}\n";
}

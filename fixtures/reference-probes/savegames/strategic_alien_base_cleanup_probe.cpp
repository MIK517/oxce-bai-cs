#include <iostream>
#include <string>
#include <vector>
enum { AMOT_SPACE = 0 };
struct Research { std::string id; };
struct CountryRule { std::string id = "COUNTRY"; const std::string& getType() const { return id; } };
struct Country {
 CountryRule rule; bool _pact, _newPact, _cancelPact;
 CountryRule* getRules() { return &rule; }
 void setCancelPact();
};
// CANCEL_PACT
struct Deployment { std::string code = "UNLOCK"; const auto& getBaseSelfDestructCode() const { return code; } };
struct CraftFollower { bool returned = false; void returnToBase() { returned = true; } };
struct UfoFollower { void resetOriginalDestination(bool) {} };
struct Target {
 std::vector<CraftFollower*> crafts; std::vector<UfoFollower*> ufos;
 auto getCraftFollowers() { return crafts; } auto getUfoFollowers() { return ufos; } ~Target();
};
// TARGET_DESTRUCTOR
struct AlienBase : Target {
 Deployment rule; std::string pact = "COUNTRY";
 Deployment* getDeployment() { return &rule; } const auto& getPactCountry() const { return pact; }
};
struct MissionRule { int operation; int getOperationType() const { return operation; } };
struct Mission {
 MissionRule rule; AlienBase* base; bool interrupted = false;
 AlienBase* getAlienBase() { return base; } void setAlienBase(AlienBase* value) { base = value; }
 MissionRule& getRules() { return rule; } void setInterrupted(bool value) { interrupted = value; }
};
struct Mod {
 bool cancel; Research research;
 bool getAllowCountriesToCancelAlienPact() const { return cancel; }
 Research* getResearch(const std::string&, bool) { return &research; }
};
struct SavedGame {
 std::vector<Mission*> _activeMissions; std::vector<Country*> _countries; std::vector<AlienBase*> bases; bool researched;
 auto* getAlienBases() { return &bases; } bool isResearched(Research*, bool) { return researched; }
 void clearLinksForAlienBase(AlienBase*, const Mod*);
};
// CLEAR_LINKS
struct Game { Mod mod; Mod* getMod() { return &mod; } };
void daily(Game* _game, SavedGame* saveGame, Mod* mod) {
 // SELF_DESTRUCT
}
int main() {
 std::cout << "{\"rows\":["; bool first = true;
 for (int complete : {0,1}) for (int cancel : {0,1}) for (int pact : {0,1}) {
  if (!first) std::cout << ','; first = false;
  AlienBase* base = new AlienBase; AlienBase other;
  Mission space{{0},base}, earth{{1},base}, unrelated{{1},&other}; Country country{{},bool(pact),true,false};
  SavedGame save{{&space,&earth,&unrelated},{&country},{base},bool(complete)}; Game game{{bool(cancel)}};
  daily(&game,&save,&game.mod);
  std::cout << '[' << complete << ',' << cancel << ',' << pact << ',' << save.bases.size() << ','
   << (space.base != nullptr) << ',' << space.interrupted << ',' << (earth.base != nullptr) << ',' << earth.interrupted
   << ',' << (unrelated.base == &other) << ',' << country._pact << ',' << country._newPact << ',' << country._cancelPact << ']';
  if (!save.bases.empty()) delete base;
 }
 std::cout << "],\"followerRows\":["; first = true;
 for (int complete : {0,1}) {
  if (!first) std::cout << ','; first = false;
  AlienBase* base = new AlienBase; CraftFollower follower; base->crafts.push_back(&follower);
  SavedGame save{{},{},{base},bool(complete)}; Game game{{false}};
  daily(&game,&save,&game.mod);
  std::cout << '[' << complete << ',' << follower.returned << ']';
  if (!save.bases.empty()) delete base;
 }
 std::cout << "],\"ambiguousRows\":[";
 {
  AlienBase* firstBase = new AlienBase; firstBase->rule.code.clear(); AlienBase* secondBase = new AlienBase;
  Mission mission{{0},firstBase}; CraftFollower follower; firstBase->crafts.push_back(&follower);
  SavedGame save{{&mission},{},{firstBase,secondBase},true}; Game game{{false}};
  daily(&game,&save,&game.mod);
  std::cout << '[' << (mission.base == firstBase) << ',' << follower.returned << ',' << save.bases.size() << ']';
  delete firstBase;
 }
 std::cout << "]}\n";
}

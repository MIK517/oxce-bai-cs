#include <iostream>
#include <string>
#include <vector>
#include <stdexcept>
struct Exception : std::runtime_error { using std::runtime_error::runtime_error; };
struct RNG { static inline bool last=false; static int generate(int a,int b){return last?b:a;} };
constexpr int AMOT_SPACE=0, AMOT_REGION_EXISTING_BASE=1, AMOT_REGION_NEW_BASE=2,
 AMOT_REGION_NEW_BASE_IF_NECESSARY=3, AMOT_EARTH_EXISTING_BASE=4, AMOT_EARTH_NEW_BASE=5,
 AMOT_EARTH_NEW_BASE_IF_NECESSARY=6;
struct RuleAlienMission { int operation; struct Wave{size_t spawnTimer=9000;} wave;
 int getOperationType()const{return operation;} const Wave& getWave(int)const{return wave;} };
struct AlienBase { int id; double x; double getLongitude()const{return x;} double getLatitude()const{return 0;} };
struct RuleRegion { bool insideRegion(double x,double)const{return x<1;} };
struct Mod { mutable RuleRegion region; RuleRegion* getRegion(const std::string&,bool)const{return &region;} };
struct SavedGame { std::vector<AlienBase*> bases; auto* getAlienBases(){return &bases;} };
struct Game { Mod mod; SavedGame save; Mod* getMod(){return &mod;} SavedGame* getSavedGame(){return &save;} };
struct Globe {};
struct AlienMission { const RuleAlienMission& _rule; std::string _region="REGION";
 size_t _nextWave=99,_nextUfoCounter=99,_liveUfos=99,_spawnCountdown=99; AlienBase* _base=nullptr; bool _interrupted=false;
 void start(Game&,const Globe&,size_t=0); };
// START
int main(){std::cout<<"{\"rows\":[";bool first=true;
 for(int op=0;op<=6;op++)for(bool empty:{false,true})for(bool last:{false,true}){
 if(!first)std::cout<<',';first=false;RNG::last=last;RuleAlienMission rule{op};AlienMission mission{rule};
 Game game;Globe globe;AlienBase b1{1,0.2},b2{2,2},b3{3,0.4};if(!empty)game.save.bases={&b1,&b2,&b3};
 bool guarded=false;try{mission.start(game,globe);}catch(const Exception&){guarded=true;}
 std::cout<<'['<<op<<','<<empty<<','<<last<<','<<guarded<<','<<mission._spawnCountdown<<','
 <<(mission._base?mission._base->id:0)<<','<<mission._interrupted<<']';
 }std::cout<<"]}\n";}

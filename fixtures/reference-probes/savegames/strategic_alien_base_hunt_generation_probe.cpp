#include <iostream>
#include <map>
#include <string>
#include <vector>
#include <stdexcept>
#include <cmath>
struct Exception:std::runtime_error{using std::runtime_error::runtime_error;};
struct RNG{static inline std::vector<std::pair<int,int>> calls;static int generate(int a,int b){calls.emplace_back(a,b);return a;}
 static bool percent(int p){return generate(0,99)<p;}};
struct WeightedOptions{std::map<std::string,size_t> _choices;size_t _totalWeight=0;
 std::string choose()const;void set(const std::string&,size_t);};
// CHOOSE
// SET
struct AlienDeployment{int range=100,chance=100,frequency=60;bool fromBase=true;std::vector<std::pair<size_t,WeightedOptions*>> _huntMissionDistribution;
 int getBaseDetectionRange()const{return range;}int getBaseDetectionChance()const{return chance;}
 int getHuntMissionMaxFrequency()const{return frequency;}bool isHuntMissionRaceFromAlienBase()const{return fromBase;}
 std::string generateHuntMission(size_t)const;};
// HUNT_MISSION
struct AlienBase{AlienDeployment* dep;int timer=50;AlienDeployment* getDeployment(){return dep;}
 int getMinutesSinceLastHuntMissionGeneration()const{return timer;}void setMinutesSinceLastHuntMissionGeneration(int v){timer=v;}
 std::string getAlienRace()const{return "RACE_A";}};
struct RuleCraft{bool undetectable=false;bool isUndetectable()const{return undetectable;}};
struct Craft{std::string status="STR_OUT";RuleCraft rule;int _damage=0;struct{int damageMax=100;} _stats;
 bool low=false,complete=false;double distance=0;const std::string& getStatus()const{return status;}
 RuleCraft* getRules(){return &rule;}bool getLowFuel()const{return low;}bool getMissionComplete()const{return complete;}
 bool isDestroyed()const;bool isIgnoredByHK()const;double getDistance(AlienBase*)const{return distance;}};
// DESTROYED
// IGNORED
struct Base{std::vector<Craft*> crafts;auto* getCrafts(){return &crafts;}};
struct MissionArea{};struct Zone{std::vector<MissionArea> areas;};struct RuleRegion{std::string type="REGION";std::vector<Zone> zones;
 const std::string& getType()const{return type;}const auto& getMissionZones()const{return zones;}};
struct Region{RuleRegion rule;RuleRegion* getRules(){return &rule;}};
constexpr int OBJECTIVE_SITE=3;
struct RuleAlienMission{bool weighted=false,noObject=false,appendBase=false;int objective=0;struct Wave{size_t spawnTimer=9000,ufoCount=1;}wave;
 bool hasRaceWeights()const{return weighted;}std::string generateRace(int)const{RNG::generate(1,1);return "RACE_B";}
 int getObjective()const{return objective;}int getSpawnZone()const{return 0;}const Wave& getWave(int)const{return wave;}
 size_t getWaveCount()const{return 1;}};
struct Mod{mutable RuleAlienMission rule;RuleAlienMission* getAlienMission(const std::string& id)const{return id=="HUNT_GEN"?&rule:nullptr;}
 RuleRegion* getRegion(const std::string&){static RuleRegion r;r.zones={{std::vector<MissionArea>(2)}};return &r;}};
struct Game;struct Globe{};void AppendBase(Game&);
struct AlienMission{RuleAlienMission rule;int id=0,area=-1;std::string race;AlienBase* base=nullptr;
 size_t _nextWave=0,_nextUfoCounter=0,_liveUfos=0,_spawnCountdown=0;
 AlienMission(const RuleAlienMission& r):rule(r){}
 void setRegion(const std::string&,Mod&){}void setId(int v){id=v;}void setRace(std::string v){race=v;}void setAlienBase(AlienBase* v){base=v;}
 const RuleAlienMission& getRules()const{return rule;}std::string getRegion()const{return "REGION";}
 void setMissionSiteZoneArea(int v){area=v;}void start(Game&,Globe&){
 const auto& _rule=rule;size_t initialCount=0;
 // START_TIMER
 }void think(Game& engine,Globe& globe){const auto& _rule=rule;
 // THINK_COUNTDOWN
 if(!rule.noObject)throw Exception("immediate spawn boundary");
 const auto& wave=rule.wave;
 // THINK_COUNTERS
 if(rule.appendBase)AppendBase(engine);
 // THINK_NEXT_TIMER
 } };
struct SavedGame{std::vector<Base*> bases;std::vector<AlienBase*> alienBases;std::vector<AlienMission*> missions;AlienBase* appendedBase=nullptr;
 auto* getBases(){return &bases;}auto* getAlienBases(){return &alienBases;}auto& getAlienMissions(){return missions;}
 int getMonthsPassed(){return 0;}int getId(const std::string&){return 1;}Region* locateRegion(AlienBase&){static Region r;return &r;}};
struct Game{SavedGame save;Mod mod;auto* getSavedGame(){return &save;}auto* getMod(){return &mod;}};
void AppendBase(Game& engine){engine.save.alienBases.push_back(engine.save.appendedBase);}
double Nautical(double d){return d*3.14159265358979323846/180.0/60.0;}
struct GeoscapeState{Game* _game;Globe* _globe;std::vector<Craft*> _activeCrafts;
 const std::vector<Craft*>* updateActiveCrafts();void baseHunting();};
// ACTIVE
// HUNTING
int main(){std::cout<<"{\"rows\":[";bool first=true;
 for(std::string name:{"disabled","waiting","grounded","undetectable","lowFuel","complete","outside","destroyed","emptyChoice","zeroChance","zeroTotal","multiple","delayed","weightedRace","site","short","twoCrafts","immediate","immediateZero","appendLive","appendDisabled"}){
 if(name=="immediate"){std::cout<<"],\"immediateRows\":[";first=true;}
 if(name=="appendLive"){std::cout<<"],\"birthRows\":[";first=true;}
 if(!first)std::cout<<',';first=false;RNG::calls.clear();
 WeightedOptions w;w.set("",1);AlienDeployment dep;dep._huntMissionDistribution={{0,&w}};
 AlienBase ab{&dep};Craft c,c2;Base b{{&c}};Game game;game.save.bases={&b};game.save.alienBases.reserve(32);game.save.alienBases={&ab};Globe globe;
 // Keep native iterators valid: birth rows establish the saved end boundary,
 // not behavior after vector reallocation invalidates C++ iterators.
 AlienDeployment newbornDep;newbornDep.chance=0;AlienBase newborn{&newbornDep};newborn.timer=newbornDep.frequency;
 bool append=name=="appendLive"||name=="appendDisabled";
 if(append){w.set("",0);w.set("HUNT_GEN",1);game.mod.rule.noObject=true;game.mod.rule.appendBase=true;game.mod.rule.wave.spawnTimer=0;game.save.appendedBase=&newborn;}
 if(name=="appendDisabled")newbornDep.range=0;
 if(name=="disabled")dep.range=0;if(name=="waiting")ab.timer=40;if(name=="grounded")c.status="STR_READY";
 if(name=="undetectable")c.rule.undetectable=true;if(name=="lowFuel")c.low=true;if(name=="complete")c.complete=true;
 if(name=="outside")c.distance=Nautical(dep.range*2);if(name=="destroyed")c._damage=100;
 if(name=="zeroChance")dep.chance=0;if(name=="zeroTotal")w.set("",0);if(name=="multiple")b.crafts.push_back(&c2);
 if(name=="delayed"||name=="weightedRace"||name=="site"||name=="short"||name=="twoCrafts"||name=="immediate"||name=="immediateZero"){w.set("",0);w.set("HUNT_GEN",1);}
 if(name=="weightedRace"){game.mod.rule.weighted=true;dep.fromBase=false;}
 if(name=="site")game.mod.rule.objective=3;if(name=="short")game.mod.rule.wave.spawnTimer=120;
 if(name=="twoCrafts")b.crafts.push_back(&c2);
 if(name=="immediate"||name=="immediateZero"){game.mod.rule.noObject=true;game.mod.rule.wave.spawnTimer=name=="immediate"?90:0;}
 GeoscapeState state{&game,&globe};state.baseHunting();
 std::cout<<"[\""<<name<<"\","<<ab.timer<<",[";bool fc=true;for(auto p:RNG::calls){if(!fc)std::cout<<',';fc=false;std::cout<<'['<<p.first<<','<<p.second<<']';}std::cout<<"],"<<game.save.missions.size()<<',';
 if(game.save.missions.empty())std::cout<<"null";
 else{auto* m=game.save.missions[0];std::cout<<'['<<m->id<<",\""<<m->race<<"\","<<m->area<<','<<m->_spawnCountdown<<','<<(m->base==&ab);if(name=="immediate"||name=="immediateZero"||append)std::cout<<','<<m->_nextWave<<','<<m->_nextUfoCounter;std::cout<<']';}
 if(append)std::cout<<','<<newborn.timer<<','<<game.save.alienBases.size();
 std::cout<<']';for(auto* m:game.save.missions)delete m;
 }std::cout<<"]}\n";}

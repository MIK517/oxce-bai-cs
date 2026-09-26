// Asset-free lifecycle probe. Capture inserts reference methods and bounded source
// fragments. Unsupported objective branches abort. The driver supplies a successful
// radar contact at half hours (radar arithmetic has its own oracle), one region/country,
// no followers or dogfights, and the reference handler order. GUI alerts become pause bits.
// GEOMETRY
struct RuleUfoStats { int speedMax=2200, damageMax=50; };
struct RuleUfo {
 int getFakeWaterLandingChance() const { return 20; }
 bool isUnmanned() const { return false; }
 std::string getType() const { return "UFO_SCOUT"; }
};
struct MissionWave { int ufoCount=1; size_t spawnTimer=9000; bool objective=false,objectiveOnTheLandingSite=false; std::string trajectory="ordinary"; };
struct UfoTrajectory {
 static const std::string RETALIATION_ASSAULT_RUN;
 int timer=2;
 size_t getWaypointCount() const { return 4; }
 size_t getZone(size_t i) const { return std::vector<size_t>{4,5,6,6}.at(i); }
 std::string getAltitude(size_t i) const { return std::vector<std::string>{"STR_HIGH_UC","STR_LOW_UC","STR_GROUND","STR_VERY_LOW"}.at(i); }
 int applySpeedPercentage(size_t i,int speed) const { return speed*std::vector<int>{100,80,50,100}.at(i)/100; }
 int groundTimer() const {return timer;}
 std::string getID() const {return "ordinary";}
};
const std::string UfoTrajectory::RETALIATION_ASSAULT_RUN="__RETALIATION_ASSAULT_RUN";
constexpr int OBJECTIVE_SCORE=0,OBJECTIVE_RETALIATION=1,OBJECTIVE_BASE=2,OBJECTIVE_INFILTRATION=3,AMOT_SPACE=0;
struct MissionRule {
 std::string getType() const {return "MISSION_LANDING";}
 MissionWave wave;
 size_t getWaveCount() const {return 1;}
 const MissionWave& getWave(int) const {return wave;}
 int getObjective() const {return OBJECTIVE_SCORE;}
 int getOperationType() const {return AMOT_SPACE;}
 int getSpawnZone() const {return -1;}
 bool isEndlessInfiltration() const {return false;}
 bool isMultiUfoRetaliationExtra() const {return false;}
 int getPoints() const {return 7;}
};
class AlienMission;
struct Waypoint:Target {};
struct Ufo:MovingTarget {
 enum UfoStatus {FLYING,LANDED,CRASHED,DESTROYED,IGNORE_ME};
 static const char* ALTITUDE_STRING[];
 UfoStatus _status=FLYING;
 std::string _altitude="STR_HIGH_UC",_direction="STR_NORTH";
 int _secondsRemaining=0,_damage=0,_huntBehavior=0,id=0,land=0,unique=0,wave=0,point=0;
 bool _detected=false;
 RuleUfoStats _stats;
 const RuleUfo* _rules;
 AlienMission* mission=nullptr;
 const UfoTrajectory* trajectory=nullptr;
 Ufo(const RuleUfo* r,int uid,int=0,int=0,int=0):_rules(r),unique(uid){}
 ~Ufo();
 void think(); void setAltitude(const std::string&); bool isCrashed() const; bool isDestroyed() const;
 void calculateSpeed() override;
 bool isHunting() const {return false;} bool isEscorting() const {return false;}
 void setMissionInfo(AlienMission*,const UfoTrajectory*);
 void setMissionWaveNumber(int n){wave=n;} int getMissionWaveNumber() const{return wave;}
 int getTrajectoryPoint() const{return point;} void setTrajectoryPoint(int n){point=n;}
 const UfoTrajectory& getTrajectory() const{return *trajectory;}
 const RuleUfoStats& getCraftStats() const{return _stats;}
 const RuleUfo* getRules() const{return _rules;}
 const std::string& getAltitude() const{return _altitude;}
 void setStatus(UfoStatus s){_status=s;} UfoStatus getStatus() const{return _status;}
 void setDetected(bool v){_detected=v;} bool getDetected() const{return _detected;}
 void setLandId(int v){land=v;} int getLandId() const{return land;}
 void setSecondsRemaining(int v){_secondsRemaining=v;}
 void setDestination(Target* target){delete _dest;_dest=target;_meetCalculated=false;calculateSpeed();}
};
const char* Ufo::ALTITUDE_STRING[]={"STR_GROUND","STR_VERY_LOW","STR_LOW_UC","STR_HIGH_UC","STR_VERY_HIGH"};
struct Globe {bool insideLand(double,double) const{return true;} bool insideFakeUnderwaterTexture(double,double) const{return false;}};
struct SavedGame {
 std::vector<Ufo*> ufos; int nextUnique=9,nextLand=40,activity=0;
 int getId(const std::string& type){return type=="STR_UFO_UNIQUE"?nextUnique++:nextLand++;}
 std::vector<Ufo*>* getUfos(){return &ufos;}
};
struct Mod {RuleRegion region;UfoTrajectory trajectory;RuleUfo ufo;
 const RuleRegion* getRegion(const std::string&,bool) const{return &region;}
 const UfoTrajectory* getUfoTrajectory(const std::string&,bool) const{return &trajectory;}
};
struct Game {SavedGame save; Mod mod; Mod* getMod(){return &mod;} SavedGame* getSavedGame(){return &save;}};
struct AlienMission {
 MissionRule _rule;size_t _nextWave=0,_nextUfoCounter=0,_spawnCountdown=30,_liveUfos=0;
 bool _interrupted=false,_multiUfoRetaliationInProgress=false;int _missionSiteZoneArea=-1;
 std::string _region="REGION"; Target* _base=nullptr;
 bool isOver() const;void think(Game&,const Globe&);
 Ufo* spawnUfo(SavedGame&,const Mod&,const Globe&,const MissionWave&,const UfoTrajectory&);
 void ufoReachedWaypoint(Ufo&,Game&,const Globe&);void ufoLifting(Ufo&,SavedGame&);
 std::pair<double,double> getWaypoint(const MissionWave&,const UfoTrajectory&,size_t,const Globe&,const RuleRegion&,const Ufo&);
 std::pair<double,double> getLandPoint(const Globe&,const RuleRegion&,size_t,const Ufo&);
 std::pair<double,double> getLandPointForMissionSite(const Globe&,const RuleRegion&,int,int,const Ufo&){throw std::runtime_error("site");}
 void logMissionError(size_t,const RuleRegion&){throw std::runtime_error("zone");}
 void addScore(double,double,SavedGame& game){game.activity+=_rule.getPoints();}
};
void Ufo::setMissionInfo(AlienMission* m,const UfoTrajectory* t){mission=m;trajectory=t;++m->_liveUfos;}
Ufo::~Ufo(){if(mission) --mission->_liveUfos;delete _dest;}
// UFO_THINK
// UFO_ALTITUDE
// UFO_CRASHED
// UFO_DESTROYED
// UFO_SPEED
// MISSION_OVER
// MISSION_THINK
// MISSION_SPAWN
// MISSION_ARRIVAL
// MISSION_LIFT
// MISSION_WAYPOINT
// MISSION_LAND
std::string number(double n){std::ostringstream s;s<<std::fixed<<std::setprecision(9)<<n;return s.str();}
void row(int tick,AlienMission* mission,Game& game,bool pause){
 std::cout<<'['<<tick<<','<<(mission?(int)mission->_nextWave:-1)<<','<<(mission?(int)mission->_nextUfoCounter:-1)<<','<<(mission?(int)mission->_spawnCountdown:-1)<<','<<(mission?(int)mission->_liveUfos:-1)<<','<<game.save.activity<<','<<(pause?1:0)<<','<<RNG::calls<<",[";
 bool first=true;for(auto* u:game.save.ufos){if(!first)std::cout<<',';first=false;
 std::cout<<'['<<u->unique<<','<<u->_status<<','<<u->point<<",\""<<u->_altitude<<"\",\""<<number(u->_lon)<<"\",\""<<number(u->_lat)<<"\",\""<<number(u->_dest->_lon)<<"\",\""<<number(u->_dest->_lat)<<"\","<<u->_speed<<','<<u->_secondsRemaining<<','<<u->id<<','<<u->land<<','<<(u->_detected?1:0)<<']';}
 std::cout<<"]]";
}
void run(const std::string& name,int timer,bool interrupted,bool multiple){
 RNG::calls=0; Game game;Globe globe;AlienMission* mission=new AlienMission();game.mod.trajectory.timer=timer;
 auto& region=game.mod.region;region._lonMin={0};region._lonMax={1};region._latMin={-1};region._latMax={1};
 for(int i=0;i<7;i++){double lon=Deg2Rad(i==4?10:i==5?8:8.01),lat=Deg2Rad(i==4?8:2);region._missionZones.push_back({{{lon,lon,lat,lat}}});}
 if(multiple){for(int i=0;i<2;i++)game.save.ufos.push_back(mission->spawnUfo(game.save,game.mod,globe,mission->_rule.wave,game.mod.trajectory));mission->_nextWave=1;}
 std::cout<<"{\"name\":\""<<name<<"\",\"rows\":[";
 for(int tick=1;tick<=721;tick++){
  bool pause=false,ended=false;
  if(interrupted && tick==2)mission->_interrupted=true;
  // Starts at 01:29:55. Larger time handlers precede the five-second loop.
  if((tick-1)%360==0){
   if(mission){mission->think(game,globe);if(mission->isOver()){delete mission;mission=nullptr;}}
   for(auto* u:game.save.ufos)if(u->_status==Ufo::FLYING || u->_status==Ufo::LANDED){
    game.save.activity+=u->_status==Ufo::LANDED?6:3;
    // Controlled successful radar collaborator; production detection has separate fixtures.
    // Base::detect still ends with RNG::percent for the hyperwave base's certain chance.
    RNG::percent(100);
    if(!u->_detected){u->_detected=true;u->id=11+(u->unique-9);if(u->_status==Ufo::LANDED && !u->land)u->land=game.save.getId("STR_LANDING_SITE");pause=true;}
   }
  }
  for(auto* u:game.save.ufos){
   if(u->_status==Ufo::FLYING){u->think();if(u->reachedDestination()){
    mission->ufoReachedWaypoint(*u,game,globe);
    if(u->_status==Ufo::LANDED && u->_detected && u->land)pause=true;
    if(u->_status==Ufo::DESTROYED){ended=true;break;}
   }}else if(u->_status==Ufo::LANDED){u->think();if(!u->_secondsRemaining)mission->ufoLifting(*u,game.save);}
  }
  if(!ended)for(auto it=game.save.ufos.begin();it!=game.save.ufos.end();)if((*it)->_status==Ufo::DESTROYED){delete *it;it=game.save.ufos.erase(it);}else ++it;
  if(tick>1)std::cout<<',';row(tick,mission,game,pause);
 }
 assert(game.save.ufos.empty() && mission==nullptr);std::cout<<"]}";
}
}
int main(){using namespace OpenXcom;std::cout<<"{\"cases\":[";run("ordinary",2,false,false);std::cout<<',';run("long",360,false,false);std::cout<<',';run("interrupted",2,true,false);std::cout<<',';run("multiple",2,false,true);std::cout<<"]}\n";}

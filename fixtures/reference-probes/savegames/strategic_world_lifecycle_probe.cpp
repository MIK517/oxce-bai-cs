// Asset-free lifecycle probe. Capture inserts reference methods and bounded source
// fragments. Unsupported objective branches abort. The driver supplies a successful
// radar contact at half hours (radar arithmetic has its own oracle), one region/country,
// no followers or dogfights, and the reference handler order. GUI alerts become pause bits.
// GEOMETRY
namespace Options {bool aggressiveRetaliation=false;}
struct RuleUfoStats { int speedMax=2200, damageMax=50; };
struct RuleUfo {
 int fakeChance=20; int getFakeWaterLandingChance() const { return fakeChance; }
 bool isUnmanned() const { return false; }
 std::string getType() const { return "UFO_SCOUT"; }
};
struct MissionWave { int ufoCount=1; size_t spawnTimer=9000; bool objective=false,objectiveOnTheLandingSite=false,objectiveOnXcomBase=false; std::string trajectory="ordinary"; };
struct UfoTrajectory {
 static const std::string RETALIATION_ASSAULT_RUN;
 int timer=2,firstAltitude=-1,secondAltitude=-1,thirdAltitude=-1;
 size_t getWaypointCount() const { return 4; }
 size_t getZone(size_t i) const { return std::vector<size_t>{4,5,6,6}.at(i); }
 std::string getAltitude(size_t i) const { if(i==2 && thirdAltitude==4)return "STR_VERY_HIGH"; if(i==0 && firstAltitude==0 || i==1 && secondAltitude==0)return "STR_GROUND"; return std::vector<std::string>{"STR_HIGH_UC","STR_LOW_UC","STR_GROUND","STR_VERY_LOW"}.at(i); }
 int applySpeedPercentage(size_t i,int speed) const { return speed*std::vector<int>{100,80,50,100}.at(i)/100; }
 int groundTimer() const {return timer;}
 std::string getID() const {return "ordinary";}
};
const std::string UfoTrajectory::RETALIATION_ASSAULT_RUN="__RETALIATION_ASSAULT_RUN";
constexpr int OBJECTIVE_SCORE=0,OBJECTIVE_INFILTRATION=1,OBJECTIVE_BASE=2,OBJECTIVE_RETALIATION=4,OBJECTIVE_SUPPLY=5,AMOT_SPACE=0;
struct MissionRule {
 int objective=OBJECTIVE_SCORE,points=7,operation=AMOT_SPACE,spawnZone=-1;
 std::string getType() const {return "MISSION_LANDING";}
 MissionWave wave;
 size_t getWaveCount() const {return 1;}
 const MissionWave& getWave(int) const {return wave;}
 int getObjective() const {return objective;}
 int getOperationType() const {return operation;}
 int getSpawnZone() const {return spawnZone;}
 bool isEndlessInfiltration() const {return false;}
 bool isMultiUfoRetaliationExtra() const {return false;}
 int getPoints() const {return points;}
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
 AlienMission* getMission() const{return mission;}
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
 std::vector<Target*> bases; auto* getBases(){return &bases;}
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
 std::pair<double,double> getLandPointForMissionSite(const Globe&,const RuleRegion&,size_t,int,const Ufo&);
 void logMissionError(size_t,const RuleRegion&){throw std::runtime_error("zone");}
 void addScore(double,double,SavedGame& game){
  // MISSION_SCORE_FILTER
  game.activity+=_rule.getPoints();
 }
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
// MISSION_SITE_LAND
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
struct MissionSite {
 Ufo* ufo;
 Ufo* getUfo() const {return ufo;}
 void setUfo(Ufo* value){ufo=value;}
};
void reactivateSite(Game* _game,MissionSite* site,bool removeSite){
 // SITE_REACTIVATION
}
void siteLiftRows(){
 struct Case{int points,objective,timer;bool remove;};
 const std::vector<Case> cases={{7,OBJECTIVE_SCORE,77,false},{7,OBJECTIVE_SCORE,77,true},
  {7,OBJECTIVE_BASE,77,true},{7,OBJECTIVE_INFILTRATION,77,true},{4,3,77,true},{-3,OBJECTIVE_SCORE,0,true}};
 bool first=true;
 for(const auto& input:cases){
  if(!first)std::cout<<',';first=false;
  Game game;AlienMission mission;mission._rule.points=input.points;mission._rule.objective=input.objective;
  Ufo ufo(&game.mod.ufo,9);ufo.setMissionInfo(&mission,&game.mod.trajectory);
  ufo.point=2;ufo.setAltitude("STR_GROUND");ufo.setStatus(Ufo::IGNORE_ME);ufo.setSecondsRemaining(input.timer);
  auto* destination=new Waypoint();destination->setLongitude(0.4);destination->setLatitude(0.2);ufo.setDestination(destination);
  ufo.think(); // IGNORE_ME must leave the stored ground timer and movement untouched.
  MissionSite site{&ufo};reactivateSite(&game,&site,input.remove);
  std::cout<<'['<<input.points<<','<<input.objective<<','<<input.timer<<','<<(input.remove?1:0)<<','
   <<ufo._status<<",\""<<ufo._altitude<<"\","<<ufo._speed<<','<<ufo._secondsRemaining<<','
   <<game.save.activity<<','<<(site.ufo?1:0)<<']';
 }
}

void transitionRows(){
 bool first=true;
 for(int objective=0;objective<=6;objective++)for(int operation=0;operation<=6;operation++)
 for(bool linked:{false,true})for(int point=0;point<3;point++){
  if(!first)std::cout<<',';first=false;
  RNG::calls=0;Game game;Globe globe;AlienMission mission;Target base;
  base.setLongitude(0.2);base.setLatitude(0.1);mission._base=linked?&base:nullptr;
  mission._rule.objective=objective;mission._rule.operation=operation;mission._nextWave=1;
  auto& region=game.mod.region;region._lonMin={0};region._lonMax={1};region._latMin={-1};region._latMax={1};
  for(int i=0;i<7;i++){double lon=Deg2Rad(i==4?10:i==5?8:8.01),lat=Deg2Rad(i==4?8:2);region._missionZones.push_back({{{lon,lon,lat,lat}}});}
  Ufo ufo(&game.mod.ufo,9);ufo.setMissionInfo(&mission,&game.mod.trajectory);ufo.point=point;
  ufo.setLongitude(Deg2Rad(8));ufo.setLatitude(Deg2Rad(2));
  auto* destination=new Waypoint();destination->setLongitude(ufo._lon);destination->setLatitude(ufo._lat);ufo.setDestination(destination);
  mission.ufoReachedWaypoint(ufo,game,globe);
  std::cout<<'['<<objective<<','<<operation<<','<<(linked?1:0)<<','<<point<<','<<ufo._status<<",\""<<ufo._altitude<<"\",\""<<number(ufo._dest->_lon)<<"\",\""<<number(ufo._dest->_lat)<<"\","<<ufo._speed<<','<<ufo._secondsRemaining<<','<<RNG::calls<<']';
 }
}
void normalLiftRows(){
 bool first=true;
 for(int objective=0;objective<=6;objective++)for(int operation=0;operation<=6;operation++){
  if(!first)std::cout<<',';first=false;
  Game game;AlienMission mission;mission._rule.objective=objective;mission._rule.operation=operation;
  Ufo ufo(&game.mod.ufo,9);ufo.setMissionInfo(&mission,&game.mod.trajectory);ufo.point=2;
  ufo.setAltitude("STR_GROUND");ufo.setSecondsRemaining(5);
  auto* destination=new Waypoint();destination->setLongitude(0.4);destination->setLatitude(0.2);ufo.setDestination(destination);
  ufo.think();mission.ufoLifting(ufo,game.save);
  std::cout<<'['<<objective<<','<<operation<<','<<ufo._status<<",\""<<ufo._altitude<<"\","<<ufo._speed<<','<<ufo._secondsRemaining<<','<<game.save.activity<<']';
 }
}

void spawnFlagRows(){
 bool first=true;
 for(bool objective:{false,true})for(bool landing:{false,true})for(bool xcom:{false,true}){
  if(!first)std::cout<<',';first=false;
  Game game;Globe globe;AlienMission mission;mission._rule.wave.objective=objective;
  mission._rule.wave.objectiveOnTheLandingSite=landing;mission._rule.wave.objectiveOnXcomBase=xcom;
  for(int i=0;i<7;i++){double lon=Deg2Rad(i==4?10:i==5?8:8.01),lat=Deg2Rad(i==4?8:2);game.mod.region._missionZones.push_back({{{lon,lon,lat,lat}}});}
  RNG::calls=0;auto* ufo=mission.spawnUfo(game.save,game.mod,globe,mission._rule.wave,game.mod.trajectory);
  std::cout<<'['<<objective<<','<<landing<<','<<xcom<<",\""<<number(ufo->_lon)<<"\",\""<<number(ufo->_lat)<<"\",\""<<number(ufo->_dest->_lon)<<"\",\""<<number(ufo->_dest->_lat)<<"\","<<RNG::calls<<']';delete ufo;
 }
}

void spawnPathRows(){
 bool first=true;
 for(bool supply:{false,true})for(bool groundFirst:{false,true})for(bool groundSecond:{false,true}){
  if(!first)std::cout<<',';first=false;
  Game game;Globe globe;AlienMission mission;Target base;base.setLongitude(0.2);base.setLatitude(0.1);
  mission._rule.objective=supply?OBJECTIVE_SUPPLY:OBJECTIVE_SCORE;mission._rule.wave.objective=supply;mission._base=supply?&base:nullptr;
  game.mod.trajectory.firstAltitude=groundFirst?0:-1;game.mod.trajectory.secondAltitude=groundSecond?0:-1;
  for(int i=0;i<7;i++){double lon=Deg2Rad(i==4?10:i==5?8:8.01),lat=Deg2Rad(i==4?8:2);game.mod.region._missionZones.push_back({{{lon,lon,lat,lat}}});}
  RNG::calls=0;auto* ufo=mission.spawnUfo(game.save,game.mod,globe,mission._rule.wave,game.mod.trajectory);
  std::cout<<'['<<supply<<','<<groundFirst<<','<<groundSecond<<','<<ufo->_status<<','<<ufo->_secondsRemaining<<','<<ufo->_speed
   <<",\""<<number(ufo->_lon)<<"\",\""<<number(ufo->_lat)<<"\",\""<<number(ufo->_dest->_lon)<<"\",\""<<number(ufo->_dest->_lat)<<"\","<<RNG::calls<<']';delete ufo;
 }
}

void pointDestinationRows(){
 bool first=true;
 for(int selected:{0,1}){
  if(!first)std::cout<<',';first=false;
  Game game;Globe globe;AlienMission mission;
  for(int i=0;i<7;i++){double lon=Deg2Rad(i==4?10:i==5?8:8.01),lat=Deg2Rad(i==4?8:2);game.mod.region._missionZones.push_back({{{lon,lon,lat,lat}}});}
  game.mod.region._missionZones[5].areas.push_back({Deg2Rad(12),Deg2Rad(12),Deg2Rad(-2),Deg2Rad(-2)});
  RNG::calls=0;RNG::intValue=selected;
  auto* ufo=mission.spawnUfo(game.save,game.mod,globe,mission._rule.wave,game.mod.trajectory);
  std::cout<<'['<<selected<<",\""<<number(ufo->_dest->_lon)<<"\",\""<<number(ufo->_dest->_lat)<<"\","<<RNG::calls<<']';delete ufo;
 }
 RNG::intValue=0;
}

void pointOriginRows(){
 bool first=true;
 for(int selected:{0,1}){
  if(!first)std::cout<<',';first=false;
  Game game;Globe globe;AlienMission mission;
  for(int i=0;i<7;i++){double lon=Deg2Rad(i==4?10:i==5?8:8.01),lat=Deg2Rad(i==4?8:2);game.mod.region._missionZones.push_back({{{lon,lon,lat,lat}}});}
  game.mod.region._missionZones[4].areas[0]={Deg2Rad(8),Deg2Rad(8),Deg2Rad(2),Deg2Rad(2)};
  game.mod.region._missionZones[4].areas.push_back({Deg2Rad(12),Deg2Rad(12),Deg2Rad(-2),Deg2Rad(-2)});
  RNG::calls=0;RNG::intValue=selected;
  auto* ufo=mission.spawnUfo(game.save,game.mod,globe,mission._rule.wave,game.mod.trajectory);
  std::cout<<'['<<selected<<",\""<<number(ufo->_lon)<<"\",\""<<number(ufo->_lat)<<"\","<<RNG::calls<<']';delete ufo;
 }
 RNG::intValue=0;
}

void rectangleDestinationRows(){
 bool first=true;
 for(bool reversed:{false,true})for(double fraction:{0.0,0.25,1.0}){
  if(!first)std::cout<<',';first=false;
  Game game;Globe globe;AlienMission mission;
  for(int i=0;i<7;i++){double lon=Deg2Rad(i==4?10:i==5?8:8.01),lat=Deg2Rad(i==4?8:2);game.mod.region._missionZones.push_back({{{lon,lon,lat,lat}}});}
  game.mod.trajectory.thirdAltitude=4;
  game.mod.region._missionZones[5].areas[0]={Deg2Rad(reversed?388:22),Deg2Rad(reversed?382:28),Deg2Rad(reversed?8:-8),Deg2Rad(reversed?-8:8)};
  RNG::calls=0;RNG::fraction=fraction;
  auto* ufo=mission.spawnUfo(game.save,game.mod,globe,mission._rule.wave,game.mod.trajectory);
  std::cout<<'['<<reversed<<','<<fraction<<",\""<<number(ufo->_dest->_lon)<<"\",\""<<number(ufo->_dest->_lat)<<"\","<<RNG::calls<<']';delete ufo;
 }
 RNG::fraction=0.0;
}

void selectedSiteRows(){
 bool first=true;
 for(bool landing:{false,true})for(int area:{0,1}){
  if(!first)std::cout<<',';first=false;
  Game game;Globe globe;AlienMission mission;
  for(int i=0;i<7;i++){double lon=Deg2Rad(i==4?10:i==5?8:8.01),lat=Deg2Rad(i==4?8:2);game.mod.region._missionZones.push_back({{{lon,lon,lat,lat}}});}
  game.mod.region._missionZones[5].areas.push_back({Deg2Rad(12),Deg2Rad(12),Deg2Rad(-2),Deg2Rad(-2)});
  mission._rule.spawnZone=5;mission._missionSiteZoneArea=area;mission._rule.wave.objective=true;mission._rule.wave.objectiveOnTheLandingSite=landing;
  RNG::calls=0;RNG::intValue=0;
  auto* ufo=mission.spawnUfo(game.save,game.mod,globe,mission._rule.wave,game.mod.trajectory);
  std::cout<<'['<<landing<<','<<area<<",\""<<number(ufo->_dest->_lon)<<"\",\""<<number(ufo->_dest->_lat)<<"\","<<RNG::calls<<']';delete ufo;
 }
}

void earthRectangleRows(){
 bool first=true;
 for(int operation=1;operation<=6;operation++)for(bool groundNext:{false,true}){
  if(!first)std::cout<<',';first=false;
  Game game;Globe globe;AlienMission mission;Target base;
  base.setLongitude(0.2);base.setLatitude(0.1);mission._base=&base;mission._rule.operation=operation;
  game.mod.ufo.fakeChance=0;game.mod.trajectory.secondAltitude=groundNext?0:-1;
  auto& region=game.mod.region;region._lonMin={0};region._lonMax={1};region._latMin={-1};region._latMax={1};
  for(int i=0;i<7;i++){double lon=Deg2Rad(i==4?10:i==5?8:8.01),lat=Deg2Rad(i==4?8:2);region._missionZones.push_back({{{lon,lon,lat,lat}}});}
  region._missionZones[4].areas[0]={Deg2Rad(8),Deg2Rad(12),Deg2Rad(1),Deg2Rad(3)};
  RNG::calls=0;auto* ufo=mission.spawnUfo(game.save,game.mod,globe,mission._rule.wave,game.mod.trajectory);
  std::cout<<'['<<operation<<','<<groundNext<<",\""<<number(ufo->_lon)<<"\",\""<<number(ufo->_lat)<<"\",\""<<number(ufo->_dest->_lon)<<"\",\""<<number(ufo->_dest->_lat)<<"\","<<RNG::calls<<']';delete ufo;
 }
}

void rectangleOriginRows(){
 bool first=true;
 for(bool reversed:{false,true})for(double fraction:{0.0,0.25,1.0}){
  if(!first)std::cout<<',';first=false;
  Game game;Globe globe;AlienMission mission;
  auto& region=game.mod.region;region._lonMin={0};region._lonMax={1};region._latMin={-1};region._latMax={1};
  for(int i=0;i<7;i++){double lon=Deg2Rad(i==4?10:i==5?8:8.01),lat=Deg2Rad(i==4?8:2);region._missionZones.push_back({{{lon,lon,lat,lat}}});}
  region._missionZones[4].areas[0]={Deg2Rad(reversed?376:12),Deg2Rad(reversed?372:16),Deg2Rad(reversed?3:1),Deg2Rad(reversed?1:3)};
  RNG::calls=0;RNG::fraction=fraction;
  auto* ufo=mission.spawnUfo(game.save,game.mod,globe,mission._rule.wave,game.mod.trajectory);
  std::cout<<'['<<reversed<<','<<fraction<<",\""<<number(ufo->_lon)<<"\",\""<<number(ufo->_lat)<<"\",\""<<number(ufo->_dest->_lon)<<"\",\""<<number(ufo->_dest->_lat)<<"\","<<RNG::calls<<']';delete ufo;
 }
 RNG::fraction=0.0;
}

void landRectangleOriginRows(){
 bool first=true;
 for(int mode=0;mode<3;mode++)for(double fraction:{0.0,0.25}){
  if(!first)std::cout<<',';first=false;
  Game game;Globe globe;AlienMission mission;
  auto& region=game.mod.region;region._lonMin={0};region._lonMax={mode==2?0.1:1};region._latMin={-1};region._latMax={1};
  for(int i=0;i<7;i++){double lon=Deg2Rad(i==4?10:i==5?8:8.01),lat=Deg2Rad(i==4?8:2);region._missionZones.push_back({{{lon,lon,lat,lat}}});}
  region._missionZones[4].areas[0]={Deg2Rad(12),Deg2Rad(16),Deg2Rad(1),Deg2Rad(3)};
  game.mod.trajectory.secondAltitude=0;game.mod.ufo.fakeChance=mode==1?100:0;
  RNG::calls=0;RNG::fraction=fraction;
  auto* ufo=mission.spawnUfo(game.save,game.mod,globe,mission._rule.wave,game.mod.trajectory);
  std::cout<<'['<<mode<<','<<fraction<<",\""<<number(ufo->_lon)<<"\",\""<<number(ufo->_lat)<<"\",\""<<number(ufo->_dest->_lon)<<"\",\""<<number(ufo->_dest->_lat)<<"\","<<RNG::calls<<']';delete ufo;
 }
 RNG::fraction=0.0;
}

void selectedRectangleOriginRows(){
 bool first=true;
 for(bool landing:{false,true})for(int fake:{0,100})for(double fraction:{0.0,0.25}){
  if(!first)std::cout<<',';first=false;
  Game game;Globe globe;AlienMission mission;
  auto& region=game.mod.region;region._lonMin={0};region._lonMax={0.1};region._latMin={-1};region._latMax={1};
  for(int i=0;i<7;i++){double lon=Deg2Rad(i==4?10:i==5?8:8.01),lat=Deg2Rad(i==4?8:2);region._missionZones.push_back({{{lon,lon,lat,lat}}});}
  region._missionZones[4].areas.push_back({Deg2Rad(12),Deg2Rad(16),Deg2Rad(1),Deg2Rad(3)});
  mission._rule.spawnZone=4;mission._missionSiteZoneArea=1;mission._rule.wave.objective=true;mission._rule.wave.objectiveOnTheLandingSite=landing;
  game.mod.ufo.fakeChance=fake;
  RNG::calls=0;RNG::fraction=fraction;
  auto* ufo=mission.spawnUfo(game.save,game.mod,globe,mission._rule.wave,game.mod.trajectory);
  std::cout<<'['<<landing<<','<<fake<<','<<fraction<<",\""<<number(ufo->_lon)<<"\",\""<<number(ufo->_lat)<<"\",\""<<number(ufo->_dest->_lon)<<"\",\""<<number(ufo->_dest->_lat)<<"\","<<RNG::calls<<']';delete ufo;
 }
 RNG::fraction=0.0;
}

void aggressiveRows(){
 bool first=true;
 for(bool enabled:{false,true})for(bool inside:{false,true}){
  if(!first)std::cout<<',';first=false;
  Game game;Globe globe;AlienMission mission;mission._rule.objective=OBJECTIVE_RETALIATION;mission._nextWave=1;
  Target outsideBase,insideBase;outsideBase.setLongitude(2);outsideBase.setLatitude(0.1);insideBase.setLongitude(0.2);insideBase.setLatitude(0.1);
  game.save.bases={&outsideBase};if(inside)game.save.bases.push_back(&insideBase);
  auto& region=game.mod.region;region._lonMin={0};region._lonMax={1};region._latMin={-1};region._latMax={1};
  for(int i=0;i<7;i++){double lon=Deg2Rad(i==4?10:i==5?8:8.01),lat=Deg2Rad(i==4?8:2);region._missionZones.push_back({{{lon,lon,lat,lat}}});}
  region._missionZones[6].areas[0]={Deg2Rad(22),Deg2Rad(28),Deg2Rad(-8),Deg2Rad(8)};
  Ufo ufo(&game.mod.ufo,9);ufo.setMissionInfo(&mission,&game.mod.trajectory);ufo.point=2;
  ufo.setLongitude(Deg2Rad(8));ufo.setLatitude(Deg2Rad(2));
  auto* destination=new Waypoint;destination->setLongitude(ufo._lon);destination->setLatitude(ufo._lat);ufo.setDestination(destination);
  RNG::calls=0;Options::aggressiveRetaliation=enabled;mission.ufoReachedWaypoint(ufo,game,globe);
  std::cout<<'['<<enabled<<','<<inside<<",\""<<number(ufo._dest->_lon)<<"\",\""<<number(ufo._dest->_lat)<<"\","<<RNG::calls<<']';
 }
 Options::aggressiveRetaliation=false;
}

}
int main(){using namespace OpenXcom;std::cout<<"{\"cases\":[";run("ordinary",2,false,false);std::cout<<',';run("long",360,false,false);std::cout<<',';run("interrupted",2,true,false);std::cout<<',';run("multiple",2,false,true);std::cout<<"],\"siteLiftRows\":[";siteLiftRows();std::cout<<"],\"transitionRows\":[";transitionRows();std::cout<<"],\"normalLiftRows\":[";normalLiftRows();std::cout<<"],\"spawnFlagRows\":[";spawnFlagRows();std::cout<<"],\"spawnPathRows\":[";spawnPathRows();std::cout<<"],\"pointDestinationRows\":[";pointDestinationRows();std::cout<<"],\"pointOriginRows\":[";pointOriginRows();std::cout<<"],\"rectangleDestinationRows\":[";rectangleDestinationRows();std::cout<<"],\"selectedSiteRows\":[";selectedSiteRows();std::cout<<"],\"earthRectangleRows\":[";earthRectangleRows();std::cout<<"],\"aggressiveRows\":[";aggressiveRows();std::cout<<"],\"rectangleOriginRows\":[";rectangleOriginRows();std::cout<<"],\"landRectangleOriginRows\":[";landRectangleOriginRows();std::cout<<"],\"selectedRectangleOriginRows\":[";selectedRectangleOriginRows();std::cout<<"]}\n";}

// AlienMission site creation and direct-wave selection, with asset-free collaborators.
// The capture inserts reference statements verbatim and omits diagnostic logging only.
#include <cmath>
#include <iomanip>
#include <iostream>
#include <map>
#include <stdexcept>
#include <string>
#include <vector>
namespace OpenXcom {
using Exception = std::runtime_error;
namespace RNG {
int calls = 0;
double fraction = 0.25;
int generate(int minimum, int maximum) { ++calls; return minimum; }
double generate(double minimum, double maximum) { ++calls; return minimum + fraction * (maximum - minimum); }
}
struct MissionArea { double lonMin, lonMax, latMin, latMax; int texture; std::string name; };
struct MissionZone { std::vector<MissionArea> areas; };
struct RuleRegion {
std::vector<MissionZone> zones; const auto& getMissionZones() const { return zones; }
bool insideRegion(double lon, double lat) const { return lon >= 0 && lon < 1 && lat >= -0.3 && lat < 0.3; }
};
constexpr int OBJECTIVE_RETALIATION = 2;
struct RuleAlienMission {
std::string siteType; int spawnZone = 0; bool respawn = false;
const std::string& getSiteType() const { return siteType; }
std::string getType() const { return "DIRECT"; }
int getSpawnZone() const { return spawnZone; }
int getObjective() const { return 0; }
bool isMultiUfoRetaliation() const { return false; }
bool respawnUfoAfterSiteDespawn() const { return respawn; }
};
struct AlienDeployment {
std::string type, marker; int minimum, maximum;
const std::string& getMarkerName() const { return marker; }
int getDurationMin() const { return minimum; }
int getDurationMax() const { return maximum; }
};
struct Texture {
std::map<std::string, int> _deployments;
std::string getRandomDeployment() const;
};
// TEXTURE_SELECTION
struct Globe { std::map<int, Texture> textures; Texture* getTexture(int id) { auto it = textures.find(id); return it == textures.end() ? nullptr : &it->second; } };
struct Mod {
Globe globe; RuleRegion region; std::map<std::string, AlienDeployment> deployments;
Globe* getGlobe() const { return const_cast<Globe*>(&globe); }
AlienDeployment* getDeployment(const std::string& id, bool required = false) const {
if (id.empty() || id == std::string(1, '\0')) return nullptr;
auto it = deployments.find(id); if (it != deployments.end()) return const_cast<AlienDeployment*>(&it->second);
if (required) throw Exception("missing deployment"); return nullptr;
}
RuleRegion* getRegion(const std::string&, bool) const { return const_cast<RuleRegion*>(&region); }
};
struct Base {
double lon, lat; double getLongitude() const { return lon; } double getLatitude() const { return lat; }
bool getRetaliationTarget() const { return false; }
};
struct MissionSite;
struct Craft { int units = 0; MissionSite* destination = nullptr; bool returned = false;
int getNumTotalUnits() const { return units; } void setDestination(MissionSite* site) { destination = site; } void returnToBase() { returned = true; } };
struct Ufo {
enum Status { FLYING, IGNORE_ME, DESTROYED }; Status status = FLYING;
struct Stats { std::string missionCustomDeploy; }; Stats stats;
Stats getCraftStats() const { return stats; }
double getLongitude() const { return 0.25; } double getLatitude() const { return 0.15; }
void setStatus(Status value) { status = value; } Status getStatus() const { return status; }
std::vector<Craft*> followers;
std::vector<Craft*> getCraftFollowers() const { return followers; }
};
struct UfoTrajectory { int getZone(int) const { return 0; } };
struct MissionWave { std::string ufoType; bool objectiveOnXcomBase = false; bool objectiveOnTheLandingSite = false; };
struct MissionSite {
const AlienDeployment* deployment; const AlienDeployment* custom; Ufo* ufo = nullptr;
double lon = 0, lat = 0; int id = 0, seconds = 0, texture = -1; std::string race, city;
MissionSite(const RuleAlienMission*, const AlienDeployment* d, const AlienDeployment* c) : deployment(d), custom(c) {}
void setUfo(Ufo* value) { ufo = value; }
void setLongitude(double value) { lon = value; }
void setLatitude(double value) { lat = value; }
void setId(int value) { id = value; }
void setSecondsRemaining(int value) { seconds = value; }
void setAlienRace(const std::string& value) { race = value; }
void setTexture(int value) { texture = value; }
void setCity(const std::string& value) { city = value; }
};
struct SavedGame {
std::vector<MissionSite*> sites; std::vector<Base*> bases; int scoreCalls = 0;
std::map<std::string, int> _ids = {{"DIRECT_MARKER",7},{"RULE_MARKER",7}};
int getId(const std::string&);
void increaseCustomCounter(const std::string&);
void decreaseCustomCounter(const std::string&);
auto* getMissionSites() { return &sites; }
auto* getBases() { return &bases; }
};
// ID_AND_COUNTER_METHODS
struct AlienMission {
RuleAlienMission _rule; std::string _race = "RACE_A", _region = "REGION"; int _missionSiteZoneArea = -1;
MissionSite* spawnMissionSite(SavedGame&, const Mod&, const MissionArea&, const Ufo*, AlienDeployment* = nullptr);
Base* selectXcomBase(SavedGame&, const RuleRegion&);
void addScore(double, double, SavedGame& game) { ++game.scoreCalls; }
void convert(SavedGame& game, const Mod& mod, Ufo& ufo, bool landing) {
const RuleRegion& regionRules = mod.region; const UfoTrajectory trajectory;
const size_t curWaypoint = 0; MissionWave wave; wave.objectiveOnTheLandingSite = landing;
// CONVERSION_BODY
}
void direct(SavedGame& game, const Mod& mod, const MissionWave& wave, const UfoTrajectory& trajectory) {
// DIRECT_SELECTION
}
};
// SPAWN_METHOD
// BASE_SELECTION
}
int main() {
using namespace OpenXcom;
std::cout << std::fixed << std::setprecision(9) << "{\"cases\":[";
bool first = true;
for (const std::string name : {"override", "rule", "terrain", "single-zero", "all-zero", "selected", "base", "multiple-bases", "missing-base", "empty-terrain", "null-choice", "zero-duration", "missing-texture"}) {
if (!first) std::cout << ','; first = false;
Mod mod; mod.deployments.emplace("DIRECT_DEPLOY", AlienDeployment{"DIRECT_DEPLOY", "DIRECT_MARKER", 2, 3});
mod.deployments.emplace("RULE_DEPLOY", AlienDeployment{"RULE_DEPLOY", "RULE_MARKER", 3, 4});
mod.globe.textures[10]._deployments = {{"DIRECT_DEPLOY", 3}, {"RULE_DEPLOY", 1}};
mod.region.zones = {{{{0.1, 0.3, -0.2, 0.2, 10, "CITY"}, {0.5, 0.5, 0.1, 0.1, 10, "CITY_SELECTED"}}}};
AlienMission mission; MissionWave wave; UfoTrajectory trajectory; SavedGame game; Base base{0.2,0.1}, secondBase{0.4,0.1};
if (name == "override" || name == "selected" || name == "base" || name == "multiple-bases" || name == "missing-base") wave.ufoType = "DIRECT_DEPLOY";
if (name == "override" || name == "rule") mission._rule.siteType = "RULE_DEPLOY";
if (name == "selected") mission._missionSiteZoneArea = 1;
if (name == "base" || name == "multiple-bases" || name == "missing-base") {
wave.objectiveOnXcomBase = true;
if (name != "missing-base") game.bases.push_back(&base);
if (name == "multiple-bases") game.bases.push_back(&secondBase);
}
if (name == "single-zero") mod.globe.textures[10]._deployments = {{"DIRECT_DEPLOY", 0}};
if (name == "all-zero") mod.globe.textures[10]._deployments = {{"DIRECT_DEPLOY", 0}, {"RULE_DEPLOY", 0}};
if (name == "empty-terrain") mod.globe.textures[10]._deployments.clear();
if (name == "null-choice") mod.globe.textures[10]._deployments = {{"", 1}, {"DIRECT_DEPLOY", 1}};
if (name == "zero-duration") {
wave.ufoType = "DIRECT_DEPLOY";
mod.deployments["DIRECT_DEPLOY"].minimum = 0; mod.deployments["DIRECT_DEPLOY"].maximum = 0;
}
if (name == "missing-texture") mod.globe.textures.clear();
RNG::calls = 0; bool error = false; try { mission.direct(game, mod, wave, trajectory); } catch (const Exception&) { error = true; }
std::cout << "{\"name\":\"" << name << "\",\"error\":" << (error ? "true" : "false") << ",\"calls\":" << RNG::calls << ",\"site\":[";
if (!game.sites.empty()) { auto* site = game.sites.front(); std::cout << '\"' << site->deployment->type << "\",\"" << site->lon << "\",\"" << site->lat << "\"," << site->id << ',' << site->seconds << ",\"" << site->race << "\"," << site->texture << ",\"" << site->city << '\"'; }
std::cout << "]}"; for (auto* site : game.sites) delete site;
}
std::cout << "],\"conversionRows\":[";
first = true;
for (bool landing : {false, true}) for (bool respawn : {false, true}) for (bool custom : {false, true}) {
if (!first) std::cout << ','; first = false;
Mod mod; mod.deployments.emplace("DIRECT_DEPLOY", AlienDeployment{"DIRECT_DEPLOY", "DIRECT_MARKER", 2, 3});
mod.deployments.emplace("CUSTOM_DEPLOY", AlienDeployment{"CUSTOM_DEPLOY", "CUSTOM_MARKER", 5, 6});
mod.region.zones = {{{{0.1, 0.3, -0.2, 0.2, 10, "CITY"}}}};
AlienMission mission; mission._rule.siteType = "DIRECT_DEPLOY"; mission._rule.respawn = respawn;
mission._missionSiteZoneArea = 0; Ufo ufo; if (custom) ufo.stats.missionCustomDeploy = "CUSTOM_DEPLOY";
SavedGame game; RNG::calls = 0; mission.convert(game, mod, ufo, landing);
auto* site = game.sites.at(0);
std::cout << "[" << (landing ? "true" : "false") << ',' << (respawn ? "true" : "false") << ',' << (custom ? "true" : "false")
<< ",\"" << site->lon << "\",\"" << site->lat << "\"," << site->seconds << ',' << RNG::calls << ',' << game.scoreCalls
<< ",\"" << (site->custom ? site->custom->type : "") << "\"," << (site->ufo == &ufo ? "true" : "false")
<< ",\"" << (ufo.status == Ufo::IGNORE_ME ? "ignore" : "destroyed") << "\"]";
delete site;
}
std::cout << "],\"suppressedConversionRows\":[";
first = true;
for (bool respawn : {false, true}) {
if (!first) std::cout << ','; first = false;
Mod mod; mod.globe.textures[10]._deployments.clear();
mod.region.zones = {{{{0.1, 0.3, -0.2, 0.2, 10, "CITY"}}}};
AlienMission mission; mission._rule.respawn = respawn; mission._missionSiteZoneArea = 0;
Ufo ufo; SavedGame game; RNG::calls = 0; mission.convert(game, mod, ufo, false);
std::cout << "[" << (respawn ? "true" : "false") << ",\"" << (ufo.status == Ufo::DESTROYED ? "destroyed" : "ignore")
<< "\"," << game.sites.size() << ',' << game.scoreCalls << ',' << RNG::calls << ']';
}
std::cout << "],\"counterConversionRows\":[";
first = true;
for (bool birth : {false,true}) for (bool decrease : {false,true}) {
if (!first) std::cout << ','; first = false;
Mod mod; mod.deployments.emplace("DIRECT_DEPLOY", AlienDeployment{"DIRECT_DEPLOY","DIRECT_MARKER",2,3});
mod.region.zones = {{{{0.1,0.3,-0.2,0.2,10,"CITY"}}}};
SavedGame game; int directId = birth ? game.getId("DIRECT_MARKER") : 0;
game.increaseCustomCounter("DIRECT_MARKER");
if (decrease) game.decreaseCustomCounter("DIRECT_MARKER");
AlienMission mission; mission._rule.siteType = "DIRECT_DEPLOY"; mission._missionSiteZoneArea = 0;
Ufo ufo; mission.convert(game, mod, ufo, false);
std::cout << '[' << (birth ? "true" : "false") << ',' << (decrease ? "true" : "false") << ',' << directId
<< ',' << game.sites.at(0)->id << ',' << game._ids.at("DIRECT_MARKER") << ']';
for (auto* site : game.sites) delete site;
}
std::cout << "],\"followerConversionRows\":[";
first = true;
for (bool respawn : {false,true}) for (int units : {0,1,2}) {
if (!first) std::cout << ','; first = false;
Mod mod; mod.deployments.emplace("DIRECT_DEPLOY", AlienDeployment{"DIRECT_DEPLOY","DIRECT_MARKER",2,3});
mod.region.zones = {{{{0.1,0.3,-0.2,0.2,10,"CITY"}}}};
AlienMission mission; mission._rule.siteType = "DIRECT_DEPLOY"; mission._rule.respawn = respawn; mission._missionSiteZoneArea = 0;
SavedGame game; Ufo ufo; Craft craft; craft.units = units; ufo.followers.push_back(&craft);
mission.convert(game,mod,ufo,false);
std::cout << '[' << (respawn ? "true" : "false") << ',' << units << ',' << (craft.destination == game.sites.at(0) ? "true" : "false")
<< ',' << (craft.returned ? "true" : "false") << ']';
for (auto* site : game.sites) delete site;
}
std::cout << "]}\n";
}

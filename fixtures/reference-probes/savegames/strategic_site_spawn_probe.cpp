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
struct RuleRegion { std::vector<MissionZone> zones; const auto& getMissionZones() const { return zones; } };
struct RuleAlienMission {
std::string siteType; int spawnZone = 0;
const std::string& getSiteType() const { return siteType; }
std::string getType() const { return "DIRECT"; }
int getSpawnZone() const { return spawnZone; }
};
struct AlienDeployment {
std::string type, marker; int minimum, maximum;
const std::string& getMarkerName() const { return marker; }
int getDurationMin() const { return minimum; }
int getDurationMax() const { return maximum; }
};
struct Texture {
std::map<std::string, int> deployments;
std::string getRandomDeployment() const {
int total = 0; for (const auto& entry : deployments) total += entry.second;
if (!total) return "";
int roll = RNG::generate(1, total);
for (const auto& entry : deployments) { if (roll <= entry.second) return entry.first; roll -= entry.second; }
throw Exception("invalid roll");
}
};
struct Globe { std::map<int, Texture> textures; Texture* getTexture(int id) { auto it = textures.find(id); return it == textures.end() ? nullptr : &it->second; } };
struct Mod {
Globe globe; RuleRegion region; std::map<std::string, AlienDeployment> deployments;
Globe* getGlobe() const { return const_cast<Globe*>(&globe); }
AlienDeployment* getDeployment(const std::string& id, bool required = false) const {
auto it = deployments.find(id); if (it != deployments.end()) return const_cast<AlienDeployment*>(&it->second);
if (required) throw Exception("missing deployment"); return nullptr;
}
RuleRegion* getRegion(const std::string&, bool) const { return const_cast<RuleRegion*>(&region); }
};
struct Base { double lon, lat; double getLongitude() const { return lon; } double getLatitude() const { return lat; } };
struct Ufo { struct Stats { std::string missionCustomDeploy; }; Stats getCraftStats() const { return {}; } };
struct UfoTrajectory { int getZone(int) const { return 0; } };
struct MissionWave { std::string ufoType; bool objectiveOnXcomBase = false; };
struct MissionSite {
const AlienDeployment* deployment; double lon = 0, lat = 0; int id = 0, seconds = 0, texture = -1; std::string race, city;
MissionSite(const RuleAlienMission*, const AlienDeployment* d, const AlienDeployment*) : deployment(d) {}
void setLongitude(double value) { lon = value; }
void setLatitude(double value) { lat = value; }
void setId(int value) { id = value; }
void setSecondsRemaining(int value) { seconds = value; }
void setAlienRace(const std::string& value) { race = value; }
void setTexture(int value) { texture = value; }
void setCity(const std::string& value) { city = value; }
};
struct SavedGame {
std::vector<MissionSite*> sites; Base* base = nullptr;
int getId(const std::string&) { return 7; }
auto* getMissionSites() { return &sites; }
};
struct AlienMission {
RuleAlienMission _rule; std::string _race = "RACE_A", _region = "REGION"; int _missionSiteZoneArea = -1;
MissionSite* spawnMissionSite(SavedGame&, const Mod&, const MissionArea&, const Ufo*, AlienDeployment* = nullptr);
Base* selectXcomBase(SavedGame& game, const RuleRegion&) { return game.base; }
void direct(SavedGame& game, const Mod& mod, const MissionWave& wave, const UfoTrajectory& trajectory) {
// DIRECT_SELECTION
}
};
// SPAWN_METHOD
}
int main() {
using namespace OpenXcom;
std::cout << std::fixed << std::setprecision(9) << "{\"cases\":[";
bool first = true;
for (const std::string name : {"override", "rule", "terrain", "selected", "base", "missing-base", "empty-terrain", "missing-texture"}) {
if (!first) std::cout << ','; first = false;
Mod mod; mod.deployments.emplace("DIRECT_DEPLOY", AlienDeployment{"DIRECT_DEPLOY", "DIRECT_MARKER", 2, 3});
mod.deployments.emplace("RULE_DEPLOY", AlienDeployment{"RULE_DEPLOY", "RULE_MARKER", 3, 4});
mod.globe.textures[10].deployments = {{"DIRECT_DEPLOY", 3}, {"RULE_DEPLOY", 1}};
mod.region.zones = {{{{0.1, 0.3, -0.2, 0.2, 10, "CITY"}, {0.5, 0.5, 0.1, 0.1, 10, "CITY_SELECTED"}}}};
AlienMission mission; MissionWave wave; UfoTrajectory trajectory; SavedGame game; Base base{0.2,0.1};
if (name == "override" || name == "selected" || name == "base" || name == "missing-base") wave.ufoType = "DIRECT_DEPLOY";
if (name == "override" || name == "rule") mission._rule.siteType = "RULE_DEPLOY";
if (name == "selected") mission._missionSiteZoneArea = 1;
if (name == "base" || name == "missing-base") { wave.objectiveOnXcomBase = true; if (name == "base") game.base = &base; }
if (name == "empty-terrain") mod.globe.textures[10].deployments.clear();
if (name == "missing-texture") mod.globe.textures.clear();
RNG::calls = 0; bool error = false; try { mission.direct(game, mod, wave, trajectory); } catch (const Exception&) { error = true; }
std::cout << "{\"name\":\"" << name << "\",\"error\":" << (error ? "true" : "false") << ",\"calls\":" << RNG::calls << ",\"site\":[";
if (!game.sites.empty()) { auto* site = game.sites.front(); std::cout << '\"' << site->deployment->type << "\",\"" << site->lon << "\",\"" << site->lat << "\"," << site->id << ',' << site->seconds << ",\"" << site->race << "\"," << site->texture << ",\"" << site->city << '\"'; }
std::cout << "]}"; for (auto* site : game.sites) delete site;
}
std::cout << "]}\n";
}

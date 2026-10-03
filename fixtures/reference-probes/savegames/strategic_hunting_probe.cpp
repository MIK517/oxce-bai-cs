// Asset-free target selection collaborators; inserted pinned methods remain verbatim.
#include <algorithm>
#include <climits>
#include <cmath>
#include <iostream>
#include <vector>
namespace OpenXcom {
double Nautical(double x) { return x / 60.0 * 3.14159265358979323846 / 180.0; }
struct Target { double distance = 0; virtual ~Target() = default; };
struct Craft : Target {
struct Rule { bool landing = false, hidden = false; bool getAllowLanding() const { return landing; } bool isUndetectable() const { return hidden; } } rule;
const Rule* _rules = &rule;
struct Stats { int speedMax = 2000, damageMax = 100; } _stats;
int _damage = 0, units = 0, id = 0; bool ignored = false;
int getMaxUnitsClamped() const { return units; }
bool isIgnoredByHK() const { return ignored; }
const Rule* getRules() const { return _rules; }
int getHunterKillerAttraction(int huntMode) const;
};
// ATTRACTION
struct Ufo {
struct Stats { int radarRange = 300; } _stats;
Craft* original = nullptr; int mode = 0; bool hunting = false;
bool isHunting() const { return hunting; }
Craft* getTargetedXcomCraft() const { return original; }
int getHuntMode() const { return mode; }
double getDistance(Target* target) const { return target->distance; }
bool insideRadarRange(Target* target) const;
};
// RADAR
Craft* select(Ufo* ufo, const std::vector<Craft*>& crafts) {
const auto* activeCrafts = &crafts;
// SELECTION
return newTarget;
}
}
int main() {
using namespace OpenXcom;
std::cout << "{\"attractionRows\":[";
bool first = true;
for (int mode : {0,1}) for (bool landing : {false,true}) for (int capacity : {0,2,8}) for (int speed : {2000,6000}) for (int damage : {0,50}) {
if (!first) std::cout << ','; first = false;
Craft craft; craft.rule.landing = landing; craft.units = capacity; craft._stats.speedMax = speed; craft._damage = damage;
std::cout << '[' << mode << ',' << (landing ? "true" : "false") << ',' << capacity << ',' << speed << ',' << damage
<< ',' << craft.getHunterKillerAttraction(mode) << ']';
}
std::cout << "],\"selectionRows\":[";
first = true;
for (int mode : {0,1}) for (int variant : {0,1,2,3,4,5,6}) {
if (!first) std::cout << ','; first = false;
Craft a,b; a.id = 1; b.id = 2; a._stats.speedMax = 2000; b._stats.speedMax = 6000;
a.distance = Nautical(100); b.distance = Nautical(100);
Ufo ufo; ufo.mode = mode; std::vector<Craft*> crafts{&a,&b};
if (variant == 1) { ufo.original = &a; ufo.hunting = true; b._stats.speedMax = a._stats.speedMax; }
if (variant == 2) { ufo.original = &a; ufo.hunting = true; a.rule.hidden = true; b.ignored = true; }
if (variant == 3) { a.ignored = true; b.rule.hidden = true; }
if (variant == 4) { a.distance = Nautical(301); b.distance = Nautical(300); }
if (variant == 5) { ufo._stats.radarRange = 0; }
if (variant == 6) { ufo.original = &a; ufo.hunting = true; a.ignored = true; }
auto* selected = select(&ufo,crafts);
std::cout << '[' << mode << ',' << variant << ',' << (selected ? selected->id : 0) << ']';
}
std::cout << "]}\n";
}

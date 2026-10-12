// DogfightState::update and the dogfight methods it calls, the gameplay parts of the DogfightState
// constructor, CraftWeaponProjectile, CraftWeapon::setAmmo/fire, the Craft pilot bonuses and the
// Ufo/Craft damage and shield setters are inserted verbatim by the capture script. Everything else
// is a minimal stub: drawing and sounds do nothing, RNG returns scripted raw values through the
// reference formula, and the globe answers land/fake-water per scenario.
#include <algorithm>
#include <cmath>
#include <cstdint>
#include <functional>
#include <iostream>
#include <sstream>
#include <string>
#include <utility>
#include <vector>

namespace OpenXcom {
using Uint8 = uint8_t;

// ---- RNG: scripted raw values, RandomState::generate formula ----------------------------------
namespace RNG {
std::vector<uint64_t> script; size_t next = 0; std::ostringstream calls; int count = 0;
int generate(int min, int max)
{
    uint64_t raw = next < script.size() ? script[next] : 0; ++next; ++count;
    int result = (int)(raw % (max - min + 1) + min);
    calls << (count > 1 ? "," : "") << '[' << min << ',' << max << ',' << result << ']';
    return result;
}
bool percent(int value) { return generate(0, 99) < value; }
}

namespace Options { bool dogfightAI = false; }

struct Exception { std::string what; explicit Exception(const std::string& w) : what(w) {} };

// ---- Text and presentation stubs ---------------------------------------------------------------
struct LocalizedText
{
    std::string text;
    LocalizedText arg(int value) const { return {text + ":" + std::to_string(value)}; }
    operator std::string() const { return text; }
};
struct Text { std::string value; void setText(const std::string& v) { value = v; } void setVisible(bool) {} };
struct Surface
{
    int getWidth() const { return 0; } int getHeight() const { return 0; }
    uint8_t getPixel(int, int) const { return 0; } void setPixel(int, int, uint8_t) {}
    void clear() {} void setVisible(bool) {} void drawRect(int, int, int, int, int) {} void offset(int) {}
};
using InteractiveSurface = Surface;
struct ImageButton { bool getVisible() const { return true; } };
struct Sound { void play() {} };
struct Element { bool TFTDMode = false; };
struct Interface { Element element; Element* getElement(const std::string&) { return &element; } };
enum ColorNames { CRAFT_MIN, CRAFT_MAX, RADAR_MIN, RADAR_MAX, DAMAGE_MIN, DAMAGE_MAX, BLOB_MIN, RANGE_METER, DISABLED_WEAPON, DISABLED_AMMO, DISABLED_RANGE, SHIELD_MIN, SHIELD_MAX };
const int STANDOFF_DIST = 560;
const int AGGRESSIVE_DIST = 64;
enum MissionObjective { OBJECTIVE_SCORE, OBJECTIVE_INFILTRATION, OBJECTIVE_BASE, OBJECTIVE_SITE, OBJECTIVE_RETALIATION };

// ---- CraftWeaponProjectile (header and source inserted verbatim) -------------------------------
} // namespace OpenXcom
// PROJECTILE_HEADER
// PROJECTILE_SOURCE
namespace OpenXcom {

// ---- Rules -------------------------------------------------------------------------------------
struct RuleCraftStats
{
    int fuelMax = 0, damageMax = 0, speedMax = 0, accel = 0;
    int hitBonus = 0, avoidBonus = 0, powerBonus = 0, armor = 0;
    int shieldCapacity = 0, shieldRecharge = 0, shieldRechargeInGeoscape = 0, shieldBleedThrough = 0;
};
template<class T> using FuncRef = std::function<T>;
enum ItemDamageType { DT_NONE, DT_AP, DT_IN, DT_HE, DT_LASER, DT_PLASMA, DT_STUN, DT_MELEE, DT_ACID, DT_SMOKE };
enum ItemDamageRandomType { DRT_DEFAULT = 0, DRT_UFO = 1, DRT_TFTD = 2, DRT_FLAT = 3, DRT_FIRE = 4, DRT_NONE = 5,
    DRT_UFO_WITH_TWO_DICE = 6, DRT_EASY = 7, DRT_STANDARD = 8, DRT_EXPLOSION = 9, DRT_UFO_WITH_FOUR_DICE = 10 };
struct RuleDamageType
{
    ItemDamageRandomType RandomType = DRT_DEFAULT; ItemDamageType ResistType = DT_NONE;
    int getRandomDamage(int power) const;
    int getRandomDamage(int power, FuncRef<int(int, int)> randFunc) const;
};
struct RuleItem { RuleDamageType type; const RuleDamageType* getDamageType() const { return &type; } };
struct RuleCraftWeapon
{
    int damage = 0, shieldDamageModifier = 100, range = 0, accuracy = 0, reloadCautious = 0, reloadStandard = 0,
        reloadAggressive = 0, ammoMax = 0, projectileSpeed = 0, tractorBeamPower = 0;
    CraftWeaponProjectileType projectileType = CWPT_CANNON_ROUND;
    int getDamage() const { return damage; } int getShieldDamageModifier() const { return shieldDamageModifier; }
    int getRange() const { return range; } int getAccuracy() const { return accuracy; }
    int getCautiousReload() const { return reloadCautious; } int getStandardReload() const { return reloadStandard; }
    int getAggressiveReload() const { return reloadAggressive; } int getAmmoMax() const { return ammoMax; }
    CraftWeaponProjectileType getProjectileType() const { return projectileType; }
    int getProjectileSpeed() const { return projectileSpeed; } int getTractorBeamPower() const { return tractorBeamPower; }
    bool unified = false; const RuleItem* clip = nullptr;
    int getSound() const { return 0; } bool unifiedDamageFormula() const { return unified; }
    const RuleItem* getClipItem() const { return clip; } const RuleItem* getLauncherItem() const { return nullptr; }
};
struct RuleCraft
{
    static const int WeaponMax = 4;
    int weapons = 0, missile = 0; bool allowLanding = true;
    int getWeapons() const { return weapons; } bool isMissile() const { return missile > 0; }
    int missilePower() const { return missile; } bool getAllowLanding() const { return allowLanding; }
};
struct RuleUfo
{
    int weaponRange = 0, weaponPower = 0, weaponReload = 0, radius = 0, score = 0, breakOffTime = 0,
        softlockThreshold = 100, splashdownSurvivalChance = 100, blobSize = 2; bool unmanned = false;
    int getWeaponRange() const { return weaponRange; } int getWeaponPower() const { return weaponPower; }
    int getWeaponReload() const { return weaponReload; } int getRadius() const { return radius; }
    int getScore() const { return score; } int getBreakOffTime() const { return breakOffTime; }
    int getSoftlockThreshold() const { return softlockThreshold; }
    int getSplashdownSurvivalChance() const { return splashdownSurvivalChance; }
    bool isUnmanned() const { return unmanned; } int getBlobSize() const { return blobSize; } int getFireSound() const { return -1; }
};
struct UfoTrajectory { static const std::string RETALIATION_ASSAULT_RUN; };
const std::string UfoTrajectory::RETALIATION_ASSAULT_RUN = "__RETALIATION_ASSAULT_RUN";
struct MissionWave { int interruptPercentage = 0; int spawnTimer = 0; };
struct RuleAlienMission
{
    int retaliationOdds = -1; MissionWave wave;
    int getRetaliationOdds() const { return retaliationOdds; } const std::string& getSpawnUfo() const { static std::string s; return s; }
    int getWaveCount() const { return 1; } const MissionWave& getWave(int) const { return wave; }
};
struct WeightedOptions { std::string choice; std::string choose() const { return choice; } };
struct AlienRace
{
    int aggression = 0; WeightedOptions weights; bool hasWeights = false;
    int getRetaliationAggression() const { return aggression; }
    const WeightedOptions* retaliationMissionWeights(int) const { return hasWeights ? &weights : nullptr; }
};
struct Globe; struct Game;
struct AlienMission
{
    const RuleAlienMission& rules; std::string region, race; int id = 0; bool interrupted = false; int shotDown = 0; bool started = false;
    explicit AlienMission(const RuleAlienMission& r) : rules(r) {}
    const RuleAlienMission& getRules() const { return rules; }
    const std::string& getRegion() const { return region; }
    void ufoShotDown(class Ufo&) { ++shotDown; }
    void setInterrupted(bool i) { interrupted = i; }
    void setId(int i) { id = i; } void setRegion(const std::string& r, const struct Mod&) { region = r; }
    void setRace(const std::string& r) { race = r; } void start(Game&, Globe&, int) { started = true; }
};

struct Mod
{
    static int DIFFICULTY_BASED_RETAL_DELAY[5];
    static int DAMAGE_RANGE, EXPLOSIVE_DAMAGE_RANGE, FIRE_DAMAGE_RANGE[2];
    static const int NO_SOUND = -1;
    std::vector<int> startSounds;
    const std::vector<int>& getStartDogfightSounds() const { return startSounds; }
    static const int UFO_FIRE = 8, UFO_HIT = 12, UFO_CRASH = 10, UFO_EXPLODE = 11, INTERCEPTOR_HIT = 10, INTERCEPTOR_EXPLODE = 13;
    int glancing = 0; int tractor[5] = {400, 200, 100, 50, 25};
    std::vector<int> firing, escape, trigger, baseRegion;
    int pilotAccuracyZeroPoint = 55, pilotAccuracyRange = 40, pilotReactionsZeroPoint = 55, pilotReactionsRange = 60;
    int bravery[3] = {90, 80, 30};
    AlienRace race; RuleAlienMission retaliationRule; bool hasRetaliationRule = true;
    Sound sound; Interface iface;
    int getUfoGlancingHitThreshold() const { return glancing; }
    int getUfoTractorBeamSizeModifier(int size) const { return tractor[size]; }
    bool getShowDogfightDistanceInKm() const { return false; }
    Sound* getSound(const std::string&, int) { return &sound; }
    Interface* getInterface(const std::string&) { return &iface; }
    const std::vector<int>& getUfoFiringRateCoefficients() { return firing; }
    const std::vector<int>& getUfoEscapeCountdownCoefficients() { return escape; }
    const std::vector<int>& getRetaliationTriggerOdds() { return trigger; }
    const std::vector<int>& getRetaliationBaseRegionOdds() { return baseRegion; }
    AlienRace* getAlienRace(const std::string&) { return &race; }
    const RuleAlienMission* getAlienMission(const std::string& id, bool) const { return id.empty() || !hasRetaliationRule ? nullptr : &retaliationRule; }
    const RuleAlienMission* getRandomMission(MissionObjective, int) const { return hasRetaliationRule ? &retaliationRule : nullptr; }
    const RuleUfo* getUfo(const std::string&, bool) const { return nullptr; }
    const UfoTrajectory* getUfoTrajectory(const std::string&, bool) const { return nullptr; }
    int getPilotAccuracyZeroPoint() const { return pilotAccuracyZeroPoint; } int getPilotAccuracyRange() const { return pilotAccuracyRange; }
    int getPilotReactionsZeroPoint() const { return pilotReactionsZeroPoint; } int getPilotReactionsRange() const { return pilotReactionsRange; }
    int getPilotBraveryThresholdVeryBold() const { return bravery[0]; } int getPilotBraveryThresholdBold() const { return bravery[1]; }
    int getPilotBraveryThresholdNormal() const { return bravery[2]; }
};
int Mod::DIFFICULTY_BASED_RETAL_DELAY[5] = {0, 0, 0, 0, 0};
int Mod::DAMAGE_RANGE = 100;
int Mod::EXPLOSIVE_DAMAGE_RANGE = 50;
int Mod::FIRE_DAMAGE_RANGE[2] = {5, 10};
// DAMAGE_TYPE_METHODS

// ---- Targets, soldiers, craft, UFO -------------------------------------------------------------
struct Target { virtual ~Target() {} double lon = 0, lat = 0; double getLongitude() const { return lon; } double getLatitude() const { return lat; } };
struct Waypoint : Target {};
struct UnitStats { int firing = 0, reactions = 0, bravery = 0; };
struct Soldier { UnitStats stats; const UnitStats* getStatsWithSoldierBonusesOnly() const { return &stats; } };
struct Base : Target {};
using CraftId = std::pair<std::string, int>;
class CraftWeapon;
class Craft : public Target
{
public:
    RuleCraft* _rules; RuleCraftStats _stats; int _damage = 0, _shield = 0; bool _lowFuel = false, _inDogfight = false;
    int _interceptionOrder = 0; Target* _dest = nullptr; std::vector<CraftWeapon*> _weapons; std::vector<Soldier*> pilots;
    Base base; int units = 1; int returned = 0, thought = 0;
    explicit Craft(RuleCraft* rules) : _rules(rules) {}
    RuleCraft* getRules() const { return _rules; }
    const RuleCraftStats& getCraftStats() const { return _stats; }
    int getDamage() const { return _damage; } int getShield() const { return _shield; }
    bool getLowFuel() const { return _lowFuel; } bool isInDogfight() const { return _inDogfight; }
    void setInDogfight(bool v) { _inDogfight = v; } void setInterceptionOrder(int v) { _interceptionOrder = v; }
    Target* getDestination() const { return _dest; } void setDestination(Target* d) { _dest = d; }
    std::vector<CraftWeapon*>* getWeapons() { return &_weapons; }
    CraftId getUniqueId() const { return {"CRAFT", 1}; }
    void returnToBase() { ++returned; _dest = &base; }
    bool think() { ++thought; return false; }
    Base* getBase() { return &base; }
    int getNumTotalUnits() const { return units; }
    const std::vector<Soldier*> getPilotList(bool, const Mod*) { return pilots; }
    int getPilotAccuracyBonus(const std::vector<Soldier*>& pilots, const Mod* mod) const;
    int getPilotDodgeBonus(const std::vector<Soldier*>& pilots, const Mod* mod) const;
    int getPilotApproachSpeedModifier(const std::vector<Soldier*>& pilots, const Mod* mod) const;
    void setDamage(int damage);
    void setShield(int shield);
    int getDamagePercentage() const;
    bool isDestroyed() const;
    int getDamageMax() const;
    int getShieldCapacity() const;
};
// CRAFT_METHODS

class CraftWeapon
{
public:
    RuleCraftWeapon* _rules; int _ammo = 0; bool _disabled = false;
    explicit CraftWeapon(RuleCraftWeapon* rules) : _rules(rules) {}
    RuleCraftWeapon* getRules() const { return _rules; }
    int getAmmo() const { return _ammo; } bool isDisabled() const { return _disabled; }
    bool setAmmo(int ammo);
    CraftWeaponProjectile* fire() const;
};
// CRAFT_WEAPON_METHODS

class Ufo : public Target
{
public:
    enum UfoStatus { FLYING, LANDED, CRASHED, DESTROYED, IGNORE_ME };
    RuleUfo* _rules; RuleCraftStats _stats; AlienMission* _mission; int _missionWaveNumber = -1;
    const UfoTrajectory* _trajectory = nullptr;
    int _damage = 0, _shield = 0, _speed = 0, _escapeCountdown = 0, _fireCountdown = 0, _tractorBeamSlowdown = 0;
    int _shieldRechargeHandle = 0, _shootingAt = 0, _hitFrame = 0, _softlockShotCounter = 0, _huntBehavior = 2;
    int _secondsRemaining = 0, _crashId = 0, _landId = 0, moves = 0, resets = 0;
    bool _interceptionProcessed = false, _hunterKiller = false; UfoStatus _status = FLYING; std::string _altitude = "STR_HIGH_UC";
    CraftId _shotDownByCraftId{"", 0}; std::vector<Craft*> followers; Target* _dest = nullptr;
    Ufo(RuleUfo* rules, AlienMission* mission) : _rules(rules), _mission(mission) {}
    const RuleUfo* getRules() const { return _rules; } const RuleCraftStats& getCraftStats() const { return _stats; }
    int getDamage() const { return _damage; } int getShield() const { return _shield; }
    int getSpeed() const { return _speed; } void setSpeed(int s) { _speed = s; }
    int getEscapeCountdown() const { return _escapeCountdown; } void setEscapeCountdown(int v) { _escapeCountdown = v; }
    int getFireCountdown() const { return _fireCountdown; } void setFireCountdown(int v) { _fireCountdown = v; }
    int getTractorBeamSlowdown() const { return _tractorBeamSlowdown; } void setTractorBeamSlowdown(int v) { _tractorBeamSlowdown = v; }
    int getShieldRechargeHandle() const { return _shieldRechargeHandle; } void setShieldRechargeHandle(int v) { _shieldRechargeHandle = v; }
    int getShootingAt() const { return _shootingAt; } void setShootingAt(int v) { _shootingAt = v; }
    int getHitFrame() const { return _hitFrame; } void setHitFrame(int v) { _hitFrame = v; }
    int getSoftlockShotCounter() const { return _softlockShotCounter; } void increaseSoftlockShotCounter() { ++_softlockShotCounter; }
    bool getInterceptionProcessed() const { return _interceptionProcessed; } void setInterceptionProcessed(bool v) { _interceptionProcessed = v; }
    bool isHunterKiller() const { return _hunterKiller; } void setHunterKiller(bool v) { _hunterKiller = v; }
    int getHuntBehavior() const { return _huntBehavior; }
    void resetOriginalDestination(Craft*) { ++resets; }
    const CraftId& getShotDownByCraftId() const { return _shotDownByCraftId; } void setShotDownByCraftId(const CraftId& id) { _shotDownByCraftId = id; }
    void setDestination(Target* d) { _dest = d; }
    Target* getDestination() const { return _dest; }
    void move() { ++moves; }
    std::vector<Craft*> getCraftFollowers() const { return followers; }
    std::string getAlienRace() const { return "RACE"; } AlienMission* getMission() const { return _mission; }
    UfoStatus getStatus() const { return _status; } void setStatus(UfoStatus s) { _status = s; }
    void setSecondsRemaining(int s) { _secondsRemaining = s; } void setAltitude(const std::string& a) { _altitude = a; }
    int getCrashId() const { return _crashId; } void setCrashId(int v) { _crashId = v; }
    int getLandId() const { return _landId; } void setLandId(int v) { _landId = v; }
    void setDamage(int damage, const Mod* mod);
    void setShield(int shield);
    bool isCrashed() const;
    bool isDestroyed() const;
};
// UFO_METHODS

// ---- World ------------------------------------------------------------------------------------
struct RegionRules { std::string type = "REGION_BASE"; bool inside = true; bool insideRegion(double, double) const { return inside; } const std::string& getType() const { return type; } };
struct Region { RegionRules rules; int activity = 0; RegionRules* getRules() { return &rules; } void addActivityXcom(int v) { activity += v; } };
struct CountryRules { bool insideCountry(double, double) const { return true; } };
struct Country { CountryRules rules; int activity = 0; CountryRules* getRules() { return &rules; } void addActivityXcom(int v) { activity += v; } };
struct SavedGame
{
    int difficulty = 0, coefficient = 0, months = 1, nextId = 1, stopped = 0; bool missionExists = false;
    std::vector<Country*> countries; std::vector<Region*> regions; std::vector<AlienMission*> missions;
    int getDifficultyCoefficient() const { return coefficient; } int getDifficulty() const { return difficulty; }
    int getMonthsPassed() const { return months; }
    Region* locateRegion(Base&) { return regions.front(); }
    AlienMission* findAlienMission(const std::string&, MissionObjective, AlienRace*) { return missionExists ? missions.front() : nullptr; }
    std::vector<AlienMission*>& getAlienMissions() { return missions; }
    int getId(const std::string&) { return nextId++; }
    std::vector<Country*>* getCountries() { return &countries; } std::vector<Region*>* getRegions() { return &regions; }
    void stopHuntingXcomCraft(Craft*) { ++stopped; }
};
struct Globe
{
    bool land = true, fakeWater = false;
    bool insideLand(double, double) const { return land; }
    bool insideFakeUnderwaterTexture(double, double) const { return fakeWater; }
};
struct Game { Mod* mod; SavedGame* save; Mod* getMod() { return mod; } SavedGame* getSavedGame() { return save; } };
struct GeoscapeState { Globe globe; int experience = 0; Globe* getGlobe() { return &globe; } void handleDogfightExperience() { ++experience; } };

struct Collections
{
    template<typename C, typename F> static void deleteIf(C& collection, size_t, F&& func)
    {
        size_t write = 0;
        for (size_t read = 0; read < collection.size(); ++read)
        {
            if (func(collection[read])) delete collection[read];
            else collection[write++] = collection[read];
        }
        collection.resize(write);
    }
};

struct Action {};
class DogfightState;
struct Timer { void think(DogfightState*, int) {} };
class State
{
protected:
    Game* _game;
    LocalizedText tr(const std::string& id) const { return {id}; }
public:
    explicit State(Game* game) : _game(game) {}
};

class DogfightState : public State
{
public:
    GeoscapeState *_state;
    Surface surfaces[4]; Text texts[8]; ImageButton buttons[6];
    Surface *_window = &surfaces[0], *_battle = &surfaces[1];
    InteractiveSurface *_btnMinimize = &surfaces[2];
    ImageButton *_btnStandoff = &buttons[0], *_btnCautious = &buttons[1], *_btnStandard = &buttons[2],
        *_btnAggressive = &buttons[3], *_btnDisengage = &buttons[4], *_btnUfo = &buttons[5];
    ImageButton *_mode = nullptr;
    Text *_txtAmmo[RuleCraft::WeaponMax] = {&texts[0], &texts[1], &texts[2], &texts[3]}, *_txtDistance = &texts[4], *_txtStatus = &texts[5];
    Craft *_craft;
    Ufo *_ufo;
    bool _ufoIsAttacking, _missileCraft, _missileImpact;
    bool _disableDisengage, _disableStandoff, _disableCautious, _disableStandard, _disableAggressive;
    bool _craftIsDefenseless, _selfDestructPressed;
    int _timeout, _currentDist, _targetDist, _weaponFireInterval[RuleCraft::WeaponMax] = {}, _weaponFireCountdown[RuleCraft::WeaponMax] = {};
    bool _end, _endUfoHandled, _endCraftHandled, _ufoBreakingOff, _destroyUfo, _destroyCraft, _weaponEnabled[RuleCraft::WeaponMax] = {};
    bool _minimized, _endDogfight, _animatingHit, _waitForPoly, _waitForAltitude;
    std::vector<CraftWeaponProjectile*> _projectiles;
    int _ufoSize, _ufoBlobSize, _craftHeight, _currentCraftDamageColor, _interceptionNumber;
    size_t _interceptionsCount;
    int _x, _y, _minimizedIconX, _minimizedIconY;
    int _weaponNum;
    int _pilotAccuracyBonus, _pilotDodgeBonus, _pilotApproachSpeedModifier, _craftAccelerationBonus;
    bool _firedAtLeastOnce, _experienceAwarded;
    bool _delayedRecolorDone;
    int _colors[13] = {};
    Timer animationTimer; Timer *_craftDamageAnimTimer = &animationTimer;
    bool _tractorLockedOn[RuleCraft::WeaponMax] = {};

    DogfightState(Game* game, GeoscapeState *state, Craft *craft, Ufo *ufo, bool ufoIsAttacking, int interceptionNumber);
    void recolor(const int, const bool) {}
    void think();
    void drawCraftDamage() {}
    void drawCraftShield() {}
    void drawUfo() {}
    void drawProjectile(const CraftWeaponProjectile*) {}
    void endDogfight();
    void animate();
    void update();
    void fireWeapon(int i);
    void ufoFireWeapon();
    void minimumDistance();
    void maximumDistance();
    void aggressiveDistance();
    void setStatus(const std::string &status);
    void btnStandoffPress(Action *action);
    void btnCautiousPress(Action *action);
    void btnStandardPress(Action *action);
    void btnAggressivePress(Action *action);
    void btnDisengagePress(Action *action);
};

DogfightState::DogfightState(Game* game, GeoscapeState *state, Craft *craft, Ufo *ufo, bool ufoIsAttacking, int interceptionNumber) : State(game),
// CONSTRUCTOR_INITIALIZERS
{
    _interceptionNumber = interceptionNumber;
// CONSTRUCTOR_OPTIONS
// CONSTRUCTOR_MODE
// CONSTRUCTOR_AGGRESSIVE
// CONSTRUCTOR_COUNTDOWNS
// CONSTRUCTOR_UFO_SIZE
// CONSTRUCTOR_SHIELD_HANDLE
}
// DOGFIGHT_METHODS

} // namespace OpenXcom

// ---- Scenarios ---------------------------------------------------------------------------------
using namespace OpenXcom;

struct WeaponSpec { int damage, range, accuracy, cautious, standard, aggressive, ammo, ammoMax, type, speed, tractor, shieldModifier; };
struct Scenario
{
    const char* name;
    RuleCraftStats craft; int craftDamage, craftShield, missilePower;
    std::vector<WeaponSpec> weapons; std::vector<UnitStats> pilots;
    RuleCraftStats ufo; RuleUfo ufoRules; int ufoDamage, ufoShield, ufoSpeed, huntBehavior; bool hunterKiller, ufoIsAttacking;
    int difficulty, coefficient, months, retaliationOdds, aggression; bool land, fakeWater, missionExists;
    std::vector<int> firing, escape, trigger, baseRegion; int glancing;
    std::vector<uint64_t> rng;
    int steps;
    std::vector<std::pair<int, char>> commands; // step -> S(tandoff) C(autious) N(standard) A(ggressive) D(isengage) M(inimize) R(estore)
    bool dogfightAI = true;          // Options::dogfightAI default
    int waveInterrupt = -1;          // >= 0: the UFO belongs to wave 0 with this interruptPercentage
    bool firstWeaponDisabled = false;
    int unifiedRandomType = -1;      // >= 0: the first weapon uses the unified damage formula with this random type
    int unifiedResistType = 0;
};

static const char* modeName(DogfightState& d)
{
    if (d._mode == d._btnStandoff) return "standoff";
    if (d._mode == d._btnCautious) return "cautious";
    if (d._mode == d._btnStandard) return "standard";
    if (d._mode == d._btnAggressive) return "aggressive";
    if (d._mode == d._btnDisengage) return "disengage";
    return "none";
}

// The scenario's inputs, so a replay can rebuild it from the oracle alone.
static std::string describe(const Scenario& s)
{
    auto stats = [](const RuleCraftStats& c) {
        std::ostringstream o;
        o << '[' << c.damageMax << ',' << c.speedMax << ',' << c.accel << ',' << c.hitBonus << ',' << c.avoidBonus << ','
          << c.powerBonus << ',' << c.armor << ',' << c.shieldCapacity << ',' << c.shieldRecharge << ',' << c.shieldBleedThrough << ']';
        return o.str();
    };
    auto list = [](const std::vector<int>& v) {
        std::ostringstream o; o << '[';
        for (size_t i = 0; i < v.size(); ++i) o << (i ? "," : "") << v[i];
        o << ']'; return o.str();
    };
    std::ostringstream o;
    o << "{\"craft\":" << stats(s.craft) << ",\"craftState\":[" << s.craftDamage << ',' << s.craftShield << ',' << s.missilePower << "],\"weapons\":[";
    for (size_t i = 0; i < s.weapons.size(); ++i)
    {
        auto& w = s.weapons[i];
        o << (i ? "," : "") << '[' << w.damage << ',' << w.range << ',' << w.accuracy << ',' << w.cautious << ',' << w.standard << ','
          << w.aggressive << ',' << w.ammo << ',' << w.ammoMax << ',' << w.type << ',' << w.speed << ',' << w.tractor << ',' << w.shieldModifier << ']';
    }
    o << "],\"pilots\":[";
    for (size_t i = 0; i < s.pilots.size(); ++i)
        o << (i ? "," : "") << '[' << s.pilots[i].firing << ',' << s.pilots[i].reactions << ',' << s.pilots[i].bravery << ']';
    auto& u = s.ufoRules;
    o << "],\"ufo\":" << stats(s.ufo) << ",\"ufoRule\":[" << u.weaponRange << ',' << u.weaponPower << ',' << u.weaponReload << ','
      << u.radius << ',' << u.score << ',' << u.breakOffTime << ',' << u.softlockThreshold << ',' << u.splashdownSurvivalChance << ','
      << (u.unmanned ? 1 : 0) << ',' << u.blobSize << "],\"ufoState\":[" << s.ufoDamage << ',' << s.ufoShield << ',' << s.ufoSpeed << ','
      << s.huntBehavior << ',' << (s.hunterKiller ? 1 : 0) << ',' << (s.ufoIsAttacking ? 1 : 0) << "],\"world\":[" << s.difficulty << ','
      << s.coefficient << ',' << s.months << ',' << s.retaliationOdds << ',' << s.aggression << ',' << (s.land ? 1 : 0) << ','
      << (s.fakeWater ? 1 : 0) << ',' << (s.missionExists ? 1 : 0) << ',' << s.glancing << ',' << (s.dogfightAI ? 1 : 0) << ','
      << s.waveInterrupt << ',' << (s.firstWeaponDisabled ? 1 : 0) << ',' << s.unifiedRandomType << ',' << s.unifiedResistType << "],\"firing\":" << list(s.firing) << ",\"escape\":" << list(s.escape)
      << ",\"trigger\":" << list(s.trigger) << ",\"baseRegion\":" << list(s.baseRegion) << ",\"steps\":" << s.steps << ",\"commands\":[";
    for (size_t i = 0; i < s.commands.size(); ++i) o << (i ? "," : "") << '[' << s.commands[i].first << ",\"" << s.commands[i].second << "\"]";
    o << "],\"rng\":[";
    for (size_t i = 0; i < s.rng.size(); ++i) o << (i ? "," : "") << s.rng[i];
    o << "]}";
    return o.str();
}

static void run(const Scenario& s, bool first)
{
    Options::dogfightAI = s.dogfightAI;
    RNG::script = s.rng; RNG::next = 0; RNG::count = 0; RNG::calls.str(""); RNG::calls.clear();
    Mod mod; mod.firing = s.firing; mod.escape = s.escape; mod.trigger = s.trigger; mod.baseRegion = s.baseRegion; mod.glancing = s.glancing;
    mod.race.aggression = s.aggression; mod.retaliationRule.wave.spawnTimer = 7;
    SavedGame save; save.difficulty = s.difficulty; save.coefficient = s.coefficient; save.months = s.months; save.missionExists = s.missionExists;
    Country country; Region region; save.countries.push_back(&country); save.regions.push_back(&region);
    RuleAlienMission missionRule; missionRule.retaliationOdds = s.retaliationOdds;
    if (s.waveInterrupt >= 0) missionRule.wave.interruptPercentage = s.waveInterrupt;
    AlienMission mission(missionRule); mission.region = "REGION_UFO";
    if (s.missionExists) save.missions.push_back(&mission);
    Game game{&mod, &save};
    GeoscapeState geoscape; geoscape.globe.land = s.land; geoscape.globe.fakeWater = s.fakeWater;
    RuleCraft craftRule; craftRule.weapons = (int)s.weapons.size(); craftRule.missile = s.missilePower;
    Craft craft(&craftRule); craft._stats = s.craft; craft._damage = s.craftDamage; craft._shield = s.craftShield;
    std::vector<Soldier> soldiers; for (auto& p : s.pilots) soldiers.push_back(Soldier{p});
    for (auto& soldier : soldiers) craft.pilots.push_back(&soldier);
    std::vector<RuleCraftWeapon> weaponRules(s.weapons.size()); std::vector<CraftWeapon*> weapons;
    RuleItem unifiedItem; unifiedItem.type.RandomType = (ItemDamageRandomType)std::max(0, s.unifiedRandomType);
    unifiedItem.type.ResistType = (ItemDamageType)s.unifiedResistType;
    if (s.unifiedRandomType >= 0 && !weaponRules.empty()) { weaponRules[0].unified = true; weaponRules[0].clip = &unifiedItem; }
    for (size_t i = 0; i < s.weapons.size(); ++i)
    {
        auto& w = s.weapons[i]; auto& r = weaponRules[i];
        r.damage = w.damage; r.range = w.range; r.accuracy = w.accuracy; r.reloadCautious = w.cautious; r.reloadStandard = w.standard;
        r.reloadAggressive = w.aggressive; r.ammoMax = w.ammoMax; r.projectileType = (CraftWeaponProjectileType)w.type;
        r.projectileSpeed = w.speed; r.tractorBeamPower = w.tractor; r.shieldDamageModifier = w.shieldModifier;
        auto* cw = new CraftWeapon(&r); cw->_ammo = w.ammo; cw->_disabled = i == 0 && s.firstWeaponDisabled; craft._weapons.push_back(cw);
    }
    RuleUfo ufoRules = s.ufoRules;
    Ufo ufo(&ufoRules, &mission); ufo._stats = s.ufo; ufo._damage = s.ufoDamage; ufo._shield = s.ufoShield; ufo._speed = s.ufoSpeed;
    ufo._huntBehavior = s.huntBehavior; ufo._hunterKiller = s.hunterKiller;
    if (s.waveInterrupt >= 0) ufo._missionWaveNumber = 0;
    if (s.ufoIsAttacking) ufo._dest = &craft;
    craft._dest = &ufo; craft._inDogfight = true;
    DogfightState dogfight(&game, &geoscape, &craft, &ufo, s.ufoIsAttacking, 1);
    std::cout << (first ? "" : ",") << "\n{\"name\":\"" << s.name << "\",\"input\":" << describe(s) << ",\"start\":[" << dogfight._currentDist << ',' << dogfight._targetDist << ",\""
        << modeName(dogfight) << "\"," << dogfight._pilotAccuracyBonus << ',' << dogfight._pilotDodgeBonus << ','
        << dogfight._pilotApproachSpeedModifier << ',' << dogfight._craftAccelerationBonus << ',' << ufo._escapeCountdown << ','
        << ufo._fireCountdown << ',' << dogfight._ufoSize << "],\"steps\":[";
    for (int step = 0; step < s.steps && !dogfight._endDogfight; ++step)
    {
        for (auto& command : s.commands)
        {
            if (command.first != step) continue;
            switch (command.second)
            {
            case 'S': dogfight._mode = dogfight._btnStandoff; dogfight.btnStandoffPress(nullptr); break;
            case 'C': dogfight._mode = dogfight._btnCautious; dogfight.btnCautiousPress(nullptr); break;
            case 'N': dogfight._mode = dogfight._btnStandard; dogfight.btnStandardPress(nullptr); break;
            case 'A': dogfight._mode = dogfight._btnAggressive; dogfight.btnAggressivePress(nullptr); break;
            case 'D': dogfight._mode = dogfight._btnDisengage; dogfight.btnDisengagePress(nullptr); break;
            case 'M': dogfight._minimized = true; break;
            case 'R': dogfight._minimized = false; break;
            }
        }
        // GeoscapeState::handleDogfights resets the flag for every window before thinking.
        ufo.setInterceptionProcessed(false);
        dogfight.think();
        std::cout << (step ? "," : "") << "\n[" << step << ',' << dogfight._currentDist << ',' << dogfight._targetDist << ",\"" << modeName(dogfight) << "\","
            << ufo._damage << ',' << ufo._shield << ',' << (int)ufo._status << ',' << ufo._speed << ',' << ufo._escapeCountdown << ','
            << ufo._fireCountdown << ',' << ufo._tractorBeamSlowdown << ',' << ufo._shootingAt << ',' << craft._damage << ',' << craft._shield << ",[";
        for (size_t i = 0; i < craft._weapons.size(); ++i)
            std::cout << (i ? "," : "") << '[' << craft._weapons[i]->_ammo << ',' << dogfight._weaponFireCountdown[i] << ',' << dogfight._weaponFireInterval[i] << ']';
        std::cout << "],[";
        for (size_t i = 0; i < dogfight._projectiles.size(); ++i)
        {
            auto* p = dogfight._projectiles[i];
            std::cout << (i ? "," : "") << '[' << p->getDirection() << ',' << (int)p->getType() << ',' << p->getPosition() << ',' << (p->getMissed() ? 1 : 0) << ']';
        }
        std::cout << "]," << (dogfight._end ? 1 : 0) << ',' << (dogfight._endDogfight ? 1 : 0) << ',' << (dogfight._ufoBreakingOff ? 1 : 0) << ','
            << dogfight._timeout << ",\"" << dogfight._txtStatus->value << "\"]";
    }
    std::cout << "],\"end\":[" << ufo._damage << ',' << (int)ufo._status << ',' << ufo._secondsRemaining << ',' << ufo._crashId << ',' << ufo._landId << ','
        << ufo._altitude.size() << ',' << craft._damage << ',' << (craft._inDogfight ? 1 : 0) << ',' << craft.returned << ',' << craft.thought << ','
        << ufo.moves << ',' << ufo.resets << ',' << (ufo._hunterKiller ? 1 : 0) << ',' << country.activity << ',' << region.activity << ','
        << mission.shotDown << ',' << save.missions.size() << ',' << geoscape.experience << ',' << (dogfight._destroyUfo ? 1 : 0) << ','
        << (dogfight._destroyCraft ? 1 : 0) << ',' << ufo._softlockShotCounter << ',' << ufo._shieldRechargeHandle << ',' << (mission.interrupted ? 1 : 0) << "],\"rng\":[" << RNG::calls.str() << "]}";
    for (auto* cw : craft._weapons) delete cw;
}

int main()
{
    // UFO: speed 0 unless stated; craft stats: fuel, damageMax, speedMax, accel, hit, avoid, power, armor, shieldCap, shieldRecharge, -, bleed.
    RuleCraftStats interceptor{}; interceptor.damageMax = 800; interceptor.speedMax = 2100; interceptor.accel = 3;
    RuleCraftStats fragile = interceptor; fragile.damageMax = 60; fragile.armor = 1;
    RuleCraftStats scout{}; scout.damageMax = 50; scout.speedMax = 2200;
    RuleCraftStats shielded{}; shielded.damageMax = 300; shielded.speedMax = 1000; shielded.shieldCapacity = 40; shielded.shieldRecharge = 150; shielded.shieldBleedThrough = 50; shielded.armor = 2;
    RuleUfo armed{}; armed.weaponRange = 30; armed.weaponPower = 20; armed.weaponReload = 20; armed.radius = 12; armed.score = 50; armed.breakOffTime = 300; armed.blobSize = 2;
    RuleUfo passive{}; passive.score = 30; passive.breakOffTime = 400; passive.blobSize = 1;
    RuleUfo quick = passive; quick.breakOffTime = 10;
    RuleUfo large = armed; large.blobSize = 6; large.score = 200;
    RuleCraftStats battleship{}; battleship.damageMax = 120; battleship.speedMax = 1500; battleship.avoidBonus = 5;
    WeaponSpec cannon{10, 10, 25, 24, 16, 8, 200, 200, 2, 8, 0, 100};
    WeaponSpec stingray{70, 30, 70, 32, 24, 16, 6, 6, 0, 8, 0, 100};
    WeaponSpec laser{70, 21, 35, 36, 24, 12, 100, 100, 4, 0, 0, 50};
    WeaponSpec tractor{0, 20, 0, 0, 0, 0, 0, 0, 2, 0, 1100, 100};
    std::vector<uint64_t> lowRolls(400, 0), highRolls(400, 99), mixed;
    for (int i = 0; i < 400; ++i) mixed.push_back((uint64_t)(i * 37 + 11));
    std::vector<Scenario> scenarios = {
        {"standoff-then-aggressive-cannon", interceptor, 0, 0, 0, {cannon}, {}, scout, passive, 0, 0, 0, 2, false, false,
            0, 0, 1, -1, 0, true, false, false, {}, {}, {}, {}, 0, lowRolls, 400, {{3, 'A'}}},
        {"cautious-stingray-crash-retaliation", interceptor, 0, 0, 0, {stingray, cannon}, {{70, 60, 85}}, scout, armed, 0, 0, 1000, 2, false, false,
            2, 2, 3, 40, 10, true, false, false, {}, {}, {}, {}, 0, lowRolls, 260, {{0, 'C'}}},
        {"standard-laser-shields-glancing", interceptor, 100, 0, 0, {laser, laser}, {{40, 40, 20}}, shielded, armed, 0, 30, 1000, 2, false, false,
            3, 3, 4, -1, 0, true, false, false, {150, 125, 100, 75, 50}, {80, 90, 100, 110, 120}, {5, 10, 15, 20, 25}, {10, 20, 30, 40, 60}, 50, mixed, 900, {{0, 'N'}}},
        {"outrun-breaks-off", interceptor, 0, 0, 0, {cannon}, {}, scout, passive, 0, 0, 3000, 2, false, false,
            0, 0, 1, -1, 0, true, false, false, {}, {}, {}, {}, 0, lowRolls, 40, {{0, 'A'}}},
        {"disengage-and-minimize", interceptor, 0, 0, 0, {cannon}, {}, scout, armed, 0, 0, 1000, 2, false, false,
            1, 1, 1, -1, 0, true, false, false, {}, {}, {}, {}, 0, highRolls, 200, {{0, 'A'}, {10, 'D'}, {12, 'M'}}},
        {"hunter-killer-attack", interceptor, 0, 0, 0, {cannon}, {{55, 55, 55}}, shielded, armed, 0, 10, 1000, 2, true, true,
            4, 4, 6, -1, 0, true, false, false, {}, {}, {}, {}, 0, mixed, 400, {{20, 'C'}}},
        {"tractor-forced-landing", interceptor, 0, 0, 0, {tractor, cannon}, {}, scout, passive, 0, 0, 600, 2, false, false,
            0, 0, 1, -1, 0, true, false, false, {}, {}, {}, {}, 0, highRolls, 400, {{0, 'A'}}},
        {"missile-craft-impact", interceptor, 0, 0, 120, {}, {}, shielded, armed, 0, 40, 500, 2, false, false,
            0, 0, 1, -1, 0, false, false, false, {}, {}, {}, {}, 0, lowRolls, 200, {}},
        {"crash-over-fake-water", interceptor, 0, 0, 0, {stingray}, {}, scout, passive, 0, 0, 1000, 2, false, false,
            0, 0, 1, 0, 0, true, true, false, {}, {}, {}, {}, 0, lowRolls, 400, {{0, 'A'}}},
        {"escape-countdown-breakoff", interceptor, 0, 0, 0, {cannon}, {}, scout, quick, 0, 0, 1000, 2, false, false,
            0, 0, 1, -1, 0, true, false, false, {}, {}, {}, {}, 0, lowRolls, 60, {{0, 'A'}}},
        {"all-miss-cautious", interceptor, 0, 0, 0, {cannon}, {}, shielded, armed, 0, 0, 1000, 2, false, false,
            0, 0, 1, -1, 0, true, false, false, {}, {}, {}, {}, 0, highRolls, 700, {{0, 'C'}}},
        {"cautious-damaged-retreats", fragile, 0, 0, 0, {cannon}, {}, shielded, armed, 0, 0, 1000, 2, false, false,
            0, 0, 1, -1, 0, true, false, false, {}, {}, {}, {}, 0, mixed, 700, {{0, 'C'}}},
        {"standoff-then-aggressive-cannon-ai-off", interceptor, 0, 0, 0, {cannon}, {}, scout, passive, 0, 0, 0, 2, false, false,
            0, 0, 1, -1, 0, true, false, false, {}, {}, {}, {}, 0, lowRolls, 400, {{3, 'A'}}, false},
        {"large-ufo-wave-interrupt", interceptor, 0, 0, 0, {cannon}, {}, battleship, large, 0, 0, 1000, 2, false, false,
            0, 0, 1, 0, 0, true, false, false, {}, {}, {}, {}, 0, lowRolls, 500, {{0, 'A'}}, true, 60},
        {"disabled-weapon-at-start", interceptor, 0, 0, 0, {cannon, stingray}, {}, scout, passive, 0, 0, 1000, 2, false, false,
            0, 0, 1, 0, 0, true, false, false, {}, {}, {}, {}, 0, lowRolls, 400, {{0, 'A'}}, true, -1, true},
        {"unified-damage-standard", interceptor, 0, 0, 0, {stingray}, {}, scout, passive, 0, 0, 1000, 2, false, false,
            0, 0, 1, 0, 0, true, false, false, {}, {}, {}, {}, 0, mixed, 400, {{0, 'A'}}, true, -1, false, 8, 1},
        {"unified-damage-default-fire", interceptor, 0, 0, 0, {stingray}, {}, scout, passive, 0, 0, 1000, 2, false, false,
            0, 0, 1, 0, 0, true, false, false, {}, {}, {}, {}, 0, mixed, 400, {{0, 'A'}}, true, -1, false, 0, 2},
        {"unified-damage-four-dice", interceptor, 0, 0, 0, {stingray}, {}, scout, passive, 0, 0, 1000, 2, false, false,
            0, 0, 1, 0, 0, true, false, false, {}, {}, {}, {}, 0, mixed, 400, {{0, 'A'}}, true, -1, false, 10, 3},
    };
    std::cout << "{\"scenarios\":[";
    bool first = true;
    for (auto& s : scenarios) { run(s, first); first = false; }
    std::cout << "\n]}\n";
}

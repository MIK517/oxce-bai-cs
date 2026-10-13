// Base defense rounds: BaseDefenseState::nextStep and applyDamage, Ufo::setDamage/setShield/isCrashed/
// isDestroyed and RuleDamageType::getRandomDamage are inserted verbatim by the capture script. Drawing,
// text and sounds are stubs; RNG returns scripted raw values through the RandomState::generate formula.
#include <algorithm>
#include <cstdint>
#include <cstdio>
#include <functional>
#include <sstream>
#include <string>
#include <vector>

namespace OpenXcom {

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
struct Mod
{
    static int DAMAGE_RANGE, EXPLOSIVE_DAMAGE_RANGE, FIRE_DAMAGE_RANGE[2];
    static const int UFO_EXPLODE = 11;
    struct Sound { void play() {} } sound;
    Sound* getSound(const std::string&, int) { return &sound; }
    const struct RuleUfo* getUfo(const std::string&, bool) const { return nullptr; }
    const struct UfoTrajectory* getUfoTrajectory(const std::string&, bool) const { return nullptr; }
};
int Mod::DAMAGE_RANGE = 100;
int Mod::EXPLOSIVE_DAMAGE_RANGE = 50;
int Mod::FIRE_DAMAGE_RANGE[2] = {5, 10};
// DAMAGE_TYPE_METHODS

struct RuleItem { RuleDamageType type; const RuleDamageType* getDamageType() const { return &type; } };
struct RuleBaseFacility
{
    std::string type = "DEFENSE"; int hitRatio = 0, defenseValue = 0, ammoNeeded = 0, ammoMax = 0, shieldDamageModifier = 100;
    bool unified = false; const RuleItem* ammo = nullptr;
    const std::string& getType() const { return type; } int getHitRatio() const { return hitRatio; }
    int getDefenseValue() const { return defenseValue; } int getAmmoNeeded() const { return ammoNeeded; }
    int getAmmoMax() const { return ammoMax; } int getShieldDamageModifier() const { return shieldDamageModifier; }
    bool unifiedDamageFormula() const { return unified; } const RuleItem* getAmmoItem() const { return ammo; }
    int getFireSound() const { return 0; } int getHitSound() const { return 0; }
};
struct BaseFacility
{
    RuleBaseFacility* rules; int ammo = 0;
    const RuleBaseFacility* getRules() const { return rules; } int getAmmo() const { return ammo; }
    void setAmmo(int a) { ammo = a; } void resetAmmoMissingReported() {}
};
struct ItemContainer
{
    const RuleItem* item = nullptr; int quantity = 0;
    int getItem(const RuleItem* r) const { return r == item ? quantity : 0; }
    void removeItem(const RuleItem* r, int q) { if (r == item) quantity = std::max(0, quantity - q); }
};
struct Base
{
    std::vector<BaseFacility*> defenses; ItemContainer storage;
    std::vector<BaseFacility*>* getDefenses() { return &defenses; } ItemContainer* getStorageItems() { return &storage; }
};
struct RuleCraftStats { int damageMax = 0, shieldCapacity = 0, avoidBonus2 = 0, armor = 0, shieldBleedThrough = 0; };
struct RuleUfo { bool unmanned = false; bool isUnmanned() const { return unmanned; } };
struct UfoTrajectory { static const std::string RETALIATION_ASSAULT_RUN; };
const std::string UfoTrajectory::RETALIATION_ASSAULT_RUN = "__RETALIATION_ASSAULT_RUN";
struct MissionWave { int interruptPercentage = 0; };
struct RuleAlienMission
{
    MissionWave wave; std::string spawnUfo;
    const std::string& getSpawnUfo() const { return spawnUfo; } int getWaveCount() const { return 1; }
    const MissionWave& getWave(int) const { return wave; }
};
struct AlienMission
{
    RuleAlienMission rules; bool interrupted = false;
    const RuleAlienMission& getRules() const { return rules; } void setInterrupted(bool i) { interrupted = i; }
};
class Ufo
{
public:
    enum UfoStatus { FLYING, LANDED, CRASHED, DESTROYED, IGNORE_ME };
    RuleUfo* _rules; RuleCraftStats _stats; AlienMission* _mission; int _missionWaveNumber = 0;
    const UfoTrajectory* _trajectory = nullptr;
    int _damage = 0, _shield = 0, _huntBehavior = 2; UfoStatus _status = FLYING;
    const RuleCraftStats& getCraftStats() const { return _stats; }
    int getDamage() const { return _damage; } int getShield() const { return _shield; }
    UfoStatus getStatus() const { return _status; }
    void setDamage(int damage, const Mod *mod);
    void setShield(int shield);
    bool isCrashed() const;
    bool isDestroyed() const;
};
// UFO_METHODS

struct TextList { void addRow(int, ...) {} void setCellText(int, int, const std::string&) {} void scrollDown(bool) {} };
struct Visible { void setVisible(bool) {} };
struct Timer { void setInterval(int) {} };
struct Game { Mod mod; Mod* getMod() { return &mod; } };
enum BaseDefenseActionType { BDA_NONE, BDA_FIRE, BDA_RESOLVE, BDA_DESTROY, BDA_END };

class BaseDefenseState
{
public:
    Game* _game; Base* _base; Ufo* _ufo; TextList* _lstDefenses; Visible* _txtInit; Visible* _btnOk; Timer* _timer;
    BaseDefenseActionType _action = BDA_NONE; int _row = -1, _passes = 0, _attacks = 0, _thinkcycles = 0, _explosionCount = 0;
    int _defenses = 0, _gravShields = 0;
    std::string tr(const std::string& s) { return s; }
    bool applyDamage(const RuleBaseFacility* rule);
    void nextStep();
};
// BASE_DEFENSE_METHODS

}

using namespace OpenXcom;

struct Defense { int hit, power, ammoNeeded, ammoMax, ownAmmo, shieldModifier, unified, randomType; };
struct Scenario
{
    const char* name; std::vector<Defense> defenses; int storageAmmo, gravShields;
    int damage, damageMax, shield, shieldCapacity, avoidBonus2, armor, bleed, unmanned, huntBehavior, interrupt;
    std::vector<uint64_t> rng;
};

int main()
{
    const std::vector<Scenario> scenarios = {
        {"vanilla-two-defenses-miss-hit", {{70, 500, 0, 0, 0, 100, 0, 0}, {50, 600, 0, 0, 0, 100, 0, 0}}, 0, 0,
            0, 1500, 0, 0, 0, 0, 0, 0, 2, 0, {80, 10, 333}},
        {"vanilla-destroys-and-stops", {{100, 900, 0, 0, 0, 100, 0, 0}, {100, 900, 0, 0, 0, 100, 0, 0}}, 0, 0,
            700, 1000, 0, 0, 0, 0, 0, 0, 2, 0, {5, 450}},
        {"grav-shield-second-pass", {{60, 400, 0, 0, 0, 100, 0, 0}}, 0, 2,
            0, 3000, 0, 0, 10, 0, 0, 0, 2, 0, {1, 2, 3, 4, 5, 6, 7, 8, 9}},
        {"own-ammo-runs-out", {{90, 300, 2, 4, 3, 100, 0, 0}}, 0, 3,
            0, 5000, 0, 0, 0, 0, 0, 0, 2, 0, {10, 20, 30, 40, 50, 60}},
        {"storage-ammo", {{90, 300, 1, 0, 0, 100, 0, 0}, {90, 300, 1, 0, 0, 100, 0, 0}}, 1, 0,
            0, 5000, 0, 0, 0, 0, 0, 0, 2, 0, {10, 20, 30, 40}},
        {"vanilla-shield", {{100, 400, 0, 0, 0, 50, 0, 0}, {100, 400, 0, 0, 0, 0, 0, 0}, {100, 400, 0, 0, 0, 200, 0, 0}}, 0, 0,
            0, 5000, 300, 300, 0, 0, 0, 0, 2, 0, {1, 100, 2, 3, 4, 250}},
        {"unified-shield-bleed-armor", {{100, 400, 0, 0, 0, 150, 1, 0}, {100, 400, 0, 0, 0, 100, 1, 3}}, 0, 0,
            0, 5000, 200, 200, 0, 30, 50, 0, 2, 0, {7, 123, 9, 77}},
        {"interrupt-on-crash", {{100, 900, 0, 0, 0, 100, 0, 0}}, 0, 0,
            0, 1000, 0, 0, 0, 0, 0, 0, 2, 40, {3, 400, 10}},
        {"unmanned-no-crash-then-destroyed", {{100, 400, 0, 0, 0, 100, 0, 0}, {100, 400, 0, 0, 0, 100, 0, 0}}, 0, 0,
            100, 900, 0, 0, 0, 0, 0, 1, 2, 60, {2, 300, 5, 400, 99}},
    };
    std::printf("{\"scenarios\":[");
    bool first = true;
    for (const auto& s : scenarios)
    {
        RNG::script = s.rng; RNG::next = 0; RNG::calls.str(""); RNG::calls.clear(); RNG::count = 0;
        RuleItem item; Base base; std::vector<RuleBaseFacility> rules(s.defenses.size());
        std::vector<BaseFacility> facilities(s.defenses.size());
        for (size_t i = 0; i < s.defenses.size(); ++i)
        {
            const auto& d = s.defenses[i];
            rules[i].hitRatio = d.hit; rules[i].defenseValue = d.power; rules[i].ammoNeeded = d.ammoNeeded;
            rules[i].ammoMax = d.ammoMax; rules[i].shieldDamageModifier = d.shieldModifier; rules[i].unified = d.unified != 0;
            rules[i].ammo = &item;
            facilities[i].rules = &rules[i]; facilities[i].ammo = d.ownAmmo;
            base.defenses.push_back(&facilities[i]);
        }
        item.type.RandomType = (ItemDamageRandomType)(s.defenses.empty() ? 0 : s.defenses.back().randomType);
        base.storage.item = &item; base.storage.quantity = s.storageAmmo;
        RuleUfo ufoRule; ufoRule.unmanned = s.unmanned != 0;
        AlienMission mission; mission.rules.wave.interruptPercentage = s.interrupt;
        Ufo ufo; ufo._rules = &ufoRule; ufo._mission = &mission; ufo._huntBehavior = s.huntBehavior;
        ufo._stats.damageMax = s.damageMax; ufo._stats.shieldCapacity = s.shieldCapacity; ufo._stats.avoidBonus2 = s.avoidBonus2;
        ufo._stats.armor = s.armor; ufo._stats.shieldBleedThrough = s.bleed; ufo._damage = s.damage; ufo._shield = s.shield;
        Game game; TextList list; Visible visible; Timer timer;
        BaseDefenseState state; state._game = &game; state._base = &base; state._ufo = &ufo; state._lstDefenses = &list;
        state._txtInit = &visible; state._btnOk = &visible; state._timer = &timer;
        state._defenses = (int)base.defenses.size(); state._gravShields = s.gravShields;
        int steps = 0;
        while (state._thinkcycles != -1 && steps < 1000) { state.nextStep(); ++steps; }
        std::printf("%s{\"name\":\"%s\",\"input\":{\"defenses\":[", first ? "" : ",", s.name);
        for (size_t i = 0; i < s.defenses.size(); ++i)
        {
            const auto& d = s.defenses[i];
            std::printf("%s[%d,%d,%d,%d,%d,%d,%d,%d]", i ? "," : "", d.hit, d.power, d.ammoNeeded, d.ammoMax, d.ownAmmo,
                d.shieldModifier, d.unified, d.randomType);
        }
        std::printf("],\"ufo\":[%d,%d,%d,%d,%d,%d,%d,%d,%d,%d],\"storageAmmo\":%d,\"gravShields\":%d,\"rng\":[",
            s.damage, s.damageMax, s.shield, s.shieldCapacity, s.avoidBonus2, s.armor, s.bleed, s.unmanned, s.huntBehavior,
            s.interrupt, s.storageAmmo, s.gravShields);
        for (size_t i = 0; i < s.rng.size(); ++i) std::printf("%s%llu", i ? "," : "", (unsigned long long)s.rng[i]);
        std::printf("]},\"steps\":%d,\"damage\":%d,\"shield\":%d,\"status\":%d,\"interrupted\":%s,\"passes\":%d,\"attacks\":%d,\"ammo\":[",
            steps, ufo._damage, ufo._shield, (int)ufo._status, mission.interrupted ? "true" : "false",
            state._passes, state._attacks);
        for (size_t i = 0; i < facilities.size(); ++i) std::printf("%s%d", i ? "," : "", facilities[i].ammo);
        std::printf("],\"storage\":%d,\"rng\":[%s]}", base.storage.quantity, RNG::calls.str().c_str());
        first = false;
    }
    std::printf("]}\n");
    return 0;
}

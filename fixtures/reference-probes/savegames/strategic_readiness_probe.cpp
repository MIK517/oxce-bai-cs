#include <algorithm>
#include <iostream>
namespace RNG { int choice; int generate(int, int) { return choice; } }
struct Rule {
 int rate, maximum; bool saving;
 int getRearmRate() const { return rate; }
 int getAmmoMax() const { return maximum; }
 bool useStatisticalBulletSaving() const { return saving; }
};
struct CraftWeapon {
 Rule* _rules; int _ammo; bool _rearming;
 void setAmmo(int value) { _ammo = std::max(0, std::min(value, _rules->maximum)); }
 int rearm(int available, int clipSize);
};
// WEAPON_REARM
struct UnitStats {
 int tu = 0, stamina = 0, health = 40, bravery = 0, reactions = 0, firing = 0;
 int throwing = 0, strength = 0, psiStrength = 0, psiSkill = 0, melee = 0, mana = 30;
 using Ptr = int UnitStats::*;
 template<typename Callback> static void fieldLoop(Callback callback) {
  for (Ptr field : {&UnitStats::tu, &UnitStats::stamina, &UnitStats::health, &UnitStats::bravery,
      &UnitStats::reactions, &UnitStats::firing, &UnitStats::throwing, &UnitStats::strength,
      &UnitStats::psiStrength, &UnitStats::psiSkill, &UnitStats::melee, &UnitStats::mana}) callback(field);
 }
 // UNIT_STATS_COMBINE
};
struct RuleSoldier {
 UnitStats trainingCaps;
 UnitStats getTrainingStatCaps() const { return trainingCaps; }
};
struct BaseSumDailyRecovery { int ManaRecovery, HealthRecovery; float SickBayAbsoluteBonus, SickBayRelativeBonus; };
struct Soldier {
 UnitStats _currentStats; float _recovery; int _manaMissing, _healthMissing; RuleSoldier* _rules = nullptr;
 int getManaMissing() const { return _manaMissing; }
 int getHealthMissing() const { return _healthMissing; }
 void healWound(float, float);
 void replenishMana(int);
 void replenishHealth(int);
 void replenishStats(const BaseSumDailyRecovery&);
 bool isFullyTrained() const;
};
// HEAL_WOUND
// REPLENISH_MANA
// REPLENISH_HEALTH
// REPLENISH_STATS
// IS_FULLY_TRAINED
bool facilityAffordable(int funds, int cost, std::initializer_list<int> refunds) {
 int refundValueTemp = 0;
 for (int refund : refunds) refundValueTemp = refund;
 return funds >= cost - refundValueTemp;
}
int main() {
 std::cout << "{\"schemaVersion\":1,\"referenceCommit\":\"4df3a5e571a1a4b5e8a46d3161fb2e21a2adba15\",\"weapons\":[";
 bool first = true;
 for (int rate : {3, 10, 20}) for (int ammo : {0, 9, 10}) for (int clip : {0, 4})
 for (int available : {0, 1, 5}) for (int choice : {0, 3}) {
  Rule rule{rate, 10, true}; CraftWeapon weapon{&rule, ammo, true}; RNG::choice = choice;
  int used = weapon.rearm(available, clip);
  if (!first) std::cout << ','; first = false;
  std::cout << '[' << rate << ',' << ammo << ',' << clip << ',' << available << ',' << choice
    << ',' << used << ',' << weapon._ammo << ',' << weapon._rearming << ']';
 }
 std::cout << "],\"recovery\":[";
 first = true;
 for (float wounds : {0.0f, 0.5f, 5.5f}) for (int manaRate : {-80, -5, 0, 6}) {
  Soldier soldier{{}, wounds, 10, 12, nullptr};
  soldier.replenishStats({manaRate, 4, 0.5f, 2.5f});
  if (!first) std::cout << ','; first = false;
  std::cout << '[' << wounds << ',' << manaRate << ',' << soldier._recovery << ',' << soldier._manaMissing << ',' << soldier._healthMissing << ']';
 }
  std::cout << "],\"training\":[";
  first = true;
  for (int delta : {-1, 0, 1}) {
   UnitStats caps; caps.tu = caps.stamina = caps.health = caps.firing = caps.throwing = caps.strength = caps.melee = 50;
   UnitStats current = caps; current.firing += delta;
   RuleSoldier rule{caps}; Soldier soldier{current, 0, 0, 0, &rule};
   if (!first) std::cout << ','; first = false;
   std::cout << '[' << delta << ',' << soldier.isFullyTrained() << ']';
  }
  std::cout << "],\"transformationCombine\":[";
  first = true;
  for (int maskValue : {0, 1}) {
   UnitStats mask, keep, reroll; mask.tu = maskValue; keep.tu = 77; reroll.tu = 50;
   UnitStats result = UnitStats::combine(mask, keep, reroll);
   if (!first) std::cout << ','; first = false;
   std::cout << '[' << maskValue << ',' << keep.tu << ',' << reroll.tu << ',' << result.tu << ']';
  }
  std::cout << "],\"facilityAffordability\":[";
  std::cout << "[0,70,40,40," << facilityAffordable(0, 70, {40, 40}) << "],";
  std::cout << "[30,70,40,40," << facilityAffordable(30, 70, {40, 40}) << "]]}\n";
}

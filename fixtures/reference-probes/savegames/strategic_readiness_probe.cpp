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
struct Stats { int health = 40, mana = 30; };
struct BaseSumDailyRecovery { int ManaRecovery, HealthRecovery; float SickBayAbsoluteBonus, SickBayRelativeBonus; };
struct Soldier {
 Stats _currentStats; float _recovery; int _manaMissing, _healthMissing;
 int getManaMissing() const { return _manaMissing; }
 int getHealthMissing() const { return _healthMissing; }
 void healWound(float, float);
 void replenishMana(int);
 void replenishHealth(int);
 void replenishStats(const BaseSumDailyRecovery&);
};
// HEAL_WOUND
// REPLENISH_MANA
// REPLENISH_HEALTH
// REPLENISH_STATS
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
  Soldier soldier{{}, wounds, 10, 12};
  soldier.replenishStats({manaRate, 4, 0.5f, 2.5f});
  if (!first) std::cout << ','; first = false;
  std::cout << '[' << wounds << ',' << manaRate << ',' << soldier._recovery << ',' << soldier._manaMissing << ',' << soldier._healthMissing << ']';
 }
 std::cout << "]}\n";
}

// GeoscapeState::time5Seconds flying-UFO shield block and Ufo::setShield are inserted verbatim.
#include <algorithm>
#include <iostream>
namespace RNG { int roll, calls; bool percent(int value) { ++calls; return roll < value; } }
struct Stats { int shieldCapacity, shieldRechargeInGeoscape; };
struct Ufo {
    Stats _stats; int _shield;
    int getShield() const { return _shield; }
    const Stats& getCraftStats() const { return _stats; }
    void setShield(int shield);
};
// SET_SHIELD
void recharge(Ufo* ufo) {
// RECHARGE_SHIELD
}
int main() {
    struct Row { int capacity, rate, shield, roll; };
    Row rows[] = {{10,125,-1,0},{10,125,3,24},{10,125,3,25},{10,100,3,99},
        {10,125,9,0},{10,-1,2,99},{10,0,2,0},{10,-2,2,0},{10,125,10,0},
        {0,100,-1,0},{10,125,11,0},{2147483647,100,2147483646,99}};
    std::cout << "{\"rows\":[";
    bool first = true;
    for (auto row : rows) {
        if (!first) std::cout << ','; first = false;
        Ufo ufo{{row.capacity,row.rate},row.shield}; RNG::roll = row.roll; RNG::calls = 0;
        recharge(&ufo);
        std::cout << '[' << row.capacity << ',' << row.rate << ',' << row.shield << ',' << row.roll
            << ',' << ufo.getShield() << ',' << RNG::calls << ']';
    }
    std::cout << "]}\n";
}

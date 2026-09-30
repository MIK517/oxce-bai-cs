// SavedGame and Mod methods plus the GeoscapeState context block are inserted verbatim.
#include <cstdint>
#include <iomanip>
#include <iostream>
#include <string>
#include <vector>

struct Region {
    std::vector<int> xcom, alien;
    const std::vector<int>& getActivityXcom() const { return xcom; }
    const std::vector<int>& getActivityAlien() const { return alien; }
};
struct Country {
    std::vector<int> funding;
    const std::vector<int>& getFunding() const { return funding; }
};
struct Base {
    int maintenance;
    int getMonthlyMaintenace() const { return maintenance; }
};
class SavedGame {
public:
    std::vector<int64_t> _funds;
    std::vector<int> _researchScores;
    std::vector<Region*> _regions;
    std::vector<Country*> _countries;
    std::vector<Base*> _bases;
    int getCurrentScore(int monthsPassed) const;
    int getCountryFunding() const;
    int getBaseMaintenance() const;
    int64_t getFunds() const { return _funds.back(); }
};
class Mod {
    double _performanceBonusFactor;
public:
    explicit Mod(double factor) : _performanceBonusFactor(factor) {}
    // BONUS_METHOD
};
// METHODS

struct Case {
    int month, research, xcomOne, alienOne, xcomTwo, alienTwo;
    int64_t funds;
    int funding, maintenance;
    double factor;
    bool newMonth;
};

int main() {
    std::vector<Case> cases = {
        {0, 30, 100, 20, 5, 2, 1000, 500, 200, 0.5, true},
        {1, 30, 100, 20, 5, 2, 1000, 500, 200, 0.5, true},
        {2, 30, 100, 20, 5, 2, 1000, 500, 200, 0.5, true},
        {1, -200, 0, 100, 0, 0, 1000, 500, 200, 0.5, true},
        {2, 30, 100, 20, 5, 2, 1000, 500, 200, 0.5, false},
        {0, 30, 100, 20, 5, 2, 1000, 500, 200, 0.125, true},
        {0, 1, 0, 0, 0, 0, 1000, 0, 0, 2147483647.5, true},
        {0, 1, 0, 0, 0, 0, 1000, 0, 0, -2147483648.5, true}
    };
    std::cout << std::boolalpha << std::setprecision(17) << "{\"rows\":[";
    bool first = true;
    for (auto& input : cases) {
        if (!first) std::cout << ',';
        first = false;
        Region one{{input.xcomOne}, {input.alienOne}};
        Region two{{input.xcomTwo}, {input.alienTwo}};
        Country country{{input.funding}};
        Base base{input.maintenance};
        SavedGame game{{input.funds}, {input.research}, {&one, &two}, {&country}, {&base}};
        SavedGame* save = &game;
        Mod configured(input.factor);
        Mod* mod = &configured;
        int month = input.month;
        bool isNewMonth = input.newMonth;
        // CONTEXT
        std::cout << '[' << month << ',' << input.research << ",[" << input.xcomOne << ',' << input.xcomTwo
                  << "],[" << input.alienOne << ',' << input.alienTwo << "]," << input.funds << ','
                  << input.funding << ',' << input.maintenance << ',' << input.factor << ',' << isNewMonth
                  << ',' << currentScore << ',' << currentFunds << ']';
    }
    std::cout << "]}\n";
}

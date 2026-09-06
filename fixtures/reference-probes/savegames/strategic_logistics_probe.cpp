// Method bodies are inserted verbatim from the pinned reference by the capture tool.
// The script hook is a deterministic test collaborator: it records its input, then
// adds seven. This checks caller ordering; it does not claim to test the script VM.
#include <iostream>
#include <cmath>
#include <cstdint>
#include <algorithm>
struct Base {
    int capacity; double used, lon, lat;
    int getAvailableStores() const { return capacity; }
    double getUsedStores() const { return used; }
    double getLongitude() const { return lon; }
    double getLatitude() const { return lat; }
    bool storesOverfull(double offset) const;
};
struct SavedGame {
    int coefficient;
    int getBuyPriceCoefficient() const { return coefficient; }
    int getSellPriceCoefficient() const { return coefficient; }
};
struct ModScript {
    enum Kind { BuyCostItem, SellCostItem };
    static inline int observed;
    template<Kind K, typename... T> static int scriptFunc2(const void*, int adjusted, T...) {
        observed = adjusted; return adjusted + 7;
    }
};
struct RuleItem {
    int price;
    int getBuyCost() const { return price; }
    int getSellCost() const { return price; }
    int getBuyCostAdjusted(const Base* base, const SavedGame* save) const;
    int getSellCostAdjusted(const Base* base, const SavedGame* save) const;
};
struct TransferItemsState {
    Base *_baseFrom, *_baseTo;
    double getDistance() const;
};
// BUY_COST
// SELL_COST
// STORES_OVERFULL
// TRANSFER_DISTANCE
int main() {
    std::cout.precision(17);
    std::cout << "{\"prices\":[";
    bool first = true;
    for (int price : {199, -199, 0, 2000000000}) {
        for (int coefficient : {0, 75, 100}) {
            RuleItem item{price}; SavedGame save{coefficient};
            int buy = item.getBuyCostAdjusted(nullptr, &save);
            int input = ModScript::observed;
            int sell = item.getSellCostAdjusted(nullptr, &save);
            if (!first) std::cout << ','; first = false;
            std::cout << "[" << price << ',' << coefficient << ',' << input << ',' << buy << ',' << sell << ']';
        }
    }
    std::cout << "],\"stores\":["; first = true;
    for (double used : {0.0, 9.999, 10.0, 10.009, 10.011, 10.05}) {
        Base base{10, used, 0, 0};
        if (!first) std::cout << ','; first = false;
        std::cout << '[' << used << ',' << base.storesOverfull(0) << ']';
    }
    std::cout << "],\"transfers\":["; first = true;
    Base origin{0, 0, 0, 0};
    for (double longitude : {0.0, 1.0, 3.141592653589793}) {
        Base destination{0, 0, longitude, 0};
        TransferItemsState transfer{&origin, &destination};
        double distance = transfer.getDistance();
        if (!first) std::cout << ','; first = false;
        std::cout << '[' << longitude << ',' << distance << ',' << (int)floor(6 + distance / 10)
            << ',' << (int)distance << ',' << (int)(5 * distance) << ',' << (int)(25 * distance) << ']';
    }
    std::cout << "]}\n";
}

// Behavioral probe distilled from Mod::loadOffsetNode and Mod::loadModData.
// Shared indices retain their original value; other non-negative indices receive
// the owning mod's cumulative reservedSpace * 1000 offset.
#include <iostream>

static int resolve(int index, int shared, int modOffset)
{
    return index >= shared ? index + modOffset : index;
}

int main()
{
    std::cout << resolve(1, 2, 0) << ' ' << resolve(2, 2, 0) << ' '
              << resolve(2, 2, 1000) << ' ' << resolve(1, 1, 1000) << '\n';
}

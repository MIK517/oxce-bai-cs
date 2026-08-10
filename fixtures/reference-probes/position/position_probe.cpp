/*
 * Synthetic OXCE compatibility probe.
 * SPDX-License-Identifier: GPL-3.0-or-later
 */
#include "Battlescape/Position.h"

#ifdef main
#undef main
#endif

#include <iostream>

using OpenXcom::Position;

int main()
{
    const Position tile(2, -3, 4);
    const Position voxel = tile.toVoxel();
    const Position clipped = Position(-17, 31, -25).clipVoxel();
    const Position containingTile = Position(-17, 31, -25).toTile();

    std::cout
        << "{\"clipVoxel\":[" << clipped.x << ',' << clipped.y << ',' << clipped.z
        << "],\"distance2d\":" << Position::distance2d(Position(0, 0, 0), Position(2, 2, 0))
        << ",\"distanceSquared\":" << Position::distanceSq(Position(1, 2, 3), Position(4, 6, 3))
        << ",\"positionSize\":" << sizeof(Position)
        << ",\"toTile\":[" << containingTile.x << ',' << containingTile.y << ',' << containingTile.z
        << "],\"toVoxel\":[" << voxel.x << ',' << voxel.y << ',' << voxel.z << "]}\n";
    return 0;
}

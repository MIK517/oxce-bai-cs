/*
 * Synthetic OXCE compatibility probe.
 * SPDX-License-Identifier: GPL-3.0-or-later
 */
#include <cstdint>
#include <cstring>
#include <fstream>
#include <iomanip>
#include <iostream>
#include <sstream>
#include <stdexcept>
#include <string>
#include <vector>

#pragma pack(push, 1)
struct Mcd
{
    unsigned char frame[8];
    unsigned char loft[12];
    unsigned short scanG;
    unsigned char unknown23[8];
    unsigned char ufoDoor;
    unsigned char stopLineOfSight;
    unsigned char noFloor;
    unsigned char bigWall;
    unsigned char gravLift;
    unsigned char door;
    unsigned char blockFire;
    unsigned char blockSmoke;
    unsigned char unknown39;
    unsigned char timeUnitsWalk;
    unsigned char timeUnitsSlide;
    unsigned char timeUnitsFly;
    unsigned char armor;
    unsigned char highExplosiveBlock;
    unsigned char dieMcd;
    unsigned char flammable;
    unsigned char alternateMcd;
    unsigned char unknown48;
    signed char terrainLevel;
    unsigned char positionLevel;
    unsigned char unknown51;
    unsigned char lightBlock;
    unsigned char footstep;
    unsigned char tileType;
    unsigned char highExplosiveType;
    unsigned char highExplosiveStrength;
    unsigned char smokeBlockage;
    unsigned char fuel;
    unsigned char lightSource;
    unsigned char targetType;
    unsigned char xcomBase;
    unsigned char unknown62;
};
#pragma pack(pop)

static_assert(sizeof(Mcd) == 62);

namespace
{
std::vector<std::uint8_t> readHex(const char *path)
{
    std::ifstream input(path);
    if (!input) throw std::runtime_error("could not open fixture");
    std::string digits;
    input >> digits;
    if ((digits.size() % 2) != 0) throw std::runtime_error("odd hex length");
    std::vector<std::uint8_t> result;
    for (std::size_t i = 0; i < digits.size(); i += 2)
        result.push_back(static_cast<std::uint8_t>(std::stoul(digits.substr(i, 2), nullptr, 16)));
    return result;
}

std::string toHex(const std::uint8_t *data, std::size_t size)
{
    std::ostringstream output;
    output << std::uppercase << std::hex << std::setfill('0');
    for (std::size_t i = 0; i < size; ++i)
        output << std::setw(2) << static_cast<unsigned int>(data[i]);
    return output.str();
}
}

int main(int argc, char **argv)
{
    try
    {
        if (argc != 3) throw std::runtime_error("usage: mcd_loftemps_probe <mcd.hex> <loftemps.hex>");
        const auto mcdBytes = readHex(argv[1]);
        const auto loftBytes = readHex(argv[2]);
        const std::size_t recordCount = mcdBytes.size() / sizeof(Mcd);
        std::cout << "{\"loftemps\":{\"trailing\":\"";
        const std::size_t loftEnd = (loftBytes.size() / 2) * 2;
        if (loftEnd < loftBytes.size()) std::cout << toHex(loftBytes.data() + loftEnd, loftBytes.size() - loftEnd);
        std::cout << "\",\"values\":[";
        for (std::size_t i = 0; i < loftEnd; i += 2)
        {
            if (i != 0) std::cout << ',';
            std::cout << (static_cast<unsigned int>(loftBytes[i]) |
                (static_cast<unsigned int>(loftBytes[i + 1]) << 8));
        }
        std::cout << "]},\"mcd\":{\"records\":[";
        for (std::size_t i = 0; i < recordCount; ++i)
        {
            if (i != 0) std::cout << ',';
            Mcd record{};
            std::memcpy(&record, mcdBytes.data() + i * sizeof(Mcd), sizeof(Mcd));
            std::cout << "{\"alternateMcd\":" << static_cast<int>(record.alternateMcd)
                << ",\"armor\":" << static_cast<int>(record.armor)
                << ",\"bigWall\":" << static_cast<int>(record.bigWall)
                << ",\"blocksFire\":" << (record.blockFire != 0 ? "true" : "false")
                << ",\"blocksSmoke\":" << (record.blockSmoke != 0 ? "true" : "false")
                << ",\"dieMcd\":" << static_cast<int>(record.dieMcd)
                << ",\"flammable\":" << static_cast<int>(record.flammable)
                << ",\"footstepSound\":" << static_cast<int>(record.footstep)
                << ",\"frames\":\"" << toHex(record.frame, 8)
                << "\",\"fuel\":" << static_cast<int>(record.fuel)
                << ",\"hasNoFloor\":" << (record.noFloor != 0 ? "true" : "false")
                << ",\"highExplosiveBlock\":" << static_cast<int>(record.highExplosiveBlock)
                << ",\"highExplosiveStrength\":" << static_cast<int>(record.highExplosiveStrength)
                << ",\"highExplosiveType\":" << static_cast<int>(record.highExplosiveType)
                << ",\"isDoor\":" << (record.door != 0 ? "true" : "false")
                << ",\"isGravLift\":" << (record.gravLift != 0 ? "true" : "false")
                << ",\"isUfoDoor\":" << (record.ufoDoor != 0 ? "true" : "false")
                << ",\"isXcomBase\":" << (record.xcomBase != 0 ? "true" : "false")
                << ",\"lightBlock\":" << static_cast<int>(record.lightBlock)
                << ",\"lightSource\":" << static_cast<int>(record.lightSource)
                << ",\"loftIds\":\"" << toHex(record.loft, 12)
                << "\",\"miniMapIndex\":" << record.scanG
                << ",\"positionLevel\":" << static_cast<int>(record.positionLevel)
                << ",\"raw\":\"" << toHex(reinterpret_cast<const std::uint8_t *>(&record), sizeof(record))
                << "\",\"smokeBlockage\":" << static_cast<int>(record.smokeBlockage)
                << ",\"stopsLineOfSight\":" << (record.stopLineOfSight != 0 ? "true" : "false")
                << ",\"targetType\":" << static_cast<int>(record.targetType)
                << ",\"terrainLevel\":" << static_cast<int>(record.terrainLevel)
                << ",\"tileType\":" << static_cast<int>(record.tileType)
                << ",\"timeUnits\":[" << static_cast<int>(record.timeUnitsWalk) << ','
                << static_cast<int>(record.timeUnitsFly) << ',' << static_cast<int>(record.timeUnitsSlide)
                << "]}";
        }
        const std::size_t mcdEnd = recordCount * sizeof(Mcd);
        std::cout << "],\"trailing\":\"";
        if (mcdEnd < mcdBytes.size()) std::cout << toHex(mcdBytes.data() + mcdEnd, mcdBytes.size() - mcdEnd);
        std::cout << "\"}}\n";
        return 0;
    }
    catch (const std::exception &error)
    {
        std::cerr << error.what() << '\n';
        return 1;
    }
}

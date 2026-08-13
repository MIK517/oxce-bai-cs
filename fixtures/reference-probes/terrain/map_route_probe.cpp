/*
 * Synthetic OXCE compatibility probe.
 * SPDX-License-Identifier: GPL-3.0-or-later
 */
#include <array>
#include <cstdint>
#include <fstream>
#include <iomanip>
#include <iostream>
#include <sstream>
#include <stdexcept>
#include <string>
#include <vector>

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

std::string toHex(const std::vector<std::uint8_t> &data, std::size_t offset)
{
    std::ostringstream output;
    output << std::uppercase << std::hex << std::setfill('0');
    for (std::size_t i = offset; i < data.size(); ++i)
        output << std::setw(2) << static_cast<unsigned int>(data[i]);
    return output.str();
}

struct Node
{
    bool dummy = false;
    int id = 0;
    int x = 0;
    int y = 0;
    int z = 0;
    int segment = 0;
    int type = 0;
    int rank = 0;
    int flags = 0;
    int reserved = 0;
    int priority = 0;
    std::array<int, 5> links{};
};
}

int main(int argc, char **argv)
{
    try
    {
        if (argc != 3) throw std::runtime_error("usage: map_route_probe <map.hex> <route.hex>");
        const auto map = readHex(argv[1]);
        const auto route = readHex(argv[2]);
        if (map.size() < 3) throw std::runtime_error("truncated MAP");
        const int length = map[0];
        const int width = map[1];
        const int levels = map[2];
        const std::size_t tileCount = static_cast<std::size_t>(width) * length * levels;
        const std::size_t tileEnd = 3 + tileCount * 4;
        if (map.size() < tileEnd) throw std::runtime_error("truncated MAP tiles");

        constexpr int nodeOffset = 10;
        constexpr int offsetX = 20;
        constexpr int offsetY = 30;
        constexpr int offsetZ = 40;
        constexpr int segment = 6;
        const std::size_t nodeCount = route.size() / 24;
        std::vector<Node> nodes;
        std::vector<int> badNodes;
        for (std::size_t i = 0; i < nodeCount; ++i)
        {
            const auto at = i * 24;
            const int localX = route[at + 1];
            const int localY = route[at];
            const int localZ = route[at + 2];
            Node node;
            if (localX >= width || localY >= length || localZ >= levels)
            {
                node.dummy = true;
                badNodes.push_back(static_cast<int>(i));
            }
            else
            {
                node.id = nodeOffset + static_cast<int>(i);
                node.x = offsetX + localX;
                node.y = offsetY + localY;
                node.z = offsetZ + levels - 1 - localZ;
                node.segment = segment;
                node.type = route[at + 19];
                node.rank = route[at + 20];
                node.flags = route[at + 21];
                node.reserved = route[at + 22];
                node.priority = route[at + 23];
                for (int link = 0; link < 5; ++link)
                {
                    const int raw = route[at + 4 + link * 3];
                    node.links[link] = raw <= 250 ? raw + nodeOffset : raw - 256;
                }
            }
            nodes.push_back(node);
        }
        for (const int badNode : badNodes)
            for (auto &node : nodes)
                if (!node.dummy)
                    for (auto &link : node.links)
                        if (link - nodeOffset == badNode) link = -1;

        std::cout << "{\"map\":{\"length\":" << length << ",\"levels\":" << levels
            << ",\"tiles\":[";
        for (std::size_t i = 0; i < tileCount; ++i)
        {
            if (i != 0) std::cout << ',';
            const auto at = 3 + i * 4;
            std::cout << '[' << static_cast<int>(map[at]) << ',' << static_cast<int>(map[at + 1])
                << ',' << static_cast<int>(map[at + 2]) << ',' << static_cast<int>(map[at + 3]) << ']';
        }
        std::cout << "],\"trailing\":\"" << toHex(map, tileEnd) << "\",\"width\":" << width
            << "},\"route\":{\"nodes\":[";
        for (std::size_t i = 0; i < nodes.size(); ++i)
        {
            if (i != 0) std::cout << ',';
            const auto &node = nodes[i];
            std::cout << "{\"dummy\":" << (node.dummy ? "true" : "false") << ",\"flags\":" << node.flags
                << ",\"id\":" << node.id << ",\"index\":" << i << ",\"links\":[";
            if (!node.dummy)
                for (std::size_t link = 0; link < node.links.size(); ++link)
                {
                    if (link != 0) std::cout << ',';
                    std::cout << node.links[link];
                }
            std::cout << "],\"position\":[" << node.x << ',' << node.y << ',' << node.z
                << "],\"priority\":" << node.priority << ",\"rank\":" << node.rank
                << ",\"reserved\":" << node.reserved << ",\"segment\":" << node.segment
                << ",\"type\":" << node.type << '}';
        }
        std::cout << "],\"trailing\":\"" << toHex(route, nodeCount * 24) << "\"}}\n";
        return 0;
    }
    catch (const std::exception &error)
    {
        std::cerr << error.what() << '\n';
        return 1;
    }
}

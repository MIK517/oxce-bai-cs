/*
 * Synthetic OXCE compatibility probe.
 * SPDX-License-Identifier: GPL-3.0-or-later
 */
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

std::uint32_t readLe32(const std::vector<std::uint8_t> &data, std::size_t offset)
{
    if (offset + 4 > data.size()) throw std::runtime_error("truncated CAT word");
    return static_cast<std::uint32_t>(data[offset]) |
        (static_cast<std::uint32_t>(data[offset + 1]) << 8) |
        (static_cast<std::uint32_t>(data[offset + 2]) << 16) |
        (static_cast<std::uint32_t>(data[offset + 3]) << 24);
}

std::string toHex(const std::vector<std::uint8_t> &data, std::size_t offset, std::size_t size)
{
    std::ostringstream output;
    output << std::uppercase << std::hex << std::setfill('0');
    for (std::size_t i = 0; i < size; ++i) output << std::setw(2) << static_cast<unsigned int>(data[offset + i]);
    return output.str();
}
}

int main(int argc, char **argv)
{
    try
    {
        if (argc != 2) throw std::runtime_error("usage: cat_entries_probe <fixture.hex>");
        const auto data = readHex(argv[1]);
        const auto firstOffset = readLe32(data, 0);
        std::vector<std::size_t> offsets;
        for (std::uint32_t i = 0; i < firstOffset / 8; ++i)
        {
            const auto offset = readLe32(data, static_cast<std::size_t>(i) * 8);
            if (offset < data.size()) offsets.push_back(offset);
        }

        std::cout << "{\"entries\":[";
        for (std::size_t i = 0; i < offsets.size(); ++i)
        {
            if (i != 0) std::cout << ',';
            const auto end = i + 1 < offsets.size() ? offsets[i + 1] : data.size();
            const auto size = end - offsets[i];
            std::cout << "{\"data\":\"" << toHex(data, offsets[i], size)
                << "\",\"length\":" << size << ",\"offset\":" << offsets[i] << '}';
        }
        std::cout << "]}\n";
        return 0;
    }
    catch (const std::exception &error)
    {
        std::cerr << error.what() << '\n';
        return 1;
    }
}

/*
 * Synthetic OXCE compatibility probe.
 * SPDX-License-Identifier: GPL-3.0-or-later
 */
#include <cstdint>
#include <fstream>
#include <iostream>
#include <map>
#include <stdexcept>
#include <string>
#include <vector>

namespace
{
std::vector<std::uint8_t> parseHex(const std::string &digits)
{
    if ((digits.size() % 2) != 0) throw std::runtime_error("odd hex length");
    std::vector<std::uint8_t> result;
    for (std::size_t i = 0; i < digits.size(); i += 2)
        result.push_back(static_cast<std::uint8_t>(std::stoul(digits.substr(i, 2), nullptr, 16)));
    return result;
}

std::map<std::string, std::string> readFixture(const char *path)
{
    std::ifstream input(path);
    if (!input) throw std::runtime_error("could not open fixture");
    std::map<std::string, std::string> result;
    std::string line;
    while (std::getline(input, line))
    {
        if (!line.empty() && line.back() == '\r') line.pop_back();
        const auto separator = line.find('=');
        if (separator == std::string::npos) throw std::runtime_error("fixture line has no equals sign");
        result[line.substr(0, separator)] = line.substr(separator + 1);
    }
    return result;
}

std::uint32_t readLe32(const std::vector<std::uint8_t> &data)
{
    if (data.size() < 4) throw std::runtime_error("TAB lacks initial word");
    return static_cast<std::uint32_t>(data[0]) |
        (static_cast<std::uint32_t>(data[1]) << 8) |
        (static_cast<std::uint32_t>(data[2]) << 16) |
        (static_cast<std::uint32_t>(data[3]) << 24);
}

std::vector<std::vector<std::uint8_t>> decode(
    const std::vector<std::uint8_t> &pck,
    const std::vector<std::uint8_t> *tab,
    int width,
    int height)
{
    const auto frameCount = tab == nullptr ? 1u :
        static_cast<unsigned int>(tab->size() / (readLe32(*tab) == 0 ? 4 : 2));
    std::vector<std::vector<std::uint8_t>> frames;
    std::size_t input = 0;
    for (unsigned int frame = 0; frame < frameCount; ++frame)
    {
        std::vector<std::uint8_t> pixels(static_cast<std::size_t>(width * height), 0);
        std::size_t output = static_cast<std::size_t>(pck.at(input++)) * static_cast<std::size_t>(width);
        while (input < pck.size())
        {
            const auto value = pck[input++];
            if (value == 255) break;
            if (value == 254)
            {
                output += pck.at(input++);
            }
            else
            {
                if (output < pixels.size()) pixels[output] = value;
                ++output;
            }
        }
        frames.push_back(std::move(pixels));
    }
    return frames;
}

void printFrames(const std::vector<std::vector<std::uint8_t>> &frames)
{
    std::cout << '[';
    for (std::size_t frame = 0; frame < frames.size(); ++frame)
    {
        if (frame != 0) std::cout << ',';
        std::cout << '[';
        for (std::size_t pixel = 0; pixel < frames[frame].size(); ++pixel)
        {
            if (pixel != 0) std::cout << ',';
            std::cout << static_cast<unsigned int>(frames[frame][pixel]);
        }
        std::cout << ']';
    }
    std::cout << ']';
}
}

int main(int argc, char **argv)
{
    try
    {
        if (argc != 2) throw std::runtime_error("usage: pck_tab_probe <fixture.hex>");
        const auto fixture = readFixture(argv[1]);
        const auto width = std::stoi(fixture.at("width"));
        const auto height = std::stoi(fixture.at("height"));
        const auto pck = parseHex(fixture.at("pck"));
        const auto tab16 = parseHex(fixture.at("tab16"));
        const auto tab32 = parseHex(fixture.at("tab32"));

        std::cout << "{\"height\":" << height << ",\"noTab\":";
        printFrames(decode(pck, nullptr, width, height));
        std::cout << ",\"tab16\":";
        printFrames(decode(pck, &tab16, width, height));
        std::cout << ",\"tab32\":";
        printFrames(decode(pck, &tab32, width, height));
        std::cout << ",\"width\":" << width << "}\n";
        return 0;
    }
    catch (const std::exception &error)
    {
        std::cerr << error.what() << '\n';
        return 1;
    }
}

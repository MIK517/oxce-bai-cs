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
struct Color
{
    std::uint8_t red = 0;
    std::uint8_t green = 0;
    std::uint8_t blue = 0;
    std::uint8_t alpha = 0;
};

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

int paletteOffset(int palette) { return palette * (768 + 6); }
int blockOffset(int block) { return block * 16; }

std::vector<Color> decode(const std::vector<std::uint8_t> &data, int count, int offset)
{
    std::vector<Color> colors(static_cast<std::size_t>(count));
    auto position = static_cast<std::size_t>(offset);
    for (int i = 0; i < count && position + 3 <= data.size(); ++i)
    {
        colors[static_cast<std::size_t>(i)].red = static_cast<std::uint8_t>(data[position] * 4);
        colors[static_cast<std::size_t>(i)].green = static_cast<std::uint8_t>(data[position + 1] * 4);
        colors[static_cast<std::size_t>(i)].blue = static_cast<std::uint8_t>(data[position + 2] * 4);
        colors[static_cast<std::size_t>(i)].alpha = 255;
        position += 3;
    }
    colors[0].alpha = 0;
    return colors;
}

void printColors(const std::vector<Color> &colors)
{
    std::cout << '[';
    for (std::size_t i = 0; i < colors.size(); ++i)
    {
        if (i != 0) std::cout << ',';
        const auto &color = colors[i];
        std::cout << '[' << static_cast<unsigned int>(color.red) << ','
            << static_cast<unsigned int>(color.green) << ','
            << static_cast<unsigned int>(color.blue) << ','
            << static_cast<unsigned int>(color.alpha) << ']';
    }
    std::cout << ']';
}
}

int main(int argc, char **argv)
{
    try
    {
        if (argc != 2) throw std::runtime_error("usage: xcom_palette_probe <fixture.hex>");
        const auto fixture = readFixture(argv[1]);
        const auto count = std::stoi(fixture.at("colorCount"));
        const auto first = parseHex(fixture.at("first"));
        const auto second = parseHex(fixture.at("second"));
        std::vector<std::uint8_t> data(static_cast<std::size_t>(paletteOffset(1)), 0);
        data.insert(data.begin(), first.begin(), first.end());
        data.resize(static_cast<std::size_t>(paletteOffset(1)), 0);
        data.insert(data.end(), second.begin(), second.end());

        std::cout << "{\"block14Offset\":" << blockOffset(14) << ",\"first\":";
        printColors(decode(data, count, paletteOffset(0)));
        std::cout << ",\"missing\":";
        printColors(decode(data, count, paletteOffset(2)));
        std::cout << ",\"palette4Offset\":" << paletteOffset(4) << ",\"second\":";
        printColors(decode(data, count, paletteOffset(1)));
        std::cout << "}\n";
        return 0;
    }
    catch (const std::exception &error)
    {
        std::cerr << error.what() << '\n';
        return 1;
    }
}

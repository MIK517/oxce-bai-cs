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

std::string toHex(const std::vector<std::uint8_t> &data)
{
    std::ostringstream output;
    output << std::uppercase << std::hex << std::setfill('0');
    for (const auto value : data) output << std::setw(2) << static_cast<unsigned int>(value);
    return output.str();
}

void standardShade(std::uint8_t &destination, std::uint8_t source, int shade)
{
    if (source == 0) return;
    const auto newShade = static_cast<std::uint8_t>(source + shade);
    destination = ((newShade ^ source) & 0xf0) != 0 ? 0x0f : newShade;
}

void replacementShade(std::uint8_t &destination, std::uint8_t source, int shade, int colorGroup)
{
    if (source == 0) return;
    const auto newShade = static_cast<std::uint8_t>((source & 0x0f) + shade);
    destination = (newShade & 0xf0) != 0
        ? 0x0f
        : static_cast<std::uint8_t>((colorGroup << 4) | newShade);
}
}

int main(int argc, char **argv)
{
    try
    {
        if (argc != 2) throw std::runtime_error("usage: indexed_operations_probe <fixture.hex>");
        const auto input = readHex(argv[1]);
        if (input.size() != 16) throw std::runtime_error("fixture must contain 16 bytes");

        std::vector<std::uint8_t> transformed(input.begin(), input.begin() + 4);
        for (auto &pixel : transformed)
        {
            if (pixel == 0) continue;
            const int minimum = pixel / 16 * 16;
            const int maximum = minimum + 16;
            int shifted = pixel + 2;
            if (shifted < minimum) shifted = minimum;
            else if (shifted > maximum) shifted = maximum;
            pixel = static_cast<std::uint8_t>(shifted);
        }
        for (auto &pixel : transformed)
            if (pixel != 0) pixel = static_cast<std::uint8_t>(pixel + 2 * (0x28 - pixel));

        std::vector<std::uint8_t> standard(6, 8);
        std::vector<std::uint8_t> replacement(6, 8);
        for (std::size_t i = 0; i < 6; ++i)
        {
            standardShade(standard[i], input[4 + i], 2);
            replacementShade(replacement[i], input[10 + i], 2, 5);
        }

        std::cout << "{\"replacementShade\":\"" << toHex(replacement)
            << "\",\"standardShade\":\"" << toHex(standard)
            << "\",\"transformed\":\"" << toHex(transformed) << "\"}\n";
        return 0;
    }
    catch (const std::exception &error)
    {
        std::cerr << error.what() << '\n';
        return 1;
    }
}

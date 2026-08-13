/*
 * Synthetic OXCE compatibility probe.
 * SPDX-License-Identifier: GPL-3.0-or-later
 */
#include <cstdint>
#include <fstream>
#include <iostream>
#include <stdexcept>
#include <string>
#include <vector>

std::vector<std::uint8_t> readHex(const char *path)
{
    std::ifstream input(path);
    if (!input) throw std::runtime_error("could not open fixture");
    std::string digits;
    input >> digits;
    std::vector<std::uint8_t> result;
    for (std::size_t i = 0; i < digits.size(); i += 2)
        result.push_back(static_cast<std::uint8_t>(std::stoul(digits.substr(i, 2), nullptr, 16)));
    return result;
}

int main(int argc, char **argv)
{
    try
    {
        if (argc != 2) throw std::runtime_error("usage: sprite_font_probe <fixture.hex>");
        const auto pixels = readHex(argv[1]);
        if (pixels.size() != 32) throw std::runtime_error("fixture must be 8x4 pixels");
        std::cout << "{\"glyphs\":[";
        for (int glyph = 0; glyph < 2; ++glyph)
        {
            int left = -1;
            int right = -1;
            const int startX = glyph * 4;
            for (int x = startX; x < startX + 4; ++x)
                for (int y = 0; y < 4 && left == -1; ++y)
                    if (pixels[y * 8 + x] != 0) left = x;
            for (int x = startX + 3; x >= startX; --x)
                for (int y = 4; y-- != 0 && right == -1;)
                    if (pixels[y * 8 + x] != 0) right = x;
            if (glyph != 0) std::cout << ',';
            std::cout << "{\"height\":4,\"width\":" << (right - left + 1)
                << ",\"x\":" << left << ",\"y\":0}";
        }
        std::cout << "],\"sizes\":{\"A\":[3,5],\"nbsp\":[1,5],\"space\":[2,5],\"tab\":[3,5],\"unknown\":[2,5]}}\n";
        return 0;
    }
    catch (const std::exception &error)
    {
        std::cerr << error.what() << '\n';
        return 1;
    }
}

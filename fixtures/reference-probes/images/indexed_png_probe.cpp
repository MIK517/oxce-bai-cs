/*
 * Synthetic OXCE compatibility probe.
 * SPDX-License-Identifier: GPL-3.0-or-later
 */
#include "lodepng.h"
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
std::vector<unsigned char> readHex(const char *path)
{
    std::ifstream input(path);
    if (!input) throw std::runtime_error("could not open fixture");
    std::string digits;
    input >> digits;
    if ((digits.size() % 2) != 0) throw std::runtime_error("odd hex length");
    std::vector<unsigned char> result;
    for (std::size_t i = 0; i < digits.size(); i += 2)
        result.push_back(static_cast<unsigned char>(std::stoul(digits.substr(i, 2), nullptr, 16)));
    return result;
}

std::string toHex(const unsigned char *data, std::size_t size)
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
        if (argc != 2) throw std::runtime_error("usage: indexed_png_probe <fixture.hex>");
        const auto png = readHex(argv[1]);
        std::vector<unsigned char> pixels;
        unsigned width = 0;
        unsigned height = 0;
        lodepng::State state;
        state.decoder.color_convert = 0;
        const unsigned error = lodepng::decode(pixels, width, height, state, png);
        if (error != 0) throw std::runtime_error(lodepng_error_text(error));
        const auto &color = state.info_png.color;
        if (lodepng_get_bpp(&color) != 8) throw std::runtime_error("fixture is not 8-bit indexed PNG");

        int transparent = 0;
        for (std::size_t index = 0; index < color.palettesize; ++index)
            if (color.palette[index * 4 + 3] == 0)
            {
                transparent = static_cast<int>(index);
                break;
            }
        if (transparent != 0)
            for (auto &pixel : pixels)
                if (pixel == transparent) pixel = 0;

        std::cout << "{\"height\":" << height << ",\"palette\":\""
            << toHex(color.palette, color.palettesize * 4) << "\",\"pixels\":\""
            << toHex(pixels.data(), pixels.size()) << "\",\"transparentIndex\":"
            << transparent << ",\"width\":" << width << "}\n";
        return 0;
    }
    catch (const std::exception &error)
    {
        std::cerr << error.what() << '\n';
        return 1;
    }
}

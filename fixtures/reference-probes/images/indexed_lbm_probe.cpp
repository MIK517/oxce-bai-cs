/*
 * Synthetic OXCE compatibility probe.
 * SPDX-License-Identifier: GPL-3.0-or-later
 */
#define SDL_MAIN_HANDLED
#include <SDL.h>
#include <SDL_image.h>
#undef main
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

std::string toHex(const std::vector<unsigned char> &data)
{
    std::ostringstream output;
    output << std::uppercase << std::hex << std::setfill('0');
    for (const auto value : data) output << std::setw(2) << static_cast<unsigned int>(value);
    return output.str();
}

void writeImage(const char *path)
{
    auto encoded = readHex(path);
    SDL_RWops *rw = SDL_RWFromConstMem(encoded.data(), static_cast<int>(encoded.size()));
    if (rw == nullptr) throw std::runtime_error(SDL_GetError());
    SDL_Surface *surface = IMG_Load_RW(rw, SDL_TRUE);
    if (surface == nullptr) throw std::runtime_error(IMG_GetError());
    if (surface->format->BitsPerPixel != 8 || surface->format->palette == nullptr)
        throw std::runtime_error("fixture did not decode to an indexed surface");

    const int transparent = (surface->flags & SDL_SRCCOLORKEY) != 0
        ? static_cast<int>(surface->format->colorkey)
        : 0;
    std::vector<unsigned char> pixels;
    for (int y = 0; y < surface->h; ++y)
    {
        const auto *row = static_cast<const unsigned char *>(surface->pixels) + y * surface->pitch;
        for (int x = 0; x < surface->w; ++x)
            pixels.push_back(row[x] == transparent && transparent != 0 ? 0 : row[x]);
    }
    std::vector<unsigned char> palette;
    for (int index = 0; index < 4; ++index)
    {
        const auto color = surface->format->palette->colors[index];
        palette.push_back(color.r);
        palette.push_back(color.g);
        palette.push_back(color.b);
        palette.push_back(index == transparent ? 0 : 255);
    }

    std::cout << "{\"height\":" << surface->h << ",\"palette\":\"" << toHex(palette)
        << "\",\"pixels\":\"" << toHex(pixels) << "\",\"transparentIndex\":"
        << transparent << ",\"width\":" << surface->w << '}';
    SDL_FreeSurface(surface);
}
}

int main(int argc, char **argv)
{
    try
    {
        if (argc != 3) throw std::runtime_error("usage: indexed_lbm_probe <pbm.hex> <ilbm.hex>");
        std::cout << "{\"ilbm\":";
        writeImage(argv[2]);
        std::cout << ",\"pbm\":";
        writeImage(argv[1]);
        std::cout << "}\n";
        return 0;
    }
    catch (const std::exception &error)
    {
        std::cerr << error.what() << '\n';
        return 1;
    }
}

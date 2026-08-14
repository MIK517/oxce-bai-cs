/*
 * Synthetic OXCE compatibility probe.
 * SPDX-License-Identifier: GPL-3.0-or-later
 */
#define SDL_MAIN_HANDLED
#include <SDL.h>
#undef main
#include <cstdint>
#include <fstream>
#include <iostream>
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

void writeWave(const char *path)
{
    auto encoded = readHex(path);
    SDL_RWops *rw = SDL_RWFromConstMem(encoded.data(), static_cast<int>(encoded.size()));
    if (rw == nullptr) throw std::runtime_error(SDL_GetError());
    SDL_AudioSpec spec{};
    Uint8 *source = nullptr;
    Uint32 sourceLength = 0;
    if (SDL_LoadWAV_RW(rw, SDL_TRUE, &spec, &source, &sourceLength) == nullptr)
        throw std::runtime_error(SDL_GetError());

    SDL_AudioCVT conversion{};
    if (SDL_BuildAudioCVT(
            &conversion,
            spec.format,
            spec.channels,
            spec.freq,
            AUDIO_S16LSB,
            spec.channels,
            spec.freq) < 0)
    {
        SDL_FreeWAV(source);
        throw std::runtime_error(SDL_GetError());
    }

    conversion.len = static_cast<int>(sourceLength);
    conversion.buf = static_cast<Uint8 *>(SDL_malloc(sourceLength * conversion.len_mult));
    if (conversion.buf == nullptr)
    {
        SDL_FreeWAV(source);
        throw std::runtime_error("audio conversion allocation failed");
    }
    SDL_memcpy(conversion.buf, source, sourceLength);
    if (SDL_ConvertAudio(&conversion) < 0)
    {
        SDL_free(conversion.buf);
        SDL_FreeWAV(source);
        throw std::runtime_error(SDL_GetError());
    }

    const auto sampleCount = conversion.len_cvt / 2;
    const auto *samples = reinterpret_cast<const std::int16_t *>(conversion.buf);
    std::cout << "{\"channels\":" << static_cast<unsigned int>(spec.channels)
        << ",\"frames\":" << sampleCount / spec.channels
        << ",\"sampleRate\":" << spec.freq << ",\"samples\":[";
    for (int index = 0; index < sampleCount; ++index)
    {
        if (index != 0) std::cout << ',';
        std::cout << samples[index];
    }
    std::cout << "]}";
    SDL_free(conversion.buf);
    SDL_FreeWAV(source);
}
}

int main(int argc, char **argv)
{
    try
    {
        if (argc != 3) throw std::runtime_error("usage: wave_pcm_probe <u8.hex> <s16.hex>");
        std::cout << "{\"s16\":";
        writeWave(argv[2]);
        std::cout << ",\"u8\":";
        writeWave(argv[1]);
        std::cout << "}\n";
        return 0;
    }
    catch (const std::exception &error)
    {
        std::cerr << error.what() << '\n';
        return 1;
    }
}

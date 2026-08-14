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

std::vector<unsigned char> makeWave(const char *path, bool tftd)
{
    auto entry = readHex(path);
    if (entry.empty()) throw std::runtime_error("empty CAT entry");
    const auto nameSize = static_cast<std::size_t>(entry[0]);
    if (1 + nameSize > entry.size()) throw std::runtime_error("truncated CAT name");
    std::vector<unsigned char> sound(entry.begin() + 1 + nameSize, entry.end());
    if (sound.size() < 12) throw std::runtime_error("short CAT sound");

    auto *samples = sound.data() + 5;
    const auto sampleCount = sound.size() - 6;
    for (std::size_t index = 0; index < sampleCount; ++index)
        samples[index] = static_cast<unsigned char>(tftd ? samples[index] + 128 : samples[index] * 4);

    const unsigned char header[] = {
        'R','I','F','F',0,0,0,0,'W','A','V','E','f','m','t',' ',16,0,0,0,
        1,0,1,0,0x11,0x2b,0,0,0x11,0x2b,0,0,1,0,8,0,'d','a','t','a',0,0,0,0,
    };
    std::vector<unsigned char> wave(std::begin(header), std::end(header));
    if (tftd)
    {
        wave.insert(wave.end(), samples, samples + sampleCount);
    }
    else
    {
        const std::uint32_t step = (8000u << 16) / 11025u;
        for (std::uint32_t offset = 0; (offset >> 16) < sampleCount; offset += step)
            wave.push_back(samples[offset >> 16]);
    }

    const auto dataSize = static_cast<std::uint32_t>(wave.size() - 44);
    const auto riffSize = dataSize + 36;
    for (int byte = 0; byte < 4; ++byte)
    {
        wave[4 + byte] = static_cast<unsigned char>(riffSize >> (byte * 8));
        wave[40 + byte] = static_cast<unsigned char>(dataSize >> (byte * 8));
    }
    return wave;
}

void writeDecoded(const char *path, bool tftd)
{
    auto encoded = makeWave(path, tftd);
    SDL_RWops *rw = SDL_RWFromConstMem(encoded.data(), static_cast<int>(encoded.size()));
    SDL_AudioSpec spec{};
    Uint8 *source = nullptr;
    Uint32 sourceLength = 0;
    if (rw == nullptr || SDL_LoadWAV_RW(rw, SDL_TRUE, &spec, &source, &sourceLength) == nullptr)
        throw std::runtime_error(SDL_GetError());

    SDL_AudioCVT conversion{};
    if (SDL_BuildAudioCVT(&conversion, spec.format, spec.channels, spec.freq,
            AUDIO_S16LSB, spec.channels, spec.freq) < 0)
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
    const auto *decoded = reinterpret_cast<const std::int16_t *>(conversion.buf);
    std::cout << "{\"channels\":" << static_cast<unsigned int>(spec.channels)
        << ",\"frames\":" << sampleCount / spec.channels
        << ",\"sampleRate\":" << spec.freq << ",\"samples\":[";
    for (int index = 0; index < sampleCount; ++index)
    {
        if (index != 0) std::cout << ',';
        std::cout << decoded[index];
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
        if (argc != 3) throw std::runtime_error("usage: cat_sound_probe <ufo-entry.hex> <tftd-entry.hex>");
        std::cout << "{\"tftd\":";
        writeDecoded(argv[2], true);
        std::cout << ",\"ufo\":";
        writeDecoded(argv[1], false);
        std::cout << "}\n";
        return 0;
    }
    catch (const std::exception &error)
    {
        std::cerr << error.what() << '\n';
        return 1;
    }
}

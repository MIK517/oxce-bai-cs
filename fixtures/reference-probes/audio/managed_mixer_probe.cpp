/*
 * Synthetic OXCE compatibility probe.
 * SPDX-License-Identifier: GPL-3.0-or-later
 */
#define SDL_MAIN_HANDLED
#include <SDL.h>
#include <SDL_mixer.h>
#undef main
#include <algorithm>
#include <cstdint>
#include <fstream>
#include <iostream>
#include <stdexcept>
#include <string>
#include <vector>

namespace
{
struct Capture
{
    std::vector<std::int16_t> samples;
    int count = 0;
};

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

void capturePostMix(void *context, Uint8 *stream, int length)
{
    auto &capture = *static_cast<Capture *>(context);
    const auto *samples = reinterpret_cast<const std::int16_t *>(stream);
    const auto available = length / static_cast<int>(sizeof(std::int16_t));
    const auto wanted = static_cast<int>(capture.samples.size()) - capture.count;
    const auto count = std::min(available, wanted);
    std::copy(samples, samples + count, capture.samples.begin() + capture.count);
    capture.count += count;
}
}

int main(int argc, char **argv)
{
    try
    {
        if (argc != 2) throw std::runtime_error("usage: managed_mixer_probe <stereo.hex>");
        static char audioDriver[] = "SDL_AUDIODRIVER=dummy";
        if (SDL_putenv(audioDriver) != 0) throw std::runtime_error("could not select dummy audio driver");
        if (Mix_OpenAudio(8000, AUDIO_S16LSB, 2, 64) != 0)
            throw std::runtime_error(Mix_GetError());
        Mix_AllocateChannels(16);

        auto encoded = readHex(argv[1]);
        Mix_Chunk *chunk = Mix_QuickLoad_RAW(encoded.data(), static_cast<Uint32>(encoded.size()));
        if (chunk == nullptr)
        {
            Mix_CloseAudio();
            throw std::runtime_error(Mix_GetError());
        }

        Capture capture{std::vector<std::int16_t>(24), 0};
        SDL_LockAudio();
        Mix_SetPostMix(capturePostMix, &capture);
        const int channel = Mix_PlayChannel(5, chunk, 1);
        if (channel < 0)
        {
            SDL_UnlockAudio();
            Mix_FreeChunk(chunk);
            Mix_CloseAudio();
            throw std::runtime_error(Mix_GetError());
        }
        Mix_Volume(channel, 64);
        if (Mix_SetPanning(channel, 255, 128) == 0)
        {
            SDL_UnlockAudio();
            Mix_FreeChunk(chunk);
            Mix_CloseAudio();
            throw std::runtime_error(Mix_GetError());
        }
        SDL_UnlockAudio();

        for (int attempt = 0; attempt < 200 && capture.count < static_cast<int>(capture.samples.size()); ++attempt)
            SDL_Delay(5);
        SDL_LockAudio();
        Mix_SetPostMix(nullptr, nullptr);
        SDL_UnlockAudio();
        if (capture.count != static_cast<int>(capture.samples.size()))
        {
            Mix_FreeChunk(chunk);
            Mix_CloseAudio();
            throw std::runtime_error("timed out waiting for dummy audio output");
        }

        std::cout << "{\"channels\":2,\"frames\":12,\"sampleRate\":8000,\"samples\":[";
        for (std::size_t index = 0; index < capture.samples.size(); ++index)
        {
            if (index != 0) std::cout << ',';
            std::cout << capture.samples[index];
        }
        std::cout << "]}\n";
        Mix_FreeChunk(chunk);
        Mix_CloseAudio();
        return 0;
    }
    catch (const std::exception &error)
    {
        std::cerr << error.what() << '\n';
        return 1;
    }
}

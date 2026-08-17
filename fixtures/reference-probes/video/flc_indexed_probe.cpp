#include <algorithm>
#include <array>
#include <cctype>
#include <cstdint>
#include <fstream>
#include <iomanip>
#include <iostream>
#include <sstream>
#include <stdexcept>
#include <string>
#include <vector>

static uint16_t u16(const std::vector<uint8_t>& data, size_t at)
{
    if (at + 2 > data.size()) throw std::runtime_error("truncated u16");
    return static_cast<uint16_t>(data[at] | (data[at + 1] << 8));
}

static int16_t s16(const std::vector<uint8_t>& data, size_t at)
{
    return static_cast<int16_t>(u16(data, at));
}

static uint32_t u32(const std::vector<uint8_t>& data, size_t at)
{
    if (at + 4 > data.size()) throw std::runtime_error("truncated u32");
    return static_cast<uint32_t>(data[at]) |
        (static_cast<uint32_t>(data[at + 1]) << 8) |
        (static_cast<uint32_t>(data[at + 2]) << 16) |
        (static_cast<uint32_t>(data[at + 3]) << 24);
}

static uint8_t take(const std::vector<uint8_t>& data, size_t& at)
{
    if (at >= data.size()) throw std::runtime_error("truncated byte");
    return data[at++];
}

static std::string hex(const uint8_t* data, size_t count)
{
    std::ostringstream output;
    output << std::uppercase << std::hex << std::setfill('0');
    for (size_t i = 0; i < count; ++i) output << std::setw(2) << static_cast<int>(data[i]);
    return output.str();
}

static void palette(const std::vector<uint8_t>& data, size_t at,
    std::array<std::array<uint8_t, 3>, 256>& colors, bool sixBit, bool cumulative)
{
    uint16_t packets = u16(data, at); at += 2;
    uint16_t previous = 0;
    for (uint16_t packet = 0; packet < packets; ++packet)
    {
        uint16_t start = take(data, at) + (cumulative ? previous : 0);
        uint16_t count = take(data, at); if (count == 0) count = 256;
        if (start + count > colors.size()) throw std::runtime_error("palette overflow");
        for (uint16_t i = 0; i < count; ++i)
        {
            for (int channel = 0; channel < 3; ++channel)
            {
                uint8_t value = take(data, at);
                colors[start + i][channel] = sixBit ? static_cast<uint8_t>(value << 2) : value;
            }
        }
        previous = count + (cumulative && packet + 1 < packets ? 1 : 0);
    }
}

static void brun(const std::vector<uint8_t>& data, size_t at, std::vector<uint8_t>& pixels, int width, int height)
{
    for (int row = 0; row < height; ++row)
    {
        ++at;
        int column = 0;
        while (column != width)
        {
            int8_t count = static_cast<int8_t>(take(data, at));
            if (count > 0)
            {
                uint8_t value = take(data, at);
                while (count--) pixels[row * width + column++] = value;
            }
            else
            {
                count = static_cast<int8_t>(-count);
                while (count--) pixels[row * width + column++] = take(data, at);
            }
        }
    }
}

static void lc(const std::vector<uint8_t>& data, size_t at, std::vector<uint8_t>& pixels, int width)
{
    int row = u16(data, at); at += 2;
    uint16_t lines = u16(data, at); at += 2;
    while (lines--)
    {
        int column = 0;
        uint8_t packets = take(data, at);
        while (packets--)
        {
            column += take(data, at);
            int8_t count = static_cast<int8_t>(take(data, at));
            if (count > 0) while (count--) pixels[row * width + column++] = take(data, at);
            else
            {
                count = static_cast<int8_t>(-count);
                uint8_t value = take(data, at);
                while (count--) pixels[row * width + column++] = value;
            }
        }
        ++row;
    }
}

static void ss2(const std::vector<uint8_t>& data, size_t at, std::vector<uint8_t>& pixels, int width)
{
    uint16_t lines = u16(data, at); at += 2;
    int row = 0;
    while (lines--)
    {
        int16_t count = s16(data, at); at += 2;
        if ((count & 0xC000) == 0xC000)
        {
            row += -count;
            ++lines;
            continue;
        }
        bool setLast = false; uint8_t last = 0;
        if ((count & 0xC000) == 0x8000)
        {
            setLast = true; last = static_cast<uint8_t>(count);
            count = s16(data, at); at += 2;
        }
        int column = 0;
        while (count--)
        {
            column += take(data, at);
            int8_t words = static_cast<int8_t>(take(data, at));
            if (words > 0) while (words--) { pixels[row * width + column++] = take(data, at); pixels[row * width + column++] = take(data, at); }
            else
            {
                words = static_cast<int8_t>(-words);
                uint8_t first = take(data, at), second = take(data, at);
                while (words--) { pixels[row * width + column++] = first; pixels[row * width + column++] = second; }
            }
        }
        if (setLast) pixels[(row + 1) * width - 1] = last;
        ++row;
    }
}

int main(int argc, char** argv)
{
    try
    {
        if (argc != 2) throw std::runtime_error("usage: flc_indexed_probe FILE");
        std::ifstream input(argv[1], std::ios::binary);
        std::string encoded((std::istreambuf_iterator<char>(input)), std::istreambuf_iterator<char>());
        std::vector<uint8_t> data;
        for (size_t i = 0; i < encoded.size();)
        {
            while (i < encoded.size() && std::isspace(static_cast<unsigned char>(encoded[i]))) ++i;
            if (i == encoded.size()) break;
            data.push_back(static_cast<uint8_t>(std::stoul(encoded.substr(i, 2), nullptr, 16))); i += 2;
        }
        if (data.size() < 128 || u16(data, 4) != 0xAF11) throw std::runtime_error("invalid FLI");
        int width = u16(data, 8), height = u16(data, 10);
        std::vector<uint8_t> pixels(width * height);
        std::array<std::array<uint8_t, 3>, 256> colors{};
        size_t frameAt = 128; int frameIndex = 0;
        std::cout << "{\"frames\":[";
        while (frameAt < data.size())
        {
            uint32_t frameSize = u32(data, frameAt);
            if (u16(data, frameAt + 4) != 0xF1FA) throw std::runtime_error("unexpected record");
            uint16_t chunks = u16(data, frameAt + 6); size_t chunkAt = frameAt + 16;
            for (uint16_t i = 0; i < chunks; ++i)
            {
                uint32_t chunkSize = u32(data, chunkAt); uint16_t type = u16(data, chunkAt + 4);
                size_t payload = chunkAt + 6;
                if (type == 0x04) palette(data, payload, colors, false, true);
                else if (type == 0x07) ss2(data, payload, pixels, width);
                else if (type == 0x0B) palette(data, payload, colors, true, false);
                else if (type == 0x0C) lc(data, payload, pixels, width);
                else if (type == 0x0D) for (int row = 0; row < height; ++row) std::fill_n(pixels.begin() + row * width, height, 0);
                else if (type == 0x0F) brun(data, payload, pixels, width, height);
                else if (type == 0x10) std::copy_n(data.begin() + payload, pixels.size(), pixels.begin());
                else if (type != 0x12) throw std::runtime_error("unsupported chunk");
                chunkAt += chunkSize;
            }
            if (frameIndex++) std::cout << ',';
            std::array<uint8_t, 12> selected{};
            for (int i = 0; i < 4; ++i) for (int c = 0; c < 3; ++c) selected[i * 3 + c] = colors[i][c];
            std::cout << "{\"index\":" << frameIndex << ",\"pixels\":\"" << hex(pixels.data(), pixels.size())
                      << "\",\"palette0to3\":\"" << hex(selected.data(), selected.size()) << "\"}";
            frameAt += frameSize;
        }
        std::cout << "],\"height\":" << height << ",\"width\":" << width << "}\n";
        return 0;
    }
    catch (const std::exception& error) { std::cerr << error.what() << '\n'; return 1; }
}

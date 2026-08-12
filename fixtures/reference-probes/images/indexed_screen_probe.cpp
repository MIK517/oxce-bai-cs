/*
 * Synthetic OXCE compatibility probe.
 * SPDX-License-Identifier: GPL-3.0-or-later
 */
#include <algorithm>
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
    if ((digits.size() % 2) != 0)
    {
        throw std::runtime_error("hex value has an odd number of digits");
    }

    std::vector<std::uint8_t> result;
    result.reserve(digits.size() / 2);
    for (std::size_t index = 0; index < digits.size(); index += 2)
    {
        result.push_back(static_cast<std::uint8_t>(std::stoul(digits.substr(index, 2), nullptr, 16)));
    }
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

void setPixelIterative(
    std::vector<std::uint8_t> &pixels,
    int width,
    int height,
    int &x,
    int &y,
    std::uint8_t value)
{
    if (x >= 0 && x < width && y >= 0 && y < height)
    {
        pixels[static_cast<std::size_t>(y * width + x)] = value;
    }
    ++x;
    if (x == width)
    {
        ++y;
        x = 0;
    }
}

std::uint16_t readLe16(const std::vector<std::uint8_t> &input, std::size_t &position)
{
    if (input.size() - position < 2) throw std::runtime_error("truncated word");
    const auto value = static_cast<std::uint16_t>(input[position] | (input[position + 1] << 8));
    position += 2;
    return value;
}

std::vector<std::uint8_t> decodeRaw(
    const std::vector<std::uint8_t> &input,
    int width,
    int height)
{
    std::vector<std::uint8_t> pixels(static_cast<std::size_t>(width * height), 0);
    std::copy_n(input.begin(), std::min(input.size(), pixels.size()), pixels.begin());
    return pixels;
}

std::vector<std::uint8_t> decodeSpk(
    const std::vector<std::uint8_t> &input,
    int width,
    int height)
{
    std::vector<std::uint8_t> pixels(static_cast<std::size_t>(width * height), 0);
    std::size_t position = 0;
    int x = 0;
    int y = 0;
    while (position < input.size() - 1)
    {
        const auto command = readLe16(input, position);
        if (command == 65535)
        {
            const auto count = readLe16(input, position);
            for (int index = 0; index < count * 2; ++index) setPixelIterative(pixels, width, height, x, y, 0);
        }
        else if (command == 65534)
        {
            const auto count = readLe16(input, position);
            for (int index = 0; index < count * 2; ++index)
            {
                if (position == input.size()) throw std::runtime_error("truncated SPK literal");
                setPixelIterative(pixels, width, height, x, y, input[position++]);
            }
        }
    }
    return pixels;
}

std::vector<std::uint8_t> decodeBdy(
    const std::vector<std::uint8_t> &input,
    int width,
    int height)
{
    std::vector<std::uint8_t> pixels(static_cast<std::size_t>(width * height), 0);
    std::size_t position = 0;
    int x = 0;
    int y = 0;
    while (position < input.size())
    {
        const auto control = input[position++];
        if (control >= 129)
        {
            const auto count = 257 - static_cast<int>(control);
            if (position == input.size()) throw std::runtime_error("truncated BDY repeat");
            const auto value = input[position++];
            const auto currentRow = y;
            for (int index = 0; index < count; ++index)
            {
                setPixelIterative(pixels, width, height, x, y, value);
                if (currentRow != y) break;
            }
        }
        else
        {
            const auto count = 1 + static_cast<int>(control);
            const auto currentRow = y;
            for (int index = 0; index < count; ++index)
            {
                if (position == input.size()) throw std::runtime_error("truncated BDY literal");
                const auto value = input[position++];
                if (currentRow == y) setPixelIterative(pixels, width, height, x, y, value);
            }
        }
    }
    return pixels;
}

void printPixels(const std::vector<std::uint8_t> &pixels)
{
    std::cout << '[';
    for (std::size_t index = 0; index < pixels.size(); ++index)
    {
        if (index != 0) std::cout << ',';
        std::cout << static_cast<unsigned int>(pixels[index]);
    }
    std::cout << ']';
}
}

int main(int argc, char **argv)
{
    try
    {
        if (argc != 2) throw std::runtime_error("usage: indexed_screen_probe <fixture.hex>");
        const auto fixture = readFixture(argv[1]);
        const auto width = std::stoi(fixture.at("width"));
        const auto height = std::stoi(fixture.at("height"));
        const auto raw = decodeRaw(parseHex(fixture.at("raw")), width, height);
        const auto spk = decodeSpk(parseHex(fixture.at("spk")), width, height);
        const auto bdy = decodeBdy(parseHex(fixture.at("bdy")), width, height);

        std::cout << "{\"bdy\":";
        printPixels(bdy);
        std::cout << ",\"height\":" << height << ",\"raw\":";
        printPixels(raw);
        std::cout << ",\"spk\":";
        printPixels(spk);
        std::cout << ",\"width\":" << width << "}\n";
        return 0;
    }
    catch (const std::exception &error)
    {
        std::cerr << error.what() << '\n';
        return 1;
    }
}

/*
 * Synthetic OXCE compatibility probe.
 * SPDX-License-Identifier: GPL-3.0-or-later
 */
#include <bit>
#include <cctype>
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
    if (!input)
    {
        throw std::runtime_error("could not open fixture");
    }

    std::string digits;
    for (char value; input.get(value);)
    {
        if (!std::isspace(static_cast<unsigned char>(value)))
        {
            digits.push_back(value);
        }
    }

    if ((digits.size() % 2) != 0)
    {
        throw std::runtime_error("hex fixture has an odd number of digits");
    }

    std::vector<std::uint8_t> bytes;
    bytes.reserve(digits.size() / 2);
    for (std::size_t index = 0; index < digits.size(); index += 2)
    {
        const auto token = digits.substr(index, 2);
        bytes.push_back(static_cast<std::uint8_t>(std::stoul(token, nullptr, 16)));
    }

    return bytes;
}

std::uint64_t readUnsigned(
    const std::vector<std::uint8_t> &bytes,
    std::size_t &position,
    std::size_t width,
    bool littleEndian)
{
    if (width > bytes.size() - position)
    {
        throw std::runtime_error("fixture ended unexpectedly");
    }

    std::uint64_t value = 0;
    for (std::size_t index = 0; index < width; ++index)
    {
        const auto shift = littleEndian ? index : width - index - 1;
        value |= static_cast<std::uint64_t>(bytes[position + index]) << (shift * 8);
    }

    position += width;
    return value;
}
}

int main(int argc, char **argv)
{
    try
    {
        if (argc != 2)
        {
            throw std::runtime_error("usage: binary_primitive_probe <fixture.hex>");
        }

        const auto bytes = readHex(argv[1]);
        std::size_t position = 0;
        const auto byteValue = readUnsigned(bytes, position, 1, true);
        const auto signedByte = static_cast<std::int8_t>(readUnsigned(bytes, position, 1, true));
        const auto uint16Little = readUnsigned(bytes, position, 2, true);
        const auto uint16Big = readUnsigned(bytes, position, 2, false);
        const auto uint32Little = readUnsigned(bytes, position, 4, true);
        const auto uint32Big = readUnsigned(bytes, position, 4, false);
        const auto singleLittleBits = static_cast<std::uint32_t>(readUnsigned(bytes, position, 4, true));
        const auto singleBigBits = static_cast<std::uint32_t>(readUnsigned(bytes, position, 4, false));
        const auto uint64Little = readUnsigned(bytes, position, 8, true);
        if (position != bytes.size())
        {
            throw std::runtime_error("fixture has trailing bytes");
        }

        std::cout << std::setprecision(9)
                  << "{\"byte\":\"" << byteValue
                  << "\",\"signedByte\":\"" << static_cast<int>(signedByte)
                  << "\",\"singleBig\":\"" << std::bit_cast<float>(singleBigBits)
                  << "\",\"singleLittle\":\"" << std::bit_cast<float>(singleLittleBits)
                  << "\",\"uint16Big\":\"" << uint16Big
                  << "\",\"uint16Little\":\"" << uint16Little
                  << "\",\"uint32Big\":\"" << uint32Big
                  << "\",\"uint32Little\":\"" << uint32Little
                  << "\",\"uint64Little\":\"" << uint64Little << "\"}\n";
        return 0;
    }
    catch (const std::exception &error)
    {
        std::cerr << error.what() << '\n';
        return 1;
    }
}

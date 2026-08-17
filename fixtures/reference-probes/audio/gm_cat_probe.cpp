/*
 * Synthetic OXCE compatibility probe.
 * SPDX-License-Identifier: GPL-3.0-or-later
 */
#include <array>
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
struct Sequence
{
    std::vector<std::uint8_t> data;
};

struct Track
{
    std::uint8_t channel;
    Sequence sequence;
};

struct Stream
{
    std::uint8_t tempo;
    std::vector<Sequence> subsequences;
    std::vector<Track> tracks;
};

struct Status
{
    std::uint32_t delta = 0;
    std::uint32_t patch = 0;
    std::uint8_t previousCommand = 0;
};

constexpr std::array<std::uint32_t, 128> Volume = {
    100,100,100,100,100,90,100,100,100,100,100,90,100,100,100,100,
    100,100,85,100,100,100,100,100,100,100,100,100,90,90,110,80,
    100,100,100,90,70,100,100,100,100,100,100,100,100,100,100,100,
    100,100,90,100,100,100,100,100,100,120,100,100,100,120,100,127,
    100,100,90,100,100,100,100,100,100,95,100,100,100,100,100,100,
    100,100,100,100,100,100,100,115,100,100,100,100,100,100,100,100,
    100,100,100,100,100,100,100,100,100,100,100,100,100,100,100,100,
    100,100,100,100,100,100,100,100,100,100,100,100,100,100,100,100,
};

std::vector<std::uint8_t> readHex(const char *path)
{
    std::ifstream input(path);
    if (!input) throw std::runtime_error("could not open fixture");
    std::string digits;
    input >> digits;
    if ((digits.size() % 2) != 0) throw std::runtime_error("odd hex length");
    std::vector<std::uint8_t> result;
    for (std::size_t index = 0; index < digits.size(); index += 2)
        result.push_back(static_cast<std::uint8_t>(std::stoul(digits.substr(index, 2), nullptr, 16)));
    return result;
}

std::uint32_t readLe32(const std::vector<std::uint8_t> &data, std::size_t &position)
{
    if (position + 4 > data.size()) throw std::runtime_error("truncated size");
    const auto result = static_cast<std::uint32_t>(data[position]) |
        (static_cast<std::uint32_t>(data[position + 1]) << 8) |
        (static_cast<std::uint32_t>(data[position + 2]) << 16) |
        (static_cast<std::uint32_t>(data[position + 3]) << 24);
    position += 4;
    return result;
}

Sequence readSequence(const std::vector<std::uint8_t> &data, std::size_t &position)
{
    const auto storedSize = readLe32(data, position);
    if (storedSize < 4 || storedSize - 4 > data.size() - position)
        throw std::runtime_error("invalid sequence size");
    Sequence result;
    result.data.assign(data.begin() + position, data.begin() + position + storedSize - 4);
    position += storedSize - 4;
    return result;
}

Stream readStream(const std::vector<std::uint8_t> &entry)
{
    if (entry.empty()) throw std::runtime_error("empty entry");
    const std::size_t nameSkip = static_cast<std::size_t>(entry[0]) + 1;
    if (nameSkip > entry.size()) throw std::runtime_error("invalid name prefix");
    std::vector<std::uint8_t> data(entry.begin() + nameSkip, entry.end());
    std::size_t position = 0;
    if (data.size() < 2) throw std::runtime_error("truncated stream");
    Stream result;
    result.tempo = data[position++];
    const auto subsequenceCount = data[position++];
    for (std::uint32_t index = 0; index < subsequenceCount; ++index)
        result.subsequences.push_back(readSequence(data, position));
    if (position == data.size()) throw std::runtime_error("missing tracks");
    const auto trackCount = data[position++];
    for (std::uint32_t index = 0; index < trackCount; ++index)
    {
        if (position == data.size()) throw std::runtime_error("missing track channel");
        const auto channel = data[position++];
        result.tracks.push_back({channel, readSequence(data, position)});
    }
    if (position != data.size()) throw std::runtime_error("trailing stream data");
    return result;
}

void writeBe16(std::vector<std::uint8_t> &output, std::uint32_t value)
{
    output.push_back(static_cast<std::uint8_t>(value >> 8));
    output.push_back(static_cast<std::uint8_t>(value));
}

void writeBe32(std::vector<std::uint8_t> &output, std::uint32_t value)
{
    output.push_back(static_cast<std::uint8_t>(value >> 24));
    output.push_back(static_cast<std::uint8_t>(value >> 16));
    output.push_back(static_cast<std::uint8_t>(value >> 8));
    output.push_back(static_cast<std::uint8_t>(value));
}

void writeDelta(std::vector<std::uint8_t> &output, std::uint32_t delta)
{
    std::uint8_t bytes[4];
    std::uint32_t count = 0;
    delta &= 0x0FFFFFFF;
    do
    {
        bytes[count++] = static_cast<std::uint8_t>(delta & 0x7F);
        delta >>= 7;
    } while (delta > 0 && count <= 3);
    while (--count) output.push_back(static_cast<std::uint8_t>(bytes[count] | 0x80));
    output.push_back(bytes[0]);
}

void writeTempo(std::vector<std::uint8_t> &output, std::uint32_t tempo)
{
    output.push_back(0xFF);
    output.push_back(0x51);
    output.push_back(3);
    const auto microseconds = 60000000 / tempo;
    output.push_back(static_cast<std::uint8_t>(microseconds >> 16));
    output.push_back(static_cast<std::uint8_t>(microseconds >> 8));
    output.push_back(static_cast<std::uint8_t>(microseconds));
}

void writeEnd(std::vector<std::uint8_t> &output)
{
    output.push_back(0xFF);
    output.push_back(0x2F);
    output.push_back(0);
}

std::uint32_t readDelta(const Sequence &sequence, std::size_t &position)
{
    std::uint32_t result = 0;
    for (int index = 0;;)
    {
        if (position == sequence.data.size()) throw std::runtime_error("truncated delta");
        const auto value = sequence.data[position++];
        result += value & 0x7F;
        if ((value & 0x80) == 0) return result;
        if (++index == 4 || position == sequence.data.size()) throw std::runtime_error("invalid delta");
        result <<= 7;
    }
}

void writeSequence(
    std::vector<std::uint8_t> &output,
    const Stream &stream,
    std::uint8_t channel,
    const Sequence &sequence,
    Status &status)
{
    std::size_t position = 0;
    std::uint8_t command = 0xFF;
    while (position < sequence.data.size())
    {
        status.delta += readDelta(sequence, position);
        if (position == sequence.data.size()) throw std::runtime_error("missing command");
        if ((sequence.data[position] & 0x80) != 0)
        {
            command = sequence.data[position++];
            if (command == 0xFF || command == 0xFD) return;
            if (command == 0xFE)
            {
                if (position == sequence.data.size()) throw std::runtime_error("missing subsequence");
                const auto index = sequence.data[position++];
                if (index >= stream.subsequences.size()) throw std::runtime_error("invalid subsequence");
                writeSequence(output, stream, channel, stream.subsequences[index], status);
                command = 0;
                continue;
            }
            command &= 0xF0;
        }
        else if (command == 0)
        {
            throw std::runtime_error("invalid running status");
        }

        if (position == sequence.data.size()) throw std::runtime_error("missing data byte");
        auto first = sequence.data[position++];
        switch (command)
        {
            case 0x80:
            case 0x90:
            {
                if (position == sequence.data.size()) throw std::runtime_error("missing velocity");
                auto second = sequence.data[position++];
                if (second != 0)
                    second = static_cast<std::uint8_t>(
                        static_cast<std::uint32_t>(second) * (channel == 9 ? 80 : Volume[status.patch]) >> 7);
                writeDelta(output, status.delta);
                output.push_back(static_cast<std::uint8_t>(command | channel));
                output.push_back(first);
                output.push_back(second);
                break;
            }
            case 0xC0:
                if (first == 0x7E) return;
                status.patch = first;
                if (first == 0x57 || first == 0x3F) first = 0x3E;
                writeDelta(output, status.delta);
                output.push_back(static_cast<std::uint8_t>(command | channel));
                output.push_back(first);
                break;
            case 0xB0:
            {
                if (position == sequence.data.size()) throw std::runtime_error("missing control value");
                auto second = sequence.data[position++];
                if (first == 0x7E) continue;
                if (first == 0)
                {
                    if (second == 0) continue;
                    writeDelta(output, status.delta);
                    writeTempo(output, 2 * second);
                    break;
                }
                if (first == 0x5B) second = 0x1E;
                writeDelta(output, status.delta);
                output.push_back(static_cast<std::uint8_t>(command | channel));
                output.push_back(first);
                output.push_back(second);
                break;
            }
            case 0xE0:
                if (position == sequence.data.size()) throw std::runtime_error("missing pitch value");
                writeDelta(output, status.delta);
                output.push_back(static_cast<std::uint8_t>(command | channel));
                output.push_back(first);
                output.push_back(sequence.data[position++]);
                break;
            default:
                throw std::runtime_error("unsupported command");
        }
        status.delta = 0;
    }
}

std::vector<std::uint8_t> convert(const Stream &stream)
{
    std::vector<std::uint8_t> output = {'M','T','h','d',0,0,0,6};
    writeBe16(output, 1);
    writeBe16(output, static_cast<std::uint32_t>(stream.tracks.size() + 1));
    writeBe16(output, 24);
    output.insert(output.end(), {'M','T','r','k',0,0,0,11,0});
    writeTempo(output, stream.tempo);
    output.push_back(0);
    writeEnd(output);

    for (const auto &track : stream.tracks)
    {
        output.insert(output.end(), {'M','T','r','k'});
        const auto lengthOffset = output.size();
        writeBe32(output, 0);
        output.insert(output.end(), {
            0, static_cast<std::uint8_t>(0xB0 | track.channel),
            0x78,0,0,0x79,0,0,0x7B,0,
        });
        Status status;
        writeSequence(output, stream, track.channel, track.sequence, status);
        writeDelta(output, status.delta);
        writeEnd(output);
        const auto length = static_cast<std::uint32_t>(output.size() - lengthOffset - 4);
        output[lengthOffset] = static_cast<std::uint8_t>(length >> 24);
        output[lengthOffset + 1] = static_cast<std::uint8_t>(length >> 16);
        output[lengthOffset + 2] = static_cast<std::uint8_t>(length >> 8);
        output[lengthOffset + 3] = static_cast<std::uint8_t>(length);
    }
    return output;
}

std::string toHex(const std::vector<std::uint8_t> &data)
{
    std::ostringstream output;
    output << std::uppercase << std::hex << std::setfill('0');
    for (const auto value : data) output << std::setw(2) << static_cast<unsigned>(value);
    return output.str();
}
}

int main(int argc, char **argv)
{
    try
    {
        if (argc != 2) throw std::runtime_error("usage: gm_cat_probe <fixture.hex>");
        const auto midi = convert(readStream(readHex(argv[1])));
        std::cout << "{\"length\":" << midi.size() << ",\"midi\":\"" << toHex(midi) << "\"}\n";
        return 0;
    }
    catch (const std::exception &error)
    {
        std::cerr << error.what() << '\n';
        return 1;
    }
}

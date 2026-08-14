/*
 * Synthetic OXCE compatibility probe.
 * SPDX-License-Identifier: GPL-3.0-or-later
 */
#include <cmath>
#include <fstream>
#include <iomanip>
#include <iostream>
#include <sstream>
#include <stdexcept>
#include <string>
#include <vector>

namespace
{
struct PointerCase
{
    double windowX;
    double windowY;
    double scaleX;
    double scaleY;
    int leftBand;
    int topBand;
    int surfaceX;
    int surfaceY;
};

int mixerVolume(int setting)
{
    constexpr double gradient = 10.0;
    constexpr int maximum = 128;
    const auto gain = (std::exp(std::log(gradient + 1.0) * setting / static_cast<double>(maximum)) - 1.0) / gradient;
    return static_cast<int>(gain * maximum);
}
}

int main(int argc, char **argv)
{
    try
    {
        if (argc != 2) throw std::runtime_error("usage: input_audio_probe <fixture.tsv>");
        std::ifstream input(argv[1]);
        if (!input) throw std::runtime_error("could not open fixture");
        std::vector<PointerCase> pointers;
        std::vector<int> volumes;
        std::string line;
        while (std::getline(input, line))
        {
            if (line.empty()) continue;
            std::istringstream fields(line);
            std::string kind;
            fields >> kind;
            if (kind == "pointer")
            {
                PointerCase value{};
                fields >> value.windowX >> value.windowY >> value.scaleX >> value.scaleY
                    >> value.leftBand >> value.topBand >> value.surfaceX >> value.surfaceY;
                if (!fields) throw std::runtime_error("invalid pointer case");
                pointers.push_back(value);
            }
            else if (kind == "volume")
            {
                int value = 0;
                fields >> value;
                if (!fields || value < 0 || value > 128) throw std::runtime_error("invalid volume case");
                volumes.push_back(value);
            }
            else
            {
                throw std::runtime_error("unknown fixture case");
            }
        }

        std::cout << std::setprecision(17) << "{\"pointers\":[";
        for (std::size_t index = 0; index < pointers.size(); ++index)
        {
            if (index != 0) std::cout << ',';
            const auto &value = pointers[index];
            const auto mouseX = value.windowX - value.leftBand;
            const auto mouseY = value.windowY - value.topBand;
            std::cout << "{\"logicalX\":" << mouseX / value.scaleX
                << ",\"logicalY\":" << mouseY / value.scaleY
                << ",\"surfaceX\":" << mouseX - value.surfaceX * value.scaleX
                << ",\"surfaceY\":" << mouseY - value.surfaceY * value.scaleY
                << ",\"windowX\":" << mouseX
                << ",\"windowY\":" << mouseY << '}';
        }

        std::cout << "],\"volumes\":[";
        for (std::size_t index = 0; index < volumes.size(); ++index)
        {
            if (index != 0) std::cout << ',';
            std::cout << "{\"mixerVolume\":" << mixerVolume(volumes[index])
                << ",\"setting\":" << volumes[index] << '}';
        }
        std::cout << "]}\n";
        return 0;
    }
    catch (const std::exception &error)
    {
        std::cerr << error.what() << '\n';
        return 1;
    }
}

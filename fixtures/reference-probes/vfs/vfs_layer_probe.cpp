/*
 * Synthetic OXCE compatibility probe.
 * SPDX-License-Identifier: GPL-3.0-or-later
 */
#include <algorithm>
#include <cctype>
#include <fstream>
#include <iostream>
#include <map>
#include <set>
#include <sstream>
#include <stdexcept>
#include <string>
#include <vector>

namespace
{
struct Entry
{
    std::string source;
    std::string layer;
    std::string mod;
};

struct Layer
{
    std::string id;
    std::string mod;
    std::map<std::string, Entry> resources;
    std::vector<Entry> rulesets;
    std::map<std::string, std::set<std::string>> directories;
};

std::string canonicalize(std::string value)
{
    std::transform(value.begin(), value.end(), value.begin(), [](unsigned char character) {
        return static_cast<char>(std::tolower(character));
    });
    return value;
}

bool isRuleset(const std::string &path)
{
    return path.size() >= 4 && path.substr(path.size() - 4) == ".rul";
}

std::vector<std::string> columns(const std::string &line)
{
    std::vector<std::string> result;
    std::size_t start = 0;
    for (;;)
    {
        const auto separator = line.find('\t', start);
        result.push_back(line.substr(start, separator - start));
        if (separator == std::string::npos)
        {
            return result;
        }
        start = separator + 1;
    }
}

const Entry *find(const Layer &layer, const std::string &path)
{
    const auto match = layer.resources.find(canonicalize(path));
    return match == layer.resources.end() ? nullptr : &match->second;
}

void printStringArray(const std::vector<std::string> &values)
{
    std::cout << '[';
    for (std::size_t index = 0; index < values.size(); ++index)
    {
        if (index != 0) std::cout << ',';
        std::cout << '\"' << values[index] << '\"';
    }
    std::cout << ']';
}
}

int main(int argc, char **argv)
{
    try
    {
        if (argc != 2)
        {
            throw std::runtime_error("usage: vfs_layer_probe <fixture.tsv>");
        }

        std::ifstream input(argv[1]);
        if (!input)
        {
            throw std::runtime_error("could not open fixture");
        }

        std::vector<Layer> layers;
        std::string line;
        while (std::getline(input, line))
        {
            if (!line.empty() && line.back() == '\r') line.pop_back();
            if (line.empty()) continue;
            const auto values = columns(line);
            if (values.size() != 4)
            {
                throw std::runtime_error("fixture row must contain four columns");
            }

            if (layers.empty() || layers.back().id != values[0])
            {
                layers.push_back(Layer{values[0], values[1], {}, {}, {}});
            }
            auto &layer = layers.back();
            const auto path = canonicalize(values[2]);
            const Entry entry{values[3], layer.id, layer.mod};
            if (isRuleset(path))
            {
                layer.rulesets.push_back(entry);
                continue;
            }

            layer.resources[path] = entry;
            const auto separator = path.find_last_of('/');
            const auto directory = separator == std::string::npos ? "" : path.substr(0, separator);
            const auto basename = path.substr(separator == std::string::npos ? 0 : separator + 1);
            layer.directories[directory].insert(basename);
        }

        if (layers.size() != 3)
        {
            throw std::runtime_error("fixture must define three layers");
        }

        const std::string lookup = "GEOGRAPH/world.dat";
        const Entry *winner = nullptr;
        std::vector<const Entry *> slice;
        std::set<std::string> language;
        std::vector<std::string> rulesets;
        for (const auto &layer : layers)
        {
            const auto entry = find(layer, lookup);
            slice.push_back(entry);
            if (entry != nullptr) winner = entry;
            const auto directory = layer.directories.find("language");
            if (directory != layer.directories.end())
            {
                language.insert(directory->second.begin(), directory->second.end());
            }
            for (const auto &ruleset : layer.rulesets) rulesets.push_back(ruleset.source);
        }

        if (winner == nullptr)
        {
            throw std::runtime_error("fixture lookup has no winner");
        }

        std::cout << "{\"language\":";
        printStringArray(std::vector<std::string>(language.begin(), language.end()));
        std::cout << ",\"rulesets\":";
        printStringArray(rulesets);
        std::cout << ",\"slice\":[";
        for (std::size_t index = 0; index < slice.size(); ++index)
        {
            if (index != 0) std::cout << ',';
            if (slice[index] == nullptr) std::cout << "null";
            else std::cout << '\"' << slice[index]->source << '\"';
        }
        std::cout << "],\"winner\":{\"layer\":\"" << winner->layer
                  << "\",\"mod\":\"" << winner->mod
                  << "\",\"source\":\"" << winner->source << "\"}}\n";
        return 0;
    }
    catch (const std::exception &error)
    {
        std::cerr << error.what() << '\n';
        return 1;
    }
}

/*
 * Synthetic OXCE compatibility probe.
 * SPDX-License-Identifier: GPL-3.0-or-later
 */
#include "ryml.hpp"
#include "ryml_std.hpp"

#include <cmath>
#include <fstream>
#include <iomanip>
#include <iostream>
#include <limits>
#include <sstream>
#include <string>

static ryml::ConstNodeRef child(const ryml::ConstNodeRef& node, const char* key)
{
    return node.find_child(ryml::to_csubstr(key));
}

static std::string scalar(const ryml::ConstNodeRef& node)
{
    const auto value = node.val();
    return std::string(value.str, value.len);
}

static void writeString(std::ostream& output, const std::string& text)
{
    output << '"';
    for (const char character : text)
    {
        if (character == '"' || character == '\\')
        {
            output << '\\';
        }
        output << character;
    }
    output << '"';
}

template<typename T>
static void writeIntegerResult(std::ostream& output, const ryml::ConstNodeRef& node)
{
    T value{};
    const bool success = c4::yml::read(node, &value);
    if (!success)
    {
        output << "null";
    }
    else if constexpr (std::is_signed_v<T>)
    {
        writeString(output, std::to_string(static_cast<std::int64_t>(value)));
    }
    else
    {
        writeString(output, std::to_string(static_cast<std::uint64_t>(value)));
    }
}

template<typename T>
static std::string floatingText(T value)
{
    if (std::isnan(value))
    {
        return ".nan";
    }
    if (value == std::numeric_limits<T>::infinity())
    {
        return ".inf";
    }
    if (value == -std::numeric_limits<T>::infinity())
    {
        return "-.inf";
    }

    std::ostringstream output;
    output << std::setprecision(std::numeric_limits<T>::max_digits10) << value;
    return output.str();
}

template<typename T>
static void writeFloatingResult(std::ostream& output, const ryml::ConstNodeRef& node)
{
    T value{};
    const bool success = c4::yml::read(node, &value);
    if (!success)
    {
        output << "null";
    }
    else
    {
        writeString(output, floatingText(value));
    }
}

int main(int argc, char** argv)
{
    if (argc != 2)
    {
        std::cerr << "Usage: yaml_scalar_probe <fixture>\n";
        return 2;
    }

    std::ifstream input(argv[1], std::ios::binary);
    std::ostringstream buffer;
    buffer << input.rdbuf();
    if (!input)
    {
        std::cerr << "Could not read fixture.\n";
        return 3;
    }

    const std::string yaml = buffer.str();
    ryml::Tree tree = ryml::parse_in_arena(ryml::to_csubstr(argv[1]), ryml::to_csubstr(yaml));
    const auto root = tree.crootref();

    std::cout << "{\"booleanColumns\":[\"name\",\"value\"],\"booleans\":[";
    bool first = true;
    for (const auto testCase : child(root, "booleans").cchildren())
    {
        if (!first) std::cout << ',';
        first = false;
        bool value{};
        const bool success = c4::yml::read(child(testCase, "value"), &value);
        std::cout << '[';
        writeString(std::cout, scalar(child(testCase, "name")));
        std::cout << ',';
        if (success) std::cout << (value ? "true" : "false"); else std::cout << "null";
        std::cout << ']';
    }

    std::cout << "],\"floating\":[";
    first = true;
    for (const auto testCase : child(root, "floating").cchildren())
    {
        if (!first) std::cout << ',';
        first = false;
        const auto valueNode = child(testCase, "value");
        std::cout << '[';
        writeString(std::cout, scalar(child(testCase, "name")));
        std::cout << ',';
        writeFloatingResult<float>(std::cout, valueNode);
        std::cout << ',';
        writeFloatingResult<double>(std::cout, valueNode);
        std::cout << ']';
    }

    std::cout << "],\"floatingColumns\":[\"name\",\"float\",\"double\"],\"integerColumns\":[\"name\",\"int8\",\"uint8\",\"int32\",\"uint32\",\"int64\",\"uint64\"],\"integers\":[";
    first = true;
    for (const auto testCase : child(root, "integers").cchildren())
    {
        if (!first) std::cout << ',';
        first = false;
        const auto valueNode = child(testCase, "value");
        std::cout << '[';
        writeString(std::cout, scalar(child(testCase, "name")));
        std::cout << ',';
        writeIntegerResult<std::int8_t>(std::cout, valueNode);
        std::cout << ',';
        writeIntegerResult<std::uint8_t>(std::cout, valueNode);
        std::cout << ',';
        writeIntegerResult<std::int32_t>(std::cout, valueNode);
        std::cout << ',';
        writeIntegerResult<std::uint32_t>(std::cout, valueNode);
        std::cout << ',';
        writeIntegerResult<std::int64_t>(std::cout, valueNode);
        std::cout << ',';
        writeIntegerResult<std::uint64_t>(std::cout, valueNode);
        std::cout << ']';
    }
    std::cout << "]}\n";
    return 0;
}

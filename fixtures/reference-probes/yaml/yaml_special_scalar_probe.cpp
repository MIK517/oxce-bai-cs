/*
 * Synthetic OXCE compatibility probe.
 * SPDX-License-Identifier: GPL-3.0-or-later
 */
#include "ryml.hpp"
#include "ryml_std.hpp"

#include <cmath>
#include <cstdint>
#include <fstream>
#include <iomanip>
#include <iostream>
#include <limits>
#include <sstream>
#include <string>
#include <vector>

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
        if (character == '"' || character == '\\') output << '\\';
        output << character;
    }
    output << '"';
}

template<typename T>
static std::string floatingText(T value)
{
    if (std::isnan(value)) return ".nan";
    if (value == std::numeric_limits<T>::infinity()) return ".inf";
    if (value == -std::numeric_limits<T>::infinity()) return "-.inf";
    std::ostringstream output;
    output << std::setprecision(std::numeric_limits<T>::max_digits10) << value;
    return output.str();
}

template<typename T>
static void writeFloating(std::ostream& output, const ryml::ConstNodeRef& node)
{
    T value{};
    if (c4::yml::read(node, &value)) writeString(output, floatingText(value));
    else output << "null";
}

static std::string hexadecimal(const std::vector<char>& bytes)
{
    static constexpr char digits[] = "0123456789abcdef";
    std::string result;
    result.reserve(bytes.size() * 2);
    for (const unsigned char value : bytes)
    {
        result.push_back(digits[value >> 4]);
        result.push_back(digits[value & 0x0f]);
    }
    return result;
}

int main(int argc, char** argv)
{
    if (argc != 2)
    {
        std::cerr << "Usage: yaml_special_scalar_probe <fixture>\n";
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

    std::cout << "{\"hexFloatingColumns\":[\"name\",\"float\",\"double\"],\"hexFloating\":[";
    bool first = true;
    for (const auto testCase : child(root, "hexFloating").cchildren())
    {
        if (!first) std::cout << ',';
        first = false;
        const auto value = child(testCase, "value");
        std::cout << '[';
        writeString(std::cout, scalar(child(testCase, "name")));
        std::cout << ',';
        writeFloating<float>(std::cout, value);
        std::cout << ',';
        writeFloating<double>(std::cout, value);
        std::cout << ']';
    }

    std::cout << "],\"base64Columns\":[\"name\",\"valid\",\"hex\"],\"base64\":[";
    first = true;
    for (const auto testCase : child(root, "base64").cchildren())
    {
        if (!first) std::cout << ',';
        first = false;
        const auto encoded = scalar(child(testCase, "value"));
        const bool valid = c4::base64_valid(ryml::to_csubstr(encoded));
        std::cout << '[';
        writeString(std::cout, scalar(child(testCase, "name")));
        std::cout << ',' << (valid ? "true" : "false") << ',';
        if (valid)
        {
            const auto source = ryml::to_csubstr(encoded);
            const std::size_t length = c4::base64_decode(source, c4::blob());
            std::vector<char> decoded(length);
            c4::base64_decode(source, c4::blob(decoded.data(), decoded.size()));
            writeString(std::cout, hexadecimal(decoded));
        }
        else
        {
            std::cout << "null";
        }
        std::cout << ']';
    }
    std::cout << "]}\n";
    return 0;
}

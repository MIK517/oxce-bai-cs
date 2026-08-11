/*
 * Synthetic OXCE compatibility probe.
 * SPDX-License-Identifier: GPL-3.0-or-later
 */
#define _C4_YML_STD_MAP_HPP_
#define _C4_YML_STD_VECTOR_HPP_

#include "ryml.hpp"
#include "ryml_std.hpp"

#include <array>
#include <cstdint>
#include <fstream>
#include <iostream>
#include <map>
#include <sstream>
#include <string>
#include <tuple>
#include <type_traits>
#include <vector>

namespace OpenXcom
{

enum class ProbeEnum
{
    Zero = 0,
};

template <typename EnumType>
typename std::enable_if<std::is_enum<EnumType>::value, bool>::type from_chars(
    ryml::csubstr buffer,
    EnumType* value) noexcept
{
    int parsed = static_cast<int>(*value);
    const bool result = ryml::atoi(buffer, &parsed);
    *value = static_cast<EnumType>(parsed);
    return result;
}

} // namespace OpenXcom

namespace std
{

template <class T1, class T2>
bool read(ryml::ConstNodeRef const& node, std::pair<T1, T2>* pair)
{
    if (!node.is_seq() || node.num_children() != 2) return false;
    node.first_child() >> pair->first;
    node.last_child() >> pair->second;
    return true;
}

template <class... T>
bool read(ryml::ConstNodeRef const& node, std::tuple<T...>* tuple)
{
    if (!node.is_seq() || node.num_children() != sizeof...(T)) return false;
    auto current = node.first_child();
    std::apply(
        [&](auto&& first, auto&&... remaining)
        {
            current >> first;
            (((current = current.next_sibling()), current >> remaining), ...);
        },
        *tuple);
    return true;
}

template <typename T, std::size_t Size>
bool read(ryml::ConstNodeRef const& node, std::array<T, Size>* array)
{
    if (!node.is_seq() || node.num_children() != Size) return false;
    auto current = node.first_child();
    std::apply(
        [&](auto&& first, auto&&... remaining)
        {
            current >> first;
            (((current = current.next_sibling()), current >> remaining), ...);
        },
        *array);
    return true;
}

} // namespace std

namespace c4::yml
{

template <class Value, class Allocator>
bool read(c4::yml::ConstNodeRef const& node, std::vector<Value, Allocator>* values)
{
    values->clear();
    values->resize(static_cast<size_t>(node.num_children()));
    size_t position = 0;
    for (ConstNodeRef const child : node)
    {
        child >> (*values)[position++];
    }
    return true;
}

template <class Allocator>
bool read(c4::yml::ConstNodeRef const& node, std::vector<bool, Allocator>* values)
{
    values->clear();
    values->resize(static_cast<size_t>(node.num_children()));
    size_t position = 0;
    bool temporary{};
    for (ConstNodeRef const child : node)
    {
        child >> temporary;
        (*values)[position++] = temporary;
    }
    return true;
}

template <class Key, class Value, class Less, class Allocator>
bool read(c4::yml::ConstNodeRef const& node, std::map<Key, Value, Less, Allocator>* values)
{
    values->clear();
    for (ConstNodeRef const child : node)
    {
        Key key{};
        Value value{};
        child >> c4::yml::key(key);
        child >> value;
        values->emplace(std::make_pair(std::move(key), std::move(value)));
    }
    return true;
}

} // namespace c4::yml

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

template<typename Value>
static void writeInteger(std::ostream& output, Value value)
{
    writeString(output, std::to_string(static_cast<std::int64_t>(value)));
}

template<typename Value>
static void writeIntegerVector(std::ostream& output, const std::vector<Value>& values)
{
    output << '[';
    bool first = true;
    for (const auto value : values)
    {
        if (!first) output << ',';
        first = false;
        writeInteger(output, value);
    }
    output << ']';
}

template<typename Key, typename Value>
static void writeIntegerMap(std::ostream& output, const std::map<Key, Value>& values)
{
    output << '[';
    bool first = true;
    for (const auto& [key, value] : values)
    {
        if (!first) output << ',';
        first = false;
        output << '[';
        if constexpr (std::is_integral_v<Key>) writeInteger(output, key); else writeString(output, key);
        output << ',';
        if constexpr (std::is_integral_v<Value>) writeInteger(output, value); else writeString(output, value);
        output << ']';
    }
    output << ']';
}

int main(int argc, char** argv)
{
    if (argc != 2)
    {
        std::cerr << "Usage: yaml_container_probe <fixture>\n";
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

    std::cout << "{\"enumColumns\":[\"name\",\"success\",\"value\"],\"enums\":[";
    bool first = true;
    for (const auto testCase : child(root, "enums").cchildren())
    {
        if (!first) std::cout << ',';
        first = false;
        OpenXcom::ProbeEnum value{};
        const bool success = c4::yml::read(child(testCase, "value"), &value);
        std::cout << '[';
        writeString(std::cout, scalar(child(testCase, "name")));
        std::cout << ',' << (success ? "true" : "false") << ',';
        if (success) writeInteger(std::cout, static_cast<int>(value)); else std::cout << "null";
        std::cout << ']';
    }

    const auto sequences = child(root, "sequences");
    std::vector<std::int32_t> integers;
    std::vector<bool> booleans;
    std::vector<std::int32_t> mappingValues;
    std::vector<std::int32_t> scalarValues;
    c4::yml::read(child(sequences, "integers"), &integers);
    c4::yml::read(child(sequences, "booleans"), &booleans);
    c4::yml::read(child(sequences, "mappingValues"), &mappingValues);
    c4::yml::read(child(sequences, "scalar"), &scalarValues);

    std::cout << "],\"sequences\":{\"integers\":";
    writeIntegerVector(std::cout, integers);
    std::cout << ",\"booleans\":[";
    first = true;
    for (const bool value : booleans)
    {
        if (!first) std::cout << ',';
        first = false;
        std::cout << (value ? "true" : "false");
    }
    std::cout << "],\"mappingValues\":";
    writeIntegerVector(std::cout, mappingValues);
    std::cout << ",\"scalar\":";
    writeIntegerVector(std::cout, scalarValues);

    std::pair<std::int32_t, std::int32_t> pair;
    std::tuple<std::int32_t, bool, std::string> tuple;
    std::array<std::int32_t, 3> array;
    std::read(child(root, "pair"), &pair);
    std::read(child(root, "tuple"), &tuple);
    std::read(child(root, "array"), &array);

    std::cout << "},\"pair\":[";
    writeInteger(std::cout, pair.first);
    std::cout << ',';
    writeInteger(std::cout, pair.second);
    std::cout << "],\"tuple\":[";
    writeInteger(std::cout, std::get<0>(tuple));
    std::cout << ',' << (std::get<1>(tuple) ? "true" : "false") << ',';
    writeString(std::cout, std::get<2>(tuple));
    std::cout << "],\"array\":[";
    first = true;
    for (const auto value : array)
    {
        if (!first) std::cout << ',';
        first = false;
        writeInteger(std::cout, value);
    }

    std::map<std::string, std::int32_t> stringInteger;
    std::map<std::string, std::int32_t> duplicate;
    std::map<std::int32_t, std::string> integerString;
    const auto maps = child(root, "maps");
    c4::yml::read(child(maps, "stringInteger"), &stringInteger);
    c4::yml::read(child(maps, "duplicate"), &duplicate);
    c4::yml::read(child(maps, "integerString"), &integerString);
    std::cout << "],\"maps\":{\"stringInteger\":";
    writeIntegerMap(std::cout, stringInteger);
    std::cout << ",\"duplicate\":";
    writeIntegerMap(std::cout, duplicate);
    std::cout << ",\"integerString\":";
    writeIntegerMap(std::cout, integerString);
    std::cout << "}}\n";
    return 0;
}

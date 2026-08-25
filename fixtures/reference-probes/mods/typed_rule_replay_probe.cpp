/*
 * Synthetic OXCE compatibility probe.
 * SPDX-License-Identifier: GPL-3.0-or-later
 */
#define _C4_YML_STD_MAP_HPP_
#define _C4_YML_STD_VECTOR_HPP_

#include "ryml.hpp"
#include "ryml_std.hpp"

#include <cstdint>
#include <fstream>
#include <iostream>
#include <map>
#include <sstream>
#include <string>
#include <vector>

namespace c4::yml
{

template <class Value, class Allocator>
bool read(ConstNodeRef const& node, std::vector<Value, Allocator>* values)
{
    values->clear();
    values->resize(static_cast<size_t>(node.num_children()));
    size_t position = 0;
    for (ConstNodeRef const child : node) child >> (*values)[position++];
    return true;
}

template <class Key, class Value, class Less, class Allocator>
bool read(ConstNodeRef const& node, std::map<Key, Value, Less, Allocator>* values)
{
    values->clear();
    for (ConstNodeRef const child : node)
    {
        Key key{};
        Value value{};
        child >> c4::yml::key(key);
        child >> value;
        values->emplace(std::move(key), std::move(value));
    }
    return true;
}

} // namespace c4::yml

struct ProbeRule
{
    std::string id;
    std::string label;
    std::int32_t value = 0;
    bool enabled = false;
    std::vector<std::int32_t> values;
    std::map<std::string, std::int32_t> mapping;
};

static ryml::ConstNodeRef child(const ryml::ConstNodeRef& node, const char* key)
{
    return node.find_child(ryml::to_csubstr(key));
}

template<typename Value>
static void tryRead(const ryml::ConstNodeRef& node, const char* key, Value& value)
{
    const auto item = child(node, key);
    if (item.valid()) c4::yml::read(item, &value);
}

static void load(ProbeRule& rule, const ryml::ConstNodeRef& node)
{
    const auto parent = child(node, "refNode");
    if (parent.valid()) load(rule, parent);
    tryRead(node, "label", rule.label);
    tryRead(node, "value", rule.value);
    tryRead(node, "enabled", rule.enabled);
    tryRead(node, "values", rule.values);
    tryRead(node, "mapping", rule.mapping);
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

int main(int argc, char** argv)
{
    if (argc != 2)
    {
        std::cerr << "Usage: typed_rule_replay_probe <ruleset>\n";
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
    const auto rulesNode = child(tree.crootref(), "probeRules");
    std::map<std::string, ProbeRule> rules;
    std::vector<std::string> order;
    for (const auto operation : rulesNode.cchildren())
    {
        auto idNode = child(operation, "type");
        if (!idNode.valid()) idNode = child(operation, "update");
        const std::string id = scalar(idNode);
        auto [position, inserted] = rules.emplace(id, ProbeRule{.id = id});
        if (inserted) order.push_back(id);
        load(position->second, operation);
    }

    std::cout << "{\"rules\":[";
    bool firstRule = true;
    for (const auto& id : order)
    {
        if (!firstRule) std::cout << ',';
        firstRule = false;
        const auto& rule = rules.at(id);
        std::cout << "{\"id\":";
        writeString(std::cout, rule.id);
        std::cout << ",\"label\":";
        writeString(std::cout, rule.label);
        std::cout << ",\"value\":" << rule.value;
        std::cout << ",\"enabled\":" << (rule.enabled ? "true" : "false");
        std::cout << ",\"values\":[";
        bool first = true;
        for (const auto value : rule.values)
        {
            if (!first) std::cout << ',';
            first = false;
            std::cout << value;
        }
        std::cout << "],\"mapping\":[";
        first = true;
        for (const auto& [key, value] : rule.mapping)
        {
            if (!first) std::cout << ',';
            first = false;
            std::cout << '[';
            writeString(std::cout, key);
            std::cout << ',' << value << ']';
        }
        std::cout << "]}";
    }
    std::cout << "]}\n";
    return 0;
}

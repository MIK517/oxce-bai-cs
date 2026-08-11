/*
 * Synthetic OXCE compatibility probe.
 * SPDX-License-Identifier: GPL-3.0-or-later
 */
#include "ryml.hpp"
#include "ryml_std.hpp"

#include <fstream>
#include <iostream>
#include <sstream>
#include <string>
#include <vector>

static std::string value(const ryml::ConstNodeRef& node)
{
    const auto scalar = node.val();
    return std::string(scalar.str, scalar.len);
}

static ryml::ConstNodeRef child(const ryml::ConstNodeRef& node, const char* key)
{
    return node.find_child(ryml::to_csubstr(key));
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

int main(int argc, char** argv)
{
    if (argc != 2)
    {
        std::cerr << "Usage: yaml_probe <fixture>\n";
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
    ryml::EventHandlerTree eventHandler;
    ryml::Parser parser(&eventHandler, ryml::ParserOptions().locations(true));
    ryml::Tree tree;
    ryml::parse_in_arena(&parser, ryml::to_csubstr(argv[1]), ryml::to_csubstr(yaml), &tree);
    tree.resolve();

    const auto root = tree.crootref();
    std::vector<ryml::ConstNodeRef> documents;
    if (root.is_stream())
    {
        for (const auto document : root.cchildren())
        {
            documents.push_back(document);
        }
    }
    else
    {
        documents.push_back(root);
    }

    const auto first = documents.at(0);
    const auto second = documents.at(1);
    const auto item = child(first, "item");
    const auto sequence = child(first, "sequence");
    std::vector<std::string> duplicateValues;
    for (const auto entry : first.cchildren())
    {
        if (entry.key() == "duplicate")
        {
            duplicateValues.push_back(value(entry));
        }
    }

    std::cout << "{\"documentCount\":" << documents.size();
    std::cout << ",\"duplicateLookup\":";
    writeString(std::cout, value(child(first, "duplicate")));
    std::cout << ",\"duplicateValues\":[";
    for (std::size_t index = 0; index < duplicateValues.size(); ++index)
    {
        if (index != 0)
        {
            std::cout << ',';
        }
        writeString(std::cout, duplicateValues[index]);
    }
    std::cout << "]";
    std::cout << ",\"item\":{";
    std::cout << "\"count\":";
    writeString(std::cout, value(child(item, "count")));
    std::cout << ",\"enabled\":";
    writeString(std::cout, value(child(item, "enabled")));
    std::cout << ",\"optionalIsNull\":" << (child(item, "optional").val_is_null() ? "true" : "false");
    std::cout << ",\"quotedNullIsNull\":" << (child(item, "quotedNull").val_is_null() ? "true" : "false");
    std::cout << "}";
    std::cout << ",\"secondDocument\":{";
    std::cout << "\"ironman\":";
    writeString(std::cout, value(child(second, "ironman")));
    std::cout << ",\"name\":";
    writeString(std::cout, value(child(second, "name")));
    std::cout << "}";
    std::cout << ",\"sequenceNulls\":[";
    for (std::size_t index = 0; index < sequence.num_children(); ++index)
    {
        if (index != 0)
        {
            std::cout << ',';
        }
        std::cout << (sequence.child(index).val_is_null() ? "true" : "false");
    }
    std::cout << "]}\n";
    return 0;
}

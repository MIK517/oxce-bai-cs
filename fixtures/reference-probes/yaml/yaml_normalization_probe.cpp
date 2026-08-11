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

static std::string text(ryml::csubstr value)
{
    return std::string(value.str, value.len);
}

static void writeString(std::ostream& output, const std::string& value)
{
    static constexpr char hexadecimal[] = "0123456789abcdef";
    output << '"';
    for (const unsigned char character : value)
    {
        switch (character)
        {
            case '"': output << "\\\""; break;
            case '\\': output << "\\\\"; break;
            case '\b': output << "\\b"; break;
            case '\f': output << "\\f"; break;
            case '\n': output << "\\n"; break;
            case '\r': output << "\\r"; break;
            case '\t': output << "\\t"; break;
            default:
                if (character < 0x20)
                {
                    output << "\\u00" << hexadecimal[character >> 4] << hexadecimal[character & 0x0f];
                }
                else
                {
                    output << character;
                }
                break;
        }
    }
    output << '"';
}

static void writeTag(std::ostream& output, const ryml::ConstNodeRef& node)
{
    if (node.has_val_tag())
    {
        output << ",\"tag\":";
        writeString(output, text(node.val_tag()));
    }
}

static void writeKey(std::ostream& output, const ryml::ConstNodeRef& node)
{
    output << "{\"kind\":\"" << (node.key_is_null() ? "null" : "scalar") << "\"";
    if (node.has_key_tag())
    {
        output << ",\"tag\":";
        writeString(output, text(node.key_tag()));
    }
    output << ",\"value\":";
    writeString(output, text(node.key()));
    output << '}';
}

static void writeNode(std::ostream& output, const ryml::ConstNodeRef& node)
{
    if (node.is_map())
    {
        output << "{\"kind\":\"mapping\"";
        writeTag(output, node);
        output << ",\"entries\":[";
        bool first = true;
        for (const auto child : node.cchildren())
        {
            if (!first) output << ',';
            first = false;
            output << "{\"key\":";
            writeKey(output, child);
            output << ",\"value\":";
            writeNode(output, child);
            output << '}';
        }
        output << "]}";
        return;
    }

    if (node.is_seq())
    {
        output << "{\"kind\":\"sequence\"";
        writeTag(output, node);
        output << ",\"items\":[";
        bool first = true;
        for (const auto child : node.cchildren())
        {
            if (!first) output << ',';
            first = false;
            writeNode(output, child);
        }
        output << "]}";
        return;
    }

    output << "{\"kind\":\"" << (node.val_is_null() ? "null" : "scalar") << "\"";
    writeTag(output, node);
    output << ",\"value\":";
    writeString(output, text(node.val()));
    output << '}';
}

int main(int argc, char** argv)
{
    if (argc != 2)
    {
        std::cerr << "Usage: yaml_normalization_probe <fixture>\n";
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
    tree.resolve();
    const auto root = tree.crootref();

    std::cout << "{\"documents\":[";
    bool first = true;
    if (root.is_stream())
    {
        for (const auto document : root.cchildren())
        {
            if (!first) std::cout << ',';
            first = false;
            writeNode(std::cout, document);
        }
    }
    else
    {
        writeNode(std::cout, root);
    }
    std::cout << "]}\n";
    return 0;
}

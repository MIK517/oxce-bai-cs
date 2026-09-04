/*
 * Synthetic OXCE compatibility probe.
 * SPDX-License-Identifier: GPL-3.0-or-later
 */
#define NOMINMAX
#define WIN32_LEAN_AND_MEAN
#include <windows.h>

#include <fstream>
#include <iostream>
#include <stdexcept>
#include <string>

namespace
{
std::wstring fromUtf8(const std::string &value)
{
    if (value.empty()) return {};
    const auto length = MultiByteToWideChar(
        CP_UTF8, MB_ERR_INVALID_CHARS, value.data(), static_cast<int>(value.size()), nullptr, 0);
    if (length == 0) throw std::runtime_error("fixture contains invalid UTF-8");
    std::wstring result(static_cast<std::size_t>(length), L'\0');
    if (MultiByteToWideChar(
            CP_UTF8, MB_ERR_INVALID_CHARS, value.data(), static_cast<int>(value.size()),
            result.data(), length) != length)
    {
        throw std::runtime_error("could not decode fixture path");
    }
    return result;
}

std::string toUtf8(const std::wstring &value)
{
    if (value.empty()) return {};
    const auto length = WideCharToMultiByte(
        CP_UTF8, 0, value.data(), static_cast<int>(value.size()), nullptr, 0, nullptr, nullptr);
    if (length == 0) throw std::runtime_error("could not size encoded fixture path");
    std::string result(static_cast<std::size_t>(length), '\0');
    if (WideCharToMultiByte(
            CP_UTF8, 0, value.data(), static_cast<int>(value.size()), result.data(), length,
            nullptr, nullptr) != length)
    {
        throw std::runtime_error("could not encode fixture path");
    }
    return result;
}

std::string canonicalize(const std::string &value)
{
    auto wide = fromUtf8(value);
    if (!wide.empty()) CharLowerW(wide.data());
    return toUtf8(wide);
}

void printJsonString(const std::string &value)
{
    std::cout << '"';
    for (const auto character : value)
    {
        if (character == '"' || character == '\\') std::cout << '\\';
        std::cout << character;
    }
    std::cout << '"';
}
}

int main(int argc, char **argv)
{
    try
    {
        if (argc != 2) throw std::runtime_error("usage: vfs_unicode_probe <fixture.tsv>");
        std::ifstream input(argv[1], std::ios::binary);
        if (!input) throw std::runtime_error("could not open fixture");

        std::cout << "{\"paths\":[";
        bool first = true;
        std::string line;
        while (std::getline(input, line))
        {
            if (!line.empty() && line.back() == '\r') line.pop_back();
            if (line.empty()) continue;
            const auto separator = line.find('\t');
            if (separator == std::string::npos) throw std::runtime_error("fixture row requires an ID and path");
            const auto id = line.substr(0, separator);
            const auto path = line.substr(separator + 1);
            if (!first) std::cout << ',';
            first = false;
            std::cout << "{\"id\":";
            printJsonString(id);
            std::cout << ",\"original\":";
            printJsonString(path);
            std::cout << ",\"canonical\":";
            printJsonString(canonicalize(path));
            std::cout << '}';
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

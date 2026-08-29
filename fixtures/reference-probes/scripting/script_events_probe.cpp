/*
 * Synthetic OXCE compatibility probe.
 * SPDX-License-Identifier: GPL-3.0-or-later
 */
#include "Engine/Exception.h"
#include "Engine/Script.h"
#include "Engine/Yaml.h"
#include "ryml.hpp"

#include <iostream>
#include <sstream>
#include <string>
#include <vector>

#ifdef main
#undef main
#endif

namespace
{
std::vector<std::string> diagnostics;

std::string escapeJson(const std::string& value)
{
    std::string result;
    for (const char c : value)
    {
        switch (c)
        {
        case '\\': result += "\\\\"; break;
        case '"': result += "\\\""; break;
        case '\n': result += "\\n"; break;
        case '\r': result += "\\r"; break;
        case '\t': result += "\\t"; break;
        default: result += c; break;
        }
    }
    return result;
}
}

namespace OpenXcom::CrossPlatform
{
void log(int, const std::ostringstream& message)
{
    const std::string text = message.str();
    if (!text.empty()) diagnostics.push_back(text);
}

OpenXcom::RawData readFileRaw(const std::string&) { return {}; }
OpenXcom::RawData getYamlSaveHeaderRaw(const std::string&) { return {}; }
}

namespace OpenXcom
{
Exception::Exception(const std::string& message) : std::runtime_error(message) { }

namespace Options
{
bool debug = false;
bool verboseLogging = false;
}
}

struct ProbeParser : OpenXcom::ScriptParserEvents<OpenXcom::ScriptOutputArgs<int&>>
{
    explicit ProbeParser(OpenXcom::ScriptGlobal* global) :
        ScriptParserEvents(global, "Probe", OpenXcom::helper::ArgName<int&>{ "result" })
    {
    }
};

struct ProbeCase
{
    const char* name;
    const char* yaml;
};

int main()
{
    const ProbeCase cases[] =
    {
        { "ordered-update", R"(events:
  - new: early
    offset: -2
    code: "add result 1; return result;"
  - new: late
    offset: 1
    code: "add result 10; return result;"
  - update: early
    offset: -1
    code: "add result 2; return result;"
)" },
        { "unknown-update-delete", R"(events:
  - update: missing
    offset: 1
    code: "add result 100; return result;"
  - delete: alsoMissing
)" },
        { "unknown-override", R"(events:
  - override: missing
    offset: 1
    code: "return result;"
)" },
        { "delete-existing", R"(events:
  - new: removed
    offset: -1
    code: "add result 100; return result;"
  - delete: removed
)" },
        { "ignore-and-zero-offset", R"(events:
  - ignore: ignored
    offset: -1
    code: "add result 100; return result;"
  - new: zero
    offset: 0
    code: "add result 1000; return result;"
)" },
    };

    std::cout << "{\"cases\":[";
    bool first = true;
    for (const auto& item : cases)
    {
        diagnostics.clear();
        bool accepted = true;
        int result = 1;
        try
        {
            OpenXcom::ScriptGlobal global;
            ProbeParser parser(&global);
            auto tree = ryml::parse_in_arena(
                ryml::to_csubstr(item.name),
                ryml::to_csubstr(item.yaml));
            OpenXcom::YAML::YamlNodeReader root(tree.crootref());
            parser.loadEvents(root["events"]);

            ProbeParser::Container current;
            current.loadContainer(item.name, "mul result 2; return result;", parser);
            auto retainedEvents = parser.releseEvents();
            ProbeParser::Output output(result);
            ProbeParser::Worker worker;
            worker.execute(current, output);
            result = output.getFirst();
        }
        catch (const std::exception& error)
        {
            accepted = false;
            diagnostics.push_back(error.what());
        }

        if (!first) std::cout << ',';
        first = false;
        std::cout << "{\"accepted\":" << (accepted ? "true" : "false")
            << ",\"diagnostics\":[";
        for (size_t index = 0; index < diagnostics.size(); ++index)
        {
            if (index) std::cout << ',';
            std::cout << '"' << escapeJson(diagnostics[index]) << '"';
        }
        std::cout << "],\"name\":\"" << item.name << "\",\"result\":" << result << '}';
    }
    std::cout << "]}\n";
}

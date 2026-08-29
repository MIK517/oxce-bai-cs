/*
 * Synthetic OXCE compatibility probe.
 * SPDX-License-Identifier: GPL-3.0-or-later
 */
#include "Engine/Exception.h"
#include "Engine/Script.h"

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

struct ProbeCase
{
    const char* name;
    const char* source;
    int seed;
};

int main()
{
    using namespace OpenXcom;
    using Parser = ScriptParser<ScriptOutputArgs<int&>>;

    const ProbeCase cases[] =
    {
        { "return-only", "return result;", 7 },
        { "set-output", "set result 42; return result;", 7 },
        { "locals-and-arithmetic", "var int temp; set temp 0x6; mul temp 0b111; set result temp; return result;", 0 },
        { "conditional", "if eq result 7; set result 11; else; set result 13; end; return result;", 7 },
        { "comment", "# leading comment\nset result 0o17; return result;", 0 },
        { "forward-label", "goto done; set result 1; done: set result 21; return result;", 0 },
        { "counted-loop", "var int sum; set sum 0; loop var i 4; add sum i; end; set result sum; return result;", 0 },
        { "nested-scope", "begin; var int local 5; set result local; end; return result;", 0 },
        { "missing-return", "set result 1;", 0 },
        { "unknown-operation", "explode result; return result;", 0 },
        { "unreachable", "return result; set result 1;", 0 },
        { "invalid-number", "set result 0xg; return result;", 0 },
        { "missing-semicolon", "return result", 0 },
        { "invalid-punctuation", "set result @; return result;", 0 },
        { "invalid-text-escape", "debug_log \"bad\\n\"; return result;", 0 },
        { "integer-overflow", "set result 2147483648; return result;", 0 },
        { "break-outside-loop", "break; return result;", 0 },
        { "variable-after-operation", "clear result; var int late; return result;", 0 },
        { "too-many-arguments", "set result 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0; return result;", 0 },
        { "unclosed-block", "begin; return result;", 0 },
        { "duplicate-label", "mark: clear result; mark: return result;", 0 },
    };

    std::cout << "{\"cases\":[";
    bool firstCase = true;
    for (const ProbeCase& item : cases)
    {
        diagnostics.clear();
        ScriptGlobal global;
        Parser parser(&global, "Probe", helper::ArgName<int&>{ "result" });
        Parser::Container script;
        script.loadContainer(item.name, item.source, parser);
        const bool compiled = static_cast<bool>(script);
        int result = item.seed;
        if (compiled)
        {
            Parser::Output output(result);
            Parser::Worker worker;
            worker.execute(script, output);
            result = output.getFirst();
        }

        if (!firstCase) std::cout << ',';
        firstCase = false;
        std::cout << "{\"compiled\":" << (compiled ? "true" : "false")
            << ",\"diagnostics\":[";
        for (size_t index = 0; index < diagnostics.size(); ++index)
        {
            if (index != 0) std::cout << ',';
            std::cout << '"' << escapeJson(diagnostics[index]) << '"';
        }
        std::cout << "],\"name\":\"" << item.name
            << "\",\"result\":" << result
            << ",\"seed\":" << item.seed
            << ",\"source\":\"" << escapeJson(item.source) << "\"}";
    }
    std::cout << "]}\n";
    return 0;
}

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
        { "arithmetic-wrap", "set result 2147483647; add result 1; mul result 2; sub result 1; return result;", 0 },
        { "aggregate-offset", "set result 3; aggregate result 4 5; offset result 2 -3; offsetmod result 2 5 11; return result;", 0 },
        { "division-modulo", "set result -17; div result 5; mod result 2; set result 21; muldiv result -4 6; return result;", 0 },
        { "bit-operations", "set result 10; shl result 2; shr result 1; bit_or result 5; bit_and result 23; bit_xor result 3; bit_not result; bit_count result; return result;", 0 },
        { "math-limits", "set result -81; abs result; sqrt result; pow result 3; limit result 100 500; limit_upper result 400; limit_lower result 300; return result;", 0 },
        { "discrete-waves", "set result -2; wavegen_rect result 8 3 9; wavegen_saw result 10 7 4; wavegen_tri result 12 8 3; return result;", 0 },
        { "trigonometric-waves", "set result 3; wavegen_sin result 12 100; wavegen_cos result 12 40; return result;", 0 },
        { "color-and-shade", "set result 0x23; get_color result result; set_color result 7; set_shade result 12; add_shade result 5; get_shade result result; return result;", 0 },
        { "combined-conditions", "if and ge result 5 lt result 10 neq result 7; set result 21; else; set result 22; end; return result;", 6 },
        { "conditional-else-if", "if eq result 1; set result 10; else eq result 2; set result 20; else; set result 30; end; return result;", 2 },
        { "loop-control", "set result 0; loop var i 6; if eq i 2; continue; end; if eq i 5; break; end; add result i; end; return result;", 0 },
        { "division-by-zero", "set result 9; div result 0; set result 10; return result;", 0 },
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

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
        { "add", "set result 30; add result 9; return result;", 0 },
        { "subtract", "set result 48; sub result 9; return result;", 0 },
        { "multiply", "set result -13; mul result -3; return result;", 0 },
        { "aggregate", "set result 30; aggregate result 3 3; return result;", 0 },
        { "offset", "set result 10; offset result 4 -1; return result;", 0 },
        { "absolute", "set result -39; abs result; return result;", 0 },
        { "absolute-minimum", "set result -2147483648; abs result; return result;", 0 },
        { "clamp-low", "limit result -39 39; return result;", -2147483648 },
        { "clamp-high", "limit result -39 39; return result;", 2147483647 },
        { "clamp-middle", "limit result -39 39; return result;", 7 },
        { "clamp-inverted", "limit result 50 20; return result;", 0 },
        { "lower", "limit_lower result 39; return result;", -4 },
        { "upper", "limit_upper result 39; return result;", 42 },
        { "singleton-wrap", "set result 2147483647; add result 1; return result;", 0 },
        { "bounded-loop", "set result 0; loop var i 4; add result 1; end; return result;", 0 },
        { "condition-positive", "if gt result 0; set result 39; else; set result -39; end; return result;", 7 },
        { "condition-negative", "if lt result 0; set result -39; else; set result 39; end; return result;", -7 },
        { "condition-zero", "if neq result 0; div result 0; end; return result;", 0 },
        { "condition-compound", "if and ge result 0 le result 39; set result 39; else; set result -39; end; return result;", 7 },
        { "condition-extreme", "if lt result -2147483648; div result 0; end; return result;", -2147483648 },
        { "calculated-divide-chain", "var int divisor -6; div divisor 2; set result -117; div result divisor; return result;", 0 },
        { "calculated-modulo-chain", "var int divisor -19; mod divisor -5; set result -156; div result divisor; return result;", 0 },
        { "calculated-muldiv-chain", "var int divisor 2147483647; muldiv divisor 2 2147483647; set result 78; div result divisor; return result;", 0 },
        { "calculated-offsetmod-chain", "var int divisor -5; offsetmod divisor 2 -3 11; set result 351; div result divisor; return result;", 0 },
        { "calculated-negative-offsetmod", "set result 5; offsetmod result 2 3 -11; return result;", 0 },
        { "variable-divide", "var int divisor -3; set result -117; div result divisor; return result;", 0 },
        { "variable-modulo", "var int divisor -5; set result -19; mod result divisor; return result;", 0 },
        { "variable-multiply-divide", "var int divisor 2; set result 13; muldiv result 6 divisor; return result;", 0 },
        { "variable-offset-modulo", "var int divisor 11; set result -5; offsetmod result 2 -3 divisor; return result;", 0 },
        { "variable-wave-rectangle", "var int period 8; set result 1; wavegen_rect result period 3 9; return result;", 0 },
        { "variable-wave-saw", "var int period 10; set result 2; wavegen_saw result period 7 4; return result;", 0 },
        { "variable-wave-triangle", "var int period 12; set result 3; wavegen_tri result period 8 3; return result;", 0 },
        { "variable-wave-sine", "var int period 12; set result 3; wavegen_sin result period 100; return result;", 0 },
        { "variable-wave-cosine", "var int period 12; set result 3; wavegen_cos result period 40; return result;", 0 },
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

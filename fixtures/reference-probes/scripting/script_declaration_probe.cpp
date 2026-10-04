/*
 * Synthetic OXCE compatibility probe.
 * SPDX-License-Identifier: GPL-3.0-or-later
 */
#include "Engine/Exception.h"
#include "Engine/Script.h"
#include "Engine/ScriptBind.h"

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

struct RuleItem
{
    static constexpr const char* ScriptName = "RuleItem";
    static void ScriptRegister(OpenXcom::ScriptParserBase* parser)
    {
        OpenXcom::Bind<RuleItem> bind(parser);
        bind.addScriptTag();
    }
};
struct Ufo
{
    static constexpr const char* ScriptName = "Ufo";
    static void ScriptRegister(OpenXcom::ScriptParserBase* parser) { OpenXcom::Bind<Ufo> bind(parser); }
};
struct RuleMod
{
    static constexpr const char* ScriptName = "RuleMod";
    static void ScriptRegister(OpenXcom::ScriptParserBase* parser) { OpenXcom::Bind<RuleMod> bind(parser); }
};
struct RandomState
{
    static constexpr const char* ScriptName = "RandomState";
    static void ScriptRegister(OpenXcom::ScriptParserBase* parser) { OpenXcom::Bind<RandomState> bind(parser); }
};
// Tile::ScriptRegister uses the same native raw value registration and BindValue template.
struct Position
{
    int x = 0, y = 0, z = 0;
    bool operator==(const Position&) const = default;
};
struct ProbeCase { const char* name; const char* source; };

int main()
{
    using namespace OpenXcom;
    using Parser = ScriptParser<ScriptOutputArgs<int&, int&>, const Ufo*, const RuleMod*>;
    const ProbeCase cases[] =
    {
        { "scalar-literal", "var int value -7; return 1 100;" },
        { "scalar-register", "var int value detection_chance; return 1 100;" },
        { "text-literal", "var text value \"copied\"; return 1 100;" },
        { "text-register", "var text original \"copied\"; var text value original; return 1 100;" },
        { "readonly-pointer", "var ptr Ufo value ufo; return 1 100;" },
        { "null-pointer", "var ptr RuleItem value null; return 1 100;" },
        { "null-editable-pointer", "var ptre RandomState value null; return 1 100;" },
        { "editable-to-readonly", "var ptre RandomState original null; var ptr RandomState value original; return 1 100;" },
        { "editable-copy", "var ptre RandomState original null; var ptre RandomState value original; return 1 100;" },
        { "tag-null", "var RuleItem.Tag value null; return 1 100;" },
        { "tag-copy", "var RuleItem.Tag original null; var RuleItem.Tag value original; return 1 100;" },
        { "pointer-set-null", "var ptr Ufo value ufo; set value null; return 1 100;" },
        { "editable-pointer-set-null", "var ptre RandomState value null; set value null; return 1 100;" },
        { "tag-set-null", "var RuleItem.Tag value null; set value null; return 1 100;" },
        { "mod-null-repair", "var ptr RuleMod value rules; set value null; set value rules; return 1 100;" },
        { "pointer-to-position-null", "var ptr Position value null; return 1 100;" },
        { "pointer-to-scalar-default", "var ptr int value; return 1 100;" },
        { "pointer-to-text-default", "var ptr text value; return 1 100;" },
        { "pointer-to-tag-default", "var ptr RuleItem.Tag value; return 1 100;" },
        { "pointer-to-scalar-null", "var ptr int value null; return 1 100;" },
        { "pointer-to-text-null", "var ptr text value null; return 1 100;" },
        { "pointer-to-tag-null", "var ptr RuleItem.Tag value null; return 1 100;" },
        { "scalar-from-text", "var int value \"wrong\"; return 1 100;" },
        { "scalar-from-pointer", "var int value ufo; return 1 100;" },
        { "scalar-from-null", "var int value null; return 1 100;" },
        { "text-from-scalar", "var text value 7; return 1 100;" },
        { "text-from-null", "var text value null; return 1 100;" },
        { "text-from-pointer", "var text value ufo; return 1 100;" },
        { "pointer-from-scalar", "var ptr RuleItem value 7; return 1 100;" },
        { "pointer-from-text", "var ptr RuleItem value \"wrong\"; return 1 100;" },
        { "pointer-from-other-type", "var ptr RuleItem value ufo; return 1 100;" },
        { "readonly-to-editable", "var ptre Ufo value ufo; return 1 100;" },
        { "tag-from-scalar", "var RuleItem.Tag value 7; return 1 100;" },
        { "scalar-from-tag", "var RuleItem.Tag tag null; var int value tag; return 1 100;" },
    };
    std::cout << "{\"cases\":[";
    bool first = true;
    for (const auto& item : cases)
    {
        diagnostics.clear();
        ScriptGlobal global;
        Parser parser(&global, "detectUfoFromBase", helper::ArgName<int&>{ "detection_type" },
            helper::ArgName<int&>{ "detection_chance" }, helper::ArgName<const Ufo*>{ "ufo" },
            helper::ArgName<const RuleMod*>{ "rules" });
        parser.registerPointerType<RuleItem>();
        parser.registerPointerType<RandomState>();
        parser.registerRawValueType<Position>("Position");
        BindValue<Position> positionBinding(&parser, "Position");
        Parser::Container script;
        script.loadContainer(item.name, item.source, parser);
        const bool compiled = static_cast<bool>(script);
        Parser::Output output(0, 0);
        if (compiled)
        {
            Parser::Worker worker(nullptr, nullptr);
            worker.execute(script, output);
        }
        if (!first) std::cout << ',';
        first = false;
        std::cout << "{\"name\":\"" << item.name << "\",\"source\":\"" << escapeJson(item.source)
            << "\",\"compiled\":" << (compiled ? "true" : "false")
            << ",\"detectionType\":" << std::get<0>(output.data)
            << ",\"detectionChance\":" << std::get<1>(output.data) << '}';
    }
    std::cout << "]}\n";
}

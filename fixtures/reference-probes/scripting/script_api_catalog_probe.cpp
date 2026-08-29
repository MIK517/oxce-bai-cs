/*
 * Synthetic OXCE compatibility probe.
 * SPDX-License-Identifier: GPL-3.0-or-later
 */
#include "Engine/Exception.h"
#include "Engine/Logger.h"
#include "Engine/Script.h"
#include "Mod/Mod.h"
#include "Mod/ModScript.h"
#include "Savegame/SavedBattleGame.h"
#include "Savegame/SavedGame.h"

#include <iostream>
#include <sstream>
#include <string>
#include <vector>

#ifdef main
#undef main
#endif

namespace
{
std::vector<std::string> logs;

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
    if (!message.str().empty()) logs.push_back(message.str());
}

OpenXcom::RawData readFileRaw(const std::string&) { return {}; }
OpenXcom::RawData getYamlSaveHeaderRaw(const std::string&) { return {}; }
}

namespace OpenXcom
{
Exception::Exception(const std::string& message) : std::runtime_error(message) { }

namespace Options
{
bool debug = true;
bool verboseLogging = true;
}
}

class ProbeGlobal : public OpenXcom::ScriptGlobal
{
public:
    void initParserGlobals(OpenXcom::ScriptParserBase* parser) override
    {
        parser->registerPointerType<OpenXcom::Mod>();
        parser->registerPointerType<OpenXcom::SavedGame>();
        parser->registerPointerType<OpenXcom::SavedBattleGame>();
    }
};

int main()
{
    using namespace OpenXcom;
    Logger::reportingLevel() = LOG_VERBOSE;
    ProbeGlobal global;
    Mod* mod = nullptr;

    ModScript::BattleUnitScripts battleUnit(&global, mod, "unit");
    ModScript::BattleItemScripts battleItem(&global, mod, "item");
    ModScript::BonusStatsScripts bonuses(&global, mod, "bonuses");
    ModScript::SkillScripts skill(&global, mod, "skill");
    ModScript::CountryScripts country(&global, mod, "country");
    ModScript::UfoScripts ufo(&global, mod, "ufo");
    ModScript::CraftScripts craft(&global, mod, "craft");
    ModScript::SoldierBonusScripts soldier(&global, mod, "soldier");

    std::cout << "{\"logs\":[";
    for (size_t index = 0; index < logs.size(); ++index)
    {
        if (index) std::cout << ',';
        std::cout << '"' << escapeJson(logs[index]) << '"';
    }
    std::cout << "]}\n";
}

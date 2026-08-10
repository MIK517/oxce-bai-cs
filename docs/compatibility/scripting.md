# OXCE scripting compatibility

| Area | Status | Reference source | Fixture | Notes |
|---|---|---|---|---|
| Lexical grammar and comments | not started | `src/Engine/Script.h`, `src/Engine/Script.cpp` |  | Inventory tokens and escape rules individually. |
| Expressions and precedence | not started | `src/Engine/Script.cpp` |  | Include short-circuit and evaluation order. |
| Statements and control flow | not started | `src/Engine/Script.cpp` |  | Branches, loops, return, and error paths. |
| Primitive and reference types | not started | `src/Engine/Script.h` |  | Preserve integer widths and null behavior. |
| Constants and enums | not started | `src/Engine/Script.cpp`, registration sites |  | Catalog names and values. |
| Registers and local values | not started | `src/Engine/Script.h`, `src/Engine/Script.cpp` |  | Include initialization and lifetime. |
| Operators and overload resolution | not started | `src/Engine/Script.cpp` |  | Split into one row per operator family. |
| Function calls and argument conversion | not started | `src/Engine/Script.cpp`, `src/Engine/ScriptBind.h` |  | Match accept/reject behavior. |
| Runtime errors and execution limits | not started | `src/Engine/Script.cpp` |  | Port must define safe resource limits. |
| Global scripts | not started | `src/Mod/ModScript.h`, `src/Mod/Mod.cpp` |  | Include load and execution order. |
| Event scripts | not started | `src/Mod/RuleEventScript.cpp` |  | Bindings arrive with owning gameplay slices. |
| Mission scripts | not started | `src/Mod/RuleMissionScript.cpp` |  | Includes selection and persistence. |
| Arc scripts | not started | `src/Mod/RuleArcScript.cpp` |  | Include state transitions and conditions. |
| Binding catalog | not started | `src/Engine/ScriptBind.h`, `ScriptRegister` call sites |  | Generate an auditable inventory before implementing bindings. |
| Persistent script state | not started | `src/Savegame/SavedGame.cpp`, script registration sites |  | Must round-trip through saves. |

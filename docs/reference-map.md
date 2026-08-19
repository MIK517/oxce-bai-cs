# Reference source map

Use this map to locate behavior in the separate C++ reference checkout. A sibling
checkout at `../oxce-bai` is conventional, but tooling should allow the location to be
configured. This is a starting point; record exact files and functions in tests and
change summaries.

| Reference area | Primary .NET owner | Notes |
|---|---|---|
| `src/Engine/Yaml.*`, `CatFile.*`, `GMCat.*` | `Oxce.Formats` | Compatibility adapters and binary/resource codecs |
| `src/Engine/ModInfo.*`, `FileMap.*`, relevant activation code in `Options.cpp` | `Oxce.Mods` | Metadata, master-chain validation/activation, layered resource catalog, ruleset order, lookup precedence, and provenance |
| `src/Engine/Script.*`, `ScriptBind.h` and `ScriptRegister` methods | `Oxce.Scripting` | Grammar, VM, types, and binding catalog |
| `src/Mod/` | `Oxce.Mods` | Rules, resources, mod ordering and merge |
| `src/Savegame/` persistence methods | `Oxce.Savegames` + `Oxce.Gameplay` | `Savegames` owns the external schema/adapters; `Gameplay` owns runtime state, identities, restoration, and final invariant validation under ADR 0008 |
| `src/Savegame/SaveConverter.*` | `Oxce.Formats` + `Oxce.Savegames` | Original game binary save import |
| `src/Basescape/` non-UI logic | `Oxce.Gameplay` | UI/state code belongs in Engine/presentation |
| `src/Geoscape/` | `Oxce.Gameplay` | Globe presentation splits into Rendering/Engine |
| `src/Battlescape/` | `Oxce.Gameplay` | Map/unit rendering splits into Rendering |
| `src/Engine/Surface.*`, `Palette.*`, shaders/scalers | `Oxce.Rendering` | Preserve indexed semantics, not necessarily algorithms |
| `src/Engine/FlcPlayer.*` | `Oxce.Formats` | Bounded FLI/FLC container, indexed-frame, palette, and embedded-audio decoding; playback belongs in Engine/platform integration |
| `src/Interface/`, state presentation in other folders | `Oxce.Engine` + `Oxce.Rendering` | UI may be redesigned while preserving actions/information |
| `src/Engine/Game.*`, `State.*`, `Action.*`, `Timer.*` | `Oxce.Engine` | State stack, loop, commands, clocks |
| SDL/OpenGL/audio/input code | `Oxce.Platform.Sdl` | Prefer SDL3; no native types leak upward |
| `src/main.cpp`, options/startup | `Oxce.App` | Composition and diagnostics |

Cross-cutting rule: a C++ folder is not automatically a C# namespace. Split mixed files
by responsibility and protect compatibility at the external boundary.

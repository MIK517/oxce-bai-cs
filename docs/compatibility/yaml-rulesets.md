# YAML and ruleset compatibility

| Area | Status | Reference source | Fixture | Notes |
|---|---|---|---|---|
| Node kinds: map, sequence, scalar, null | not started | `src/Engine/Yaml.h`, `src/Engine/Yaml.cpp` |  | Preserve missing versus explicit null. |
| Multiple YAML documents | not started | `src/Engine/Yaml.cpp`, `src/Savegame/SavedGame.cpp` |  | Rules and saves exercise different document layouts. |
| Anchors, aliases, and merge keys | not started | `src/Engine/Yaml.cpp`, rapidyaml integration |  | Inventory exact accepted syntax before choosing parser settings. |
| Scalar conversion and defaults | not started | `src/Engine/Yaml.h`, `src/Mod/LoadYaml.h` |  | Includes booleans, signed/unsigned integers, floats, and strings. |
| Enum conversion | not started | `src/Mod/LoadYaml.h`, individual `Rule*.cpp` loaders |  | Unknown-value diagnostics are compatibility behavior. |
| Source locations and diagnostics | not started | `src/Engine/Yaml.cpp` |  | Include file, document, line, and column where available. |
| Duplicate mapping keys | not started | `src/Engine/Yaml.cpp` |  | Determine accept/reject and winning-value behavior. |
| Resource and recursion limits | not started | rapidyaml integration |  | Port must impose explicit safe limits even if reference is permissive. |
| Mod discovery and metadata | not started | `src/Engine/FileMap.cpp`, `src/Mod/Mod.cpp` |  | Includes masters, dependencies, conflicts, and activation. |
| File lookup precedence and case behavior | not started | `src/Engine/FileMap.h`, `src/Engine/FileMap.cpp` |  | Must be tested per supported platform. |
| Ruleset file load order | not started | `src/Mod/Mod.cpp` |  | Ordering must not depend on dictionary enumeration. |
| Rule inheritance | not started | `src/Mod/Mod.cpp`, individual `Rule*.cpp` loaders |  | Split by rule family when implemented. |
| List/map merge, replacement, and deletion | not started | `src/Mod/Mod.cpp`, `src/Mod/LoadYaml.h` |  | Record aliases and special marker syntax individually. |
| Cross-rule linking and missing references | not started | `src/Mod/Mod.cpp` |  | Diagnostics must identify rule and source provenance. |
| Harness canonical JSON | partial | fixture-tool implementation | `bootstrap-json` | Harness self-test only; it does not claim YAML compatibility. |

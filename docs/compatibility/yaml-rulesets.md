# YAML and ruleset compatibility

| Area | Status | Reference source | Fixture | Notes |
|---|---|---|---|---|
| Node kinds: map, sequence, scalar, null | partial | `src/Engine/Yaml.h`, `src/Engine/Yaml.cpp` | `yaml-reference-semantics` | Immutable DOM preserves order, duplicate entries, and missing versus the four reference null spellings. Complex keys and tags need broader fixtures. |
| Multiple YAML documents | partial | `src/Engine/Yaml.cpp`, `src/Savegame/SavedGame.cpp` | `yaml-reference-semantics` | Two-document parsing is compatible; representative full save normalization remains. |
| Anchors, aliases, and merge keys | partial | `src/Engine/Yaml.cpp`, rapidyaml integration | `yaml-reference-semantics` | Backward aliases and mapping/sequence merges are bounded and resolved. Cycles, merge ordering variants, and tag interactions remain. |
| Scalar conversion and defaults | partial | `src/Engine/Yaml.h`, `src/Engine/Yaml.cpp`, `src/Mod/LoadYaml.h`, rapidyaml `charconv.hpp` | `yaml-reference-semantics`, `yaml-scalar-conversions` | Strings, signed/unsigned integer widths and wraparound, numeric prefixes, Boolean spellings/numeric fallback, floats, missing defaults, and explicit null match the captured reference. Hexadecimal floats and base64 remain. |
| Generic enum conversion | compatible | `src/Engine/Yaml.h` | `yaml-container-conversions` | Enums use wrapped signed 32-bit numeric conversion and accept values not declared by the enum, matching the generic reference overload. Named string enums and their diagnostics remain owned by individual rule loaders. |
| Generic container conversion | compatible | `src/Engine/Yaml.h` | `yaml-container-conversions` | Sequence, mapping, pair, tuple, and fixed-array helpers preserve legacy replacement, child iteration, fixed arity, sorted-map, and first-duplicate behavior. |
| Deterministic emission | partial | `src/Engine/Yaml.h`, `src/Engine/Yaml.cpp`, `src/Savegame/SavedGame.cpp` | unit round-trip tests | Emits stable LF block YAML with bounded bytes/depth, multiple documents, scalar quoting, nulls, tags, and backward aliases. Complex keys, style parity, merge reconstruction, and full-save normalization remain. |
| Source locations and diagnostics | partial | `src/Engine/Yaml.cpp` | `malformed-location.yml`, unit tests | Parser and conversion failures carry source name and one-based line/column spans. Message parity remains. |
| Duplicate mapping keys | compatible | `src/Engine/Yaml.cpp` | `yaml-reference-semantics` | Entries are preserved in source order and ordinary lookup returns the first value. |
| Resource and recursion limits | partial | rapidyaml integration | unit tests | File bytes, nesting, nodes, documents, and aliases have explicit configurable limits. Broader adversarial tests remain. |
| Mod discovery and metadata | not started | `src/Engine/FileMap.cpp`, `src/Mod/Mod.cpp` |  | Includes masters, dependencies, conflicts, and activation. |
| File lookup precedence and case behavior | not started | `src/Engine/FileMap.h`, `src/Engine/FileMap.cpp` |  | Must be tested per supported platform. |
| Ruleset file load order | not started | `src/Mod/Mod.cpp` |  | Ordering must not depend on dictionary enumeration. |
| Rule inheritance | not started | `src/Mod/Mod.cpp`, individual `Rule*.cpp` loaders |  | Split by rule family when implemented. |
| List/map merge, replacement, and deletion | not started | `src/Mod/Mod.cpp`, `src/Mod/LoadYaml.h` |  | Record aliases and special marker syntax individually. |
| Cross-rule linking and missing references | not started | `src/Mod/Mod.cpp` |  | Diagnostics must identify rule and source provenance. |
| Harness canonical JSON | partial | fixture-tool implementation | `bootstrap-json` | Harness self-test only; it does not claim YAML compatibility. |

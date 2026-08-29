# OXCE scripting compatibility

Phase 4 targets reference commit `4df3a5e571a1a4b5e8a46d3161fb2e21a2adba15`.
The generated [`script-inventory.json`](script-inventory.json) is the source ledger;
refresh it with `tools/update-script-inventory.ps1`. Binding and constant entries remain
candidates until overload signatures, parser groups, visibility, and runtime ownership
are confirmed from the exact registration functions.

The `script-core` oracle compiles the reference `src/Engine/Script.cpp`, not a grammar
reimplementation. `tools/capture-script-core-reference.ps1` records normalized compile
outcomes, reference log diagnostics, and final output-register values for deterministic
binding-free programs. This is specification evidence for upcoming work; no row claims
a managed implementation merely because an oracle exists.

## Core language

| Area | Status | Reference source | Fixture | Required behavior / next split |
|---|---|---|---|---|
| Whitespace, line tracking, and `#` comments | partial | `src/Engine/Script.cpp` (`ScriptRefTokens::getNextToken`) | `script-core` (`comment`); lexer unit fixtures | Managed tokenization ignores comments through newline and retains one-based line/column/offset spans. Compiler-level diagnostic parity remains. |
| Symbols, dotted references, and labels | partial | `src/Engine/Script.cpp` (`ScriptRefTokens`, `findOperationAndArg`) | syntax unit fixtures; `script-core` (`forward-label`, `duplicate-label`) | Symbol character and colon-label syntax is implemented. Receiver rewriting and semantic label creation/use remain compiler work. The reference oracle confirms forward and duplicate labels fail in the captured forms. |
| Statement terminator and token errors | partial | `src/Engine/Script.cpp` (`ScriptRefTokens::getNextToken`, `ScriptParserBase::parseBase`) | `script-core` (`invalid-number`, `missing-semicolon`, `invalid-punctuation`, `too-many-arguments`); syntax unit fixtures | Managed syntax requires semicolons, reports invalid tokens with spans, and enforces 16 arguments. Exact compiler diagnostic composition remains. |
| Decimal, hexadecimal, binary, and octal integers | compatible for lexical conversion | `src/Engine/Script.cpp` (`SelectedToken::parse`, numeric token parsing); `libs/rapidyaml/c4/charconv.hpp` | `script-core` (`locals-and-arithmetic`, `comment`, `invalid-number`, `integer-overflow`); integer unit fixtures | Prefixes and malformed forms are recognized, and conversion deliberately wraps modulo 32 bits like the pinned c4 conversion. Operation-level arithmetic remains VM work. |
| Text literals and escapes | partial | `src/Engine/Script.cpp` (`ScriptRefTokens::getNextToken`, `ScriptText`) | `script-core` (`invalid-text-escape`); lexer unit fixtures | Double-quoted text accepts only escaped quote and backslash and rejects newlines and unterminated input. Compiled storage lifetime remains compiler work. |
| Primitive `int`, `text`, `label`, and `null` types | partial | `src/Engine/Script.h` (`ArgEnum`, `ArgRegisteType`); `ScriptParserBase` constructor | `script-inventory`; type/overload unit fixtures | Stable managed primitive IDs and register/writable/reference/editable modifiers implement reference compatibility scoring. Catalog registration and separator use remain. |
| Registers, output registers, locals, and constants | partial | `src/Engine/Script.h` (`ScriptWorkerBase`, `ParserWriter`); `src/Engine/Script.cpp` (`parseVar`, `parseConst`) | `script-core` (`return-only`, `set-output`, `locals-and-arithmetic`, `nested-scope`, `variable-after-operation`); symbol/layout unit fixtures | Bounded aligned layout and non-shadowing scoped symbols are implemented as foundations. Declaration compilation, initialization, and output copy remain. |
| Required return and return arity | not started | `src/Engine/Script.cpp` (`parseReturn`, `ScriptParserBase::parseBase`) | `script-core` (`return-only`, `missing-return`) | A non-empty compiled program ends in a return; returned arguments match parser outputs. Add zero-/multi-output parser cases. |
| Unreachable-code rejection | not started | `src/Engine/Script.cpp` (`ScriptParserBase::parseBase`) | `script-core` (`unreachable`) | Reject operations after return/break except the matching block terminator. |
| `begin`, `if`, `else`, and `end` blocks | not started | `src/Engine/Script.cpp` (`parseBegin`, `parseIf`, `parseElse`, `parseEnd`) | `script-core` (`conditional`) | Preserve nesting, condition evaluation order, missing/extra terminators, and declarations at block start. |
| `loop`, `break`, and `continue` | not started | `src/Engine/Script.cpp` (`parseLoop`, `parseBreak`, `parseContinue`) |  | Capture bounded deterministic loops, labels, nesting, and invalid placement before implementation. |
| Arithmetic and assignment operations | not started | `src/Engine/Script.cpp` (`MACRO_PROC_DEFINITION`) | `script-core` (`set-output`, `locals-and-arithmetic`) | Inventory all 54 built-in names, then split overloads and exact 32-bit overflow/division/modulo/error behavior per operation family. |
| Comparisons and boolean aggregation | not started | `src/Engine/Script.cpp` (`eq`, `neq`, comparisons, `aggregate`) | `script-core` (`conditional`) | Preserve eager evaluation order and nested prefix-expression argument parsing; add every comparison and aggregation operator. |
| Bit, color, shade, wave, and math helpers | not started | `src/Engine/Script.cpp` (`MACRO_PROC_DEFINITION`) | `script-inventory` | Split each operation family with edge cases, especially shifts, `pow`, `sqrt`, modulo, shade saturation, and invalid periods/divisors. |
| Overload resolution and argument separators | partial | `src/Engine/Script.cpp` (`findBestOverloadProc`); `src/Engine/ScriptBind.h` | type/overload unit fixtures | Compatibility scoring, writable/reference restrictions, best-match selection, and ambiguity are implemented. Receiver rewriting and separator-driven overloads remain compiler work. |
| Compile diagnostics | partial | `src/Engine/Script.cpp` (`ScriptParserBase::parseBase`) | `script-core` rejected cases; lexer/syntax unit fixtures | Foundational structured diagnostics retain precise source spans. Parser/parent identity and semantic-operation categories arrive with compilation. |
| Runtime errors and execution limits | partial foundations | `src/Engine/Script.cpp` (`ScriptWorkerBase::executeBase`); `src/Engine/Script.h` limits | `script-inventory`; syntax/layout/IR unit fixtures | Nine outputs, 16 arguments, and `64 * sizeof(void*)` register bytes are declared and enforced by foundational models. VM instruction, recursion/event, trace, and allocation limits remain. |
| Deterministic execution trace and final values | not started | `src/Engine/Script.cpp` (`ScriptWorkerBase`, generated proc dispatch) | `script-core` accepted cases | Extend the oracle from final output to stable instruction/event traces without depending on raw reference bytecode layout. |

## Registration and integration

| Area | Status | Reference source | Fixture | Required behavior / next split |
|---|---|---|---|---|
| Core operation inventory | not started | `src/Engine/Script.cpp` (`ScriptParserBase` constructor, `MACRO_PROC_DEFINITION`) | `script-inventory` | Generated ledger records 54 distinct built-in operation names and six primitive/pseudo-types. Refine into overload/signature rows. |
| `ScriptRegister` inventory | not started | all reference `ScriptRegister` definitions | `script-inventory` | Generated ledger records 24 definitions and exact source lines. Confirm helper registrations called transitively by each definition. |
| Binding and constant catalog | not started | `src/Engine/ScriptBind.h`; `ScriptRegister` call sites | `script-inventory` | Generated candidates contain 503 named binding calls and 99 constant registrations. Resolve prefixes, overloads, types, aliases, descriptions, visibility, and parser-group membership before generating managed declarations. |
| Parser/event group catalog | not started | `src/Mod/ModScript.h`; parser construction in `src/Mod/Mod.cpp` | `script-inventory` | Generated ledger records 35 parser structs. Inventory constructor register names, output arguments, event inheritance, defaults, and load order. |
| Global scripts and tags | not started | `src/Mod/Mod.cpp` (`ModScriptGlobal`); `src/Engine/Script.h` (`ScriptGlobal`) | `script-inventory` | Capture global constant/tag registration, rule-list namespaces, file provenance, and initialization/load order. |
| Rule-local script envelopes | partial preservation only | typed Phase 3 rule loaders; reference rule `ScriptRegister` methods | typed-rule and family fixtures | Phase 3 preserves or explicitly defers script-bearing YAML. Phase 4 must compile it through owning parser definitions and advance `scripts-compiled` independently of `linked`. |
| Event scripts | not started | `src/Engine/Script.h` (`ScriptParserEventsBase`); `src/Mod/RuleEventScript.cpp` |  | Capture parent/current/global event ordering, offsets, override/delete behavior, and deterministic traces. |
| Mission and arc scheduling scripts | not started | `src/Mod/RuleMissionScript.cpp`, `RuleArcScript.cpp`, `RuleEventScript.cpp` | mission-event typed fixtures only | Typed scheduling declarations exist; runtime selection/state transitions and embedded code compilation need distinct fixtures. |
| Persistent script values | not started | `src/Engine/Script.h` (`ScriptValuesBase`); 27 inventoried owners; save registration sites | `script-inventory` | Define gameplay-owned typed values in Phase 4 and external representation in Phase 5. Include unknown/missing tags and version migration. |
| Gameplay binding implementations | not started | inventoried `ScriptRegister` sites across `Mod` and `Savegame` | `script-inventory` | Signatures are centralized in Phase 4; executable behavior arrives with owning strategic/tactical gameplay slices. Compilation support must not imply that an unavailable host binding can execute. |

## Harness workflow

From the repository root on Windows with Visual Studio C++ tools:

```powershell
.\tools\update-script-inventory.ps1
.\tools\capture-script-core-reference.ps1
```

Both commands discover the conventional reference checkout, accept `-ReferenceRoot`,
and refuse a revision other than the pinned commit. Captures write to ignored
`artifacts/reference-script-core/` by default. Updating committed expected output
requires an explicit `-OutputPath`; review semantic changes before changing the fixture.

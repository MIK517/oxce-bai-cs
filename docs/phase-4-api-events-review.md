# Phase 4 API, events, and value-state review

Review date: 2026-08-29.

## Outcome

The managed scripting boundary now has the contracts required to consume an exact API
catalog without coupling `Oxce.Scripting` to gameplay assemblies:

- stable binding IDs, source provenance, parser-group membership, parameters, parsers,
  and constants live in one immutable catalog model;
- compilation resolves catalog constants and scalar host calls, records only used
  declarations in immutable IR, and enforces writable arguments;
- runtime providers are installed separately, with distinct missing-capability and
  provider-failure diagnostics;
- global event definitions compose by stable offset around the current script, including
  reference `new`, `update`, `override`, `delete`, `ignore`, warning, failure, and limit
  behavior;
- typed tags are globally named, one-based, source-attributed, and bounded; mutable
  value instances capture nonzero entries and restore transactionally without YAML
  ownership in scripting.

The new `script-events` C++ oracle compiles the pinned reference engine and captures
event ordering and mutation behavior. It verifies that negative events run before the
current script, positive events run after it, updates replace in place before the final
stable offset sort, unknown update/delete warn, unknown override throws, ignored code
does not install an event, and an invalid zero offset is diagnosed and skipped.

## Catalog audit correction

The generated inventory is a discovery ledger, not an executable declaration catalog.
Its 503 binding-name candidates contain 96 `addCustomConst` occurrences and 407 callable
registration occurrences across several helper forms. Those occurrences still omit
the template-resolved parameter types, writable/reference modifiers, overload aliases,
transitive helper registrations, and parser membership needed for safe compilation.

Generating declarations directly from those strings would create false APIs and could
silently select the wrong overload. The exact catalog is therefore isolated into a
focused sequential branch. That branch must:

1. resolve every one of the 24 `ScriptRegister` roots and its transitive helpers;
2. normalize constants separately from callable overloads;
3. assign stable IDs only after owner, name, full parameter alternatives, aliases,
   visibility, and source location are known;
4. derive membership from the 35 parser types and their constructor registrations;
5. generate runtime declarations and a checked ledger from the same reviewed input;
6. reject duplicate IDs, unresolved candidates, missing parser groups, and generated
   drift in tests.

This adds one CI branch but removes a much larger review and compatibility risk from
the content-closure branch. Branches remain sequential, so each is pushed only after
its local integration gate and merged before the next begins.

## Reference sources inspected

Reference commit: `4df3a5e571a1a4b5e8a46d3161fb2e21a2adba15`.

- `src/Engine/Script.h`: `ScriptParserEventsBase`, `ScriptParserEvents`, `ScriptTag`,
  `ScriptGlobal`, `ScriptValuesBase`, and `ScriptValues`.
- `src/Engine/Script.cpp`: event loading/release, global load lifecycle, tag registration,
  sparse value load/save, and worker execution around the current script.
- `src/Engine/ScriptBind.h`: registration helper families and type inference boundary.
- `src/Mod/ModScript.h`: 35 inventoried parser declarations, of which 34 are event
  parsers in the current discovery ledger.
- `src/Mod/Mod.cpp`: `ModScriptGlobal`, `RuleList`, mod constants, global initialization,
  and value-state ownership.
- The 24 inventoried `ScriptRegister` definitions, sampled through `Armor::ScriptRegister`
  to confirm constants, overloads, fields, helper expansion, values, and debug bindings
  coexist under one root.

## Deliberate remaining boundaries

- The exact gameplay API declarations are not inferred from candidate strings; they are
  the next branch.
- The current compiler host-call path covers scalar parameters. Custom references,
  receiver rewriting, separators, text lifetime, and overload alternatives arrive with
  the generated catalog that requires them.
- Event execution currently models writable scalar outputs. Exact read-only output reset
  and host argument registers require parser signatures from that catalog.
- Scripting owns value schema and transactional state semantics. Phase 5 still owns YAML
  names, migrations, unknown-field policy, and persistence transactions.

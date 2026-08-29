# Phase 4 implementation plan and baseline review

Review date: 2026-08-29.

## Baseline

Phase 2 asset loading and Phase 3 typed-content closure are healthy. At the start of
Phase 4, release builds complete without warnings and all 371 public and locally
available compatibility tests pass. Complete `xcom1` and
`xcom1` -> `40k` -> `40k_ROSIGMA_edits` installations reached `linked` during the
Phase 3 closure audit.

The scripting implementation itself has not started. `Oxce.Scripting` contains only
its assembly marker. The `script-core` fixture verifies captured C++ reference output;
it does not yet exercise a managed compiler or VM. `ScriptsCompiled` is represented as
a content capability but no build advances to it. Typed loaders preserve or diagnose
script-bearing input for later compilation.

The pinned source inventory identifies 54 core operation names, 24 `ScriptRegister`
definitions, 503 binding-name candidates, 99 constant registrations, 35 parser types,
and 27 script-value ownership sites. Candidate binding names are not semantic API
declarations until their overloads, types, visibility, prefixes, and parser membership
have been confirmed from the owning registration functions.

The architecture/performance work previously listed as the first immediate task in the
implementation plan is complete. Aggregate content is parsed once, diagnostic severity
is retained beyond the message limit, large scalar-key mappings use a compatible lazy
index, presentation is revision-driven, and content stages have separate benchmarks.

## Phase 4 completion contract

Phase 4 guarantees that:

1. Compatible scripts can be tokenized, parsed, type-checked, bound, and compiled.
2. Binding-free and synthetic host-bound programs execute with compatible deterministic
   state and event ordering.
3. Reference binding signatures, constants, types, and parser/event membership are
   declared in one auditable catalog.
4. A content build reaches `ScriptsCompiled` only after every script-bearing node in
   its published graph compiles successfully.
5. A declared binding without an installed gameplay provider compiles, but invocation
   fails intentionally with an actionable runtime-capability diagnostic.

Implementing the behavior of every gameplay binding is not a Phase 4 gate. Binding
implementations arrive with their owning strategic or tactical vertical slices. Mission
and arc scheduling are gameplay work; Phase 4 compiles their script envelopes. Phase 4
defines persistent script-value schemas and runtime state semantics, while Phase 5 owns
save YAML, migrations, unknown fields, and transactional restoration.

## Required implementation slices

### Specification and evidence

- Refine candidate inventories into exact operation overloads, writable arguments,
  receiver rewriting, aliases, constants, types, parser groups, and registration order.
- Expand pinned C++ probes across tokens, literals, declarations, overloads, control
  flow, operation families, runtime failures, limits, globals, events, tags, and values.
- Compare semantic traces rather than the reference bytecode representation.
- Split broad scripting-matrix rows as each behavior becomes executable.

### Language front end

- Lexer and statement parser with precise source spans, comments, semicolons, symbols,
  dotted references, labels, text escapes, and signed integer bases.
- Types, symbols, output parameters, locals, constants, scopes, and bounded register
  layout.
- Reference-compatible overload scoring, ambiguity, writable arguments, pointer
  editability, receiver rewriting, and the argument-separator pseudo-type.
- Structured diagnostics retaining failure category, parser/parent identity, operation
  or argument, and location.

### Compiler and VM

- Immutable semantic IR with stable operation, type, and binding identifiers.
- Control-flow lowering for blocks, conditionals, loops, labels, break, continue,
  return, and unreachable-code validation.
- All core operations with deliberate signed 32-bit wrapping, division, modulo, shift,
  power, square-root, shade, color, and wave semantics.
- Compact register-array execution, explicit host-binding dispatch, bounded execution,
  and optional deterministic traces stored outside the hot instruction representation.

### Registration, globals, events, and values

- Generated runtime declarations from one reviewed semantic catalog.
- Exact parser groups and transitive `ScriptRegister` registrations.
- Global constants, namespaces, tags, file provenance, and load order.
- Rule-local defaults and global-event `new`, `update`, `override`, `delete`, and
  `ignore` behavior, including offsets, ordering, and limits.
- Typed value schemas and storage whose instances are owned by gameplay state.

### Content integration and closure

- Promote the phase-specific aggregate into a durable staged content snapshot.
- Compile preserved script nodes after linking without reparsing rulesets.
- Gate `ScriptsCompiled` independently of `ResourcesResolved` and never publish a false
  success after bounded diagnostic retention.
- Close public and private script corpora, add stage benchmarks, update matrices, and
  record a Phase 4 closure audit.

## Feature branches and CI policy

Phase 4 is delivered through five sequential branches. Each branch contains reviewable
commits but is pushed only after its integration gate passes locally. The next branch
starts from `main` after the previous branch is synchronously rebased and merged.

1. `codex/phase4-language-foundation`: architecture decision, refined foundational
   inventory, expanded oracle, lexer, syntax, types, symbols, register layout, overload
   resolution, and immutable IR contracts.
2. `codex/phase4-core-vm`: control-flow lowering, binding-free core operations, VM,
   runtime limits, traces, and robustness tests. Synthetic host dispatch moves to the
   API/events branch so its contracts are built from the reviewed catalog rather than
   a temporary parallel abstraction.
3. `codex/phase4-script-api-events`: catalog/runtime contracts, provider separation,
   globals, tags, event composition/execution, and script-value state seam. The detailed
   audit and split rationale are recorded in the
   [API/events review](phase-4-api-events-review.md).
4. `codex/phase4-api-catalog`: exact reviewed API catalog, generated declarations,
   parser membership, custom types, receiver/separator lowering, and drift checks.
5. `codex/phase4-content-closure`: durable content snapshot, script compilation stage,
   capability gating, public/private corpus closure, benchmarks, and final documentation.

This is the smallest practical branch count. One branch would combine unrelated risk
and prevent useful review; one branch per low-level slice would multiply the three-OS
and coverage workflow without creating additional integration confidence. SDL validation
should not run because these branches do not modify application, rendering, engine, or
platform paths.

### Delivery status

| Branch | Status | Evidence / remaining boundary |
|---|---|---|
| `codex/phase4-language-foundation` | merged | Lexer, syntax tree, type and symbol foundations, overload scoring, bounded register layout, immutable IR, initial pinned core oracle. |
| `codex/phase4-core-vm` | merged | Scalar declarations and returns, structured control flow, all public binding-free core operation families, bounded register VM, structured runtime failures, and optional traces. The pinned oracle covers 33 compile/execution cases. |
| `codex/phase4-script-api-events` | merged | Immutable catalog contracts, catalog-driven scalar host calls, explicit missing providers, global event composition/execution, typed tags, and transactional value-state seam. The pinned event oracle covers ordering and all five mutation directives. |
| `codex/phase4-api-catalog` | merged | The pinned metadata probe resolves 60 concrete parsers, 755 overloads, 132 constants, and 94 emitted type forms with zero unresolved declarations. The reviewed fixture is the embedded runtime source; typed parser construction, receiver rewriting, separator matching, and count/ID drift checks consume it. See the [catalog review](phase-4-api-catalog-review.md). |
| `codex/phase4-content-closure` | merged | Durable one-parse content snapshot, transactional script-stage gating, defaults/tags/events/rule/stat compilation, public/private corpus closure, benchmark coverage, and [closure audit](phase-4-status.md). |
| `codex/phase4-full-install-corrections` | implemented for review | Full-installation corrections for overload scoring, list-loop lowering, typed core-name dispatch and value copy, file-scoped `RuleList`, corpus error gating, and a reusable installation audit command. |

## Authoritative references

The primary Phase 4 reference files at commit
`4df3a5e571a1a4b5e8a46d3161fb2e21a2adba15` are:

- `src/Engine/Script.h`, `Script.cpp`, and `ScriptBind.h`;
- `src/Mod/ModScript.h` and script construction/loading in `src/Mod/Mod.cpp`;
- the 24 inventoried `ScriptRegister` definitions;
- the inventoried `ScriptValues` owners and their save registration sites.

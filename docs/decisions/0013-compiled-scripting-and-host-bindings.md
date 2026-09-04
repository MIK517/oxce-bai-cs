# ADR 0013: Compiled scripting and host-binding capabilities

## Status

Accepted for Phase 4.

## Context

OXCE scripts combine a compact core language with hundreds of registered operations on
immutable rules and mutable campaign or battle objects. `Oxce.Scripting` must compile
existing scripts without depending on gameplay, while gameplay must not expose mutable
runtime objects or persistence representations merely to satisfy the compiler.

The reference implementation emits a compact byte stream containing function dispatch
details and native-layout values. Copying that representation would bind the port to
pointer size, function pointers, and C++ object layout. Runtime C# code generation would
also complicate diagnostics, bounded execution, deployment, and future Native AOT
evaluation.

## Decision

`Oxce.Scripting` owns the language, stable type and operation identifiers, immutable
semantic IR, compiler, VM, event infrastructure, and the declaration side of the host
binding catalog. The catalog records names, overloads, writable arguments, aliases,
visibility, parser/event membership, and stable numeric IDs from one auditable source.

Binding declarations and implementations are separate. Gameplay and resource owners
install narrow providers for bindings they can execute. Compilation may resolve a
declared binding without a provider. Invocation then fails intentionally with a
structured missing-capability diagnostic; it never silently substitutes a default.

The VM uses instruction arrays, numeric operation/binding IDs, and bounded register
storage. Source maps and optional traces are immutable side data rather than fields on
hot instructions. Runtime C# code generation and raw native function pointers are not
used.

`ScriptsCompiled` means every script-bearing node in the content snapshot was parsed,
type-checked, bound against the declared catalog, and lowered to immutable executable
IR. It does not mean every gameplay provider or event source is installed.

Each parsed ruleset document has a stable ordered identity that is carried into rule
operation provenance. File-local API visibility is indexed by that identity, not by a
case-folded physical source path. Paths remain diagnostic metadata and may differ only
by case in a ZIP or on a case-sensitive filesystem.

Script-value schemas and type semantics live in scripting. Mutable instances and their
lifecycle belong to gameplay. Savegames maps those gameplay-owned capture/restoration
contracts to external names, versions, migrations, and unknown-field sidecars.

Reference limits of nine outputs, sixteen parsed arguments, and 64 pointer-sized bytes
of register storage are retained. The port additionally applies configurable bounds for
compiled instructions and bytes, execution instructions, call/event depth, trace size,
and aggregate content compilation. Limit failures are structured and distinct from
compile errors, operation failures, missing providers, and invalid restored state.

## Consequences

- The compiler remains platform-independent and cannot call gameplay directly.
- Complete mod corpora can establish compilation compatibility before their gameplay
  subsystems are executable.
- Stable semantic IR may differ from reference bytecode while preserving observable
  language behavior.
- Immutable binding declarations are indexed once by exact parser group; compiler
  overload selection preserves declaration visibility, scoring, ordering, and ambiguity
  behavior without allocating temporary candidate-result arrays.
- File scopes share API catalogs by monotonic tag-catalog revision and current mod. A
  final public tag catalog remains distinct from transient compiler builders.
- Each gameplay slice must implement and test all providers and event sources it claims
  as compatible; `ScriptsCompiled` alone is not such a claim.
- Phase 5 can restore script values transactionally without making YAML or save DTOs
  part of gameplay state.

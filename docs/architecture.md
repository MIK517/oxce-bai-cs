# Architecture

## Principles

1. Compatibility behavior is explicit and testable.
2. Domain code does not know SDL, filesystem layout, or serialization libraries.
3. External formats are parsed through owned compatibility APIs.
4. The port may improve internal design without changing mod-visible semantics.
5. Headless execution is a first-class capability for fast scenario testing.

The implemented pre-Phase-4 corrections are recorded in the
[2026-08-29 architecture and performance review](architecture-performance-review.md).
The current runtime boundary, findings, and ordered follow-up work are recorded in the
[post-Phase-4 assessment](post-phase-4-assessment.md) and
[implementation plan](post-phase-4-implementation-plan.md).

## Projects

| Project | Responsibility |
|---|---|
| `Oxce.Core` | Small shared primitives, identifiers, coordinates, time, owned structured diagnostics, randomness abstractions |
| `Oxce.Formats` | YAML compatibility DOM and codecs for original X-COM binary/resource formats |
| `Oxce.Scripting` | OXCE lexer/parser, type system, compiler/IR, VM, events, bindings infrastructure |
| `Oxce.Mods` | Mod discovery/order, ruleset composition, typed rules, resource catalog |
| `Oxce.Resources` | Resolved-resource runtime, lazy decoding, streaming, bounded caches, preload groups, and cache/archive telemetry |
| `Oxce.Savegames` | External save schema, compatible read/write, migrations, unknown-field preservation, and adapters to gameplay-owned capture/restore contracts |
| `Oxce.Gameplay` | Geoscape, bases, interception, battlescape, AI, mission generation, rule execution, mutable runtime state, invariants, and save-neutral capture/restore contracts |
| `Oxce.Rendering` | Indexed surfaces, palettes, software primitives, text/layout models, render commands |
| `Oxce.Engine` | State stack, loop, input abstractions, clocks, orchestration, headless host |
| `Oxce.Platform.Sdl` | SDL3 native interop, windows, input devices, audio, GPU presentation |
| `Oxce.App` | Composition root, command line, configuration, startup and fatal diagnostics |

Tests and tooling are deliberately separate from production code. `Oxce.FixtureTool`
will inspect, normalize, hash, and compare reference artifacts.

Compatibility-facing code reports owned structured diagnostics through
`IDiagnosticSink`. Formatting and output providers remain composition concerns;
`Oxce.Engine` adapts diagnostic events to Microsoft logging without exposing logging
types to lower projects. See [ADR 0009](decisions/0009-structured-diagnostics-and-logging.md).

## Dependency direction

```text
App -> Engine -> Gameplay -> Mods -> Scripting -> Core
App -> Engine -> Resources -> Mods
Resources -> Formats -> Core
Resources -> Rendering -> Core
App -> Savegames -> Gameplay
Savegames -> Formats -> Core
Savegames -> Mods
App -> Platform.Sdl -> Engine
Platform.Sdl -> Rendering -> Core
```

`Gameplay` must not reference `Savegames`. `Savegames` is an external adapter that
references gameplay-owned, save-neutral capture and restoration contracts. The
application composes the adapter with the simulation. Do not introduce reciprocal
references or move mutable runtime state into a common persistence-shaped model merely
to avoid this dependency direction. See
[ADR 0008](decisions/0008-gameplay-owned-state-and-save-adapters.md).

`Gameplay` also must not reference `Oxce.Resources`; rule/resource IDs and gameplay
decisions remain independent of decoded surfaces, audio, caches, and filesystem state.
`Oxce.Resources` consumes lightweight descriptors resolved by `Oxce.Mods`, as defined
by [ADR 0014](decisions/0014-dedicated-resource-runtime.md).

## Runtime composition

The application composes four independently testable pipelines:

1. **Content:** locate files -> parse formats -> order mods -> merge rules -> validate.
2. **Simulation:** input command -> gameplay rule -> state mutation -> events.
3. **Persistence:** gameplay capture/restore contracts <-> compatibility save
   representation <-> YAML.
4. **Presentation:** state snapshot -> indexed/software render commands -> SDL output.

The headless host runs the first three without SDL. Compatibility tests should prefer
this path and use presentation tests only where visibility or decoded graphics matter.

Content composition is one staged build, not one parse per typed consumer. Ordered
ruleset inputs are parsed once into session-owned compatibility nodes, dispatched to
named and special composers, typed, linked, resource-resolved, and script-compiled. The
published content snapshot is immutable; parse-only state may be released once retained
unknown/deferred nodes and compiled scripts have explicit owners. The normal runtime
path must release parse/audit-only state after those ownership transfers; tools may
request an explicit audit artifact when operation history is required.

The concrete build-session, runtime-publication, structured-node, and optional-audit
lifetimes are defined in [content lifetime](content-lifetime.md). Application code must
publish `RuntimeContent`; retaining `ContentBuildSession` or `ContentSnapshot` as the
runtime root would also retain build-only state. See
[ADR 0015](decisions/0015-runtime-content-publication.md).

## Data modeling

- Use explicit stable string IDs for rules and resources.
- Separate unresolved DTO/node data from validated linked rules.
- Separate immutable rule definitions from mutable campaign/battle state.
- Gameplay owns mutable campaign/battle state, its invariants, persistent identities,
  and the semantic contracts used to capture and restore it.
- Save adapters own external field names, legacy defaults, versions, migrations,
  unknown YAML preservation, and atomic file replacement. Raw YAML nodes and
  serializer concerns do not enter gameplay state.
- Restoration is staged: parse and bound external data, allocate stable identities,
  populate values, resolve object/rule references, restore script values, validate the
  complete graph in gameplay, and only then publish the state.
- Capture must observe a consistent simulation state. Start with a straightforward
  snapshot and measure it; large tactical collections may later use segmented visitors
  or copy-on-write, but persistence must not mutate live state while writing.
- Resolve cross-references in a dedicated link/validation pass.
- Represent optional/missing/null distinctly where OXCE does.
- Keep serialization logic near compatibility DTOs or codecs, not spread through UI.

Do not use reflection, `InternalsVisibleTo`, public setters added for deserialization,
or partially initialized runtime entities as the persistence boundary. Save loading may
need restoration operations unavailable to ordinary gameplay commands, but those
operations remain explicit gameplay APIs and enforce final invariants.

## Managed extensions

Loadable C# assemblies are trusted engine extensions, not a replacement for compatible
OXCE mods or scripts. Their stable contracts belong in a small, versioned abstractions
assembly when the first extension slice is implemented. Extensions receive narrow,
read-only views or snapshots and submit commands through validated gameplay APIs; they
do not receive mutable runtime entities, persistence DTOs, or YAML nodes. In-process
assemblies are not a security boundary and must be treated as fully trusted code.

## Script runtime ownership

Compiled scripts publish packed immutable instruction, operand, binding-slot, and
source-map tables. Runtime callers own reusable bounded execution frames; those frames
carry scalar, text, and reference values through nested host calls without making
gameplay objects part of the compiler model. The allocating dictionary/result API is a
tooling adapter, not the hot gameplay ABI. See
[ADR 0016](decisions/0016-packed-script-runtime.md).

## Rendering

Indexed 8-bit surfaces and palettes are the canonical compatibility layer. The SDL3
backend may upload an RGBA texture each frame or use a shader palette lookup. Software
operations must remain testable without a GPU. Modern UI may be layered over the same
simulation, but legacy resources and palette-index semantics remain supported.

## Native and deployment policy

Start with self-contained .NET deployments and SDL3 dynamic libraries. Do not make
Native AOT a Phase 0 requirement. Keep reflection and dynamic-code usage controlled so
AOT can be evaluated after scripting, YAML, and platform dependencies stabilize.

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
| `Oxce.Mods` | Installation bootstrap, mod discovery/order, ruleset composition, typed rules, resource catalog |
| `Oxce.Resources` | Resolved-resource runtime, lazy decoding, streaming, bounded caches, preload groups, and cache/archive telemetry |
| `Oxce.Savegames` | External save schema, compatible read/write, migrations, unknown-field preservation, and adapters to gameplay-owned capture/restore contracts |
| `Oxce.Gameplay` | Geoscape, bases, interception, battlescape, AI, mission generation, rule execution, mutable runtime state, invariants, and save-neutral capture/restore contracts |
| `Oxce.Rendering` | Indexed surfaces, palettes, software primitives, text/layout models, render commands |
| `Oxce.Engine` | State stack, loop, input abstractions, clocks, orchestration, headless host |
| `Oxce.Extensions.Abstractions` | Versioned, implementation-independent contracts for trusted managed extensions |
| `Oxce.Extensions` | Manual extension discovery/loading, capability adapters, lifecycle, diagnostics, and failure containment |
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
App -> Extensions -> Gameplay
Extensions -> Extensions.Abstractions
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

Resource resolution is owned by `Oxce.Mods`: it converts declarations and VFS winners
into immutable, provenance-bearing descriptors and generation-scoped typed handles.
`Oxce.Resources` consumes those descriptors for lazy decode, explicit preload groups,
streaming media, and a bounded size-aware LRU. The application and resource browser
must use that service rather than opening descriptor source paths. Shared `common`
assets are mapped below game-specific external data and mod layers so normal OXCE
installation precedence is preserved.

Runtime rule linking is also owned by `Oxce.Mods`. A successful content generation
publishes dense immutable projections for the first strategic families, with typed
generation-scoped handles for eager links and optional resolved handles alongside IDs
for reference-defined runtime lookups. Numeric slots are internal and never enter mod
or save schemas. Resource and script references share the same generation boundary.
The complete typed catalog, deferred YAML, and provenance live in the separate cold
`ContentCompatibilityData` sidecar and are not retained by normal gameplay publication.
See [ADR 0017](decisions/0017-generation-scoped-runtime-rule-handles.md) and
[ADR 0023](decisions/0023-hot-runtime-content-and-versioned-capabilities.md).

The concrete build-session, runtime-publication, structured-node, and optional-audit
lifetimes are defined in [content lifetime](content-lifetime.md). Application code must
publish `RuntimeContent`; retaining `ContentBuildSession` or `ContentSnapshot` as the
runtime root would also retain build-only state. See
[ADR 0015](decisions/0015-runtime-content-publication.md).

Installation layout, activation planning, structured startup failures, coarse progress,
and cancellable runtime publication are centralized in `Oxce.Mods.Bootstrap`. Normal
application entry points receive only `RuntimeContent`; audit and measurement tools
reuse planning while retaining explicit control of build-only lifetimes. See
[ADR 0020](decisions/0020-installation-bootstrap.md).

The production bootstrap may restore a versioned compiled-content reconstruction image
after planning. Its key binds the ordered ruleset contents, resource identities, mod
metadata, compatibility/compiler revisions, and current limits. A hit still creates a
fresh generation and reruns runtime rule linking; failures fall back to the normal build.
Audit tools bypass this optimization so compatibility evidence always exercises source
parsing and composition. See
[ADR 0022](decisions/0022-versioned-compiled-content-cache.md).

## Data modeling

- Use explicit stable string IDs for rules and resources.
- Use typed handles only inside a content generation; translate to and from stable IDs
  at persistence, script, diagnostic, and extension boundaries.
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
- Routine presentation reads use gameplay-owned query views rather than persistence
  capture. Query and command ports are separate capabilities; the initial campaign
  overview copies only scalar totals and visible base/facility data. Time-advance
  events retain fixed-size trigger summaries with allocation-free ordered replay. See
  [ADR 0019](decisions/0019-bounded-time-events-and-campaign-queries.md).
- OXCE save adapters overlay implemented fields onto an opaque parsed source document
  so later-strategy, tactical, and mod-owned nodes survive partial-model round trips;
  gameplay never sees that sidecar. See
  [ADR 0018](decisions/0018-campaign-state-and-oxce-save-overlay.md).
- Resolve cross-references in a dedicated link/validation pass.
- Represent optional/missing/null distinctly where OXCE does.
- Keep serialization logic near compatibility DTOs or codecs, not spread through UI.

Do not use reflection, `InternalsVisibleTo`, public setters added for deserialization,
or partially initialized runtime entities as the persistence boundary. Save loading may
need restoration operations unavailable to ordinary gameplay commands, but those
operations remain explicit gameplay APIs and enforce final invariants.

## Managed extensions

Loadable C# assemblies are trusted engine extensions, not a replacement for compatible
OXCE mods or scripts. Their stable contracts live in the small, versioned
`Oxce.Extensions.Abstractions` assembly; discovery, loading, lifecycle, capability
adaptation, and callback containment live in `Oxce.Extensions`. Extensions receive
narrow, read-only query views and submit commands through validated gameplay APIs;
they do not receive mutable runtime entities, persistence DTOs, YAML nodes, runtime
handles, or a general service provider. In-process assemblies are not a security
boundary and must be treated as fully trusted code.

The experimental API `0.2` grants campaign access as an explicit capability bundle.
Query, command, and committed-event contracts have stable IDs and independent versions;
query and command interfaces remain separate. New strategic or tactical features add or
version narrow capabilities instead of growing one shared access interface. See
[ADR 0023](decisions/0023-hot-runtime-content-and-versioned-capabilities.md).

Versioned, bounded extension state is represented independently of YAML and is encoded
by `Oxce.Savegames`. Extension-free saves remain in the ordinary compatibility scope;
state declared necessary for continuation cannot be silently ignored when its owning
extension is absent. The accepted policy and deferred tactical-AI capability are in
[ADR 0021](decisions/0021-versioned-managed-extensions.md).

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

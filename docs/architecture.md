# Architecture

## Principles

1. Compatibility behavior is explicit and testable.
2. Domain code does not know SDL, filesystem layout, or serialization libraries.
3. External formats are parsed through owned compatibility APIs.
4. The port may improve internal design without changing mod-visible semantics.
5. Headless execution is a first-class capability for fast scenario testing.

## Projects

| Project | Responsibility |
|---|---|
| `Oxce.Core` | Small shared primitives, identifiers, coordinates, time, diagnostics, randomness abstractions |
| `Oxce.Formats` | YAML compatibility DOM and codecs for original X-COM binary/resource formats |
| `Oxce.Scripting` | OXCE lexer/parser, type system, compiler/IR, VM, events, bindings infrastructure |
| `Oxce.Mods` | Mod discovery/order, ruleset composition, typed rules, resource catalog |
| `Oxce.Savegames` | Save schema, compatible read/write, object/reference reconstruction, migrations |
| `Oxce.Gameplay` | Geoscape, bases, interception, battlescape, AI, mission generation, rule execution |
| `Oxce.Rendering` | Indexed surfaces, palettes, software primitives, text/layout models, render commands |
| `Oxce.Engine` | State stack, loop, input abstractions, clocks, orchestration, headless host |
| `Oxce.Platform.Sdl` | SDL3 native interop, windows, input devices, audio, GPU presentation |
| `Oxce.App` | Composition root, command line, configuration, startup and fatal diagnostics |

Tests and tooling are deliberately separate from production code. `Oxce.FixtureTool`
will inspect, normalize, hash, and compare reference artifacts.

## Dependency direction

```text
App -> Engine -> Gameplay -> Savegames -> Mods -> Scripting
 |       |          |            |          |        |
 |       +----------+------------+----------+------> Core
 |                                                   ^
 +-> Platform.Sdl -> Rendering ----------------------+ 

Formats -> Core
Mods -> Formats
Savegames -> Formats
```

This is a starting dependency graph, not permission to create cycles. If gameplay
models needed by saves cause a cycle, move stable contracts/value objects downward or
introduce narrow interfaces; do not add reciprocal project references.

## Runtime composition

The application composes four independently testable pipelines:

1. **Content:** locate files -> parse formats -> order mods -> merge rules -> validate.
2. **Simulation:** input command -> gameplay rule -> state mutation -> events.
3. **Persistence:** runtime state <-> compatibility save representation <-> YAML.
4. **Presentation:** state snapshot -> indexed/software render commands -> SDL output.

The headless host runs the first three without SDL. Compatibility tests should prefer
this path and use presentation tests only where visibility or decoded graphics matter.

## Data modeling

- Use explicit stable string IDs for rules and resources.
- Separate unresolved DTO/node data from validated linked rules.
- Separate immutable rule definitions from mutable campaign/battle state.
- Resolve cross-references in a dedicated link/validation pass.
- Represent optional/missing/null distinctly where OXCE does.
- Keep serialization logic near compatibility DTOs or codecs, not spread through UI.

## Rendering

Indexed 8-bit surfaces and palettes are the canonical compatibility layer. The SDL3
backend may upload an RGBA texture each frame or use a shader palette lookup. Software
operations must remain testable without a GPU. Modern UI may be layered over the same
simulation, but legacy resources and palette-index semantics remain supported.

## Native and deployment policy

Start with self-contained .NET deployments and SDL3 dynamic libraries. Do not make
Native AOT a Phase 0 requirement. Keep reflection and dynamic-code usage controlled so
AOT can be evaluated after scripting, YAML, and platform dependencies stabilize.


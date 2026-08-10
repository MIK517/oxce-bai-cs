# General implementation plan

Current bootstrap evidence and remaining gates are tracked in
[`phase-0-status.md`](phase-0-status.md).

## Objective and delivery strategy

Deliver a compatibility-complete .NET 10 engine in executable vertical slices. The C++
engine remains the playable reference until the release gate is met. Each phase produces
fixtures and working behavior; phases are not bulk source-translation milestones.

Status terms used below:

- **Gate:** required before dependent phases claim compatibility.
- **Parallel:** may advance concurrently once its prerequisites are stable.
- **Done:** exit criteria are demonstrated by automated tests and updated matrices.

## Phase 0 — Bootstrap and specification harness

Deliverables:

- Install/pin the .NET 10 SDK in development and CI environments.
- Add the selected unit-test framework, coverage, formatting, static analysis, and CI
  builds for Windows, Linux, and macOS.
- Add the full GPL-3.0-or-later license and preserve upstream attribution.
- Inventory supported OXCE rule nodes, script APIs, save nodes, formats, options, and
  gameplay subsystems into compatibility matrices.
- Implement `Oxce.FixtureTool` manifests and normalized JSON/text output.
- Capture a small, redistributable oracle corpus from a pinned C++ reference build.
- Establish benchmark and allocation baselines for mod loading, tactical pathfinding,
  AI turns, and indexed rendering.

Exit gate:

- CI builds every project on the three desktop platforms.
- At least one synthetic fixture flows through the reference capture and .NET comparison
  pipeline, and expected output records the reference commit.

## Phase 1 — Files, YAML, diagnostics, and common primitives

Deliverables:

- Virtual file catalog with OXCE search precedence, normalized paths, case rules, and
  mod-aware provenance.
- Buffered binary readers/writers with explicit endian and bounds behavior.
- YAML compatibility DOM supporting maps, sequences, scalars, null/missing distinction,
  multiple documents, locations, references/aliases, and deterministic emission.
- Typed conversion helpers matching OXCE defaults, numeric parsing, enum handling, and
  error reporting.
- Logging, diagnostics, command-line paths, options foundation, localization primitives,
  IDs, coordinates, clocks, and injected random sources.

Exit gate:

- The YAML feature matrix required by bundled rules and saves is compatible.
- Malformed-input and resource-limit tests are present.
- Representative ruleset and save YAML can be parsed and semantically normalized.

## Phase 2 — Original asset formats and indexed rendering foundation

Deliverables:

- Palette and indexed-surface implementation with blit, crop, shade, transparency,
  primitives, text support, and palette conversion.
- Decoders for SCR/DAT, SPK, BDY, PCK/TAB and general images used by OXCE.
- Parsers for CAT, maps, routes, MCD/terrain, voxel/LOFT data, fonts, palettes, and other
  original resources identified by the matrix.
- SDL3 window/input/presentation spike with an indexed frame displayed cross-platform.
- Audio abstraction and a decision on SDL audio/mixer replacement; defer rare codecs
  only with explicit matrix status.

Exit gate:

- A resource-browser tool loads owned UFO and TFTD installations through normal search
  rules and displays representative geoscape/battlescape assets.
- Exact decoded indexed data tests pass where formats are deterministic.

## Phase 3 — Mod system and typed rules

Deliverables:

- Mod metadata discovery, master selection, dependencies/conflicts, activation, ordering,
  resource overlays, and diagnostics.
- Generic ruleset merge pipeline reproducing OXCE inheritance, replacement, deletion,
  list ordering, and reference resolution.
- Typed rule objects grouped by vertical slice, starting with interfaces/resources and
  campaign-start data, then strategic and tactical rules.
- Localization, extra strings/sprites/sounds, interface themes, and resource offsets.
- A rule dump/diff command that compares normalized C++ and .NET resolved rules.

Exit gate:

- Bundled standard mods resolve without unsupported nodes.
- Selected large community mods parse and link; unsupported features fail explicitly,
  never by silent ignore.

## Phase 4 — OXCE scripting language

Deliverables:

- Lexer/parser with source positions and reference-compatible diagnostics.
- Type system, constants, registers, overload resolution, expressions/statements, limits,
  and script IR/bytecode.
- VM with deliberate integer and error semantics and no runtime C# code generation.
- Global/event scripts, inheritance/override/delete behavior, persistent script values,
  and debugging traces.
- Binding registration generated or declared from a single auditable catalog. Bindings
  arrive with the owning gameplay rule, but the complete catalog is tracked centrally.

Exit gate:

- Script grammar/operation matrices are complete for the targeted branch.
- Corpus scripts compile with matching accept/reject behavior.
- Execution fixtures match final state and event order for deterministic inputs.

This phase may overlap later gameplay work, but no gameplay subsystem is mod-compatible
until all of its script bindings and events are implemented.

## Phase 5 — Saves and runtime state foundation

Deliverables:

- Immutable rules linked to distinct mutable campaign and battle state models.
- Campaign header and body loading, object IDs/references, mod validation, RNG state,
  script values, and unknown/version handling.
- Stable save writing and atomic/recoverable file replacement.
- Original UFO/TFTD save import supported by the reference converter.
- Semantic snapshot and round-trip comparison tooling.

Exit gate:

- Representative early/mid/late campaign saves and active battle saves load, inspect,
  save, and reload without semantic loss for implemented subsystems.
- Missing-mod and corrupt-save failures are actionable and covered.

## Phase 6 — Playable strategic vertical slice

Suggested order:

1. New campaign and starting base.
2. Time progression and globe/world model.
3. Personnel, inventory, craft, transfers, facilities, and finance.
4. Research and manufacture.
5. Alien missions, UFO movement/detection, interception, and sites.
6. Monthly processing, events, campaign success/failure, statistics/diaries.

Build a functional UI, but optimize for complete actions and information rather than
copying every legacy state. Every slice includes save fields, rules, and script bindings.

Exit gate:

- A modded campaign can be played from creation through launching a tactical mission,
  with strategic save/reload at each major transition.

## Phase 7 — Playable battlescape vertical slice

Suggested order:

1. Map generation, tiles, routes/nodes, deployments, units, inventories.
2. Camera/selection and action legality.
3. Movement, pathfinding, doors, falling, terrain interactions, fire/smoke/light.
4. Line of sight/voxel collision, projectiles, explosions, damage, death/unconsciousness.
5. Reactions, morale/panic, melee, psi, special abilities, environmental effects.
6. Turn processing, AI, objectives, abort/end mission, recovery and debriefing.
7. Tactical save/reload and all tactical script bindings/events.

Exit gate:

- Representative UFO, terror, base defense, alien base, final, underwater, and custom
  deployments can be completed under the compatibility corpus.
- Scenario snapshots agree on legal actions and rule-driven transitions; individual
  random outcomes and pixels may differ.

## Phase 8 — Feature breadth and user experience

Deliverables:

- Ufopaedia, statistics, diaries, graphs, mod manager, options, key binding, notes,
  debug/test facilities, screenshots, and remaining UI flows.
- Audio/music, AdLib/MIDI strategy, FLC/video playback, shaders/scalers as selected,
  touch/controller support if retained, accessibility and high-DPI behavior.
- Packaging, data discovery, portable mode, crash reporting, localization, updater
  strategy, and migration documentation.
- Close every remaining rule, script, asset, save, and gameplay matrix entry.

## Phase 9 — Compatibility hardening and release

Deliverables:

- Automated runs across the full mod/save/asset corpus on supported operating systems.
- Long campaigns, repeated save cycles, memory/allocation profiling, malformed-input
  testing, and performance tuning using representative large mods.
- Documented intentional differences, known issues, minimum hardware, packaging, and
  upgrade/rollback procedures.
- Community beta with save backups and clear compatibility reporting.

Release gate:

- No silent unsupported rules or script operations in the declared compatibility scope.
- Strategic and tactical save compatibility matrices are complete.
- Original asset format matrix is complete.
- Representative vanilla and large modded campaigns are finishable.
- Crashes, data loss, and incorrect rule execution are release blockers.

## Workstream coordination

The following can proceed in parallel after Phase 1 interfaces stabilize:

- Resource codecs and indexed rendering.
- YAML/rules inventory and typed rule batches.
- Script parser/VM core.
- Reference fixture capture and scenario harness.

Gameplay work should be sliced end-to-end across rules, state, scripting, persistence,
and minimal UI. Avoid assigning all saves or all UI to a late integration phase.

## Immediate next tasks

1. Run the defined CI matrix after repository hosting is configured.
2. Add focused executable C++ oracle probes as compatibility-sensitive primitives are
   investigated.
3. Refine generated inventory candidates into exact rule keys, script APIs, save nodes,
   formats, options, and gameplay scenarios as their loaders are inspected.
4. Implement the YAML compatibility spike against a ruleset, a two-document save, and
   an error/location fixture.
5. Implement one indexed asset decoder and display it in an SDL3 window as the platform
   feasibility spike.

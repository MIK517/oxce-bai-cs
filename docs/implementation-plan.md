# General implementation plan

Current bootstrap evidence and remaining gates are tracked in
[`phase-0-status.md`](phase-0-status.md).

Phase 2 evidence and explicit playback deferrals are tracked in
[`phase-2-status.md`](phase-2-status.md).

The completed Phase 3 mod-loading and typed-rule closure, including its explicit
later-phase deferrals, is tracked in [`phase-3-status.md`](phase-3-status.md).

Controlled optimization measurement is defined in
[`performance-baselines.md`](performance-baselines.md); CI compiles but does not execute
the BenchmarkDotNet project.
Post-Phase-4 engineering budgets and dependency-selection policy are defined in
[`performance-targets.md`](performance-targets.md).

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
- Owned structured diagnostics with bounded collection and a standard logging adapter;
  command-line paths, options foundation, localization primitives, IDs, coordinates,
  clocks, and injected random sources.

Exit gate:

- The YAML feature matrix required by bundled rules and saves is compatible.
- Malformed-input and resource-limit tests are present.
- Representative ruleset and save YAML can be parsed and semantically normalized.

## Phase 2 — Original asset formats and indexed rendering foundation

Status: **Done**. See the [Phase 2 closure audit](phase-2-status.md).

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

Status: **Done**. See the [Phase 3 closure audit](phase-3-status.md). Resource
decoding/offset publication and script compilation remain assigned to their owning
later phases.

Deliverables:

- Mod metadata discovery, master selection, master-chain dependencies, activation,
  ordering, resource overlays, and diagnostics. OXCE has no general dependency/conflict
  collections; do not invent a port-specific schema as part of compatibility work.
- Generic ruleset merge pipeline reproducing OXCE inheritance, replacement, deletion,
  list ordering, and reference resolution.
- Typed rule objects grouped by vertical slice, starting with interfaces/resources and
  campaign-start data, then strategic and tactical rules.
- Localization, extra strings/sprites/sounds, interface themes, and platform-neutral
  resource declarations. Decoded resources and final shared offsets advance through the
  separate `resources-resolved` gate owned by resource/runtime integration.
- A rule dump/diff command that compares normalized C++ and .NET resolved rules.

Implemented closure uses deterministic composed and typed audit dumps. The typed dump
is a schema-versioned manifest rather than reflection serialization of the runtime
graph; it includes normalized provenance, deferred counts, and a composed semantic
digest suitable for fixture comparison.

Exit gate and evidence:

- Bundled standard mods resolve without unsupported nodes.
- Selected large community mods parse and link; unsupported features fail explicitly,
  never by silent ignore.

Bundled public fixtures satisfy the automated repository gate. The optional private
corpus exercises the large script-heavy 40k chain, but a synthetic empty `xcom1` master
cannot supply copyrighted base rules; that run proves parsing and typed-manifest breadth,
not a complete `linked` result.

Installation-level acceptance on 2026-08-28 closes the Phase 3 gate. Complete `xcom1`
and `xcom1` -> `40k` -> `40k_ROSIGMA_edits` installations both reach `linked` with zero
validation issues or error diagnostics. Corpus-scale inputs exposed and now cover
`refNode` identity consumption, reference-compatible Ufopaedia type dispatch,
alien-mission waves that may spawn a UFO, a deployment, or no object, and the
distinction between eager `afterLoad` links and IDs retained for runtime lookup. The
Rosigma run reports 204 unresolved runtime IDs as explicit warnings and preserves five
unsupported transformation properties as deferred-property warnings; neither class is
silently discarded or mistaken for a completed later-phase capability.

## Phase 4 — OXCE scripting language

Status: **Done**. See the [Phase 4 closure review](phase-4-status.md).

The detailed baseline, completion contract, implementation slices, and five-branch
delivery sequence are recorded in the [Phase 4 implementation plan](phase-4-plan.md).
The completed implementation boundary and corpus evidence are recorded in the
[Phase 4 closure review](phase-4-status.md).
Binding declarations are completed in this phase; gameplay binding implementations
remain part of their owning vertical slices as defined by
[ADR 0013](decisions/0013-compiled-scripting-and-host-bindings.md).

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

## Phase 5 — Runtime content and first persisted gameplay slice

The ordered architecture and performance work that begins this phase is specified in
the [post-Phase-4 implementation plan](post-phase-4-implementation-plan.md). It
separates build/audit content from runtime content, compacts script execution, resolves
and owns resources, and links the first strategic rules to runtime handles before
gameplay depends on bootstrap representations.

Deliverables:

- A compact immutable runtime content package that can release parse/audit-only state
  after scripts and resources have explicit owners.
- A reusable compact script execution ABI suitable for gameplay hot paths.
- Resolved resource descriptors, exact shared offsets, lazy decoded ownership, bounded
  caches, and an honest `resources-resolved` capability.
- Content-scoped typed rule handles and strongly typed runtime projections for the first
  strategic slice while preserving external IDs.
- Distinct mutable campaign state owned by `Oxce.Gameplay`, including stable persistent
  identities and save-neutral capture/restoration contracts for new-campaign creation,
  calendar/time, countries/regions, and the starting base.
- `Oxce.Savegames` adapters for the implemented state only, covering object
  IDs/references, mod validation, script values, versions, migrations, and
  persistence-owned unknown-field sidecars. `Oxce.Gameplay` retains no persistence
  reference, as required by ADR 0008.
- Staged restoration that validates the complete object/rule/script graph in gameplay
  before publishing it; no reflection, persistence-only public setters, or partially
  initialized runtime entities.
- Stable save writing and atomic/recoverable file replacement.
- Consistent semantic capture plus round-trip comparison tooling. Measure snapshot
  allocation and latency before adding segmented or copy-on-write capture.

Exit gate:

- Vanilla and representative modded new campaigns can be created headlessly with
  compatible starting state and installed script providers for the implemented slice.
- Representative C++ saves containing the implemented state load, inspect, save, and
  reload without semantic loss. Broader campaign, active-battle, and original-save
  conversion support advances with the gameplay state that can validate it.
- Missing-mod and corrupt-save failures are actionable and covered.

Status (2026-09-03): the campaign foundation satisfies this gate for public,
vanilla UFO, vanilla TFTD, and staged 40k/Rosigma scenarios. Supplied vanilla UFO,
TFTD, and Rosigma saves,
including opaque active-battle state, load and round-trip their implemented strategic
subset. See [campaign foundation status](campaign-foundation-status.md) and
[ADR 0018](decisions/0018-campaign-state-and-oxce-save-overlay.md). A minimal indexed
SDL view now creates and operates this slice without coupling gameplay to SDL.

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
Each slice uses gameplay-owned capture/restoration contracts and an external save
adapter; persistence representations must not become runtime models.

Time advancement now publishes fixed-size trigger summaries with allocation-free
ordered replay, and presentation uses separate lightweight campaign query and command
ports instead of persistence capture. See
[ADR 0019](decisions/0019-bounded-time-events-and-campaign-queries.md).

Installation discovery, activation planning, runtime content publication, progress,
cancellation, and structured failures are now centralized for application and headless
scenario startup. Audit and memory-measurement tools reuse planning without surrendering
their specialized lifetime controls. See
[ADR 0020](decisions/0020-installation-bootstrap.md).

## Immediate next tasks

1. Deliver personnel, inventory, craft, transfers, facilities, and finance as the next
   persisted strategic slice, including their owning script providers and UI actions.
2. Continue adding focused executable C++ probes, compatibility matrix entries, and
   controlled benchmarks with every compatibility-sensitive slice.

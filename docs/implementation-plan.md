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
The current Windows x64 framework-dependent, self-contained, and SDL3-staged distribution
sizes are recorded in [`performance-baselines.md`](performance-baselines.md); release packaging
changes must be compared with the matching profile rather than a cross-profile total.

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

The detailed [Phase 6 delivery plan](phase-6-plan.md) defines sequential feature
branches, including the branch 4 foundation/ordinary-operations/integration split, their commit purposes,
prerequisites, reference entry points and
acceptance gates. Prerequisite time-dispatch and mobile save-ownership work belongs in
the first logistics branch. The strategic deployment request closes this phase's side
of mission launch; playable tactical generation and consumption remain Phase 7.

Branch 1 now satisfies its [bounded logistics acceptance](strategic-logistics-status.md),
including fresh/cache corpus classification, indexed UI, mobile saves and preflight guards.
Branch 2 satisfies its [bounded strategic-readiness acceptance](strategic-readiness-status.md),
including base/facility management, personnel development, transformations, craft loadout,
servicing, save reload and indexed UI actions. Branch 3 satisfies its
[research and production acceptance](strategic-research-production-status.md), including
ordered unlocks, active projects and queues, resource/fund accounting, save reload and
indexed UI actions. Branch 4a implements the bounded
[world foundation](strategic-world-foundation-status.md); branch 4b implements bounded
[ordinary UFO and craft operations](strategic-world-operations-status.md) and satisfies
its supported UFO lifecycle closure gate with headless acceptance evidence. The
2026-09-26 scope revision assigns remaining mission/arc/event scheduling, broader
world lifecycles, scripts and pursuit to branch 4c; the approved
[4c/4d split](phase-6-world-integration-plan.md) assigns globe UI and combined branch 4
acceptance to 4d before branch 5. Branch 4b merged in `e10d96f`. Campaign-event payload
effects remain guarded until branch 6; 4c owns scheduling and persistence.
Branch 4c now includes [alien-base activity and discovery](strategic-world-alien-base-activity-status.md):
daily scoring, facility sight and craft patrol sight with save/reload and precise
transition guards. [Daily alien-base mission generation](strategic-world-alien-base-generation-status.md)
now schedules linked missions with region/race selection and persistent limits.
[Monthly upgrades and race evolution](strategic-world-alien-base-monthly-status.md)
now execute through an isolated registered world operation, including link repair
and save preservation. Existing operation-base initialization, broader ordinary
objective waves and delayed hunting mission creation now execute; see
[mission status](strategic-mission-script-status.md). Creation-tick immediate waves,
new-base/deployment effects, special UFO hunting/escorts and actual monthly caller
composition remain open with the other 4c capabilities. Research-driven linked
base deletion is covered by its cleanup slice below.

The first branch of the [2026-09-05 review](project-review-2026-09-05.md) corrects
compiled-cache resource dependencies: shared TAB/CAT headers and lengths now participate
in cache identity, with public loose/ZIP, malformed-input, precedence, override, and
cached/fresh regression scenarios. The second repair now projects starting scientists
and engineers, preserves difficulty/overlay defaults, and verifies fresh/cached campaign
creation through atomic save/reload. The startup measurement and CI efficiency branch
is also implemented; see [startup/CI status](startup-ci-efficiency-status.md).
It adds stage attribution, reduces metadata-key allocation, and cancels superseded PR
validation. Cache deserialization remains the primary warm-start bottleneck; the
5-second target and native artifact caching are not claimed complete.

Suggested order:

1. New campaign and starting base.
2. Time progression and globe/world model.
3. Personnel, inventory, craft, transfers, facilities, and finance.
4. Research and manufacture.
5. Alien missions, UFO movement/detection, interception, and sites.
6. Monthly processing, events, strategic endings and underlying statistics/diary data;
   full presentation remains Phase 8 and battle-derived outcomes remain Phase 7.

Build a functional UI, but optimize for complete actions and information rather than
copying every legacy state. Every slice includes save fields, rules, and script bindings.

Exit gate:

- A modded campaign can be operated from creation through a validated, persisted
  strategic deployment request, with strategic save/reload at each major transition.
  Phase 7's first battle generator must consume that request to close the broader
  playable mission-launch gate; a request alone does not constitute a playable battle.

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

Trusted managed extensions use a separate versioned abstractions assembly, manual
installation, narrow capabilities, callback failure containment, and a bounded
save-neutral state envelope. Tactical AI and live save integration remain attached to
their owning gameplay slices. See
[ADR 0021](decisions/0021-versioned-managed-extensions.md).

## Immediate next tasks

The September compatibility-guardrail, script-allocation, compiled-content-cache,
hot-runtime-partitioning, and portable Unicode-path slices are complete. The production
content root no longer retains typed compatibility/provenance graphs, virtual resource
identity is deterministic across hosts, and managed-extension API `0.2` exposes
separately versioned campaign query, command, and event capabilities.

1. Continue the [Phase 6 delivery plan](phase-6-plan.md) with world simulation and
   mission generation; retain the logistics/readiness/economy time and ownership guards.
   Keep each branch integrated across commands, script providers, saves and UI actions.
2. Continue adding focused executable C++ probes, compatibility matrix entries, and
   controlled benchmarks with every compatibility-sensitive slice.

Branch 4c increment: [research-driven alien-base cleanup](strategic-world-alien-base-cleanup-status.md)
owns daily self-destruction, safe mission unlinking and optional pact cancellation.
Live Earth-operation interruption and remaining 4c capabilities remain guarded.

### Ordinary mission lifecycle and contact checkpoint (2026-10-03)

Eight further 4c implementation cycles cover operation-base waypoint returns,
ordinary airborne arrivals across objectives, ordinary ground arrivals, takeoffs
with objective score filtering, Supply spawn suppression/constructor defaults,
no-object wave classification, flag-aware airborne spawning, and transient ignored
UFO contacts. Every slice has pinned reference evidence, focused fresh/cache tests,
transition reload coverage, and an audit. Coverage corrections are a separate commit:
site guard tests now select actual site areas, and the undefined Rosigma dummy wave
has an explicit 360-tick classification with its resolved no-effect rule asserted.

All 19 staged saves retain explicit stopping horizons: ten executable at 360 ticks,
one blocked at 125 ticks, eight tactical saves preserved at zero ticks. This horizon
does not establish unrestricted continuation. The ignored-contact set clears on
reload because SavedGame does not serialize it in the reference engine.

The headless gate remains open: forced/new base creation, infiltration/pacts and
special objective effects, deployment/site spawning, regional spawn geometry,
immediate hunting waves, detection VM providers, hunter/escort behavior and actual
initial/monthly world hook composition remain outstanding. Combat, deployment,
event effects and integrated monthly progression keep their branch 5/6 boundaries.
Branch 4d is untouched. See [mission status](strategic-mission-script-status.md).

Validation for this checkpoint: root `dotnet test --no-restore` passes all 1,522
tests, with no failures or skips, including checksum, private corpus and populated
world scenarios. Formatting verification, regenerated code-map validation and
`git diff --check` pass. Captured probes remain available under artifacts; obsolete
probe object files were removed. The pinned C++ checkout is clean.

### Four-batch 4c checkpoint (2026-10-03)

Twelve implementation slices, audited in four groups of three, add ordinary ground
spawn paths, ordered origin/destination areas and rectangular Earth-operation
origins; explicit mission-site waypoint selection; live research interruption and
linked base destruction; immediate no-object generation and hunting; and aggressive
retaliation arrival RNG. Each slice is committed with pinned evidence, focused
fresh/cache tests, save/reload coverage and status updates. Audit corrections cover
stale guard tests and a confirmed technical-region work-limit bypass.

The branch remains incomplete. Outstanding world effects and final acceptance are
listed in [mission status](strategic-mission-script-status.md). Interception,
deployment, event payload execution and integrated month rollover retain branch
5/6 guards. Branch 4d has not started.

Validation: root `dotnet test --no-restore` passes all 1,554 tests with no failures
or skips. Formatting verification, checksum tests, regenerated code-map validation
and `git diff --check` pass. All 19 staged-save horizons remain explicit and
unchanged (ten at 360 executable ticks, one blocked at 125 ticks, eight tactical
saves preserved at zero ticks). The pinned C++ checkout is unchanged. Useful
captures are retained; the four obsolete probe object files were removed.

### Additional three-slice 4c batch (2026-10-03)

Three committed slices extend origin spawning to separated Space rectangles,
land-tested rectangular areas and selected objective rectangles. Pinned captures,
fresh/cache tests, RNG accounting, transition reloads and atomic guards cover the
new paths. The combined audit confirmed a point-origin pole-normalization
regression; its correction and regression fixtures are implemented. Root tests
passed 1,568 cases before the correction; the user reports all post-fix tests
passing. Fixture input checksums, code-map validation and `git diff --check` pass.
The changed pinned text inputs use LF bytes to match clean Git checkouts.
Formatting verification remains blocked by sandbox named-pipe access and must
be completed before a major push. See
[mission status](strategic-mission-script-status.md) for evidence and remaining
4c scope. Branch 4d remains untouched.

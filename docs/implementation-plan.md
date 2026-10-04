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
The user also verified formatting from normal PowerShell with
`dotnet format Oxce.slnx --verify-no-changes --no-restore` (exit code 0).
The audit fix is committed as `1d532c1`; this batch's validation is complete. See
[mission status](strategic-mission-script-status.md) for evidence and remaining
4c scope. Branch 4d remains untouched.

### Direct mission-site generation batch (2026-10-03)

Three slices implement explicit deployment waves, mission-rule/terrain deployment
selection and player-base targeting. The pinned `strategic-site-spawn` capture
includes actual AlienMission creation/base selection and Texture selection blocks.
Fresh/cache tests cover RNG order, immediate chains, marker IDs, activity, suppression
and transition reloads. The batch audit fixes idle dictionary allocations, missing
hourly discovery and an atomic activity forecast for weighted null outcomes, and
bounds scans of unreachable deployment entries. Existing objective decision tests
now verify direct-site execution and precise remaining guards.
See [site generation status](strategic-world-site-spawning-status.md).

The headless gate remains open. UFO-to-site conversion, zero-hour immediate despawn,
empty marker namespaces, site scheduling scripts, new-base objectives,
infiltration/pacts, detection VM providers, UFO hunting/escorts and composed world
hooks remain outstanding. Branch 5/6 boundaries remain; branch 4d is untouched.

Validation: root `dotnet test --no-restore --no-build` passes all 1,605 tests
without failures or skips after the final regressions were compiled. Earlier
root runs and focused tests built the changes. Checksums, private corpus,
allocation budgets, formatting verification, code-map validation and `git diff --check` pass. The reference
checkout remains clean; obsolete probe objects were removed.

### Mission-site expiry batch (2026-10-03)

Three slices implement missing/interrupted instant-event suppression, live UFO
follower restoration and safe zero-hour creation/expiry. Instant event payloads
remain branch 6 work and are never converted to scheduled events. Known mission
unlock research still guards before the tick. Pinned event and timer captures
cover eligibility, weighted choice and target/UFO destination reset; creation
captures include zero-hour durations. Fresh/cache transition reloads and atomic
limits cover each slice. The audit corrects a reproduced same-day research/marker
collision through the shared counter range forecast, and bounds aggregate despawn
scans. See [expiry status](strategic-world-site-timer-status.md) and
[creation status](strategic-world-site-spawning-status.md).

Branch 4c remains incomplete: UFO-to-site conversion, empty marker namespaces,
mission-unlock research, site script scheduling, new-base objectives,
infiltration/pacts, real detection VM providers, UFO hunting/escorts and composed
world hooks remain open. Branch 5/6 guards remain; branch 4d is untouched.

Final batch validation: root `dotnet test --no-restore --verbosity quiet` passes
all 1,630 tests with zero failures or skips, including fixture checksums, the
private corpus and allocation budgets. Formatting verification, code-map and
diff checks pass. The pinned reference checkout remains clean.

### Selected objective UFO site conversion (2026-10-03)

The first slice implements selected objective UFO conversion, immediate site
revelation and delayed UFO cleanup on five-second ticks below the half-hour
trigger. Canonical pinned captures, fresh/cache reloads and atomic ID/score tests
cover it. Custom deployment, respawn linkage, compound half-hour counter forecasts
and craft follower transfer retain specific guards. See
[conversion status](strategic-world-ufo-site-conversion-status.md).

The second conversion slice adds optional custom deployment metadata from
effective UFO race stats. Captured custom rows, missing references, race overrides
and reloads pass. It retains the primary strategic deployment and its duration.

The third slice adds dormant respawn linkage for converted UFOs and transition
reloads through site expiry/reactivation. Craft follower transfer and combined
half-hour conversion remain specific guards; the headless gate remains open.

The conversion batch audit confirmed a false landing guard when terrain always
suppressed a respawn-enabled site. The forecast now treats that UFO as terminal,
matching the reference's return before later UFO/craft movement. Pinned suppression
rows and regressions cover no-site/no-link outcomes, skipped craft state, shield
recharge and current-tick fallthrough. Branch 4c remains incomplete; branch 4d is
untouched.

Final batch validation: root `dotnet test --no-restore --verbosity quiet` passes
all 1,644 tests with zero failures or skips, including checksums, all 19 staged
saves and allocation checks. Formatting verification, code-map and diff checks
pass. An unchanged patrol allocation assertion failed once before the updated
conversion expectations were compiled; it passed in the focused rerun and both
subsequent root runs without weakening its assertion. The pinned C++ checkout
remains clean; the regenerated probe object was removed.

### Combined-tick UFO site conversion (2026-10-03)

Conversion now runs safely on half-hour/day ticks after ordered research, direct
birth and expiry counter changes. Captured ID/counter methods and 84 focused
conversion/spawn/timer tests verify fresh/cache ordering, reload and atomic
collisions. Same-tick direct births followed by a shared marker decrease retain
a specific guard. Craft follower transfer remains the next slice. Branch 4c is
incomplete; branch 4d is untouched.

Craft follower transfer now covers passengers, vehicle instances and empty-craft
return for dormant conversions. Fuel-before-transfer, landing/equipment options,
terminal current-tick fallthrough and transition reloads have focused coverage.
Actual landing remains guarded for branch 5.

Damaged selected-site arrivals now proceed because AlienMission::ufoReachedWaypoint
replaces Ufo::setAltitude's transient crash/landed result before any later
Geoscape callback. The expanded probe inserts setAltitude, isCrashed and
isDestroyed verbatim. Forty captured combinations cover the half-damage boundary,
destruction threshold, unmanned rules, kamikaze behavior and dormancy. Fresh/cache
tests preserve damage and stable respawn links across reload. Ordinary damaged
ground arrivals retain their atomic guard; this does not enable crash creation,
interception or tactical deployment.

The batch audit reproduced premature landing guards both before and after a
terminal UFO arrival. Conversion validation now stops at the actual UFO-pass
boundary, while follower transfer validates the reachable conversions separately
from craft arrival. A later terminal arrival still permits earlier transfers but
skips all craft landings. Activity forecasts honor ordinary terminal arrivals too.
Regressions verify unchanged skipped craft positions, reload, and no reservation
of skipped conversion scores or markers. The previous crash-guard fixture now
selects an ordinary arrival, retaining its strict atomic assertion.

Root validation passes all 1,662 tests with no failures or skips, including
fixture checksums, all private-corpus classifications and allocation budgets.
The headless 4c gate remains open; branch 4d is untouched.

Hunter target selection now has pinned executable evidence and a gameplay-owned
operation for attraction, saved-order selection and original-target retention.
See [hunting status](strategic-world-hunting-status.md). Live retargeting and
movement are the next two slices; the 4c gate remains open.

The retarget slice adds reference destination backup, hunt speed, escort speed and
waypoint restoration operations. Captured transitions retain the original waypoint
across hunt-to-escort changes and leave speed unchanged on restoration. Explicit
selected speed controls the five-second/one-minute fast path; ten-minute retargeting
is always due. Mod::hunterKillerFastRetarget defaults true and survives fresh/cache
projection. The redundant-HK-alert option defaults true, honors fixed user options
and persists through the save adapter. Compiler revision 33 invalidates stale caches.
Eleven focused tests pass. The next slice connects live retargeting and movement.

Live imported hunters and escorts now use registered ten-minute retargeting after
base hunting, plus the explicit slow-speed fast path before five-second movement.
Retargeting honors projected fuel returns, preserves original waypoints, allocates
contact markers and publishes gameplay-owned hunt notifications. Alert suppression
does not prevent revelation or targeting. Movement follows craft/UFO positions in
reference list order; snapshot capture resolves final target coordinates without
changing movement. Deleted escort targets restore their original waypoint.

Combat arrivals stop atomically before mutation. Retargeting involving same-tick
grounded auto-patrol relaunches or airborne craft transfers retains specific
forecast guards. Escort chains/cycles and malformed originals fail intentionally.
Deletion scans are bounded before nested work. Future hunter wave creation remains
guarded until spawn/retarget forecasts compose; this slice enables loaded actors.
The headless gate remains open and branch 4d is untouched.

The three-slice batch audit reproduced a same-tick boundary bypass: a hunter's
new revelation was absent from an imported pursuer's craft-arrival forecast.
Craft interception checks now use the retarget projection before any mutation.
Regression tests also reject signed 32-bit intermediate attraction overflow even
when later terms would produce an in-range result; each C++ operation is bounded.
The root allocation gate caught eager closure allocation in the escort forecast
on ordinary flight. An explicit identity scan removes that allocation, preserving
the existing 8/32/128-UFO budgets. Handler-order assertions include both registered
retarget callers without weakening their exact ordering.

Final cycle validation passes all 1,692 root tests with no failures or skips,
including fixture checksums, all 19 staged-save classifications, 72-hour composed
scenarios and populated allocation budgets. Formatting verification, code-map
validation and diff checks pass. The pinned C++ checkout remains clean; obsolete
probe object output was removed. Branch 4c remains incomplete and 4d is untouched.

Hunter constructor initialization is now an executable gameplay operation.
The pinned Ufo constructor capture has 192 rows covering nonpositive percentages,
failed/successful rolls, percentages above 100, literal modes/behaviors and the
ordered optional random selections. Failed rolls consume no mode/behavior draws;
nonpositive percentages consume no RNG. Supply objective callers retain the
reference constructor defaults. Positive hunter spawning remains guarded until
the following slices connect safe wave contexts.

Grounded non-escort hunter waves now use the captured constructor operation
before geometry selection. Wave values inherit rule defaults only at -1.
Initial ground timers above one keep retargeting outside the creation tick;
fresh/cache tests cover constructor metadata, ground countdown, lift and reload.
Airborne hunter creation and spawn escorts remain precisely guarded.

Airborne non-escort hunter waves now execute when existing craft and undelivered
craft transfers cannot supply a current or auto-patrol target. That structural
proof keeps the creation-tick path ordinary; later dispatch uses the loaded
hunter retarget capability. Rule-derived hunter percentage/mode/behavior and
fresh/cache creation/targeting reloads are covered. Candidate-bearing airborne
spawns and creation-time escorts retain the existing atomic wave boundary.

The constructor/spawn batch audit confirmed an empty hunter trajectory could
index its first waypoint before the existing minimum-length guard. Length and
altitude validation now precede hunter context checks. Empty/one-point regressions
assert an unchanged campaign and RNG, and a separate auto-patrol regression
checks that a possible same-tick relaunch cannot enter the no-candidate path.

The previous supply constructor-choice fixture now executes its formerly guarded
no-candidate hunter cases and asserts captured hunter flags, counts, live links
and reload. Objective supply ships still assert the default non-hunter
constructor. Mode/behavior capture draws now alternate so swapped draw order
cannot pass the shared canonical oracle comparison.

Final constructor/spawn cycle validation passes all 1,701 root tests with no
failures or skips, including checksums, private classifications, endurance and
populated allocation checks. Formatting verification, code-map validation and
diff checks pass. The reference checkout is clean and obsolete probe object
output was removed. The remaining 4c headless gate is open; 4d is untouched.

Airborne hunter waves now accept existing airborne craft ignored by the pinned Craft::isIgnoredByHK rule (low fuel or mission complete). Four captured truth-table rows and fresh/cache spawn reloads cover this extension; active targets remain guarded.

New airborne hunters also accept undetectable existing craft, which the pinned saved-order selection trace excludes before attraction evaluation. Fresh/cache tests assert the effective rule, non-hunting spawn and reload. Original targets retain their separate reference semantics; transferred craft remain conservative.

New flying hunter waves reserve their aggregate craft-scan work together with loaded retarget actors before the creation tick. A reproduced 1001-by-1001 workload previously threw after creation; it now blocks without state or RNG changes. The no-target structural scan is cached only for the current preflight/tick, keeping immediate chains bounded.

The fourth batch audit checks returning/undetectable exclusions and per-tick cache lifetime. An hourly-production reservation test covers the finite queue upper bound. Suspected multi-craft output undercount was rejected: runtime linking already enforces exactly one craft per production unit. No additional gameplay defect was confirmed in these three slices.

Final fourth-cycle validation passes all 1,710 root tests with zero failures or
skips. Fixture checksums, private-corpus classifications, 72-hour scenarios,
allocation budgets, formatting verification, code-map and diff checks pass.
The pinned C++ checkout remains clean, and obsolete capture object output was
removed. Branch 4c remains incomplete; branch 4d has not started.

Creation-time escort initialization now has pinned executable evidence from
AlienMission::spawnUfo and Ufo::setEscortedUfo/backupOriginalDestination.
Eighty captured rows cover successful/failed hunter status, the escort flag,
missing leaders, mission filtering, hunter exclusions, first saved-order leader,
all leader statuses, origin relocation, original destination and full speed.
The owning gameplay operation reproduces those rows; live wave callers are the
next two slices. Combat and deployment remain branch 5.

- Branch 4c escort creation slice: enabled single airborne escort births, including
  no-leader requests and same-tick expired-leader recovery. Focused hunting/escort
  verification passed (59 cases); immediate-wave composition follows separately.

- Branch 4c immediate escort slice: finite wave chains preserve earlier/later
  leader order and origin semantics, including unmarked leader reloads and bounded
  population scans. Focused verification passed (66 hunting/escort cases).

- Branch 4c escort batch audit: reproduced and fixed relocated-escort activity
  overflow after mutation. Preflight now includes saved and possible earlier-wave
  leader positions; boundary and terminal-arrival persistence regressions pass.

- Final escort-cycle root validation: 1,745 passed, zero failures/skips, including
  private classifications, endurance, checksum and allocation coverage. Code-map
  and diff checks pass, as does formatting verification; 4c remains open and 4d is untouched.

- 4c grounded escort slice: extended pinned status traces and enabled long-timer
  grounded births with active craft. Focused hunting/escort tests pass (87 cases).

- 4c creation-tick grounded takeoff slice: timer-one hunter/escort births with
  active craft are enabled. Scoring follows site/earlier-UFO order, terminal
  arrivals skip newborn lifts, and 138 focused cases pass with reload coverage.

- 4c saved idle escort slice: replaced the blanket newborn-leader boundary with
  structural checks for craft pursuit, waypoint arrival and UFO followers.
  Ordered attachment and reload/resource-limit tests pass (146 focused cases).

- Grounded/saved-escort batch audit: reviewed creation, landed/takeoff scoring,
  retarget ordering, terminal fallthrough, follower repair and aggregate work
  limits against the pinned C++ methods. No confirmed defect was found. All
  1,761 root tests pass with zero failures/skips, including private corpus,
  endurance, checksums and allocation checks. Formatting verification, code-map
  and diff checks pass. The reference checkout is clean and obsolete probe
  object output was removed. Branch 4c remains open; 4d has not started.

- 4c site-script candidate slice: pinned the exact processCommand pruning loop
  in strategic-mission-site-candidates. The gameplay projection matches 256
  combinations of occupied regions, recent point locations, rectangular
  texture/direct/site-type eligibility, missing zones and invalid negative
  zones. Live site command scheduling remains the next slice.

- 4c live site-script slice: explicit single-region site commands now choose the
  weighted initial mission, draw target-base odds, retry mission types in
  circular order, select a valid area and store repeat/run history before
  starting the owned mission. The full pinned command block captures seven
  selection/order traces. Fresh/cache scheduling, direct site birth after
  reload and tagged adhoc acceptance replace obsolete blanket site guards.
  All 55 focused scheduling cases pass. Multi-region rejection sampling remains
  a precise boundary until a bounded selection proof is available.

- 4c bounded site-region/history slice: isolated commands now select weighted
  or uniform regions when every possible selected region has candidates.
  Unsafe retry paths and broader batches with changing multi-region candidates
  stop before RNG. The pinned command capture includes uniform selection and
  global candidate-index offsets. Point history persists across direct site
  births and suppresses exhausted location sets; live batch occupancy produces
  false labels for later conditionals. History capacity and all scan/repeat
  checks are reserved before mutation. All 66 focused scheduling cases pass
  fresh/cache and transition reloads. Due event effects remain branch 6.

- Site-script batch audit confirmed that empty location-history keys bypassed
  the new capacity check and were omitted from work accounting. The regression
  fails before the fix. Preflight now reserves both location entries and new
  variable keys across the whole batch, and charges enumeration of empty keys.
  No RNG or campaign mutation occurs when either history limit is exceeded.

- Site-script batch final validation: all 1,775 root tests pass with zero
  failures/skips, including private corpus classifications, endurance, checksums
  and allocation coverage. Formatting verification, regenerated code-map and
  diff checks pass. The pinned reference checkout remains clean; the obsolete
  site-candidate probe object was removed. Weighted/uniform safe site scheduling
  and history are accepted for these slices; arbitrary region rejection
  sampling, composed multi-region forecasts and the broader 4c gate remain
  open. Event payload execution and branch 4d remain deferred.

- 4c bounded region-selector slice: the pinned site-command probe now captures
  rejected weighted and uniform regions. WorldMissionSiteSelection conditions
  the distribution on eligible regions, preserving relative weights, uniform
  region order and per-region area choice. Fully eligible draw ranges remain
  unchanged; rejected draws are omitted under the contract's RNG-stream
  non-goal. ADR 0030 records the decision and safety requirements. Seventeen
  focused site-script cases pass; live adoption follows next.

- 4c live conditioned-region slice: weighted and uniform site scripts now
  filter regions removed by occupancy, base targeting and repeat history
  without an unbounded retry loop. Composed commands observe live candidate
  changes; earlier multi-region guards are removed. Positive selected/listed
  timeline region sets must agree before RNG, and work reservations include
  earlier possible mission/history growth. Fresh/cache composed batches,
  default countdown draws and reloads pass in 72 focused scheduling cases.

- 4c empty site-command slice: the pinned reference confirms an empty mission
  listing returns false after target-base odds, without touching race, delay,
  history or run counters. Shared monthly/adhoc execution now follows that
  path, including negative conditional fallthrough and unchanged exhausted
  counters. Fresh/cache reload coverage confirms future scheduling remains
  available while due tagged event payloads still stop before mutation.
  All 76 focused scheduling cases pass.

- Conditioned site-scheduling batch audit found no confirmed implementation
  defect. Added fresh/cache regression coverage for divergent signed listing
  and unsigned region-selection timelines: the whole batch blocks before any
  command mutates state or draws RNG. Corrected the current site status summary.
  Final validation passes all 1,786 root tests with zero failures/skips,
  including private classifications, endurance, fixture checksums and populated
  allocation gates. Formatting verification, code-map and diff checks pass.
  The reference checkout is clean; the obsolete probe object was removed.
  Broader 4c acceptance remains open and branch 4d is untouched.

- 4c operation-base evidence/option slice: a new pinned full AlienMission::start
  capture records 72 controlled creation searches, including technical regions,
  fake-water preferences, 99th-attempt acceptance and 100th fallback/suppression.
  Mod.cpp defaults allowAlienBasesOnWrongTextures to true. Its explicit false
  override now projects into immutable runtime settings; compiler revision 34
  rebuilds older caches. The existing initialization-prefix oracle is retained.
  Actual base creation remains guarded until the following gameplay slice.

- 4c bounded operation-base selector: gameplay now owns the reference's
  fake-water odds, ordered area and coordinate draws, technical-region bypass
  and finite 100-attempt search. The final attempt either returns the forced
  point or suppresses creation according to the projected option. All 72
  pinned traces match outcomes, land checks and draw counts/state. The selector
  has no state effects; live initialization follows in the next slice.

- 4c live operation-base creation: scheduled mission/adhoc commands now create
  and link operation bases for regional-new and missing fallback modes; Earth
  new-base retains reference existing-base reuse. Base age, immediate hunting
  timer, race, marker identity and mission links persist through creation and
  no-object mission cleanup reloads. Mission points are awarded at creation,
  except for infiltration. Composed batches reserve marker IDs, population,
  geographic work and worst-case activity before RNG or mutation. Empty/missing
  zones fail intentionally, and unused creation fields are skipped on reuse.
  All 94 focused operation/scheduling tests pass. Shared mission/UFO/player marker
  namespaces and broader unavailable transitions retain precise boundaries.

- Operation-base batch audit reproduced deleted sidecar resurrection when a
  replacement reused deployment and marker ID before rewrite. The regression
  fails fresh/cache before the fix. A separate world-owned 64-bit birth sequence
  now supplies persistent creation keys, survives deletion/reload and cannot be
  changed through gameplay counters. Exhaustion blocks before RNG; inconsistent
  saved birth identities fail intentionally. ADR 0031 records the save extension.

- The same audit also confirmed missing craft-marker collision and legacy terror
  namespace checks in creation. Both reproductions scheduled ambiguous targets
  before the fix. Creation now reserves existing and incoming craft identities with bounded
  scans and rejects the legacy namespace before RNG, matching the established
  alien-base upgrade/save-resolution boundary.

- The final resource audit also reproduced omitted regional containment costs.
  Candidate preflight now reserves region-area scans, fallback checks, live
  selection and earlier possible births before enumerating bases. Forced-new
  operations skip irrelevant existing-base identity scans. The regression
  exceeds the old budget and is now refused with unchanged campaign/RNG.

- Operation-base cycle final validation: all 1,813 root tests pass with zero
  failures/skips, including fixture checksums, staged-save classifications,
  composed endurance scenarios and populated allocation checks. All 109 focused
  scheduling tests pass. Formatting verification, regenerated code-map and diff
  checks pass. Confirmed sidecar, marker-identity and geometry-work findings are
  fixed with regressions; the reference checkout remains clean and the obsolete
  probe object was removed. Broader 4c acceptance remains open; 4d is untouched.


### Branch 4c: composed scheduling evidence and eligibility forecast

Pinned GeoscapeState::determineAlienMissions arc/mission/event prefix and the initial GeoscapeState::init block in strategic-world-composition. The fixture isolates caller order and dependent eligibility; research, mission creation and event storage are controlled collaborators, not new payload/objective oracles. Pure preflight eligibility now represents research grants/disables and increasing variable/marker intervals without changing state or consuming RNG. Month rollover, event effects and branch 4d remain guarded. The composed caller and initial entry are the next slices.


Branch 4c scheduling/entry cycle closes with registered monthly world composition and once-only public EnterCampaignWorld after first-base placement. Caller eligibility is preflighted across arc research, mission counters and event history before any RNG/mutation; score/funds remain fixed. Initial accounting and legacy world links are guarded, and entry expires stale logistics quotes. Batch audit fixed prospective operation-base identity collisions and removed a nonreference nonmonthly mode. Root verification passes 2,123 tests. Newly created bases with monthly upgrades/evolution, remaining detection providers/forecasts and final composed acceptance keep 4c open; branch 4d remains untouched.


Newborn/monthly/startup cycle closes: root dotnet test passes 2,130 tests without failures or skips, including the unchanged legacy-save month-boundary regression, new explicit-entry armor guard, checksums/private corpus and allocation cases. Formatting, code-map and diff checks pass. The newborn monthly composition guard is removed; future event payloads, combat/deployment and integrated monthly campaign progression remain guarded. Remaining detection providers/forecasts and final full closure audit keep branch 4c open; branch 4d is untouched.

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

Branch 4c detection damage profiles: pinned RuleDamageType defaults and Mod built-in table, RuleItem damage/melee pointer identity and script classification wrappers in strategic-detection-damage. Immutable per-item runtime projections retain float storage, predefined resets and signed overrides; cache revision 47 invalidates older projections. Base/craft VM tests cover all 20 built-ins plus raw defaults, nulls and pointer identity through fresh/cache and transition reload. RuleInventory.getMoveToCost remains explicitly guarded; raw damage conversions and arithmetic forecasts follow in the next slice. No tactical damage or event payload is enabled.

Branch 4c detection damage conversions: captured and enabled all eleven RuleItem.cpp getDamageToScript float multiplications plus armor-percent round-away behavior. Tests compare raw/default/built-in/custom positive and negative values, float precision loss, defined signed boundaries and null receivers through actual base/craft VM calls, cache and reload. Whole-program reference unions follow literal item lookup, primary/melee retrieval and aliases; consumed profile arithmetic is validated before ticks with bounded forecast work. Nonfinite/out-of-range results and unproven variable powers remain precise restrictions. Tactical damage application and random damage with a state are not enabled.

Branch 4c damage-query forecasts: enabled native BindSet pointer-copy operations and captured their identity/null behavior. Conservative fixed-point unions include alias and profile reassignments; scalar intervals include all literal/copy/swap writes, with unknown arithmetic widened to the full signed range. Full-range inputs execute when the consumed multiplier is safe; null profiles bypass unused power and rule data. Candidate-union and instruction work are bounded before expansion. Fresh/cache base/craft tests cover aliases, unrelated unsafe items, null short-circuiting, scalar reassignment, profile reassignment and an intentionally exhausted forecast budget. Arbitrary unsafe power calculations remain precisely guarded; no tactical or campaign-event effect runs.

Branch 4c damage-query batch audit: reproduced and fixed a false preflight rejection when a null Mod receiver looked up an item with unsafe damage fields. Receiver and STR_NONE sentinel short-circuits now retain null profile candidates without reading unconsumed arithmetic. Fresh/cache null-Mod and sentinel regressions pass; alias/profile/scalar bounds remain conservative. The committed batch passed 2,167 root tests before the new audit regressions. Formatting also identified a CRLF rewrite in the existing binding tests; restored the repository LF convention.

Damage-profile/query cycle closes: root dotnet test passes 2,171 tests with zero failures/skips, including the confirmed audit regressions, fixture checksums, private corpus and allocation gates. Formatting verification, regenerated code-map and diff checks pass. Compiler/cache revision is 47; generated native source, executable and trace remain useful, and the temporary object was removed. The reference checkout remains clean. Detection providers/forecasts and final branch-wide closure still keep the 4c gate open; 4d remains untouched.

Branch 4c inventory movement-cost evidence/projection: captured exact RuleInventory::getCost and ScriptBind const-member behavior in strategic-detection-inventory-cost, including pointer identity, negative/int-boundary costs and null source. Immutable ordinal runtime cost maps preserve typed rule replacement/inheritance; fresh/cache projection tests pass and cache revision advances to 48. Native null destination and missing cost entries are undefined; their VM provider will be enabled only with pre-tick reference forecasts. This slice does not remove its existing guard.

Branch 4c inventory movement VM query: enabled RuleInventory.getMoveToCost with native same-pointer/null-source behavior and signed cost values. A bounded control-flow reference forecast records states before each instruction, follows typed lookups/copies and joins both branch/loop possibilities. Nonnull destination and every possible distinct source/target cost are validated before the tick; same-register queries always return zero. Actual base/craft VM tests compare the native fixture through cache and reload, and null/conditional missing-cost cases stop without mutation. The previous generic binding guard is replaced by precise destination/cost restrictions.

Branch 4c shared reference forecasting: damage queries and missing conventional inventory checks now use the bounded instruction-level forecast, removing the obsolete whole-program reference union. Single eq/neq null branches refine possible pointers on both successors; aliases propagate the refined values. Tests cover profile replacement before a query, null-checked inventory aliases, null Mod branches with missing roles, loop-carried invalid targets and intentionally exhausted state-expansion budgets through fresh/cache/base/craft paths. Unproven correlated or arbitrary scalar paths retain precise guards. No branch 5/6 transition is enabled.

Inventory movement/reference-forecast cycle closes: batch audit found no confirmed issue. Root dotnet test passes 2,197 tests without failures/skips, including native fixture checksums, private corpus and allocation gates. Formatting verification, code-map and diff checks pass. Cache revision is 48. The native source/executable/trace are retained and the obsolete object removed; the pinned reference remains clean. Movement queries now execute with precise destination/cost guards and shared pointer forecasts. Branch 4c remains open for remaining detection providers/forecasts and final closure audit; 4d is untouched.

Branch 4c detection value copies/metadata alias: enabled native BindSet value copies for text and typed tag IDs as well as existing references. The pinned RuleSoldier.getType binding deliberately receives RuleSkill, matching its native wrapper/catalog; it now executes with native text/null defaults. strategic-detection-value-copy captures the exact template/wrapper. Fresh/cache base/craft VM tests cover text copies, tag copies, skill identity, nulls and reload. This adds no mutable world operation or tactical behavior.

Branch 4c random-damage short-circuits: captured the exact RuleItem.cpp getRandomDamageScript wrapper with a throwing unconsumed damage stub and recording RNG. Null profile or null RandomState returns zero without reading power/modifiers or drawing RNG. The VM provider now executes those provable paths, including editable state aliases, while nonnull profile/state computation retains a precise forecast restriction and runtime backstop. Fresh/cache base/craft/reload tests preserve native zero results even with unconsumed unsafe damage data. Actual random/tactical damage remains deferred.

Branch 4c scalar query forecasts: replaced the whole-program scalar union with bounded intervals at each instruction. Literal/copy/swap writes and item getPower/getMeleePower queries overwrite the interval; branch/loop joins retain possible values, and arbitrary arithmetic widens conservatively. Null random-damage results supply a zero interval to later conversions. Actual VM tests compose native power readers and all eleven float conversions, overwrite caller outputs, ignore later unused assignments and guard loop-carried overflow; explicit state-expansion budget tests pass fresh/cache. This is read-only script query support, with nonnull random damage and unproven arithmetic still guarded.

Branch 4c value/scalar batch audit: reproduced two confirmed regressions. Text declarations incorrectly stored scalar zero, and explicit text clear was unavailable; compiler initialization/clear now emits native empty text, verified against captured Script.cpp Func_clear_text and standalone VM plus base/craft/reload cases. Compiler/cache revision advances to 49. Forecast initialization also threw for legitimate reused block register offsets; reference/scalar state maps now initialize each offset once while declaration instructions reset its value at scope entry. The new fresh/cache scoped-register regression failed before the fix. Native Script.cpp variable/clear and scope handling plus the existing compiler scope-reuse tests establish the behavior.

Value-copy/null-random/scalar cycle closes: both confirmed audit regressions are fixed. Root dotnet test passes 2,239 tests with no failures/skips, including text defaults/clear, reused scalar/reference scopes, native fixture checksums, private corpus and allocation gates. Formatting verification, code-map and diff checks pass. Compiler/cache revision is 49. Generated native source/executables/traces remain useful; their obsolete objects were removed, and the pinned reference remains clean. Remaining detection providers/forecasts and final full closure audit keep branch 4c open. Branch 4d has not started.

Branch 4c typed detection declarations: the pinned full native parser capture in script-declaration establishes 23 initializer acceptance/rejection cases via Script.cpp parseVariableImpl and ScriptBind.h BindSet/ArgSelector. Variable initialization now validates exact type/reference compatibility and editable-pointer direction, preserving native null pointer/tag alternatives. Invalid initializers fail during content compilation instead of corrupting typed VM/forecast state. Unit regressions reproduced all nine tested invalid declarations before the fix; native fixture comparison and fresh/cache base/craft reload scenarios cover valid copies feeding damage queries. Compiler/cache revision advances to 50. Remaining detection providers/forecasts and final closure audit keep 4c open; 4d is untouched. Validation: root dotnet test passes 2,254 tests with no failures/skips; formatting verification, fixture checksums, code-map and diff checks pass. Native capture was repeated with identical output. Twenty obsolete probe objects were removed; the executable and trace remain useful, and the reference checkout is clean.

Typed declaration slice audit confirmed a remaining native-overload gap: pointers to scalar/text/tag/Position value types were accepted with null initializers, although native BindSet/BindValue exposes only value assignment for them. Declaration type validation now rejects these pointer forms before emitting a program, also covering absent initializers. The native fixture expands to 30 cases and malformed RUL tests prove invalid detection scripts cannot publish ScriptsCompiled or RuntimeLinked content. Compiler/cache revision advances to 51. Legitimate reference/tag/text copies retain the existing fresh/cache/reload acceptance. Audit fix validation: root dotnet test passes 2,262 tests without failures/skips; formatting verification, fixture checksums, code-map and diff checks pass. All 30 native cases match the retained trace, the probe objects are removed, and the reference checkout is clean. No further confirmed issue was found in this slice. Branch 4c remains open and 4d is untouched.

Branch 4c bounded detection arithmetic: captured 24 full native Script.cpp cases in strategic-detection-numeric, including arithmetic, inverted clamp ordering, signed wrap and calculated numeric operands. Instruction-level intervals now retain add/subtract/multiply/aggregate/offset, clamp, absolute, bit-count and square-root results; uncertain wrap widens and growing back edges widen before transfer to ensure bounded fixed points. Actual base/craft queries consume computed powers through fresh/cache/reload paths. Eleven focused numeric tests pass, including sign/wrap envelopes and exhausted forecast work. Reference: Script.cpp scalar operation definitions and Script.h parser/worker. Runtime forecast changes require no cache schema change. Combat/event/month guards and 4d are untouched.

Branch 4c calculated numeric safety: divisor and wave-period checks now consume bounded per-instruction scalar forecasts instead of requiring literals. All four divide/modulo helpers and five wave operations execute with proven nonzero divisors or positive periods; zero-inclusive, nonpositive, loop-carried and exhausted-work cases stop before the tick. Nine native calculated-operand traces run through both detection callers, fresh/cache and transition reload; caller input clamping also proves a safe divisor. Existing restriction messages remain precise and the structural loop/instruction limits remain in their owner. Reference: Script.cpp division/mulDiv/mulAddMod and wave helper bodies captured in strategic-detection-numeric. No combat/event/month or UI boundary changes.

Branch 4c native Mod receiver aliases: removed the obsolete whole-program direct-rules-input restriction for getSmokeReduction. A bounded instruction-level reference proof now requires the actual runtime catalog at each reachable call; typed declaration/set copies and null-tested repair paths execute, while possible-null branches, loop-carried nulls and exhausted forecast work stop before mutation. Existing native signed smoke arithmetic and ScriptBind pointer-copy evidence feed fresh/cache base/craft/reload regressions. Reference: Mod.cpp getSmokeReduction/ScriptRegister and ScriptBind.h BindSet/Script.cpp variable and condition handling. No tactical visibility calculation or later-branch transition is enabled. Native explicit pointer/editable-pointer/tag set-null assignments, needed for alias repair and loss, now compile through the target's typed BindSet provider; 34 full-parser declaration/copy cases and three compiler regressions pin the semantics. Compiler/cache revision advances to 52.

Detection arithmetic/numeric safety/Mod alias cycle audit closes: root dotnet test passes 2,292 tests without failures/skips, including private corpus, native fixture checksums and allocation gates. The batch audit found no additional confirmed runtime defect; it corrected the new numeric oracle comparison to canonical JSON while retaining all case/status checks, and corrected switch-expression indentation flagged by formatting verification. The focused 20-test numeric suite and final formatting verification pass, as do code-map and diff checks. Native captures/executables remain useful; forty obsolete objects were removed and the pinned reference remains clean. Compiler/cache revision is 52. Remaining forecast/provider and final closure work keep 4c open; 4d remains untouched.

Branch 4c scalar condition forecasts: per-instruction scalar states now refine comparison successors for six signed comparisons, true conjunctions and false disjunctions. Impossible intervals remove that numeric successor; divisor/damage preflight skips scalar-unreachable calls. Endpoint exclusions remain conservative when an interior not-equal hole cannot be represented. The native numeric trace expands to 29 cases with sign, zero, conjunction and extreme paths. Fresh/cache base/craft/reload tests prove safe branch divisors, bounded conditional powers and impossible calls, and boundary/empty-domain tests pass. Reference: Script.cpp parseCondition and test_le/test_eq operations plus Script.h signed scalar ABI. No binding capability, combat, event or month boundary is relaxed.

Branch 4c damage output forecasts: immutable RuleDamageType classification, armor-percent and all eleven float conversion outputs now propagate into later arithmetic/query inputs. Endpoint conversion checks preserve float rounding/truncation and widen invalid native float-to-int results; null candidates contribute zero and unknown profiles retain all primary/melee possibilities within the work budget. Classification execution and forecast share the same reader. Native strategic-detection-damage rows feed composed divisors and subsequent conversions through fresh/cache base/craft reload tests. Reference: RuleItem.cpp getResistTypeScript/getRandomTypeScript/getArmorIgnoreScript/getAoeScript/getArmorEffectivenessScript/getDamageToScript and RuleDamageType::isDirect. No random damage, tactical effect, event payload or month boundary is enabled; cache revision remains 52.

Branch 4c text lookup forecasts: bounded literal/copy/default/clear text states now resolve dynamic RuleMod.getRuleItem/getRuleInventory names at each call. Branch and loop joins retain every possible name, native STR_NONE item and null Mod lookups yield null, and unknown host text outputs remain conservative. Text-state work including string lengths is charged before reference expansion. Native damage/cost and text-copy evidence feeds fresh/cache base/craft/reload composition tests, unrelated-rule isolation, unsafe joins and work exhaustion. Reference: Mod.cpp getItemScript/getInventoryScript/getItem/getInventory, ScriptBind.h BindSet and Script.cpp text initialization/clear. Cache revision remains 52; no later-branch boundary changes.

Condition/damage-output/text-name cycle audit closes: no additional confirmed runtime defect was found in the batch. Root dotnet test passes 2,324 tests with zero failures/skips, including native checksums, private corpus and populated allocation gates. Formatting verification, code-map and diff checks pass. Forecasts remain conservative for interior not-equal holes, unknown host text and unsupported bindings, and invalid arithmetic/query inputs stop before the tick. Twenty obsolete native numeric objects were removed; captures/executables remain useful and the pinned reference is clean. Compiler/cache revision remains 52. Remaining read-query forecasts/providers and the final full closure audit keep 4c open; branch 4d is untouched.

Branch 4c division/modulo output forecasts: div/mod/muldiv/offsetmod now retain conservative signed result intervals, enabling subsequent damage reads and computed divisors. muldiv and offsetmod preserve native 64-bit intermediates rather than wrapping early; ordinary remainder follows the dividend sign and normalized remainder follows the divisor sign. Zero-inclusive divisors and uncertain narrowing widen, with the existing pre-tick guard unchanged. The native arithmetic capture expands to 34 cases with composed helpers and negative normalized modulo; 30 focused numeric tests pass, including exhaustive small signed-domain containment. Reference: Script.cpp mulDiv_h/mulAddMod_h/div/mod and ScriptVmCore signed ABI. Cache revision remains 52; later-branch boundaries and 4d are unchanged.

Branch 4c immutable item scalar forecasts: the VM and forecast now share all eighteen enabled RuleItem scalar readers, including accuracy, power, armor, waypoints, weight, battle type and handling/single-target flags. Per-call reference candidates retain exact signed values, null zero and conservative unknown-rule unions before subsequent arithmetic or damage queries. Native strategic-detection-scripts default/custom reader rows feed calculated divisors and half-health conversion through fresh/cache base/craft/reload. All 113 focused item-rule tests pass. Reference: RuleItem.cpp ScriptRegister and isSingleTargetScript/getBattleTypeScript with RuleItem.h getters. No new binding surface or tactical effect is enabled; cache revision stays 52.

Branch 4c inventory cost output forecasts: validated RuleInventory.getMoveToCost results now retain signed intervals for later divisors and damage queries. Same-register identity and null-source calls produce zero without consuming destinations; candidate pairs preserve native same-slot identity, exact costs and null zero, while missing/unproven destinations or costs widen and retain existing precise guards. Candidate work is reserved before pair enumeration. Native strategic-detection-inventory-cost rows cover all seven identity/null/signed cases through composed fresh/cache base/craft/reload VM tests. Reference: RuleInventory.cpp getCost/ScriptRegister and ScriptBind.h null-receiver wrapper. No inventory movement or tactical effect is executed; cache revision remains 52.

Division/item-reader/inventory-cost cycle audit closes: no additional confirmed defect was found. Root dotnet test passes 2,333 tests with zero failures/skips, including native checksums, private corpus and populated allocation checks. Formatting, regenerated code-map and diff checks pass. The 34-case native numeric capture and exhaustive signed-domain containment verify the new arithmetic envelopes; item/cost composition retains precise unavailable-input guards. Twenty obsolete numeric probe objects were removed, while useful captures/executables remain. Reference checkout is clean and cache revision remains 52. Final query/provider and full acceptance reconciliation keep 4c open; 4d is untouched.

Branch 4c linked rule reference forecasts: all seven enabled named RuleMod lookup families now preserve literal/copied text candidates, null receivers, missing names and item-only STR_NONE behavior. Unit spawn/live/recovery and Research lookup/needed-item getters follow immutable linked projections, using canonical research identity strings. Reference work is charged before candidate/link expansion. Eight focused fresh/cache base/craft/reload tests use native ruleLookup and Unit-link evidence to prove null branches and compose linked-item damage reads without unrelated unsafe rules. Reference: Mod.cpp getItemScript/getInventoryScript/getArmorScript/getSoldierScript/getSkillScript/getUnitScript/getRuleResearch; Unit.cpp getters/ScriptRegister; RuleResearch.h getLookup/getNeededItem and RuleResearch.cpp ScriptRegister. No script provider/effect or cache schema changes; 4d remains untouched.

Branch 4c immutable rule scalar forecasts: enabled Armor/Soldier/Craft/UFO/Inventory/Research readers and Mod settings share the same execution/forecast implementation. Known rule references preserve exact values; unknown receivers conservatively include every family rule plus null zero, with candidate/string work charged before reads. Existing native Soldier/Armor stat fixtures now consume each output as a divisor before equality checks. Native armor-vision and Mod-settings rows compose with float conversions through fresh/cache base/craft/reload. Eight new composition and all 48 existing binding tests pass. Reference: Armor.cpp, RuleSoldier.cpp, RuleCraft.cpp, RuleUfo.cpp, RuleInventory.cpp, RuleResearch.cpp and Mod.cpp ScriptRegister/getter bodies, with ScriptBind null ABI captured in strategic-detection-scripts. Dynamic instance/campaign readers retain conservative ranges; no binding/effect, cache schema or later-branch boundary is changed.

Branch 4c bitwise result forecasts: complements, nonnegative masks and bitwise identities preserve conservative signed intervals; known valid shift counts retain bounded arithmetic-right or nonwrapping-left results. Unknown counts/wrap/sign combinations widen. Native strategic-detection-numeric expands to 41 cases with masks, complements and signed shifts, consumed by actual base/craft VM damage reads across fresh/cache/reload. Thirty-five focused tests pass, including exhaustive small signed bitwise domains and masked caller-input powers. Reference: Script.cpp bit_and/bit_or/bit_xor/bit_not/shl/shr and the signed scalar VM ABI. No compiler/cache schema or effect/transition boundary changes.

Linked-reference/immutable-reader/bitwise cycle audit closes: no additional confirmed runtime defect was found. Root dotnet test passes 2,354 tests with zero failures/skips, including native fixture checksums, private corpus and populated allocation gates. Formatting, regenerated code-map and diff checks pass. Immutable reader dispatch retains existing native values/null ABI, reference links use canonical identities, and bitwise envelopes passed exhaustive signed-domain checks. Twenty obsolete numeric probe objects were removed; useful captures/executables remain and the pinned reference is clean. Cache revision stays 52. Remaining query composition/forecasts and final branch-wide acceptance reconciliation keep 4c open; 4d is untouched.

Branch 4c wave output forecasts: native rectangle/saw/triangle and sine/cosine result ranges now compose with subsequent numeric/damage operations. Exact argument points follow the pure native helper bodies; interval results retain normalized phases, signed caps, negative-size zero and finite amplitude envelopes. Unproven periods, unsafe trigonometric narrowing and extreme unbounded amplitudes widen while existing period/conversion guards remain. Native numeric capture expands to 47 cases with negative caps/sizes/amplitudes. Forty focused tests pass, including native query composition through fresh/cache base/craft/reload and exhaustive small integer wave domains. Reference: Script.cpp wavegen_rect_h/wavegen_saw_h/wavegen_tri_h/wavegen_sin_h/wavegen_cos_h. No speculative script execution, effect or cache-schema changes.

Branch 4c bounded read-query domains: native boolean getters, category/research predicates and validated clock fields now retain result intervals including null zero. Smoke reduction composes its native unchecked signed product with division by sixty, retaining bounded outputs even when the product wraps; receiver proof remains mandatory. Eight focused tests consume boolean/calendar and native signed smoke traces in later damage queries through fresh/cache base/craft/reload. Reference: Ufo/ Country bool getters, RuleItem.cpp hasCategoryScript, SavedGame script research queries, GameTime getters and Mod.cpp getSmokeReduction; CampaignTime validation supplies calendar invariants. Unbounded years/counters/difficulty and unrelated mutable state remain conservative. No new provider/effect, cache revision or transition boundary changes.

Branch 4c query composition acceptance: the 72-hour stepped/batched/reloaded scenario now has fresh/cache and initial-entry variants with copied-name lookups, chained damage/arithmetic/bit/wave/smoke reads, clock fields, research predicates and country-list booleans through both detection callers. Existing 36-contact/landing, servicing, recall/relaunch, finite population and exact snapshot equivalence assertions remain. Populated workloads record 8/32/128 UFOs at one/eight bases, including cold and naturally reached warm boundaries plus persistence costs, without loosening allocation gates. Integration reproduced a signature-separator indexing exception in text/reference/scalar forecasts; all three now skip nonruntime separator parameters like ScriptVmCore.InvokeHostBinding. Two focused ABI regressions and all ten endurance/population cases pass. Reference: ScriptBind/Script.h list ABI and ScriptCompiler list lowering, Geoscape time/detection ordering and the existing native query fixtures. Cache revision stays 52; branch-wide closure remains pending and 4d is untouched.

Wave/bounded-query/endurance cycle audit closes: the confirmed list-signature separator indexing defect was fixed in the composition slice; the subsequent batch audit found no additional defect. Root Release dotnet test passes 2,374 tests with zero failures/skips, including native checksums, private corpus and populated allocation gates. Formatting verification, regenerated code-map and diff checks pass. The 47-case numeric oracle pins signed waves and arithmetic, and 72-hour query scenarios preserve stepped/batched/reloaded equality. Query boundary measurements retain cold setup (~1.3 MB) and naturally reached warm costs (~576-579 bytes per additional UFO); existing cold differential allocation limits are unchanged. Twenty obsolete numeric probe objects were removed; useful captures/executables remain and the pinned reference is clean. Cache revision remains 52. Remaining query/provider reconciliation and final branch-wide acceptance keep 4c open; 4d is untouched.

Branch 4c country-rule query composition: Country.getRuleCountry now forecasts linked immutable country projections, including proven null and conservative unknown-country candidates within the work budget. FundingCap uses the shared execution/forecast reader, preserving native signed units and null zero. Native strategic-detection-scripts countryRules rows feed half-health conversions through fresh/cache base/craft and reload; explicit saved country identity avoids the fixture master overriding a same-name test rule. All twelve focused immutable-rule tests pass. Reference: Country.cpp getRules/ScriptRegister, RuleCountry.cpp getFundingCap/ScriptRegister and ScriptBind const-member null ABI. No mutable funding consequence, new provider, cache schema or later-branch transition is enabled.

Branch 4c null entity links: proven-null Ufo.getRules/getRuleUfo, Craft.getRuleCraft and GeoscapeGame.getTime now retain null output references at each instruction rather than widening to unrelated rules or clocks. Unknown nonnull entities remain conservative. Extended the pinned native detection capture with exact ScriptBind const-member scalar/pointer output-overwrite templates; existing trace fields remain byte-value equivalent. Twelve focused reference-link tests pass, including fresh/cache base/craft, impossible nonnull branches and save reload. Craft rule calls remain confined to the native craft parser surface. Reference: ScriptBind.h BindFuncImpl const specializations, Ufo.cpp/Craft.cpp rule registration and SavedGame.cpp getTimeScript. Cache revision stays 52; no new provider or mutable effect.

Branch 4c null scalar query composition: native zero results from proven-null UFO/craft instance/stat getters, country histories, campaign counters/difficulty and clock year now overwrite unbounded scalar inputs before later conversion/divisor forecasts. Nonnull or nullable mutable receivers remain unknown. Native const-member output-overwrite evidence and existing wrapper/stat fixtures feed fresh/cache base/craft and reload tests for every craft stat and dynamic getter families. Fourteen focused bounded-query tests pass; explicit nullable receiver and exhausted reference/scalar work cases preserve conservative behavior. Reference: ScriptBind.h const member null ABI, Ufo.cpp/Craft.cpp scalar/stat bindings, Country.h history getters, SavedGame.cpp wrappers and GameTime getters. No mutable consequence, new provider, cache revision or branch 5/6 boundary change.

Country/null-link/null-scalar cycle audit closes: no additional confirmed runtime defect was found. Root dotnet test passes 2,388 tests with zero failures/skips, including native fixture checksums, private corpus and populated allocation gates. Formatting verification, regenerated code-map and diff checks pass. The native capture adds scalar/pointer null-output overwrite evidence without changing existing trace values; fresh/cache, both caller APIs and transition reload tests cover country units, null links and scalar conversions. Nullable mutable receivers and exhausted work remain conservative. Minor test spacing and the previously recorded warm allocation lower bound (~576 bytes/UFO) were corrected. One obsolete native detection object was removed; useful captures/executables remain and the pinned reference is clean. Cache revision remains 52. Remaining read-query/provider reconciliation and full branch-wide acceptance keep 4c open; 4d remains untouched.

Branch 4c immutable identity text forecasts: enabled RuleItem/Armor/Craft/Skill/Soldier/Unit type and inventory ID providers now share their execution reader with finite text forecasts. Unknown receivers include every immutable family identity plus native empty text; candidate/string expansion is charged before retaining names. Dynamic lookups exclude unrelated rule families, while possible unsafe returned identities still stop before the tick. Native strategic-detection-scripts metadata/inventory rows feed fresh/cache base/craft composition and transition reload; all 73 focused text/binding tests pass. Reference: RuleItem.cpp/Armor.cpp/RuleCraft.cpp/RuleSkill.cpp/RuleSoldier.cpp/Unit.cpp getTypeScript and RuleInventory.cpp getIdScript. The native RuleSoldier wrapper continues to receive RuleSkill. No provider, effect, compiler/cache revision or later-branch boundary change.

Branch 4c text condition forecasts: finite identity/name sets refine equality and inequality successors, true conjunctions and false disjunctions. Aggregate feasibility also removes proven-impossible true disjunctions and false conjunctions; reference/numeric guards skip those unreachable instructions. Unknown text and NUL-bearing domains remain conservative, with string work reserved before comparison/filtering. Native numeric capture expands from 47 to 50 cases with text copy/equality/combined branches; all old traces remain unchanged. Seventy focused text/numeric tests pass, including fresh/cache base/craft and reload, unsafe alternative retention and direct impossible-instruction checks. The oracle harness now supplies the typed catalogue needed for native text declarations. Reference: Script.cpp Func_test_eq_text/parseConditionImpl and Script.h text ABI. No VM speculation, cache revision, effect or later-branch boundary changes.

Branch 4c exact identity feedback: reference and text forecasts now alternate conservative proofs until metadata-derived names stabilize. Known receivers retain exact type/ID text, including native empty text from null; later named lookups exclude unrelated unsafe rules even through several acyclic identity chains and guarded loops. Initial unknown metadata avoids eagerly expanding unrelated catalogues. Every pass and state comparison consumes one shared bounded work budget, and exhaustion rejects preflight intentionally. Thirty-five focused text tests pass through fresh/cache, both detection callers and transition reload, including four successive identity lookups, null chains, unsafe names and resource limits. Reference: native metadata/null wrappers in strategic-detection-scripts, Script.cpp text conditions/copies and Mod.cpp named lookup wrappers. This runs abstract proofs only, with no script execution, RNG or campaign mutation; cache revision remains 52 and later-branch boundaries remain guarded.

Identity-text/condition/feedback cycle audit closes: no additional confirmed runtime defect was found. Root dotnet test passes 2,402 tests with zero failures/skips, including native checksums, private corpus and populated allocation gates. Formatting verification, regenerated code-map and diff checks pass. The audit checked native empty text, read-only reader alias types, branch set feasibility, scoped register reuse, and shared-budget accounting across feedback passes. Metadata refinement remains conservative for unknown/NUL-bearing comparisons and unproved loop correlations. Twenty obsolete numeric probe objects were removed; useful captures/executables remain and the pinned reference is clean. Native numeric evidence now has 50 cases; cache revision remains 52. Final query/provider and branch-wide acceptance reconciliation keep 4c open; 4d is untouched.

Branch 4c detection caller domains: scalar forecasts now retain the actual detection callers' boolean already_tracked and 0..10800 globe distance inputs, plus validated UFO altitude/status domains including IgnoreMe. Mutable radar inputs, counters and output registers remain conservative. Five fresh/cache base/craft composition and endpoint tests pin native damage-query use, oracle distances and output isolation. Reference: Base.cpp/Craft.cpp detection callers, Target.cpp getDistance, fmath.h XcomDistance and Ufo.h status/altitude getters; CampaignWorld.Restore validates entity domains. No provider, cache revision or later-branch transition change.

Branch 4c consumed detection fields: bounded reference/scalar reachability now limits UFO/craft statistic, damage/shield and tag metadata to reads that can execute on a nonnull receiver. Native null wrappers overwrite outputs with zero without consuming live overflow/undefined-denominator fields; impossible scalar/text paths no longer block those fields. Nullable live reads retain precise pre-tick guards and every instruction remains in the execution work bound. Six fresh/cache base/craft/reload tests reproduce the old null-read failure and cover overflow, unreachable calls and live denominator rejection. Reference: ScriptBind.h BindMember/BindFunc const null wrappers, Ufo.cpp/Craft.cpp getDamagePercentage and ScriptRegister. A live craft with installed armor overflow proves the craft path; zero-maximum craft are destroyed and cannot serve as actual detection callers. No provider, cache revision or later-branch boundary is changed.

Branch 4c composed identity/caller/field acceptance: the existing 72-hour fresh/cache, initial-entry and stepped/batched/reloaded workloads now execute repeated metadata-to-name lookups, bounded caller/UFO values and null instance reads through both detection parsers. An unrelated unsafe damage rule and an impossible metadata-name branch with a zero divisor verify proof isolation during actual VM execution. The populated 8/32/128-UFO workloads at one/eight bases record cold/warm simulation and persistence allocations with every prior differential bound unchanged. All ten endurance/population cases pass; 36-contact/landing, fuel/servicing/recall/relaunch and exact snapshot equality assertions remain intact. Reference: pinned strategic-world, strategic-detection-scripts, strategic-detection-damage and strategic-detection-numeric captures; ScriptBind, Mod/RuleItem identity readers and Geoscape detection ordering. No cache revision or later-branch boundary change; 4d is untouched.

Caller-domain/consumed-field/composed-acceptance cycle audit closes: no additional confirmed runtime defect was found. The implementation slice fixed the reproduced null-read preflight defect; the batch corrected test spacing and initializer layout. Root dotnet test passes 2,413 tests with zero failures/skips, including native checksums, private corpus and populated allocation gates. Final formatting verification, regenerated code-map and diff checks pass. The 19 staged saves classify as ten executable at the thirty-minute horizon, one blocked after 125 ticks and eight tactical preservation-only. Expanded query cold setup allocates about 8 MB per campaign; naturally reached warm boundaries remain about 576-579 bytes per additional UFO, with prior differential limits unchanged and exact reload equality retained. No new native objects were generated; useful captures, corpus classifications and allocation measurements remain, and the pinned reference is clean. Cache revision remains 52. Remaining query/provider reconciliation and full branch-wide acceptance keep 4c open; 4d is untouched.

Branch 4c half-hour hunting maintenance: pure target-selection forecasts now apply ordinary-fuel refuelling and auto-patrol relaunch before the ten-minute hunter caller. Native setFuel/refuel/setDestination and the time30Minutes auto-patrol block expand strategic-hunting with 48 maintenance rows, retaining all prior traces. Fifty focused hunting tests pass, including fresh/cache and reload before/after relaunch, low-fuel retention and launch delay. Grounded craft stay excluded unless refuelling actually produces an eligible relaunch. Item-stock and hourly servicing forecasts remain precisely guarded for the next slices. No RNG, stock, identity or campaign mutation occurs in preflight; combat/event/month boundaries and 4d remain unchanged.

Branch 4c shared hunting fuel stocks: target forecasts now apply item refuelling to every grounded craft in saved order, using a bounded private stock projection. Ordinary non-patrolling consumers can prevent a later auto-patrol launch; fuel refunds, missing-item notifications, low-fuel clearing and full-tank launches follow the existing shared CraftLogistics operation. Eighteen native two-craft rows supplement the 48 maintenance rows, with every earlier trace retained. All 54 focused hunting tests pass through fresh/cache and transition reloads, with canonical native comparisons for item maintenance and scarce shared stocks. Hourly changes remain guarded pending the next slice. No stock, RNG or campaign state is mutated during preflight, and later-branch boundaries are unchanged.

Branch 4c hourly hunting maintenance: the pure craft projection now applies hourly repairs and the native empty-rearming transition before half-hour refuelling. It preserves the one-hour delay after repair completion and never speculates on ammunition-saving RNG. Twenty-four native hourly rows cover repairs, rearming, refuelling and already-ready craft; all prior hunting traces remain unchanged. Fifty-six focused hunting tests pass through fresh/cache and save reloads. Hourly item stocks, production ammunition reuse and incoming craft still have explicit composition guards for subsequent work. Reference: Craft.cpp repair/rearm/refuel and GeoscapeState time1Hour/time30Minutes fallthrough. No new combat/event/month transition or 4d work is enabled.

Hunting-maintenance cycle audit closes: confirmed incoming grounded auto-patrol and relaunched-leader escort-fuel forecast gaps now refuse the affected tick atomically; three regressions and two malformed-maintenance cases cover the boundaries. Craft-reference fuel scans are charged against the shared work budget. Root dotnet test passes 2,426 tests with zero failures/skips, including native checksums, the private corpus and populated allocation gates. The first root run hit a cold query allocation outlier (462.6 B/UFO against 448); focused endurance and the final root run pass with the original bounds unchanged. Test formatting was corrected. The obsolete native hunting object was removed; useful captures/executables remain and the pinned reference is clean. Hourly stocks, incoming craft and escort fuel composition remain subsequent 4c slices; no 4d or later-branch effect work was begun.

Branch 4c composed escort fuel: hunter preflight now builds one pure ordered craft projection, applies all maintenance/relaunches, then fuel use in saved base/craft order. The shared AdvanceCraftFuel transition accepts projected leader states, so a relaunch or an earlier low-fuel return supplies the speed consumed by later escorts. This removes the audit-added relaunch-leader guard. Four new native strategic-hunting rows insert getFuelConsumption/consumeFuel/returnToBase and the exact time10Minutes escort/fuel-return caller; all older traces remain unchanged. Eighty-eight focused hunting/craft-operation tests pass, including fresh/cache canonical order comparisons, notification fallthrough and reload. Incoming craft and hourly stock/ammunition effects remain precise subsequent work; 4d and combat/event/month boundaries are unchanged.

Branch 4c incoming hunter targets: due craft transfers now append in native base/transfer order to the pure projection, apply setBase/checkup and half-hour maintenance without receiving the earlier hourly service pass. Projected coordinates and live craft health participate in UFO arrival/combat preflight. The prior arriving-craft guard is removed for these supported transitions. Forty-eight native transfer/checkup rows retain all older hunting traces; fresh/cache canonical tests cover due/pending transfers, imported status, damage, full tanks, relaunch delays and reload. A close incoming target refuses combat before delivery. Hourly item-stock and ammunition-reuse composition remain explicit subsequent work; later-branch boundaries and 4d remain unchanged.

Branch 4c supplied hunting stocks: hourly projections now separate servicing, all transfer deliveries/item reuse, half-hour refuelling and ordered fuel use. Item arrivals feed shared fuel stocks and existing/new craft reuse without selecting RNG. The reference ItemsArrivingState batch ordering is now explicit in the transfer handler, with bounded all-arrival reuse work even when no UFO exists. Seventy-two supplied fuel/ammunition rows and twelve combined item/craft order rows retain all earlier native traces. Focused tests cover fresh/cache canonical outcomes, pending transfers, scarce hourly stocks, transition reloads and malformed work limits. Facility/craft ammunition consumption, production stock/craft consumption and finishing daily research stocks retain precise pre-tick composition checks. Reference: Transfer::advance, ItemsArrivingState constructor, Craft::reuseItem/refuel/rearm and GeoscapeState hourly fallthrough. Later-branch boundaries and 4d remain unchanged.

Composed escort/incoming/supplied-stock cycle audit closes: the confirmed malformed transfer-hour boundary now stops before delivery or item reuse, with two native invalid-arrival rows and hunter/non-hunter reload regressions. Reviewed saved craft order, per-base transfer batches, incoming checkup/service delay, projected target coordinates/health, combat refusal, scarce stocks and aggregate reuse work. Root dotnet test passes 2,448 tests with zero failures/skips, including native checksums, private corpus and populated allocation gates; their original limits are unchanged. Formatting corrections and unused-import cleanup were applied. The obsolete hunting probe object was removed, useful captures/executables remain and the pinned reference is clean. Facility/craft ammunition, production stock/consumption and research-stock composition are still precise 4c work; branch-wide closure remains open and 4d is untouched.

Branch 4c facility ammunition stocks: execution and hunter forecasts now share the pure BaseFacility::rearm transition, preserving alert suppression, full-ammo reset, build-time eligibility and disabled-facility behavior. Facility consumption runs after craft servicing and before transfers/refuelling, removing the prior facility-stock composition guard. Thirty-six native facility/fuel rows retain all earlier traces and compare fresh/cache outcomes with transition reloads. All 117 focused hunting/readiness tests pass; malformed consumed fields and int-max rates have focused unit coverage. Reference: BaseFacility.cpp rearm and GeoscapeState time1Hour facility loop. No host/native dependency, new save field or later-branch transition is introduced; 4d remains untouched.

Branch 4c deterministic craft ammunition stocks: hunter preflight now reuses CraftServicing.Rearm with a refusing random provider, applying deterministic stock consumption in native craft order. It never selects a bullet-saving draw or touches campaign RNG. A draw sharing the fuel item retains a precise atomic stochastic-stock guard; unrelated ammunition cannot change the craft's service status during that hour. Forty-eight native CraftWeapon::rearm/Craft::rearm shared ammunition/fuel rows retain all older traces, covering missing stock, clip rounding, completion delay and stochastic boundaries. Fresh/cache cases reload before and after supported transitions; blocked cases preserve the entire snapshot. All 100 focused hunting/readiness and 17 servicing tests pass. Production and daily research stock composition remain subsequent 4c work; later branch boundaries and 4d remain unchanged.

Branch 4c incomplete production composition: execution and hunter preflight share the pure ProductionProgress completion count. Existing non-fallback units paid at start cannot consume or deliver stocks/craft until a completion, so their unrelated pending work no longer blocks hunting. Fallback staff assignment and completing production retain precise composition guards. The pinned capture adds 96 Production::getAmountProduced/step rows; fresh/cache cases cover pending fuel, craft-consumption and ammunition projects, fallback refusal, progress overflow and transition reload. Focused native hunting/economy and pure progress tests pass. Reference: Production.cpp completion-count prefix and getAmountProduced; no event payload, combat, month guard or 4d behavior changes.

The facility/ammunition/production batch audit closes with two confirmed fixes: unchanged facility-service results retain their existing records (the idle-campaign allocation test caught the unconditional hourly copy), and daily research composition checks cover ammunition reuse without item fuel plus cross-base held-item refunds. The latter has fresh/cache reload regressions and a non-hunter control showing cancellation/refund followed by same-tick relaunch. Research scanning is bounded and shared once per hunting forecast. Reference: Base::removeResearch, GeoscapeState::time1Day and SavedGame primary research side effects before hourly servicing/transfers. Final root dotnet test passes 2,477 tests with zero failures/skips; native checksums, private corpus, populated measurements and code-map checks pass. One initial query allocation sample exceeded its original 448 B/UFO limit; focused endurance and both subsequent root runs passed without changing any threshold. Useful captures/executables remain; the obsolete hunting object was removed and the pinned reference remains clean. Completing production, stochastic shared ammunition, research stock forecasts and branch-wide closure remain open 4c work; later branches and 4d remain unchanged.

Branch 4c stock-neutral daily research: the research capability now supplies a pure bounded proof that daily item stocks and incoming item deliveries remain unchanged. It includes direct completions and every possible free/protected-free topic, preserves global cancellation uncertainty when any project holds an item, and distinguishes lookup/automatic addFinishedResearch from primary item side effects. Stock-free completions no longer trigger hunter fuel/ammunition guards. Fresh/cache loaded fixtures complete research plus a free topic, launch fuel/ammunition craft and reload both sides; guarded stock-changing fixtures remain intact. Reference: GeoscapeState::time1Day, SavedGame::handlePrimaryResearchSideEffects and Base::removeResearch. Event scheduling remains gameplay-owned; due payloads and later branch boundaries remain guarded. Branch 4c closure is still open and 4d is untouched.

Branch 4c deterministic research refunds: the research capability now projects simple daily completions and global obsolete-project cancellation in saved base order, returning immutable per-base stock changes before hourly servicing. Choices involving rewards, lookups, protected unlocks, disablers and matching corpse units remain explicit uncertainties. The pinned Base::removeResearch fragment contributes sixteen native held/finished/return/stock rows; cancellation now correctly avoids refunding the needed item for an already-finished destroying project. Fresh/cache fixtures compare every native row and reload cancellations, direct refunds and cross-base refund/relaunch transitions. Stock overflow stops atomically. No campaign RNG, identity or state is changed by this forecast. Spawned item deliveries are the next slice; branch 4c and later transition boundaries remain open/guarded, and 4d is untouched.

Branch 4c fixed research item deliveries: deterministic daily research projections now append the direct spawned item and every list entry as one-hour arrivals, preserving native count minimums, list order, duplicate entries and missing-item behavior. Hunter forecasts apply refunds before hourly service, then existing plus research arrivals as one batch before refuelling/reuse. Seventy-two pinned primary research/transfer/refuel rows compare canonical fresh/cache output and reload both sides. Fixed ammunition delivery now prevents a no-item-fuel craft's same-tick relaunch correctly. Transfer identity and stock overflow refuse before research, RNG, clock or inventory changes. Reward/disabler/lookup/protected-unlock and corpse uncertainties remain precise research composition boundaries. Reference: SavedGame::handlePrimaryResearchSideEffects spawned-item fragment, Transfer::advance and ItemsArrivingState batch reuse. Branch 4c closure is still open; due event payloads, combat/month progression and 4d remain deferred.

The research-stock batch audit closes with a confirmed end-of-day guard fix: already-disabled held projects can cancel/refund without any fresh research completion. A bounded research capability query now identifies this case, and the projection applies disabled cleanup after all daily base loops. The regression first reproduced a close hunter consuming an unavailable combat tick, then passes atomically with the fix; fresh/cache cases also verify the safe refund/relaunch and reload. Reviewed deterministic source ordering, global cancellation, finished-item semantics, duplicate/missing spawned items, all-arrival reuse, ID/stock limits and pure RNG/identity handling. Root dotnet test passes 2,497 tests with zero failures/skips, including native checksums, 19 staged save classifications and populated performance gates with unchanged limits. The corpus retains ten executable 360-tick horizons, one 125-tick guard stop and eight tactical preservation cases. Code-map/diff checks pass; useful captures/executables remain, the obsolete hunting object is removed and the pinned reference is clean. Compound research stock consequences, completing/fallback production, stochastic shared ammunition, remaining hunter/escort births, detection reconciliation and branch-wide headless closure remain 4c work. Branch 4d is untouched; due event payloads, combat/deployment and unrestricted month progression remain later-branch boundaries.

Branch 4c lookup stock composition: daily stock forecasts accept status-neutral lookup discovery while preserving its addFinishedResearch-only semantics. Lookup item payloads and matching projects remain untouched by primary side effects; lookup disablers/re-enablers and automatic disablers retain precise uncertainty guards. Four captured native primary-topic rows pin direct/free membership with lookups excluded. Fresh/cache scenarios exercise held lookup projects, direct fuel arrivals, unchanged lookup stocks and reloads before/after hunter selection. Reference: GeoscapeState::time1Day primary topic collection, SavedGame::handlePrimaryResearchSideEffects and Base::removeResearch. No RNG, identity or campaign state is changed during the forecast; later branch boundaries and 4d are unchanged.

Branch 4c research discovery stock composition: execution and preflight now share the addFinishedResearch discovery transition over live or copied completion/status sets. Direct and lookup disablers, re-enablers, automatic zero-cost chains and protected-unlock cancellation eligibility now participate in ordered daily stock refunds. Forecasts account for completing facility functions, active projects and refunded needed items before automatic discovery; work stays bounded and no RNG/state is consumed. Sixteen pinned native discovery/disabled-refund rows compare fresh/cache daily results and transition reloads, including already-complete sources and already-finished held projects. Reward choices and corpse item projections remain for subsequent slices. Reference: SavedGame::addFinishedResearch/hasUndiscoveredProtectedUnlock and GeoscapeState::time1Day construction/research ordering. Branch 4c closure and later-branch guards remain open; 4d is unchanged.

Branch 4c deterministic daily research rewards: stock forecasts now select sequential free topics and unambiguous eligible outcomes without consuming RNG, including duplicate candidates and prerequisite-gated protected rewards. Shared discovery runs the bonus and its lookup before the direct topic, then catalogue-ordered primary item arrivals and global obsolete-project cancellation. Seventy-two pinned selectGetOneFree/remaining-reward rows cover completed/disabled candidates, gated groups and duplicates; fresh/cache fixtures verify fuel, retained/cancelled cross-base held projects, reloads and atomic refusal of distinct random outcomes. Focused hunting/research compatibility passes 148 tests. Reference: SavedGame::selectGetOneFree/hasUndiscoveredGetOneFree and GeoscapeState::time1Day bonus-before-core ordering. Completing production, stochastic stock choices, corpse projections and final headless closure remain open 4c work; later branch boundaries and 4d are unchanged.

The research-composition batch audit confirms and fixes automatic discovery ordering: SavedGame::getAvailableResearchProjects iterates the research std::map in UTF-8 name order, independently of ruleset insertion and listOrder. Runtime catalogs now derive that immutable order on both fresh and cache construction; execution and forecast share it. Native competing-disabler and BMP/supplementary-name traces pin queued topics that become disabled after queueing, and fresh/cache reload scenarios verify safe hunter selection plus atomic combat stops. Protected-unlock membership scans are now charged at their actual nested cost; an 1,100-topic malformed-work scenario refuses before any tick mutation. Reviewed live/projected discovery semantics, global cancellations, reward eligibility, duplicate/missing arrivals and RNG/identity purity. Branch 4c remains open; 4d and later-branch boundaries remain unchanged.

Cycle validation: root dotnet test passes 2,511 tests with no failures/skips, including checksum verification, 19 staged-save classifications and populated 8/32/128-UFO measurements under unchanged limits. The corpus retains ten executable 360-tick horizons, one 125-tick stop and eight tactical preservation-only cases. Code-map/diff checks and reference-checkout cleanliness pass. Useful probe captures/executables remain; the obsolete compiled hunting object is removed at this milestone.

Branch 4c zero-quantity production delivery: the pinned Production::step item fragment retains zero-valued outputs as observable delivery/reuse operations. Immediate zero outputs now interrupt eligible craft ammunition servicing exactly like nonzero outputs; delayed zero item transfers retain their identity, hours and payload through save rewrites and arrive/clean up once. Snapshot validation permits zero only for item transfers and still rejects negative quantities and empty staff transfers. Seventy-two native delivery/refuel/reuse rows include zero quantities; fresh/cache tests compare zero rows and reload both sides, plus incoming-zero cleanup regressions. Reference: Production.cpp fixed item delivery, Transfer::advance/save, Craft::reuseItem and Geoscape hourly ordering. This fixes delivery semantics without enabling speculative production forecasts; branch 4c and later transition gates remain open, and 4d is unchanged.

Branch 4c final prepaid production forecasts: registered production-owned operations now project final fixed item outputs before hunter retargeting, after servicing/transfers and before every base refuels. Final units never charge inputs again, including paid materials/craft with insufficient current funds. Immediate outputs preserve native zero-quantity reuse; delayed outputs stay future arrivals and consume bounded transfer identity headroom, coordinated with earlier daily research arrivals and same-hour cleanup. Seventy-two native fixed-delivery rows compare fresh/cache hunting outcomes and both transition reloads; stock/identity overflow stops atomically. Ongoing/fallback, sold, random, craft and person production remain precise stock composition boundaries. Reference: Production::step final-unit break and item delivery, GeoscapeState::time1Hour ordering. Focused hunting/economy tests pass 169 cases; later branches and 4d are unchanged.

Branch 4c recurring free production forecasts: non-fallback zero-cost/no-input projects now project every fixed item completion in native order, honoring finite caps and infinite production. Native completion-count and delivery fragments contribute 192 composed rows for zero/multiple units, zero/two outputs, delayed transfers, shared fuel and ammunition reuse. Fresh/cache scenarios reload before/after and preserve negative funds for free unit starts. Work, cumulative future item quantities, identity slots and captive populations are bounded before any campaign mutation. Sold, funded/material-consuming, fallback, random/craft/person outputs and nonzero research-score effects retain explicit composition guards pending their projections. Focused hunting/economy tests pass 181 cases. Reference: Production::step, getAmountProduced, haveEnoughMoneyForOneMoreUnit and fixed delivery/reuse. Branch 4c closure remains open; later boundaries and 4d are unchanged.

The production batch audit fixes a confirmed mixed-producer identity hole: an unsupported random producer could allocate transfers omitted from the fixed-producer budget, overflowing only after tick mutation began. A precise capability guard now blocks that mixed future stream before clock/RNG/inventory changes; fresh/cache regressions reproduced the overflow before the fix and pass after it. Cross-handler cases also verify daily research consuming the final identity slot and same-hour cleanup releasing an imported highest ID. A production-funded relaunch remains atomically blocked at same-tick hunter interception. Reviewed prepaid/recurring unit counts, all-base ordering, zero quantity reuse and persistence, aggregate incoming/captive quantities, resource limits and remaining score/accounting/fallback uncertainty. Branch 4c remains open; 4d and later-branch boundaries are unchanged.

Cycle validation: all 2,544 root tests pass with zero failures/skips, including native checksums, private save horizons and original populated performance gates. Code-map/diff checks and pinned-reference cleanliness pass. Useful captures/executables remain; the regenerated hunting object is removed at this milestone.

Branch 4c funded production composition: non-fallback fixed item projects without consumed inputs now forecast paid continuation in saved base/project order. The current unit is prepaid; later starts use the same checked accounting operation as execution, stop on insufficient money, and avoid charging after the finite last unit. Unknown earlier sale/cost effects retain a precise shared-accounting guard. Sixty-four native Production::step continuation/startItem/money rows compare fresh/cache unit counts, funds, stop reasons, stock and hunter targets with both transition reloads. Focused hunting/research compatibility passes 193 tests. Material consumption and fallback staffing remain subsequent slices; later-branch boundaries and 4d are unchanged.

Branch 4c consumed production materials: fixed item completions now apply immediate output/reuse before each continuation input check. Delayed outputs stay unavailable to the next unit, and failed input checks leave funds unchanged. The production-owned forecast consumes only projected stocks and preserves saved project order; unknown earlier material effects retain a precise shared-material guard. Seventy-two native money/material/startItem rows compare unit counts, stop reasons, stocks, future arrivals and hunter outcomes with fresh/cache reloads. Craft input consumption and fallback staffing remain subsequent work; branch 4d and later-branch transitions are unchanged.

Branch 4c fallback production composition: execution and forecasts share the queued-space/engineer assignment transition. Forecasts include due engineer arrivals and workshop construction completed at the preceding daily hook, then preserve project order and ongoing occupancy. The exact hourly reference caller confirmed that project removal and staff release occur after every production step in a base; execution now retains completed/stopped projects until that pass, so later fallback work cannot borrow their engineers early. Seventy-two native staffing rows and four deferred-release rows compare fresh/cache counts, stocks, funds-independent stops and hunter results with transition reloads. Focused hunting/research compatibility passes 203 cases. Capture inserts the native progress enum instead of synthesized values. Sold, craft-consuming, random/craft/person outputs and score effects remain explicit subsequent 4c work; later-branch guards and 4d are unchanged.

The paid/material/fallback production batch audit fixes two confirmed issues. A fallback project could acquire staff after ordinary preflight and overflow its spent time after the tick began when no hunter existed; fresh/cache regressions reproduced the exception. A shared bounded staffing forecast now validates every hourly production pass and supplies the hunter projection, preserving due transfers, daily construction and deferred project removal. Idle bases also bypass empty removal-list allocation; the unchanged 16,384-byte summary test caught that regression and now passes. Reviewed prepaid units, money-before-material checks, output-before-input reuse, saved project/base order, future quantity/identity bounds and transition reloads. Focused compatibility/endurance passes 215 tests and foundation/progress passes 22. One initial populated query sample exceeded its existing limit; the focused rerun passes without changing it. Branch 4c remains open; 4d and later-branch boundaries are unchanged.

Cycle validation: all 2,563 root tests pass with zero failures/skips, including reference checksums, 19 private save classifications and unchanged populated performance limits. Code-map/diff checks pass. Useful captures/executables remain; the obsolete compiled hunting object is removed at this milestone. The pinned reference checkout is unchanged.

Branch 4c native craft inputs: Production::haveEnoughMaterialsForOneMoreUnit uses the required quantity only to count grounded craft, while startItem removes one first matching craft per listed type, including zero-valued entries and airborne first matches. Twenty-four native eligibility/selection traces reproduced the zero-input mismatch before the fix and now compare fresh/cache results with empty unload payloads and reloads. Execution preserves that split; counter lower bounds treat zero craft inputs as possible consumption. The hunter forecast retains a precise consumption guard for these now-correct transitions until the next slice. Existing hunting/research compatibility passes 207 tests. Reference: Production::startItem, Base::getCraftCountForProduction/removeCraft and Craft::unload. Branch 4d and later-branch boundaries are unchanged.

Branch 4c craft consumption forecasts: execution and preflight share material eligibility and first-craft selection. Each projected start removes item inputs before craft unloading, adds inventory/weapons/vehicle ammunition through the existing unload operation, and removes consumed actors before refuelling/retargeting. Twenty-eight pinned item-unload/refuel rows cover missing input, payload-driven relaunch and a valid int-max inventory that requires item-first ordering. Fresh/cache cases reload both sides; unsafe unload stock/quantity arithmetic refuses atomically. The prior ordinary craft-consumption stock guard is removed for fixed zero-score item producers. Focused hunting/research/counter tests pass 223 cases. Reference: Production::startItem, Craft::unload and Base::getCraftCountForProduction. Deleted-target pursuit composition is the next slice; later branches and 4d remain unchanged.

Branch 4c consumed target repair: a registered world-owned operation now applies Target::~Target follower consequences when production removes a craft. Craft return destinations/speeds change before later hourly, refuel and fuel hooks; hunted UFOs restore the original waypoint without changing speed. Forecast sources share the craft return operation, and absent projected identities remain absent even for imported hunting UFOs with the hunter-killer flag clear. Four native destructor/fuel rows compare initial production commands and same-hour removal with fresh/cache reloads. Work bounds include repeated removals, arrivals and possible production births before any tick mutation; a 1,001-by-1,001 follower workload refuses atomically. Focused hunting/research/counter tests pass 229 cases. Reference: Target::~Target, Craft::returnToBase/setDestination and Ufo::resetOriginalDestination. No notification performs mutation; command/tick operations retain the single writer. Branch 4c remains open; later-branch transitions and 4d are unchanged.

The craft-input/unload/pursuit batch audit fixes a confirmed late-validation boundary. An invalid grounded craft follower at an unplaced base could cause a production stop only after its prepaid output and clock were mutated; fresh/cache regressions reproduced this for current and same-hour arriving followers. Production preflight now supplies bounded possible removal types, and world preflight validates existing plus due follower payloads before the tick. The exact registry-order assertion includes the new preflight without reducing strictness. Reviewed zero/multiple craft quantities, first-craft selection, item-before-unload overflow, identity lower bounds, synchronous return/restoration, future arrivals/birth work budgets and deleted-target reloads. Focused existing/arriving follower, native removal and populated performance checks pass with unchanged limits. The first full query allocation sample exceeded its original bound; both focused populated reruns pass without relaxing it. Branch 4c remains open; sold/random/craft/person production, score effects, stochastic/corpse projections and final headless reconciliation remain subsequent work. Later-branch boundaries and 4d are unchanged.

The final ordering check also reproduced an unstable material partition: sorting equal craft entries reordered an 18-craft native input list and its follower notifications. Material planning now appends craft inputs after items while retaining the reference order within each group, without allocating a craft list for item-only projects. The pinned startItem trace and fresh/cache transition-reload regression cover the complete deletion and feedback sequence.

Two default-runtime root runs exceeded the unchanged query boundary allocation gate (501.8 and 450.1 B per additional UFO); isolated default and process-local non-tiered runs passed. Inspection found interface enumeration in WorldGeometry.InsideRegion. Indexed traversal preserves the existing ordered geographic predicate and removes that enumeration allocation regardless of JIT optimization. The original populated measurements fall from approximately 369 to 208 B per additional UFO for eight-base query boundaries; warm-boundary costs fall by 160 B/UFO. Existing native geography fixtures and allocation thresholds remain unchanged. This addresses the concrete allocation source without claiming a proven explanation for runtime-dependent sample variation.

Cycle validation: all 2,583 root tests pass with zero failures/skips after the fixes, including native checksums, 72-hour scenarios, all 19 private save classifications and the original populated allocation gates. No test or threshold was relaxed. Code-map/diff checks pass; the pinned reference is clean. Useful captures/executables remain, and the compiled hunting object was removed. Branch 4c headless closure remains open and branch 4d is untouched.

Branch 4c fixed production sales: hunter projections now apply unscripted, fixed item sales before each continuation money/material check in saved base/project order. Sold outputs create neither stock/reuse nor delayed transfers, and sale coefficients truncate before quantity multiplication. The pinned Production::step, RuleItem::getSellCostAdjusted and SavedGame::setFunds fragments add 128 continuation rows plus two mixed-sign ledger rows, with fresh/cache same-base and cross-base reload coverage. The audit reproduced and fixed netting mixed sale signs in live execution, which previously lost separate income/expenditure entries, and a no-hunter accounting exception after tick mutation. Production-owned preflight now reserves signed capacity for every potential completion/start, bounding scripted prices by their int range without executing a future-context VM. These conservative capacity bounds intentionally refuse extreme imported ledgers without relying on later opposite-sign offsets or uncertain production stops. Local capacity tests cover signed headroom, independent ledgers, large counts and malformed bounds. Direct/global time-reading sale scripts retain a precise hunter forecast guard while ordinary execution still runs the real VM at the advanced clock. Random/craft/person production, score effects and price-script projections remain subsequent 4c work; later-branch boundaries and 4d are unchanged. Reference checkout remains read-only.

Slice validation: all 2,617 root tests pass with zero failures/skips, including native fixture checksums, private save classifications, 72-hour scenarios and populated allocation gates. Full formatting verification and code-map/diff checks pass. No test or threshold was relaxed. The pinned reference checkout is clean; useful captures remain and the compiled probe object was removed. Branch 4c remains open and branch 4d is untouched.

Branch 4c scored production: fixed item and sale output with positive/negative manufacture points now participates in hunter stock/funding forecasts. Twenty-four pinned Production::step / SavedGame::addResearchScore rows cover per-unit points before continuation, finite/infinite caps, material stops and subsequent refuel/hunting. A shared signed-capacity preflight bounds daily primary/reward/lookup/automatic discoveries plus hourly output before tick mutation, without selecting random rewards. Extreme score histories are conservatively refused rather than relying on cancellation or uncertain stops. Fresh/cache fixtures and transition reloads pass; later-branch boundaries and 4d remain unchanged.

Branch 4c deterministic random manufacture: positive-weight choices with identical ordered item outputs now compose with fixed output, sales, continuation materials, reuse/refuel and hunting without speculative RNG. Sixty-four pinned random-delivery traces cover one/two identical choices, zero quantities, auto-sale, delayed output and finite/infinite continuation. Random output is delivered even when fixed output is sold, and every queued zero-quantity entry reserves its transfer identity. Per-project history preflight bounds all reachable positive-weight item increments plus unsold fixed output, preventing overflow before mutation even without hunters. Fresh/cache and history-limit reload tests pass. Genuinely different outcomes keep precise composition guards; the mixed uncertain-transfer fixture now contains distinct choices. Later-branch boundaries and 4d are unchanged.

Branch 4c scientist/engineer production: delayed staff output now composes with fixed/deterministic-random item output, funding, reuse and hunter selection. Forty-eight pinned Production::step / Base::getUsedQuarters rows cover default/minimum personnel delay, finite/infinite completion, intermediate living-space fallthrough and final continuation checks. Inspection found and corrected the missing one-quarter reservation for each person-producing project, including queued projects; deferred removal/cancellation releases that reservation. Production-owned preflight bounds possible transfer creation and personnel headroom before mutation, including ordinary worlds with no hunters. Fresh/cache native rows, queued cancellation/reloads, transfer exhaustion and population limits pass. Soldier generation and genuinely uncertain outputs remain guarded in hunter composition; later-branch boundaries and 4d are unchanged.

The score/random/personnel batch audit bounds empty protected-reward groups, accounts for the existing saved transfer population before production creates more, and rejects imported personnel totals that overflow after adding project reservations. Regression fixtures cover 1,001-by-1,001 empty groups, 100,000 existing transfers and signed personnel limits. Twenty-four additional native rows pin omitted personnel transfer delay at 24 hours while preserving the original 48 rows. No speculative VM/RNG, fixture strictness or allocation threshold was weakened.

Cycle validation: all 2,647 root tests pass with zero failures/skips after the batch audit fixes, including fixture checksums, the 19 private save classifications, composed 72-hour scenarios and unchanged population/allocation checks. Full formatting and regenerated code-map/diff verification pass. The reference checkout remains clean; useful native captures remain and the compiled object was removed. Branch 4c remains open; branch 4d is untouched.

Branch 4c corpse evidence and projections: 512 pinned removal/daily-interrogation traces distinguish held-item return from corpse recovery across finished/cancelled research, retainCorpses, unit lookup and both recovery flags. Runtime units link armor, armor links its geoscape corpse, and items retain recover/recoverCorpse defaults; fresh/cache tests pass with compiler revision 53. Campaign retainCorpses defaults false per Options.cpp, honors fixedUserOptions and survives the save adapter/reference option snapshot. The separate native callers can award two corpses on completion; gameplay application and forecasting follow in the next slices. Reference: Base::removeResearch, GeoscapeState::time1Day, RuleResearch::isHoldingNeededItem, Armor::getCorpseGeoscape and RuleItem recovery getters. Later-branch boundaries and 4d remain unchanged.

Branch 4c live research recovery: completion applies the Base::removeResearch held/corpse effect and the separate daily interrogation corpse effect before discoveries. Cancellation applies only the removal effect, looking up the unit by research name even when neededItem differs. All 512 native rows pass fresh/cache and before/after reloads; the shared local lifecycle rule uses the same verified oracle. Command/daily preflight bounds recovered stocks, including possible earlier cross-base cancellations, so corpse overflow refuses before staff/project/time mutation. Completed projects remain deleted after rewriting saves. Hunter corpse stock composition follows in the next slice; later-branch boundaries and 4d are unchanged.

Branch 4c corpse composition: daily stock projection now uses the same removal/daily recovery operation as execution before automatic discoveries and hourly inputs, including earlier-base cancellations and final disabled-project cleanup. The old unit/corpse forecast guard is removed. Another 128 pinned recovery/refuel/hunting rows pass fresh/cache plus transition reloads; cross-base fixtures prove a cancelled finished project receives only the removal corpse and cannot run its later daily caller. Recovery changes retain exact item identities and do not invoke item reuse for direct corpse addition. Random reward choices and other uncertain compositions retain their specific guards. Later-branch boundaries and 4d remain unchanged.

The corpse batch audit removes empty-research preflight allocations and avoids duplicate unit-armor runtime diagnostics: typed validation still rejects absent/missing armor, with two focused regressions. All 2,669 root tests pass with zero failures/skips, including the unchanged allocation gates and private corpus classifications. Native checksums and code-map/diff verification pass; no fixture expectations or limits were relaxed. Branch 4c remains open and 4d is untouched.

Branch 4c native craft manufacture: runtime STR_CRAFT projections now retain only the first producedItems entry, matching RuleManufacture::afterLoad; later undefined names are ignored. Production creates fixed weapons, moves to its base and runs checkup before immediate insertion or delayed transfer, sharing existing craft operations. Thirty-two pinned Production::step / Craft::setBase/checkup rows cover weapon rearming, zero/nonzero fuel, negative/zero/positive craft delays and coordinates; fresh/cache transition reloads plus local first-output tests pass. Compiler revision 54 invalidates old projections. Hunter birth composition follows in subsequent slices; later-branch boundaries and 4d are unchanged.

Branch 4c delayed craft composition: deterministic delayed craft manufacture now participates in ordered funding/material continuation and transfer-identity forecasts without inserting a future craft into current hunter candidates. Native birth fixtures now run alongside an existing item-fuel auto-patrol craft through fresh/cache reloads. Shared production preflight bounds produced craft IDs against saved counters, existing craft and transfer payloads, total craft population, transfer births and malformed service capacities before ordinary or hunter ticks mutate. Twelve focused failure cases plus the existing native initialization rows pass with unchanged atomicity assertions. Immediate craft births remain the next slice; later-branch boundaries and 4d are unchanged.

Branch 4c immediate craft composition: deterministic craft completions now append in reference base/project order before continuation, random item reuse, half-hour shared refuelling and hunting. A pure identity allocator includes due/remaining transfers, saved craft floors and prior delayed births; uncertain daily research or other production identities retain precise guards. Another 256 pinned craft-birth/continuation/fuel/hunting traces pass fresh/cache and reloads, covering funds/material stops, fixed weapons and immediate/delayed output. A same-hour newborn input fixture verifies later craft consumption and deleted-actor reloads. All 285 hunting cases pass before the added chain cases. Later-branch boundaries and 4d are unchanged.

The three-slice craft audit reproduced two missing preflight bounds: daily research could increment the craft counter into exhaustion before production, and repeated identity scans did not charge empty bases. Shared production capacity now uses bounded post-research counter ranges and charges base visits plus research/reward/counter scans. Four fresh/cache regressions reproduce the prior overflow/affected-tick mutation and now refuse atomically, including a 1,001-base by 1,001-type workload. Reviewed first-output selection, fixed weapon/checkup ordering, delayed transfer timing, continuation stops, shared fuel, same-hour newborn consumption and deleted-actor reloads. No expectations or allocation/work limits were relaxed.

Cycle validation: all 2,696 root tests pass with zero failures/skips after the confirmed craft audit fixes, including native checksums, 19 private save classifications, composed 72-hour scenarios and unchanged population/allocation gates. Full formatting verification and code-map/diff checks pass, the reference checkout remains clean, and the compiled hunting object was removed while useful captures remain. Branch 4c headless closure remains open; 4d is untouched.

Branch 4c ordered research counter projection: the daily research forecast now records primary/reward counter changes in reference catalog order after discovery and cross-base cancellation, with duplicate side effects suppressed. Counter-only completions no longer take the unchanged-stock shortcut. Sixteen native SavedGame primary-counter/default/clamp traces pass local arithmetic and fresh/cache forecast-vs-execution/reload fixtures, including earlier-base cancellation without duplicate effects. The forecast remains pure and work-bounded; the following slices connect its exact changes to craft identity allocation and capacity. Later-branch boundaries and 4d are unchanged.

Branch 4c exact research-to-craft identities: the world passes the pure ordered daily counter stream into production forecasting, removing the ambiguous-ID guard for single, sequential and repeated-identical free rewards. Eight additional native traces pin one/two-base sequential completion: a later base keeps its project while another free reward remains. Fresh/cache craft identities match both counter oracles with before/after reloads and no speculative RNG. Genuinely different random outcomes retain their precise forecast guard. The next slice shares exact counter bounds with ordinary production preflight; later-branch boundaries and 4d are unchanged.

Branch 4c exact ordinary craft capacity: production preflight now uses the same deterministic daily counter stream as hunter identity projection. Valid ordered decreases can free counter headroom, while existing craft/transfer payloads remain identity floors independently of the persisted counter. Six additional native near-limit traces preserve all earlier rows; fresh/cache single/sequential/identical reward births pass at int-max-minus-one. Saved actor floors and genuinely different reward choices still refuse atomically, preserving RNG and the affected tick. Unknown outcomes retain conservative bounded ranges. Later-branch boundaries and 4d are unchanged.

The counter batch audit reproduced research-only counter overflow with no production or world sites. Research-owned daily preflight now validates every deterministic primary counter transition and reserves positive bounds for unknown reward paths without crediting unselected decreases/cancellation. It also bounds potential cross-base counter work; a 1,001-base by 1,001-counter fixture refuses on repeated attempts without clock/RNG/project mutation. Presence caching is published only after a completed scan. Six fresh/cache audit regressions pass. Native near-limit ordered decreases remain enabled; later-branch boundaries and 4d are unchanged.

The final counter audit restricts uncertain bounds to direct completions and possible free rewards: lookup and automatic addFinishedResearch discoveries do not execute primary counter effects. A near-limit lookup-only counter regression passes fresh/cache and reloads without a false refusal. All prior bounds and RNG-preservation assertions remain unchanged.

Cycle validation: all 2,717 root tests pass with zero failures/skips after the counter batch audit fixes, including native fixture checksums, all 19 private save classifications, composed 72-hour scenarios and unchanged population/allocation gates. Full formatting verification and regenerated code-map/diff checks pass. The pinned reference checkout remains clean; useful captures remain and the compiled hunting object was removed. Branch 4c headless closure remains open; branch 4d is untouched.

Branch 4c soldier production preflight: full pinned Production::step personnel delivery and Mod::genSoldier duplicate retry traces establish one transfer/identity per birth, minimum/default delay, template/name ordering and the ten-attempt pending-name limit with controlled constructor/name/nationality collaborators. Production-owned preflight bounds generation/template work, malformed stats, possible bonus overflow, soldier population and post-research identities including pending actors and craft types sharing STR_SOLDIER. Twelve fresh/cache atomic-refusal fixtures pass, with save/reload identity coverage. The next slices apply nationality/pending-name semantics and compose soldier births with hunting; branch 4d and later-branch boundaries are unchanged.

Branch 4c manufactured soldier caller semantics: production now uses the shared base-location nationality selector and includes pending-transfer names in Mod::genSoldier's duplicate check. All 24 pinned personnel/duplicate traces pass fresh/cache, with generated identity, minimum/default delay, template-before-final-name ordering, RNG retry counts and reloads before birth, after birth and after arrival. A two-name-pool country fixture selects the first country match. Generation remains inside live execution; forecasts do not consume its draws. Reference: Production::step, Mod::genSoldier, SavedGame::selectSoldierNationalityByLocation and Soldier::genName. Fourteen combined focused cases pass; branch 4d and later-branch boundaries remain unchanged.

Branch 4c soldier production composition: deterministic strategic outputs now compose soldier births with continuation accounting/materials, living quarters, item reuse/refuelling and hunting without speculative soldier generation or RNG. Thirty-six new native soldier/quarters/hunting rows pass fresh/cache and transition reloads. Production uses a shared pure counter stream for person-before-craft, craft-before-person and same-project births when a craft is named STR_SOLDIER; three native ordering traces and reloads pin both identities. Unknown prior production/research identities retain precise guards. Soldier transfers remain delayed by at least one hour. The six composed cases pass; branch 4d and later-branch boundaries remain unchanged.

The soldier production batch audit reproduced and fixed three gaps: growing pending-name scans now charge births across all projects/bases and transfers from other production; world marker forecasts include soldier births and preserve conservative bounds for mixed craft/soldier allocator floors; site-expiry counter capacity includes preceding actor production. The expiry fixture also reproduced the existing craft variant as an overflow after tick mutation. Fourteen fresh/cache regressions now refuse atomically, and safe marker births retain reload coverage. Generation work arithmetic is checked before multiplication; production identity scans charge soldiers, bases and cleanup. No native expectations, fixture strictness or allocation/work thresholds were relaxed. Branch 4c remains open; branch 4d and later-branch boundaries are unchanged.

Cycle validation: all 2,749 root tests pass with zero failures/skips after the soldier production batch audit fixes, including native fixture checksums, all 19 private save classifications, composed 72-hour scenarios and unchanged population/allocation gates. Full formatting verification and regenerated code-map/diff checks pass. The reference checkout is clean at the pinned revision; useful captures remain and the compiled probe object was removed. No unexpected access denials occurred. Branch 4c headless closure remains open; branch 4d is untouched.

Branch 4c ordered UFO escort movement: pinned Ufo::think/MovingTarget::move traces now cover 54 chain, ring and self-target steps across all six saved list orders. Movement preflight uses an iterative per-tick position prefix, following earlier actors after movement and later actors before movement; no recursive graph traversal or speculative RNG is needed. Saved-chain guards are removed, while craft combat/deployment guards remain. Fresh/cache fixtures reload before and between every step, and large escort graphs refuse atomically at the existing work limit. Reference: GeoscapeState::time5Seconds and ufoHuntingAndEscorting, Ufo::think, MovingTarget::move. Newborn escort composition and terminal-arrival fallthrough are subsequent slices. Branch 4d is untouched.

Branch 4c newborn escort chains: the ordinary-wave guard for imported hunting/escort leaders is removed. Eight new pinned AlienMission::spawnUfo traces establish that leader flags never change first-non-hunter selection, relocation, original waypoint backup or full-speed attachment, including grounded sources. Four fresh/cache composed cases cover ordinary, hunting, chained and self-target leaders with reloads before birth, after birth and after the following movement tick. Existing no-potential-craft and newborn resource/pursuit guards remain until those contexts can be projected. Branch 4d and later-branch boundaries are unchanged.

Branch 4c terminal escort forecasts: the ordered position prefix now stops after a native terminal arrival, retaining later actors at their old coordinates. Site-conversion suppression invalidates the prefix because it can change dormancy into destruction. Six new pinned list-order traces preserve every prior oracle row. The combat capability check now follows the same saved-order terminal fallthrough: a skipped later hunter remains unchanged, then refuses atomically on the following tick; an earlier hunter still blocks the entire tick. Four fresh/cache boundary fixtures and six combined focused cases pass with transition reloads. Unsupported crash-state validation remains intact. Reference: GeoscapeState::time5Seconds destroyed-UFO return, Ufo::think and MovingTarget::move. Branch 4d and later-branch ownership are unchanged.

The escort batch audit reproduced and fixed two gaps: the lifecycle fixture also owns a checksum for the shared world probe (a fresh native recapture proves its oracle is unchanged), and ordered escort prefixes must charge linear mission lookups for takeoff forecasts. Two fresh/cache resource regressions previously advanced a tick and now refuse atomically at the unchanged work limit. No native oracle rows, malformed-state checks or performance limits were weakened. Branch 4c remains open; 4d is untouched.

Escort cycle validation: all 2,763 root tests pass with zero failures/skips after the audit fixes, including native fixture checksums, all 19 private save classifications, composed 72-hour scenarios and the unchanged population/allocation gates. Full formatting verification, code-map and diff checks pass. The pinned C++ checkout is clean. Useful captures remain; the three compiled probe objects were removed. No unexpected access denials occurred. Branch 4c remains open; branch 4d is untouched.

Branch 4c hourly hunter population forecasts: a production-owned internal operation bounds craft births with the pinned Production::step/getAmountProduced completion count and shared fallback staffing. Infinite queues now reserve only possible births in the current hour instead of triggering a blanket finite-queue guard; finite queues also stop reserving their lifetime quantity. Existing and due incoming craft participate without predicting random continuation or generating actors. The existing native production-progress rows cover both valid time values, spent/assigned combinations and finite/infinite queues, with fresh/cache reloads around the creation tick. Large birth/retarget combinations retain the unchanged work-limit refusal. Branch 4d is untouched.

Branch 4c delayed craft population boundary: positive manufacture transfer delays keep births out of the current tick's hunter candidate population, because hourly arrivals have already run. The native productionCraftBirthRows establish immediate versus delayed delivery; six fresh/cache composed cases pair 1,001 possible craft births with 1,001 new hunters. Immediate births retain an atomic work-limit refusal; one/three-hour births remain pending, permit the creation tick and survive reload with unchanged timers and craft identities. Production's own total-population/transfer guards remain in force. No RNG or actors are generated by the forecast. Branch 4d is untouched.

Branch 4c shared hunter forecasts and candidate work: common servicing/transfer/production inputs are projected and charged once per tick, while each hunter charges actual craft candidates plus base and mission scans. Four delayed-birth continuation fixtures reproduced false following-tick refusals and now advance after reload with pending transfers unchanged. Empty-base scans also reproduced a missing work bound; saved and newborn hunter populations now refuse atomically, including the creation tick. Native selection/order semantics and all existing work/allocation limits remain unchanged. Newborn work reserves both actor and base/mission visits. Reference: GeoscapeState::ufoHuntingAndEscorting and existing strategic-hunting traces. Branch 4d is untouched.

Hunter population cycle validation: all 2,775 root tests pass with zero failures/skips, including pinned fixture checksums, 19 private save classifications, composed 72-hour scenarios and the unchanged allocation gates. The batch audit checked hourly completion bounds, fallback staffing, transfer arrival order, common-input charging and candidate scans; no additional confirmed defect was found after the three slice fixes. Full formatting verification and code-map/diff checks pass. Branch 4c remains open and 4d is untouched.

Branch 4c escort ring endurance: four fresh/cache cases compare 72 hours of single-step, batched and hourly-reloaded simulation in forward/reverse saved order. Opening positions match the pinned Ufo::think/MovingTarget movement rows; every hourly checkpoint validates live counts and stable target links. No actors are deleted or duplicated across reload. Reference: GeoscapeState::time5Seconds, Ufo::think and the strategic-world escortMovement fixture. All four checks pass; branch 4d and later-branch boundaries are unchanged.

Branch 4c populated escort allocation: 8/32/128-actor forward/reverse rings now record cold and warm ticks plus transition reloads. Measurement reproduced 2.47 KB per additional UFO/tick from repeated immutable preflight projections. Tick-local source-identity memoization and reusable prefix buffers reduce this to at most 883 B in the measured cases, guarded by a new 1 KB actor/tick budget. Site-suppression and each new preflight clear both caches. All eleven native movement, terminal, endurance and allocation checks pass; no existing ordinary-flight limits changed. Reference: Ufo::think/MovingTarget::move and GeoscapeState saved-order flight. Branch 4d is untouched.

Branch 4c successive escort cleanup: three new pinned Target::~Target/resetOriginalDestination chain traces distinguish direct repair from transitive links and preserve original waypoints and escort speeds. Four fresh/cache forward/reverse cases follow terminal arrival, retained dead actors, next-tick cleanup, all subsequent deletions and reloads around every transition; no deleted actor or dangling link returns. The deletion oracle compares stable links and waypoint coordinates, since live UFO target coordinates depend on the separate movement pass. All four checks pass. Reference: Ufo::~Ufo, Target::~Target, Ufo::resetOriginalDestination and GeoscapeState::time5Seconds. Branch 4d and later-branch boundaries remain unchanged.

Escort closure batch validation: all 2,784 root tests pass with zero failures/skips, including native checksums, all 19 private save classifications, ordinary and escort 72-hour runs and populated allocation checks. The audit reviewed memo source identity, per-tick/site-suppression invalidation, terminal fallthrough, direct follower repair and saved-order persistence; no further confirmed defect was found. Full formatting and code-map/diff checks pass. The pinned reference remains clean. Useful captures and measurements remain; the compiled hunting object was removed. Branch 4c remains open; branch 4d is untouched.

Branch 4c saved idle escorts with UFO followers: the newborn leader guard now permits this formation when ordinary-arrival and craft-pursuit dependencies are absent. Six new pinned movement rows compose native idle attachment with an appended leader and an existing follower in both saved orders. Four fresh/cache cases compare exact positions, speeds and stable links with reload after every step. All 68 escort spawn/endurance checks pass, including unchanged affected-tick atomicity and movement work limits. The obsolete follower scan is removed; craft pursuit and original-waypoint arrival guards remain. The shared lifecycle probe was recaptured with unchanged oracle and refreshed input checksum. Branch 4d is untouched.

Branch 4c escort detection VM acceptance: four fresh/cache forward/reverse rings execute native-bound isEscorting/isHunting/isHunterKiller reads and persisted instance tags through the real detection VM. Four successive half-hour boundaries and double save rewrites preserve flags, tags, hyperwave detection and live mission counts. Native strategic-detection-scripts tag rows and Ufo detection bindings provide evidence; all four cases pass. No script is executed speculatively by preflight. Branch 4d and later-branch boundaries are unchanged.

Branch 4c craft/escort endurance composition: fresh/cache cases compare 72 hours of stepped, batched and reloaded escort rings with real post-takeoff craft pursuit, patrol, recall, automatic fuel return and servicing. Stable UFO identity survives commands and save rewrites; live counts remain exact while detection runs through the real VM. Native checkup clears LowFuel on same-tick return, so the fixture proves later automatic fuel return through post-patrol servicing with depleted fuel. Both composed cases pass. Existing native pursuit, movement, fuel and detection fixtures back the participating operations. Branch 4d and later-branch guards remain unchanged.

The newborn-follower batch audit confirmed a portability defect in the hunting input manifest: it hashed a CRLF worktree template while Git committed LF according to .gitattributes. The template is now LF and a fresh native recapture proves every oracle field unchanged; its manifest hashes the committed bytes. An inventory of all manifest-owned text inputs found no other worktree/index size mismatch. Arrival/pursuit guards, follower ordering, VM execution and 72-hour craft composition were also reviewed without another confirmed gameplay defect. Branch 4d is untouched.

Newborn-follower batch validation: all 2,793 root tests pass with zero failures/skips after the committed-byte checksum fix, including all manifests, all 19 private save classifications, ordinary/escort/craft 72-hour runs and unchanged population/allocation gates. Full formatting and code-map/diff checks pass; the pinned reference checkout is clean. Useful native captures and measurements remain; compiled probe objects were removed. No unexpected access denials occurred. Branch 4c remains open; branch 4d is untouched.

Branch 4c zero-radar newborn hunters: native Ufo::insideRadarRange cannot select a craft at range zero, but GeoscapeState::ufoHuntingAndEscorting evaluates attraction first. Mission preflight now validates candidates through the existing pure craft forecast before permitting these births alongside active craft. Random constructor modes validate both outcomes without RNG; proofs are tick-local and include mission, UFO type and mode. Twelve fresh/cache cases cover ordinary/escort births, exact links, reloads and atomic malformed-attraction refusal. All 71 focused newborn/escort checks pass. Reference: Ufo.cpp, Craft::getHunterKillerAttraction, GeoscapeState.cpp and strategic-hunting selectionRows. Positive-radar retarget and later-branch guards remain; branch 4d is untouched.

Branch 4c newborn hunter transfer timing: the no-target proof now includes pending craft only on an hourly tick when their remaining timer is at most one hour. Six fresh/cache cases exercise ordinary and escort births before the hour, with two-hour transfers and with due one-hour arrivals. Previously blocked safe births advance, pending timers and identities survive reload, and due arrivals still refuse atomically before mutation. All 77 focused newborn/escort tests pass. Native evidence: Transfer::advance, GeoscapeState::time1Hour and strategic-hunting incomingCraftRows. No scheduling or later-branch boundary changes; branch 4d is untouched.

Branch 4c incoming craft pursuit timing: saved idle escorts can attach to newborn leaders while potential craft pursuers remain in transit beyond this tick. Six fresh/cache cases compose early/late leader creation with pre-hour and two-hour transfers, exact native-backed movement, stable targets and transition reloads. Due one-hour arrivals retain the atomic pursuit forecast refusal, including automatic patrol craft. All 83 focused newborn/escort tests pass. Reference: Transfer::advance, GeoscapeState::time1Hour/ufoHuntingAndEscorting and existing incoming/movement rows. Craft pursuit and waypoint dependencies still guard affected transitions; branch 4d is untouched.

The zero-radar/transfer batch audit checked native candidate arithmetic before radar selection, random and negative constructor modes, mode-specific proof keys across immediate waves, tick-local invalidation, hourly delivery/checkup ordering and loaded-escort pursuit dependencies. Ten additional fresh/cache cases cover valid mode zero, overflow in mode one/negative modes, both random outcomes and a later immediate wave with a different mode. All 28 newborn checks pass; no further confirmed gameplay defect was found. Due-arrival, craft pursuit, waypoint, work-limit and later-branch guards remain. Branch 4c remains open; branch 4d is untouched.

Zero-radar/transfer cycle validation: all 2,827 root tests pass with zero failures/skips, including fixture checksums, all 19 private save classifications, composed 72-hour runs and unchanged population/allocation gates. Full formatting verification and code-map/diff checks pass. The pinned C++ reference remains clean; no new native artifacts need cleanup in this batch and useful earlier captures remain. No unexpected access denials occurred. Branch 4c remains open; branch 4d is untouched.

Branch 4c grounded auto-patrol newborn proofs: existing pure servicing/fuel forecasts now establish that a positive-radar newborn has no eligible craft when auto-patrol craft remain grounded this tick. Native maintenanceRows back four fresh/cache ordinary/escort cases; refuelling that actually launches a craft retains the retarget refusal. Forecast candidate scans are bounded and shared; zero-radar arithmetic validation remains unchanged. Guard fixtures explicitly distinguish native ready-at-arrival craft from refuel-triggered relaunch. All 97 focused newborn/escort checks pass. Reference: Craft::checkup/refuel, GeoscapeState half-hour servicing and ufoHuntingAndEscorting. Branch 4d is untouched.

Branch 4c grounded craft pursuit proofs: saved idle escorts can attach to newborn leaders when the shared servicing forecast leaves every potential pursuer grounded. Two fresh/cache native maintenance cases preserve exact source movement and stable links through reload; refuel-triggered launches retain atomic pursuit refusal. Hidden, ignored and damaged airborne craft still count as pursuers. Incoming guard fixtures use native arrival/refuel semantics explicitly. All 99 focused newborn/escort checks pass. Reference: Craft::checkup/refuel, Transfer::advance, GeoscapeState::ufoHuntingAndEscorting and maintenance/movement rows. Branch 4d is untouched.

Branch 4c shared-stock newborn acceptance: four fresh/cache ordinary/escort cases compose all 18 pinned sharedFuelRows with future hunter births. Exact fuel, statuses and remaining item stocks match native ordered servicing whenever both craft stay grounded; any actual launch retains an atomic retarget refusal. Reloads before/after birth and following ticks preserve actor identities and avoid duplicate effects. All four checks pass, and the broader hunting run passed its other 457 cases; its four shared fixture failures were corrected to represent zero stock by absence, retaining strict input validation. Reference: Craft::refuel, GeoscapeState ordered servicing and strategic-hunting sharedFuelRows. Branch 4d is untouched.

Grounded-craft batch audit: reviewed forecast ownership, candidate versus pursuer eligibility, native checkup/refuel launch ordering, shared stock consumption, tick-local proof invalidation, constructor-mode keys and unchanged work limits. No additional gameplay defect was confirmed. The first root run reported a patrol allocation-gate failure in a path without mission births; three isolated reruns, all 23 craft-operation tests and the unchanged full root rerun passed. The allocation threshold and measurement code remain unchanged, and the non-reproduced observation is retained here for future audits. All 2,837 root tests pass with zero failures/skips, including fixture checksums, 19 private save classifications, 72-hour scenarios and population/allocation gates. Branch 4c remains open; branch 4d is untouched.

Grounded-craft cycle closure: full formatting verification and code-map/diff checks pass. The pinned reference checkout remains clean. Useful previous native captures remain; this batch generated no additional native probes requiring cleanup. No unexpected access denials occurred. All three slices and their batch audit are committed; branch 4c remains open and 4d is untouched.

Branch 4c existing-base grounded craft forecasts: alien-base hunting now uses the shared pure servicing/fuel forecast before treating a grounded automatic patrol craft as airborne. Native strategic-hunting maintenanceRows back fresh/cache cases for incomplete refuelling and actual launches, with exact fuel/status, timer persistence and atomic immediate-mission refusal. All 27 focused base-hunting tests pass. Reference: GeoscapeState::baseHunting/time30Minutes, Craft::refuel/checkup. Branch 4d remains untouched.

Branch 4c incoming base-hunting craft forecasts: due hourly arrivals now use native checkup plus shared half-hour refuelling before hunting eligibility. Fresh/cache cases cover partial fuel, refuel-triggered launches, ready-at-arrival craft and future transfers; safe arrivals persist while unsupported immediate hunting remains atomic. All 29 focused base-hunting tests pass. Reference: Transfer::advance, Craft::checkup/refuel, GeoscapeState::time1Hour/baseHunting and strategic-hunting incomingCraftRows. Branch 4d remains untouched.

Branch 4c newborn objective-base hunting forecasts: creation-tick hunting now applies shared servicing and due-arrival eligibility instead of assuming any automatic patrol flag launches a craft. Fresh/cache native maintenance cases cover existing and incoming craft, safe base births, exact fuel/status/timer reloads and retained live-supply-source refusal after an actual launch. All 40 objective-base tests pass. Reference: GeoscapeState::baseHunting/time30Minutes, Craft::checkup/refuel, Transfer::advance and maintenanceRows. Branch 4d remains untouched.

Base-hunting craft batch audit: reviewed all three slices against native caller order, eligibility, due-transfer boundaries, shared-stock ownership, tick-local forecast reset and work limits. No confirmed defect found. All 2,843 root tests pass with zero failures/skips, including manifests, private save classifications and existing endurance/allocation gates. Branch 4c remains open and 4d untouched.

Branch 4c spatial newborn hunter proofs: ordinary positive-radar births may proceed when every eligible forecast craft is outside every bounded spawn origin. Inclusive native radar edges remain guarded, uncertain areas use conservative radii and checked work, and proofs include the wave index. Native capture adds distant and negative-range selection cases with all prior properties unchanged. Six focused checks pass for native selection, point/regional fresh/cache births, transition reloads and nearby refusal; fixtures assert their distinct compiled regions. Reference: Ufo::insideRadarRange, GeoscapeState::ufoHuntingAndEscorting, AlienMission::spawnUfo/getWaypoint. Branch 4d untouched.

Branch 4c spatial newborn escort proofs: positive-radar escorts use the native first saved non-hunter leader origin before testing craft separation. With no saved leader, prior immediate-wave births retain a source forecast guard. Fresh/cache cases place craft near nominal spawn origins but far from the stationary saved leader, verify stable escort identity and transition reloads, and retain atomic refusal near the actual leader. Guard fixtures now explicitly colocate eligible craft and leaders. Reference: AlienMission::spawnUfo and strategic-hunting spawnEscortRows/selectionRows. Branch 4d untouched.

Branch 4c negative-radar newborns: native negative ranges cannot select any craft, including colocated targets. Preflight now permits these ordinary/escort births while validating attraction for every possible constructor mode and retaining signed stat range checks. Fresh/cache cases cover minus one and int-min ranges, stable links/reloads and atomic attraction overflow; all 115 newborn/escort checks pass. Reference: Ufo::insideRadarRange and newly captured strategic-hunting selectionRows variant eight. Branch 4d untouched.

Spatial radar batch audit: reviewed inclusive radar boundaries, conservative region radii, actual saved leader relocation, prior-wave leader guards, signed range/mode validation, wave-specific proof keys, reset and bounded scans. No confirmed issue found in these three slices. All 2,855 root tests pass with zero failures/skips; committed fixture bytes match manifest hashes and prior native rows remain unchanged. Code-map/diff checks pass. An older base-hunting escort-fuel forecast risk is queued for a deterministic reproduction in the next cycle. Branch 4c remains open; 4d untouched.

Branch 4c existing-base ordered escort fuel: reproduced a preflight bypass with an undetectable leader refuelling/relaunching before its visible escort consumes fuel. The old saved-speed projection marked the escort low-fuel, then runtime entered unavailable immediate hunting after mutation. Escort candidates now use the shared ordered forecast; both fresh/cache reproductions stop atomically and all 31 base-hunting tests pass. Native evidence: Craft::getFuelConsumption/consumeFuel, GeoscapeState::time10Minutes/baseHunting and strategic-hunting escortFuelRows. Branch 4d untouched.

Branch 4c newborn base ordered escort fuel: reproduced the same hidden-leader relaunch mismatch during objective-base creation, where runtime reached unsupported live-source supply hunting after the base birth. Active escorts now use the shared ordered craft forecast for newborn-base eligibility as well. Both fresh/cache reproductions stop before mutation; all 73 existing/newborn base-hunting tests pass. Reference: GeoscapeState::baseHunting/time10Minutes, Craft fuel methods and strategic-hunting escortFuelRows. Branch 4d untouched.

Branch 4c zero-chance hunting compositions: existing and newborn bases skip speculative craft/stock forecasts when detection chance cannot generate a mission, retaining timer range checks and live native percent-zero draws. Fresh/cache cases reproduce the former false production-stock guard, compare actual servicing/production with an independent world-free control, verify persisted timers and keep positive-chance stochastic guards. All 77 base-hunting/objective-base tests pass. Reference: GeoscapeState::baseHunting, native RNG::percent behavior and strategic-alien-base-hunt-timer zeroChance row. Branch 4d untouched.

Ordered-fuel/zero-chance batch audit: confirmed and reproduced absent forecast entries when native zero-quantity production inputs remove an airborne escort before hunting. Existing/newborn base consumers now skip removed actors, including grounded entries, rather than throwing or falling back to deleted state. The audit also restored bounded craft-population accounting for zero-chance runtime scans, with a tick-local shared population bound. Fresh/cache removal/reload cases and positive/zero-chance work-limit cases pass; all 82 focused tests pass. Native evidence: Production::startItem, Target destructor/follower repairs, GeoscapeState::baseHunting and deletedCraftFollowerRows. These fixes are an additional audit commit. Branch 4c remains open; 4d untouched.

Three-cycle closure: all nine 4c slices and three batch audits are committed, including an additional commit for confirmed deleted-candidate and work-bound findings. All 2,868 root tests pass with zero failures/skips, including checksums, all 19 private save classifications, 72-hour scenarios and population/allocation gates. Full formatting verification and regenerated code-map/diff checks pass. The pinned C++ checkout is clean. Useful native source/executable/capture outputs remain; the rebuilt hunting object was removed. A self-caused overlapping test/build executable lock was reported and resolved by waiting; no sandbox access failure or host configuration/ACL change occurred. Branch 4c remains open; in-range newborn hunting and immediate UFO-generating base missions remain guarded. Branch 4d is untouched.

Branch 4c creation-time ordinary hunter pursuit: airborne births now use the shared servicing/production/fuel forecast and bounded spawn regions to prove that every possible eligible target remains beyond the first movement step. Runtime retains native saved-order attraction selection, random constructor modes, hunt speed, waypoint backup, revelation and alerts; preflight draws no RNG and allocates no actor identity. Fresh/cache fixtures compare pinned selectionRows/retargetRows, transition reloads, due craft arrivals and shared fuel relaunches. Colocated births retain the precise branch 5 combat refusal, and noAlert hunters reserve contact markers because hunting still reveals them. All 481 hunting/newborn/escort checks pass. Reference: AlienMission::spawnUfo, GeoscapeState::time30Minutes/time10Minutes/ufoHuntingAndEscorting/time5Seconds, Ufo hunting destination methods and Craft fuel/attraction. Escort candidate births and branch-wide closure remain subsequent work; 4d is untouched.

Branch 4c creation-time escort hunter pursuit: candidate-bearing escort births use the native first saved ordinary leader origin, then may switch to craft hunting while retaining their original trajectory waypoint. Combat reach uses initialized escort speed when huntSpeed is nonpositive, rather than the nominal trajectory speed. The pinned capture adds the negative hunt-speed transition with all previous rows/properties unchanged. Ten fresh/cache constructor-mode and retained-speed cases reproduce the old guard and verify targeting, movement, reloads and atomic combat refusal. Colocated servicing/arrival fixtures now retain the branch 5 reason. Prior immediate waves without a saved leader remain a specific source-forecast boundary. Reference: AlienMission::spawnUfo escort initialization, Ufo::setTargetedXcomCraft/setEscortedUfo and GeoscapeState retarget/movement ordering. Branch 4d remains untouched.

Branch 4c loaded escort craft dependencies: attaching a saved idle escort to a newborn leader no longer blocks unrelated airborne, ignored/hidden, incoming or refuel-relaunched craft. A bounded iterative walk identifies direct and transitive UFO escort dependencies, including cycles, before applying the existing pure production/fuel forecast to affected pursuers. Deleted or returning actors cannot resurrect a dependency; actual active pursuit retains its specific creation-tick boundary. Eight fresh/cache chain/cycle/patrol fixtures reproduce the false guard; existing hidden/ignored and hourly/refuel cases now verify both permitted independence and atomic dependent refusals. All 77 escort compatibility checks pass with transition reloads. Reference: GeoscapeState::ufoHuntingAndEscorting/time5Seconds, Craft::think and existing spawnEscortSequenceRows/movement fixtures. Ordinary waypoint arrival and affected craft pursuit forecasts remain subsequent work; 4d is untouched.

Creation-time hunting batch audit: confirmed and reproduced proof reuse across two immediate births in the same wave. The first possible ordinary birth can initialize a later escort at full speed; its predecessor's no-leader trajectory-speed proof is insufficient. Tick-local proofs now include the within-wave UFO counter as well as mission, wave, type and mode, and both fresh/cache regressions refuse before mutation. Reviewed conservative spherical bounds, inclusive radar versus combat reach, nonpositive hunt-speed preservation, all constructor modes, noAlert marker capacity, saved-order targeting, follower chains/cycles and forecast purity. All 2,898 root tests pass with zero failures/skips after the fix, including native checksums, all 19 private save classifications, endurance and populated allocation gates. Code-map and diff checks pass; committed fixture bytes match the manifest. The native object was removed, while useful capture/source/executable artifacts remain and the pinned reference is clean. Branch 4c remains open; 4d untouched.

Branch 4c immediate escort source envelopes: creation-time hunting now includes first possible ordinary leaders from earlier validated waves and births in the same wave. Saved-order selection retains earlier stochastic origins until the first guaranteed ordinary birth; the no-leader outcome preserves nominal position/speed. Bounded region envelopes and both initialization-speed outcomes are proved without RNG or identities. Eight fresh/cache point/regional cases verify retargeting from a prior leader beyond the nominal radar footprint; 32 deterministic seeds in each cache case cover all four two-birth constructor outcomes, marker capacity and reloads. The previous audit regression still refuses atomically, now with the precise combat reason. All 155 newborn/escort checks pass. Reference: AlienMission::spawnUfo, Ufo initialization/retarget methods and pinned spawnEscortSequenceRows/constructorRows. Branch 4d untouched.

Branch 4c newborn leader pursuit: direct and transitive craft pursuers may advance when conservative movement envelopes prove they cannot reach a saved escort after its creation-tick attachment. The proof includes both idle/full-speed outcomes and bounds the reference latitude/longitude Euler step, falling back to the whole globe near a pole; it draws no RNG or identities. Four fresh/cache distant-pursuit cases verify actual craft movement, stable target coordinates and transition reloads, while colocated dependencies remain atomic refusals. All 81 escort compatibility checks and 12 focused pursuit cases pass. Reference: GeoscapeState::time5Seconds/ufoHuntingAndEscorting, Craft::think, MovingTarget::move and pinned escort spawn/movement traces. Branch 4d untouched.

Branch 4c cancelled idle-escort arrivals: a bounded flight-only projection identifies the first guaranteed ordinary point leader in a due score/space mission. Saved escorts that would otherwise reach an ordinary, terminal or landing waypoint now forecast the native attachment before all arrival, follower and craft checks, without allocating future identities or changing radar script inputs. Six fresh/cache arrival cases and four added ordered-follower cases match existing pinned attachment/movement evidence with transition reloads. Uncertain constructor/area cases retain precise guards, and a cancelled terminal arrival cannot hide same-tick craft interception. All 96 escort checks pass. Reference: AlienMission::think/spawnUfo, GeoscapeState::ufoHuntingAndEscorting/time5Seconds, Ufo::setEscortedUfo and MovingTarget movement. Branch 4d untouched.

Escort-source/pursuit/arrival batch audit: reviewed first-leader constructor/order outcomes, flight-only projection lifetime, radar script separation, original waypoint backup, terminal fallthrough, craft interception and conservative Euler/pole bounds. A suspected prior-birth overflow was disproved: centralized mission-counter preflight already refuses it before mutation; two fresh/cache regressions record that boundary. No confirmed gameplay defect remained. All 2,929 root tests pass with zero failures/skips, including native checksums, all 19 private-save classifications, endurance and populated allocation gates. Committed hunting fixture bytes match their manifest; code-map/diff checks pass and the pinned C++ checkout is clean. Branch 4c remains open; 4d untouched.

Branch 4c deterministic point-leader pursuit: the flight-only attachment projection now applies to every eligible saved idle escort, beyond cancelled waypoint arrivals. Direct/transitive stationary pursuers remain safely separated from the moved target; nearby fast craft use the precise branch 5 interception guard. Hidden/ignored pursuers retain live links, and radar loss reports the actual moved source before switching the craft to patrol. Eight new fresh/cache cases cover stationary pursuit, contact-loss reloads and uncertain-leader atomic refusal. All 106 escort checks pass; the unchanged work limit now refuses a large future formation in the earlier escort-position preflight. Reference: native retarget/list-order movement and Craft contact-loss handling at the pinned commit. Branch 4d untouched.

Branch 4c base-origin leader forecasts: guaranteed ordinary leaders now project linked alien-base position overrides for regional and Earth operations. The shared origin helper retains native first-leader order and race-specific escort speed. Four fresh/cache fixtures exercise both operation modes with both races, actual pursuit coordinates and reloads; the same cases verify daily source self-destruction interrupts the mission, consumes no future UFO identity and creates no phantom leader. All 139 escort/operation-base checks and four expanded transition cases pass. Evidence: strategic-mission-origin rows and AlienMission::spawnUfo ordinary base override at the pinned reference. Branch 4d untouched.

Branch 4c objective-aware first leaders: flight projections now cover ordinary births in infiltration, base, site and supply missions as well as score missions. Suppressed no-object supply waves are skipped, and objective supply constructors force the ordinary-leader outcome even with a 100-percent hunter rule. Retaliation remains outside this proof. Twelve new fresh/cache cases exercise all four objectives, suppressed versus immediate later supply leaders, stable links, untouched site/base state and transition reloads. The 134 escort/objective checks and four added suppression cases pass. Evidence: AlienMission::spawnUfo/think, strategic-mission-origin supplyRows and existing constructor/escort movement traces. Branch 4d untouched.

Point/base/objective leader batch audit: reviewed deterministic versus uncertain constructors, first-leader order, source overrides and daily deletion, forced/suppressed supply births, terminal fallthrough, craft interception and bounded work. Two added fresh/cache VM regressions verify half-hour detection reads idle state before attachment and later reads persisted escort state; future flight proofs never appear in script inputs or saves. No confirmed gameplay defect was found. All 2,953 root tests pass with zero failures/skips, followed by both added VM cases, including existing private corpus/endurance/allocation/checksum gates. Code-map/diff checks pass and the pinned reference is clean. Branch 4c remains open; 4d untouched.

Branch 4c production price time forecasts: the existing read-only price VM now runs in hourly production preflight with the next tick's time and day count, unchanged campaign difficulty/month count and native before/current/after script order. VM instructions share the one-million-work budget with production, and event counts retain the existing cap. Eight fresh/cache item/global VM cases compare controlled native caller-context accounting traces at hourly/daily boundaries and reload before/after delivery. Daily research queries retain a precise refusal pending the next slice. Evidence: RuleItem::getSellCostAdjusted, Production::step and SavedGame time/day/month/difficulty bindings at the pinned reference; the native fixture explicitly uses controlled pure script callbacks rather than claiming C++ VM execution. Branch 4d untouched.
Branch 4c production price research forecasts: actually executed isResearched queries now use bounded guaranteed/possible post-daily research sets. Known completions and unchanged answers execute; consumed uncertain random-reward answers refuse before mutation. Null references and unexecuted query branches keep native false/control-flow behavior. The native context fixture now covers research with actual C# VM execution for item/global events, debug-mode exclusion and daily completion ordering; eight further fresh/cache cases verify uncertainty, unrelated/dead/null queries, transition reloads and RNG equivalence against the same world with a constant callback. Evidence: SavedGame::isResearchedScript and GeoscapeState daily research before Production::step at the pinned reference. Branch 4d untouched.
Branch 4c shared scripted-sale acceptance: ordinary production now validates prices in the future hourly/daily context before mutation, reserves exact read-only scripted accounting values and bounds repeated VM delivery work across requested units. This closes a reproduced world-free divide-by-zero after time/progress mutation and a repeated-script budget gap. Ordered global before/current/after price callbacks now compose with paid continuation, shared/cross-base funds, delivery delays, refuelling and hunter targeting against the existing native sale traces. Eight failure/work-limit cases and four near-64-bit accounting cases cover fresh/cache hunting and ordinary campaigns; existing 14 overflow refusals remain intact. Evidence: RuleItem::getSellCostAdjusted, Production::step and SavedGame::setFunds at the pinned reference. Branch 4d untouched.
Production price batch audit: reviewed native price inputs, null/debug research semantics, daily/hourly fallthrough, before/current/after event order, shared accounting, scalar widths and aggregate VM/research work. Four additional fresh/cache regressions prove inactive projects never invoke their price script and a due midnight caller uses the valid future branch rather than today's failing branch. No further confirmed defect was found. All 2,983 root tests pass with zero failures/skips, plus four added audit cases; committed native fixture checksums and captured output match, code-map/diff checks pass and the pinned C++ checkout remains clean. The disposable native object was removed while the useful capture/executable were retained. Branch 4c remains open; 4d untouched.
Branch 4c ordinary craft consumption before base hunting: loaded alien-base preflight now consults bounded production removal types and the shared post-production craft forecast for affected independent airborne craft. Deleted craft no longer trigger an unavailable hunting-mission guard; surviving craft retain it. Four new fresh/cache independent-craft variants reproduce the stale actor and verify consumed/surviving outcomes, native hunt timer progression and transition reloads; all 40 alien-base hunting checks pass. Evidence: Production::startItem craft removal followed by GeoscapeState::baseHunting/updateActiveCrafts, pinned production input/deletion and alien-base hunt timer traces. Unrelated types keep the local fuel path. Branch 4d untouched.
Branch 4c newborn-base craft consumption: creation-tick base hunting now shares the bounded production removal-type proof used by loaded bases. Independent airborne craft removed by an earlier production continuation cannot generate phantom hunting missions or reserve their counters; surviving craft retain precise supply/source boundaries. Four new fresh/cache variants reproduce and close the stale ordinary-craft guard, verify the newborn timer and transition reloads, and pass with all 90 loaded/newborn base hunting checks. Evidence: GeoscapeState::time1Hour production precedes time30Minutes base creation and time10Minutes::baseHunting; pinned base-objective, craft consumption and hunting timer fixtures. Branch 4d untouched.
Branch 4c correlated first-leader origins: a saved idle escort may now use a flight-only proof when stochastic earlier ordinary constructors and the first guaranteed later ordinary birth all share one exact point. The proof preserves native first-leader order without choosing identities or drawing RNG; differing points and a chain without a guaranteed leader retain precise guards. Four new native retarget traces pin same/different origins and both earlier constructor outcomes. Four fresh/cache composed cases use 32 persisted seeds, verify both outcomes, stationary pursuit coordinates and reloads; all 128 escort checks pass. Evidence: AlienMission::spawnUfo, constructor flags, GeoscapeState::ufoHuntingAndEscorting and existing ordered movement fixtures at the pinned reference. Branch 4d untouched.
Craft consumption/correlated leader batch audit: reviewed native production-before-hunting order, affected-type scoping, shared forecast lifetime, surviving and deleted candidates, newborn timer/counter reservations, stochastic first-leader ordering and guaranteed point proofs. Two additional fresh/cache cases exercise all 32 seeds with a fast pursuer and retain the precise atomic branch 5 interception guard. No additional confirmed defect was found. All 3,001 root tests pass with zero failures/skips, including private classifications, endurance/allocation and checksum gates. Committed native inputs match their manifest and capture; code-map/diff checks pass, the pinned reference is clean and the disposable native object was removed. Branch 4c remains open; 4d untouched.
Branch 4c exact daily research discoveries: the shared research preflight can now return an immutable completed-topic set while bypassing its stock-only shortcut when discoveries are requested. It preserves ordered deterministic bonuses, protected/sequential rewards, lookup and automatic discovery, disabling/re-enabling and cross-base cancellation without RNG or campaign mutation. Six fresh/cache fixture cases cover all pinned reward/discovery rows plus a stock-free completion, compare projected sets with actual daily progression and reload transitions; all pass. Evidence: GeoscapeState::time1Day and SavedGame::selectGetOneFree/addFinishedResearch at 4df3a5e571a1a4b5e8a46d3161fb2e21a2adba15, strategic-hunting researchRewardRows/researchDiscoveryRows. Price callers are connected in the next slice; genuine random alternatives retain the existing refusal. Branch 4d untouched.

Branch 4c exact research-dependent prices: consumed daily isResearched queries now use the shared exact completed-topic projection before the existing guaranteed/possible fallback. Deterministic free/protected/sequential rewards, duplicate candidates, automatic/lookup discoveries and disabling/re-enabling execute in actual post-daily order; genuine alternatives still refuse atomically. Projection work and immutable-set creation share the production budget, and repeated queries reuse the result. Six new fresh/cache item/global price cases compose all native reward and discovery rows, verify accounting, unchanged RNG against constant callbacks and transition reloads; all 16 focused price/discovery/uncertainty checks pass. Evidence: SavedGame::isResearchedScript/selectGetOneFree/addFinishedResearch, GeoscapeState daily-before-hourly ordering and the pinned strategic-hunting research fixtures. Branch 4d untouched.

Branch 4c research metadata price providers: buy/sell price VMs now expose the four reference-registered RuleResearch getters for cost, points, lookup and needed item. Immutable linked metadata retains null outputs and signed scalar widths; item and research pointers are canonical across lookups and item inputs, fixing a reproduced pointer-comparison failure. Twenty-four newly captured native getter traces import the pinned methods with collaborator field renaming and explicitly exclude the C++ VM; prior oracle rows are unchanged. Four fresh/cache item/global VM cases exercise every trace, pointer identity, hourly hunting composition and transition reloads; all 20 focused getter/research price checks pass. Evidence: RuleResearch.cpp/h ScriptRegister/getters, RuleResearch::afterLoad, Mod rule readers and the verified script API catalog at the pinned reference. Branch 4d untouched.

Research-price batch audit: reviewed ordered daily discoveries/rewards, exact and conservative research answers, forecast purity and work limits, linked metadata, native null/default/sentinel rules, pointer identity and before/current/after price execution. Confirmed and reproduced two additional provider defects: repeated getTime calls boxed different pointers and getRuleItem returned a defined STR_NONE item. A shared per-invocation clock reference and native sentinel exclusion fix both; eight fresh/cache item/global regressions and two extended atomic budget cases pass. Two new pinned clock-pointer traces preserve all earlier oracle rows. All 3,021 root tests pass with zero failures/skips, including private classifications, endurance/allocation and checksum gates; full formatting, code-map/diff and native input/output checks pass. The pinned C++ checkout is clean and the disposable native object was removed. The original timed window elapsed during interruption; this finishes the announced sixth cycle and stops. Branch 4c remains open; 4d untouched.

Branch 4c price item metadata: production and current price VMs now share the existing immutable RuleItem scalar readers and ordered category queries, including native signed values, defaults and nulls. Current-price callers use the same bounded before/current/after execution as future callers, and category scans charge the common work budget. Eight fresh/cache item/global VM scenarios exercise pinned itemReaders/category replacement fixtures, actual hunting and save transitions. Evidence: RuleItem.cpp ScriptRegister/readers/hasCategoryScript and strategic-detection-scripts at the pinned reference. Branch 4d untouched.

Branch 4c price tags: read-only price providers now expose typed RuleItem, RuleResearch, RuleMod and saved GeoscapeGame values, including null/missing values and native text/tag declaration copies. Lazy tag initialization and metadata registration charge the shared price work budget; no world state or RNG is mutated. Four fresh/cache item/global VM scenarios use verified native tag/value-copy fixtures and saved campaign tags through hunting and transition reloads; all 12 metadata checks pass. Evidence: ScriptValuesBase::getBase and RuleItem/RuleResearch/Mod/SavedGame ScriptRegister at the pinned reference. Branch 4d untouched.

- Phase 6 / 4c: price scripts now read native Mod view/darkness settings and signed smoke reduction through immutable settings. Null member getters return zero; only consumed static smoke calls with a null Mod stop before mutation. Pinned Mod.cpp and strategic-detection-scripts modSettings/smokeReduction fixtures cover fresh/cache, item/global events and reloads.

- Phase 6 / 4c: bounded price scripts now resolve armor/skill rules, preserve native text identity (including RuleSoldier.getType's skill receiver), armor vision/stat defaults, canonical pointers and typed rule tags. Evidence: pinned Mod.cpp, Armor.cpp, RuleSkill.cpp and RuleSoldier.cpp; strategic-detection-scripts/value-copy traces; fresh/cache item/global price execution with transition reloads.

- Phase 6 / 4c: price scripts now resolve native Unit and Soldier lookups, spawn/live-alien/civilian-recovery links, all immutable soldier/armor stat groups and Soldier tags. Linked items retain the same pointer identity as Mod lookups; special recovery sentinels and signed stat widths follow pinned Unit.cpp/RuleSoldier.cpp/Mod.cpp and strategic-detection-scripts traces. Fresh/cache item/global cases reload before and after production.

- Phase 6 / 4c: price scripts now execute generic/conventional Inventory lookups, id/type/hand readers and movement costs. Native null-source/self-move short circuits, signed costs and pointer identity are preserved; consumed missing roles/costs or null destinations block transactionally. Evidence: pinned Mod.cpp/Mod.h/RuleInventory.cpp plus strategic-detection-scripts/inventory-cost traces, fresh/cache item/global runs and reload guards.

- Phase 6 / 4c: price scripts now expose native Item damage/melee profile pointers and five damage classification readers, preserving separate profile identity, null defaults and single-target semantics. Evidence: pinned RuleItem.cpp ScriptRegister/wrappers and all strategic-detection-damage classification rows; fresh/cache item/global VMs with hunting and reloads. The first two batches passed 3,045 and 3,077 root tests respectively, full formatting and code-map/checksum gates; their audits found no confirmed defects. No host/access issue occurred; 4d remains untouched.

- Phase 6 / 4c: bounded price VMs now execute eleven deterministic damage conversions and armor percentage reads using shared native float semantics. All 65 conversion rows, rounding/boundary rows, null defaults and consumed-only unsafe conversions are covered across fresh/cache item/global scripts and save transitions. Evidence: pinned RuleItem.cpp getDamageToScript/getArmorEffectivenessScript and strategic-detection-damage; no random draws or tactical effects are enabled.

- Phase 6 / 4c: native random damage calls now short-circuit safely when the profile or RandomState is null, without reading unsafe power/multipliers or consuming RNG; nonnull random execution remains precisely unavailable. Four fresh/cache item/global cases cover typed state aliases, current sale quotes, production, RNG equivalence and reloads. Four more cases prove current and forecast price execution is bounded across before/current/after programs (each individually fits), refusing atomically on aggregate exhaustion. Evidence: pinned RuleItem.cpp getRandomDamageScript and verified nullRandomDamage/unusedRandomCalls traces.

Three-cycle price-provider batch audit: nine implementation slices completed and committed. Reviewed immutable/null metadata, typed tags and saved values, canonical pointers, event order/current/future context, signed smoke arithmetic, float conversion bounds, RNG purity and aggregate VM work. No confirmed implementation defect was found. Four additional fresh/cache item/global purchase cases close current buy-price coverage, including before/current/after ordering and snapshot purity. All 3,105 root tests pass with zero failures/skips, including native fixture checksums, private classifications, endurance and allocation gates. Full formatting, code-map and diff checks pass; the pinned reference checkout is clean. Generated artifacts were reviewed; reusable build output and existing captures remain useful. No unexpected access denial occurred. Branch 4c remains open for remaining mission-generation/uncertainty projections and final headless acceptance; branch 4d is untouched.

Branch 4c random-production envelopes: pinned Production::step now has 48 inert-variant traces covering shared fuel, weighted selection, zero quantities, sales, delayed/immediate delivery and history. A bounded pure quantity/presence envelope reserves possible output without RNG and permits only caller-approved variations; missing zero keys remain distinct effects. Focused unit fixtures cover common quantities, reordering, zero-weight exclusion and shared work exhaustion. Live forecast integration follows in this cycle; 4d untouched.

Branch 4c immediate inert random production: hunting forecasts now accept unequal weighted output sets when differing items cannot affect fuel, reuse, captive counts or any active manufacturing input. Common fuel/material quantities remain exact; inert quantities are shadow-only upper bounds. Actual RNG, chosen stock and history remain untouched by preflight. Forty-eight pinned Production::step traces, fresh/cache seeded outcome coverage, transition reloads and atomic sensitive/overflow guards back the change. Delayed variants follow next; 4d untouched.

Branch 4c delayed inert random production: equal-cardinality weighted variants now participate in hunting forecasts, with per-item arrival ceilings and actual transfer counts rather than union-key counts. Pending arrivals, zero quantities and transfer-id exhaustion are validated before mutation; varying delivery cardinality stays precisely guarded. All 48 pinned native variants run through fresh/cache scenarios and transition reloads. Evidence: Production::step weighted delivery and Transfer::advance at the pinned reference; 4d untouched.

Random-production batch audit: all 3,128 root tests passed with zero failures/skips, including checksums, private classifications, endurance and allocation gates. The new envelope helper accepted equal quantities with reordered protected effects; its boundary now explicitly retains effect order, with a focused regression. Loaded rules canonicalize map order, so an attempted live reproduction did not establish a current campaign defect; the invalid reproduction was removed rather than weakening its assertion. Existing 48 native variants still pass. Further validation is recorded at the following cycle close; 4d untouched.

Branch 4c price country funding: bounded current/future price VMs now execute native saved-order Country lists, null/empty/nested lists, canonical Country-to-RuleCountry links, current funding and signed funding caps. Funding remains stable before the existing integrated month guard. Verified strategic-detection-scripts countryList/countryRules traces plus fresh/cache item/global scripts and transition reloads provide executable evidence. Reference: SavedGame::ScriptRegister/filterCountryConstScript and BindListInitImpl/BindListLoopImpl, Country::ScriptRegister/getCurrentFunding/getRules and RuleCountry::getFundingCap. 4d untouched.

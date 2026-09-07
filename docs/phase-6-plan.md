# Phase 6 — Playable strategic campaign implementation plan

Plan date: 2026-09-05. Baseline: `752e976` on `main`.
Status: branch 1 implemented with bounded acceptance documented in
[strategic logistics status](strategic-logistics-status.md); branches 2–6 remain planned.
This is not a Phase 6 completion claim.

## Goal and scope

Deliver a strategic campaign that can be operated through a usable indexed UI and
the same headless command API: manage bases and personnel, supply and equip craft,
research and manufacture, advance the world, intercept targets, and commit a validated
tactical deployment request. All implemented transitions must survive save/reload.

Phase 5 satisfies its bounded campaign-foundation gate. It does not provide executable
behavior for every preserved save node. Currently soldier/craft state is principally
identity, campaign commands place the starting base or advance the calendar, and
unimplemented strategic/battle fields survive through an opaque persistence sidecar.
See [foundation status](campaign-foundation-status.md),
[gameplay matrix](compatibility/gameplay.md), and [save matrix](compatibility/saves.md).

Phase 6 owns strategic rules and decisions, including OXCE-specific variants used by
the targeted reference. It includes strategic training, transformations, mission/arc/
event scheduling, and strategic campaign endings; these must not disappear behind a
vanilla-only acceptance gate. Battle generation, tactical execution, battle-derived
progression/recovery/debriefing, and an actual final-mission victory remain Phase 7.
Full diaries/graphs/Ufopaedia presentation and media polish remain Phase 8; the underlying
strategic data and necessary action feedback belong here.

The Phase 6 launch boundary is an executable, persisted deployment request containing
validated strategic participants and reference-derived deployment inputs. A headless
consumer verifies it; the UI clearly identifies that tactical execution is unavailable.
This is the strategic half of launch, not a playable battle or a fabricated compatible
`battleGame`. The broader roadmap's playable launch gate closes only when Phase 7's
first generator consumes it. Keep that distinction in status reports and matrix rows.

## Delivery shape

Use **six sequential feature branches**, each from the previous merged `main`.
No separate prerequisite, serializer, scripting, UI, or final-audit branch is planned.
Prerequisites lead branch 1; every branch carries its own integration and closure work.

| Order | Branch | Playable acceptance boundary | Planned commits |
|---|---|---|---:|
| 1 | `codex/strategic-base-logistics` | Inspect stores, buy/sell/hire, and deliver or transfer supplies and personnel safely | 7 |
| 2 | `codex/strategic-base-readiness` | Build/manage bases, develop personnel, equip craft, and complete servicing | 7 |
| 3 | `codex/strategic-research-production` | Complete research and production chains with compatible unlocks and staff allocation | 6 |
| 4 | `codex/strategic-world-simulation` | Generate missions, move targets, detect activity, and dispatch/recall craft | 6 |
| 5 | `codex/strategic-interception-deployment` | Resolve interceptions and commit/reload a validated deployment handoff | 6 |
| 6 | `codex/strategic-campaign-cycle` | Complete monthly evaluation, campaign events/endings, and integrated strategic closure | 6 |

Commit counts are review units, not quotas. Combine adjacent small commits, or split a
large one locally without adding PRs. Keep commits buildable; pair each behavior with
its focused tests. A fixture-capture commit can introduce an oracle without checking in
a failing test. Final UI/scenario commits integrate earlier tested commands rather than
being the first time those commands are exercised. Split a branch only if an unforeseen
subsystem cannot be reviewed coherently or requires an independently useful long delay;
record the revised scope before starting another branch.

Create branch 1 before committing changes. Its first commit records this reviewed
implementation plan and the roadmap link; the seven implementation units below follow
that planning commit. Keep the plan in branch 1 rather than opening a planning-only PR.

## Work required before the first new gameplay action

These are the opening commits of branch 1, not another preparatory phase.

1. **Pin evidence and enumerate ownership.** Verify the reference checkout commit,
   retain existing backward-save fixtures, and capture small redistributable strategic
   scenarios. Inventory each affected rule property, save node, script binding/event,
   option, and UI decision; assign every omission to one of the six branches or an
   explicit Phase 7/8 boundary. Track defaults, widths, ordering, errors, and fixture IDs.
   Expand the existing matrices; do not mark a typed property as executable gameplay.
2. **Execute time effects during simulation.** `GeoscapeState::timeAdvance` dispatches
   month -> day -> hour -> thirty minutes -> ten minutes -> five seconds through
   fallthrough on a month boundary. The current C# summary counts exclusive trigger
   categories after advancing; replaying that summary cannot implement dependent
   mutations. Add an ordered single-writer dispatcher with actual interruption/stop
   semantics, bounded command work, and safe notification delivery. Preserve the
   existing summary contract or explicitly version it. Test boundary ordering, pauses,
   partial advancement, and repeated small advances versus a large advance with the
   same scripted random choices, speed/options, and notification acknowledgements.
   A popup pause stops the next five-second tick; it does not cancel the current tick's
   remaining fallthrough handlers. Preserve handler-local early returns and distinguish
   committed notifications from missing-capability stops. Preserve reference behavior
   that depends on selected speed (including hunter/killer retargeting); command batching
   is equivalent only when that simulation context is unchanged.
   Preserve the allocation gate for the empty workload;
   establish separate measurements for populated workloads.
3. **Make persistence ownership mobile.** Index unknown entity fields by campaign-wide
   soldier ID and reference-compatible craft identity across bases and transfer
   containers. Define transfer identity/matching where the external schema lacks IDs;
   do not infer identity from mutable list position. Save-owned indexes must distinguish
   moved, deleted, and recreated entities. Restore the complete graph before publishing;
   duplicate ownership and dangling references fail intentionally. Test A -> transit ->
   B, reorder, deletion, legacy missing fields, and repeated rewrite/reload.
4. **Guard partial-model operation.** Distinguish inspect/preserve support from playable
   support. An imported active battle or unresolved behavior required by an action must
   block that action with a useful diagnostic, not silently progress opaque state.
   Add a capability/preflight check at the command boundary. Broaden eligibility as
   each branch installs its providers and state. Do not reject harmless unknown fields
   simply because they are unknown.
   This also applies to newly created campaigns and future effects with no live entity
   yet, such as mission generation and monthly funding. Define each branch's playable
   time horizon and stop before a tick requiring missing behavior, without partially
   applying that tick or advancing past it. Recheck eligibility as state changes.
   Where an unsupported outcome depends on scripts or randomness within a tick, guard
   the producing path conservatively until its outcomes are supported; do not run scripts
   speculatively, rewind random choices, or invent rollback to discover eligibility.
   Isolated dispatcher/handler fixtures may test later boundaries with explicit test
   providers, but cannot establish that a full imported campaign can cross them.

No startup rewrite, native SDL artifact cache, framework replacement, general ECS,
parallel simulation, new scripting language, or additional host tooling is required.
Warm startup remains above target, predominantly in deserialization; keep the measured
backlog and revisit if expanded projections cause a material regression. Correctness,
bounded state, and an operable campaign take precedence over that independent target.

## Shared implementation contract

- `Oxce.Mods` owns immutable runtime rule projections and generation-scoped links;
  `Oxce.Gameplay` owns mutable state, commands, queries, simulation, and save-neutral
  capture/restore. `Oxce.Savegames` owns schema/defaults/unknown-field overlays and
  atomic files. UI orchestration lives in Engine/App; native calls stay in the SDL host.
- Inspect exact C++ methods before implementing each behavior. Source lists below are
  investigation entry points, not claims that every listed method has been audited.
  Normative revision: `4df3a5e571a1a4b5e8a46d3161fb2e21a2adba15` in the separate
  read-only reference checkout. Existing private `ab5041e` saves remain migration inputs.
- Each state-owning commit includes snapshot/restore and adapter coverage. Promote
  formerly opaque fields explicitly and ensure overlays cannot restore stale values.
  Persist IDs rather than runtime handles, and resolve cycles/references transactionally.
- Each new rule decision includes the owning OXCE host binding/event at its real call
  site, script-value ownership, reference ordering, and malformed-input tests. Do not
  defer a required provider to a generic late scripting pass. Commands validate before
  mutation where reference semantics permit; do not invent rollback across script
  calls or alter reference-visible partial/failure behavior.
- Reuse prepared script frames and injected `IRandomSource`. Compare eligibility,
  thresholds, order, bounds, and legal outcomes; exact C++ RNG stream parity is excluded.
- Purchase/research/mission eligibility dependencies arrive when first consumed. Branch
  1 must read the relevant completed-research and option state even though project
  progression lands in branch 3. Apply the same rule to costs, capacities, calendars,
  entity creation and immediate arrival effects. Partial models may preserve untouched
  imported fields; newly created entities need real initial values and any field read
  or changed by an enabled action needs an explicit owner. Do not fill those gaps with
  placeholders or defer random generation until a later load or branch.
- Keep routine UI queries separate from save capture. Every branch exposes usable
  localized labels, quantities/status, unavailable-action reasons, confirmation where
  the action needs it, and completion/failure feedback. Include keyboard operation,
  simulation pause/speed behavior, and save/load access as they become relevant.
- Extend managed capability contracts only for actual use, with versioning and no
  implementation types. Integrate live extension save state no later than branch 5,
  or earlier if a state-owning extension first participates; required missing extension
  state cannot be dropped. Tactical AI extension capabilities stay in Phase 7.
- Update compatibility matrices and compiler/cache revisions when projections or
  reconstruction semantics change. Test both fresh and restored content. No package
  additions are assumed; any necessary dependency receives the repository review.

## Branch 1 — Strategic base logistics

**Scope:** prerequisite simulation/persistence corrections plus inventory, transactions,
recruitment/dismissal, purchases and inter-base transfers. Own initial soldier generation
and purchased-craft initialization, plus enough state for transfer, dismissal/sale and
arrival effects. Untouched imported entity fields may remain in mobile sidecars; richer
readiness behavior follows in branch 2 without regenerating existing entities.

**Reference entry points:** `Geoscape/GeoscapeState.cpp` time dispatch and arrival logic;
`Basescape/PurchaseState.cpp`, `SellState.cpp`, `TransferItemsState.cpp`,
`TransferConfirmState.cpp`, `SackSoldierState.cpp`; `Savegame/Base.cpp`, `Transfer.cpp`,
`ItemContainer.cpp`, `Soldier.cpp`, `Craft.cpp`, `SavedGame.cpp`; `Mod/Mod.cpp::genSoldier`,
soldier name pools and corresponding Mod rules. Include `SavedGame::addMonth`, purchase
limit logs, `Craft::initFixedWeapons` and `Craft::checkup` in the logistics evidence.

**Commit purposes, in order:**

1. Capture logistics and boundary-time reference fixtures; add Phase 6 ownership and
   fixture ledger, including imported-playability classifications.
2. Implement ordered in-simulation trigger dispatch, bounded advancement and interruption;
   preserve query/event consumers and add action preflight for unsupported live state.
3. Preserve mobile entity sidecars through transfer containers and cross-base rewrites;
   add graph validation and save-neutral ownership transitions with persistence tests.
4. Implement stock/funds transactions and eligibility projections: buy/sell quantities,
   prices, required research, storage/containment/hangar/quarters rules, and finite limits.
   Own monthly purchase/hire logs, save/reload and their reset in `SavedGame::addMonth`
   order now; branch 6 integrates this hook without resetting twice.
5. Implement recruitment/dismissal and transfer lifecycle: costs, travel time, staff,
   soldiers, craft/cargo, arrival notifications, and reference-defined failure conditions.
   Generate soldier stats, nationality/name, default armor and required initial state at
   recruitment, including duplicate-name handling and `spawnedSoldierTemplate` application
   in reference order; persist them before transit. Reuse generation for new-campaign
   personnel with the starting-template caller's own semantics. Initialize purchased
   craft and fixed weapons, and implement arrival base reassignment and `checkup` status/
   rearming effects. Branch 2 owns subsequent service progression and loadout editing.
   Capture transfer-specific distance/cost arithmetic here; do not substitute the later
   globe movement distance formula. Model assignment/cargo changes required by enabled
   sale, dismissal or transfer paths even where assignment editing remains branch 2.
6. Add stores, purchase/sale and transfer UI with current totals, costs, arrival times,
   legal quantity limits, and save/load during transit; expose the same headless commands.
7. Close logistics integration: cached/fresh corpus scenarios, unknown-field conservation,
   malformed/overflow tests, measurements and matrix/status updates.

**Acceptance:** eligible new and imported UFO/TFTD/modded states can buy, sell and receive supplies;
a two-base fixture moves an entity through transit and reload without losing its unknown
fields or duplicating inventory. Funds and quantities follow reference widths/rounding.
Insufficient funds, unavailable purchases and capacity violations match reference action
rules. Recruit vanilla and template-defined modded soldiers, save/reload during transit,
and verify initial stats, name, armor and template state survive arrival without rerolls.
Cover purchased craft fixed weapons and arrival status, and purchase-limit persistence/
reset. Test arrival at hour/day/month boundaries and notification fallthrough in isolated
reference fixtures; full campaign scenarios must respect the preflight time horizon until
all required timed systems exist. Include a blocked-boundary scenario proving no partial
tick mutation. Two-base fixtures exercise transfer before additional-base UI arrives in
branch 2. Report corpus eligibility and stopping reasons, not blanket imported playability.

## Branch 2 — Base and force readiness

**Scope:** additional bases, facilities/capacities, full strategic personnel state,
training/recovery/transformations, craft equipment and servicing. This is one operational
readiness branch because facility capacity, staff assignments and craft state interact.

**Reference entry points:** `Basescape/PlaceFacilityState.cpp`, `DismantleFacilityState.cpp`,
`SoldierInfoState.cpp`, `SoldierArmorState.cpp`, `SoldierTransformationState.cpp`,
`CraftEquipmentState.cpp`, `CraftWeaponsState.cpp`, `CraftSoldiersState.cpp`,
`CraftPilotSelectState.cpp`; `Geoscape/BuildNewBaseState.cpp`, `ConfirmNewBaseState.cpp`,
`Globe.cpp` placement queries, training states and timed
handlers; `Savegame/BaseFacility.cpp`, `Soldier.cpp`, `Craft.cpp`, `CraftWeapon.cpp`,
`Vehicle.cpp`; corresponding facility/soldier/transformation/craft/item/armor rules.

**Commit purposes:**

1. Capture construction, personnel and servicing oracles; inventory currently deferred
   transformation properties and give each an implemented or tactical-owned boundary.
2. Implement new-base placement/costs, facility placement/connectivity, construction,
   dismantling, replacement/disable semantics, capacities and maintenance calculation.
   Bring forward geography and UI selection needed for legal base placement: land and
   fake-underwater texture checks, research gates, region costs and coordinate handling.
   Apply the applicable checks to starting-base placement too. Branch 4 extends these
   queries for world simulation rather than introducing their first usable version.
3. Extend branch 1's initialized soldiers with assignments, armor changes, roles/piloting,
   health/recovery and reference-defined personnel restrictions; preserve all owned saves.
   Promote preserved imported fields without rerolling stats or replacing names/armor.
4. Implement strategic training, psi-training and transformations with resources, timing,
   eligibility and real script providers; leave battle-earned changes explicitly Phase 7.
5. Implement craft loadouts/crew/vehicles and repair/refuel/rearm lifecycle, stock use,
   readiness rules and counters. Persist work in progress and reuse branch 1 creation,
   arrival/checkup and transfers without reinitializing delivered craft.
6. Add base layout/capacity, personnel/assignment/training and craft loadout/readiness UI;
   exercise additional-base creation and end-to-end inter-base operations.
7. Close readiness scenarios, imported-field promotion, bounds, script/event fixtures,
   performance measurements and matrix updates.

**Acceptance:** create a second base, build facilities, staff it, train/transform eligible
personnel, equip a craft and advance it to readiness. Reload during construction,
recovery/training and each service state. Reject blocked dismantling, disconnected
placement and incompatible equipment according to the reference. Exercise OXCE mod
variants, including deferred transformation properties; list tactical-only effects
explicitly rather than discarding them. Maintenance calculation is usable now; branch 6
owns the final monthly ledger/report integration.
As in branch 1, distinguish isolated monthly training fixtures from playable advancement
through a month boundary whose other campaign effects are not yet implemented.

## Branch 3 — Research and production

**Scope:** research progression/unlocks and manufacturing queues, with staff/facility,
inventory and finance integration. Research and manufacture share one branch because
unlocks and resource consumption form a single executable economy chain.

**Reference entry points:** `Savegame/ResearchProject.cpp`, `Production.cpp`, relevant
research/availability helpers in `SavedGame.cpp`; `Basescape/ResearchInfoState.cpp`,
`NewResearchListState.cpp`, `ManufactureInfoState.cpp`, `ManufactureStartState.cpp`;
`Geoscape/GeoscapeState.cpp` timed completion and `ResearchCompleteState.cpp`;
`Mod/RuleResearch.cpp`, `RuleManufacture.cpp`.

**Commit purposes:**

1. Capture unlock/dependency, staff and completion-order fixtures, including mod-only
   research/manufacturing fields and completed-research migration cases.
2. Implement research eligibility, allocation/cancellation and project persistence;
   account for items/specimens, prerequisites, exclusions and facility requirements.
3. Implement research progression/completion, discoveries, rewards/free/dependent
   research, notifications and script events with exact reference ordering.
4. Implement production queues, allocation, material/money consumption, completion,
   repeated/infinite production, autosell and any supported alternative outputs.
5. Add research/production UI showing eligibility, staff, materials, progress, queue
   order and completion; connect unlocks to purchases/facilities/equipment.
6. Close a persisted research -> unlock -> manufacture -> equip scenario with script
   fixtures, resource/fund accounting, malformed graphs and matrix/measurement updates.

**Acceptance:** reproduce vanilla and modded dependency/reward chains, empty resources,
insufficient funds, zero staff, cancellation and multiple completions at one boundary.
Reload active projects and compare state/event outcomes with uninterrupted progression.
Report unsupported cycles or values according to reference semantics rather than
silently sorting them into a different order.

## Branch 4 — World simulation and mission generation

**Scope:** globe queries, strategic targets and movement, mission/arc/event scheduling,
UFO/site/alien-base lifecycle, detection, and dispatch/recall. Interception combat follows
in branch 5. Monthly mission generation must work here, not wait for branch 6's report.

**Reference entry points:** `Geoscape/Globe.cpp`, `GeoscapeState.cpp` including
`determineAlienMissions` and timed handlers; `Savegame/Target.cpp`, `MovingTarget.cpp`,
`Waypoint.cpp`, `AlienStrategy.cpp`, `AlienMission.cpp`, `Ufo.cpp`, `AlienBase.cpp`,
`MissionSite.cpp`, `GeoscapeEvent.cpp`; corresponding region, trajectory, mission,
mission-script, arc-script and event rules under `Mod/`.

**Commit purposes:**

1. Capture world geometry, mission selection/scheduling, movement and detection traces;
   establish deterministic choice injection and persistent target-reference fixtures.
2. Extend branch 2's placement geography with globe/region/terrain/depth queries and
   target graph persistence, including
   longitude wrapping, waypoint identity, target deletion and reference bounds.
3. Implement alien strategy, mission/arc/event eligibility and scheduling, waves and
   site/alien-base generation; install reference-ordered daily/monthly hooks.
4. Implement UFO/craft movement, fuel/range, pursuit/return/patrol, detection/loss and
   target lifetime. Validate cross-target references on load and during deletion.
5. Add navigable globe, target information, detection notifications and craft dispatch/
   recall controls, with meaningful time-speed interruption and headless equivalents.
6. Close multi-day and month-boundary world scenarios, bounded population/save tests,
   scripting coverage, representative allocation measurements and matrix updates.

**Acceptance:** controlled choices produce the expected eligible mission, trajectory,
spawn order and detection decisions. A craft can pursue and return safely; target
expiry/deletion leaves no dangling references. Reload in-flight UFOs, craft, scheduled
waves and sites. Cover TFTD depth/terrain and modded deployment-only/no-object waves.
Event scheduling/state begins here; branch 6 completes campaign-wide consequences and
monthly evaluation. Required immediate consequences cannot be placeholders until then.
Until interception/deployment and monthly evaluation arrive, stop before a transition
that needs them. Multi-day/month-boundary subsystem fixtures do not waive these guards
for playable campaigns; branch 6 closes integrated monthly progression.

## Branch 5 — Interception and strategic deployment handoff

**Scope:** interception combat, outcomes, landing legality and the strategic side of
mission launch, including base-defense approach. Tactical maps, shots and mission
results are not implemented by this branch.

**Reference entry points:** `Geoscape/DogfightState.cpp`, `InterceptState.cpp`,
`ConfirmLandingState.cpp`, `ConfirmCydoniaState.cpp`, `BaseDefenseState.cpp`,
`GeoscapeState.cpp`; `Savegame/CraftWeapon.cpp`, `CraftWeaponProjectile.cpp`, `Ufo.cpp`,
`Craft.cpp`; `Battlescape/BattlescapeGenerator.cpp` caller/input contract and relevant
deployment/terrain/craft-weapon/UFO rules. Inspect the generator boundary without
mechanically porting its tactical implementation.

**Commit purposes:**

1. Capture interception legality, timing, ammunition/damage and landing/deployment
   decision fixtures, including multi-craft and modded weapon/depth cases.
2. Implement interception state and resolution: approach/range, modes, weapons,
   disengagement, fuel interruption, damage/destruction and reference script hooks.
3. Integrate strategic outcomes: crashes/sites, craft returns, losses, scores and
   base-defense pre-battle effects; restore interruption state where reference permits.
4. Define and execute the deployment request boundary: destination/deployment IDs,
   participants/loadouts, terrain/depth/race/context, eligibility, resource reservation
   and exactly-once transition. Persist pending state separately from real battle state;
   document any port-only envelope and its C++ readability limitations.
5. Add interception/landing/loadout confirmation UI and a headless request consumer;
   integrate/version live managed extension persistence if not already required earlier.
6. Close dispatch -> intercept -> land -> reload handoff scenarios, rollback/cancellation
   where reference permits, target-loss tests, extension-state tests and matrix updates.

**Acceptance:** deterministic choices reproduce legal combat outcomes and resource use;
launch cannot duplicate participants or consume them twice after reload. Cover landed/
crashed UFO, terror/custom site, alien-base/final destination and base-defense request
variants where strategically applicable. Explicitly document any reference save point
that cannot represent an in-progress dogfight; do not invent C++ save support. No fake
battle graph or simulated victory satisfies acceptance. Phase 7 consumes the request
and later supplies the real mission-result/recovery integration.

## Branch 6 — Monthly campaign cycle and Phase 6 closure

**Scope:** complete monthly accounting/evaluation, diplomacy/pacts, campaign-wide
events/objectives/endings, and cross-system strategic closure. This branch integrates
existing mission/training monthly hooks in reference order; it must not reapply them.

**Reference entry points:** `Geoscape/GeoscapeState.cpp::time1Month`,
`MonthlyReportState.cpp`, `GeoscapeEventState.cpp`, `FundingState.cpp`;
`Savegame/SavedGame.cpp`, `Country.cpp`, `Region.cpp`, `AlienStrategy.cpp`,
`GeoscapeEvent.cpp`; corresponding event/arc/mission rules and strategic ending callers.

**Commit purposes:**

1. Capture month/year rollover, funding/maintenance, scores/pacts, chained events and
   ending fixtures; audit the ownership ledger for remaining strategic omissions.
2. Implement reference monthly ledger and country/region evaluation, funding changes,
   pacts, history retention, debt/failure conditions and interaction with earlier hooks.
3. Complete campaign event effects, progression/objectives and strategic endings;
   preserve selection/order, once-only state, scripts and interrupted notifications.
   Define battle-result inputs for Phase 7 without claiming battle-derived outcomes.
4. Add monthly/event/failure UI and essential summaries; finish missing strategic
   controls/options, save/load/pause flows and informative unsupported-tactical states.
5. Add integrated multi-month and repeated-save scenarios for UFO, TFTD and the staged
   large mod; exercise resource conservation, graph identities, script events and
   bounded histories/populations. Measure populated simulation and save/restore costs.
6. Perform Phase 6 closure audit: reconcile rule/save/binding/option matrices, publish
   evidence and residual Phase 7/8 obligations, and update the launch-gate status.

**Acceptance:** play the entire implemented strategic loop through several month
boundaries with save/reload at each major transition. Funding, maintenance, projects,
transfers, training, missions and events occur once in reference order. Strategic
failure paths are executable. Tactical success, recovery, battle-driven promotions and
final victory remain unclaimed until Phase 7 supplies real outcomes.

## Validation and CI policy

For each branch:

1. Start only after the preceding PR is merged, required checks pass and local `main`
   is updated. Review base changes and active work before creating the next branch.
2. Capture public synthetic reference fixtures before implementation. Record reference
   commit, inspected methods, commands, expected semantics and any private provenance.
   Extend the headless scenario tool rather than depending on one-off probes alone.
3. Run focused local tests while implementing: defaults/widths/order, commands/scripts,
   malformed inputs, bounds, interrupted transitions, and save ownership. Include a
   scripted random source for branches with stochastic behavior.
4. On the completed candidate run Release build, full tests and formatting once. Run
   fresh/cached UFO/TFTD/modded scenarios and the affected private-save corpus. Separate
   executable scenarios, inspect/preserve-only round trips and expected preflight blocks
   in the results; each branch must have positive executable corpus cases within its
   supported horizon as well as blocked cases. Capture
   pinned-reference saves for newly executable mid/late strategic state; preservation
   tests of older saves alone are insufficient. Try C++ reload of emitted ordinary
   saves where feasible and report any gap separately from C# round-trip success.
5. Exercise the new UI flow and relevant indexed/native smoke checks. Preserve the
   three-platform/coverage CI matrix and SDL path scope. Scenario comparisons should
   be semantic and finite; long performance/soak work runs locally unless a short stable
   regression case belongs in CI.
6. Push one completed candidate and open one PR. Do not use draft PRs or per-commit
   pushes as a way to save CI: pull-request workflows still run. Address failures
   locally, then push the complete correction. Merge using Rebase and merge and verify
   resulting main checks before continuing. Never skip required checks to meet a count.

Six successful first-candidate PRs imply approximately **12 main-CI workflow runs**
(one per PR plus one per merge), currently four jobs each: about 48 job executions.
SDL-sensitive branches add their existing three-platform PR validation. This is a
configuration-based planning floor, not a promise about hosted runs, failures or branch
protection. Opening another planning-only PR would add a cycle; keep this plan with
branch 1 unless a separate review is explicitly requested. No CI configuration change
is necessary for this plan.

## Phase 6 completion checklist

- Six acceptance boundaries pass using shared gameplay commands in headless and UI paths.
- Every strategic feature in the targeted reference inventory has an owner, fixture
  evidence and explicit status; typed/parsed/preserved-only state is not called compatible.
- Each owning script provider executes at the real event site; no supported strategic
  action reaches a missing-provider placeholder or silently ignores a deferred property.
- Imported saves can continue the implemented strategy safely; unsupported live tactical
  state is preserved and clearly blocked. Cross-base/transit ownership is lossless.
- Save/restore, event interruption and month-boundary tests pass across the full strategic
  graph. Runtime memory, command work and histories remain bounded with recorded baselines.
- Strategic deployment requests are validated and persistent; the Phase 7 generator,
  tactical saves, mission results/debriefing and playable-launch integration remain
  explicitly tracked. Full original binary-save conversion and release breadth stay on
  the later completion roadmap, not hidden in a Phase 6 claim.
- Publish `phase-6-status.md` with exact corpus/check results, intentional differences,
  unresolved gaps and the first Phase 7 acceptance scenario. Do not manufacture a
  completion percentage from test counts or source-file counts.

## Planning evidence

This plan reviewed the current architecture, compatibility contract and matrices,
campaign foundation/commands/state, save overlays, extension deferrals, performance
targets and CI configuration. It directly checked C++ time dispatch and monthly hook
ordering in `GeoscapeState.cpp`, and verified the reference subsystem file inventory.
The follow-up sequencing review checked `PurchaseState.cpp` recruitment/templates and
craft initialization, `Mod.cpp::genSoldier`, the `Soldier` constructor, `Transfer::advance`,
`Craft::checkup`, `SavedGame::addMonth`, `TransferItemsState::getDistance`, and
`BuildNewBaseState::globeClick`. It assigned creation/arrival prerequisites and purchase
limit resets to branch 1, placement geography to branch 2, and clarified tick pause,
speed context and partial-campaign acceptance boundaries. The roadmap launch gate and
first planning commit are explicitly aligned with this delivery sequence.
Detailed method-level behavior capture for each feature is assigned to its owning
branch; this document is not a completed audit or new compatibility evidence.

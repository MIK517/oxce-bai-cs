# Monthly mission-script subsystem

Branch 4c incremental slice. Pinned reference:
`4df3a5e571a1a4b5e8a46d3161fb2e21a2adba15`.

`GeoscapeState::determineAlienMissions` collects eligible mission commands before
processing them. `processCommand` then chooses the ordinary non-site mission's region,
mission type and race, increments strategy history, starts its first wave and removes
the chosen table entry when `useTable` is set. The relevant methods are
`GeoscapeState.cpp::determineAlienMissions/processCommand`,
`RuleMissionScript.cpp::getDelay/generate`, `AlienMission.cpp::start`,
`AlienStrategy.cpp::addMissionRun/removeMission`, and
`WeightedOptions.cpp::choose` in the pinned C++ checkout.

The registered `ICampaignWorldMissionScripts` operation and internal
`ScheduleMonthlyWorldMissions` fixture command execute this phase through the campaign
transaction gate. The score and eligible funds are supplied by the future monthly
caller. The campaign time handler does not invoke it yet: arcs precede missions and
events follow them, and the month guard remains in place.

Supported scripts create ordinary space missions from explicit mission timelines.
Ordinary selection uses explicit regions; base targeting can omit region weights
when its odds guarantee that path. Races come from explicit script weights or the
selected mission rule's race timeline. Eligibility uses the shared live campaign triggers and the
run limit at list-collection time. Processing preserves rule order, dynamic conditional
labels, an odds draw for each processed command, the ordinary target-base odds draw,
the reference weighted-choice order and fixed/random delay before the first-wave
countdown. A missing conditional label permits execution, including label zero; a
recorded false label satisfies a negative conditional. A run limit reached by an
earlier command in the same collected list does not remove a later command. Mission
IDs, counters and strategy-table removal persist through OXCE save/reload.

The phase preflights all potentially executable choices before RNG or mutation.
Duplicate positive labels, unresolved choices, unsafe delays/timers and ID or
population exhaustion stop atomically. It conservatively blocks scripts that could
enter site generation, implicit strategy-table fallback, or non-space
objectives. Those paths and regional spawn/objective work belong to subsequent 4c
slices. Some choices that a later odds or conditional draw would skip are also
conservatively blocked. A 4,096-script and 100,000-mission bound keeps work finite.

`strategic-script-rules` captures the exact C++ weighted timeline methods and now
also captures `RuleMissionScript::getDelay`; `strategic-wave-chain` separately captures
mission countdown transitions. `strategic-mission-scripts.rul` composes these with
actual world rules. `StrategicMissionScriptTests` covers fresh/cache, mission creation,
selection RNG order, conditional and run history, table removal, reload, invalid
labels/links/delays, unavailable objectives and exhausted IDs.

The original subsystem passed its root checks at introduction. Current validation
for the expanded scheduler is recorded below.

Arc scheduling now has an [isolated subsystem](strategic-arc-script-status.md), and
tagged adhoc scheduling has a [shared executor](strategic-adhoc-script-status.md).
Outstanding: mission site and strategy fallback paths,
actual initial/monthly hook composition. This subsystem
does not claim integrated monthly campaign progression or branch 4c acceptance.

Mission scripts now use `RuleAlienMission::generateRace` when their own race
weights are absent. The reverse mission timeline, empty/future timeline failure,
ordinal weighted selection, explicit override and RNG order are preserved.
The pinned `strategic-alien-base-generation` oracle includes the exact method;
`StrategicMissionRaceFallbackTests` covers fresh/cache and persisted run history.
Site missions and strategy region/mission fallback remain guarded.

Ordinary script missions now apply `AlienMission::setRegion`'s single remap;
strategy removal still uses the selected source region. The existing pinned
base-generation probe inserts the exact method. Fresh/cache regressions cover
remapping, chained aliases (one hop), unresolved targets, table history and reload.

Ordinary mission scripts with explicit mission types now target player-base
regions using the reference's uniform starting list entry, circular retry,
duplicate base regions and active-mission pruning. All-busy candidates fail the
label without creating a mission or changing run/table history. Missing region
weights are permitted for guaranteed base targeting; mixed odds still validate
both paths. Candidate work is bounded before mutation. The pinned
`strategic-mission-base-target` probe and fresh/cache tests cover selection,
failure labels, RNG order, resource limits and reload. Implicit strategy fallback,
site scheduling and campaign month composition remain unavailable.

Validation after all three scheduler slices and their audits: root
`dotnet test --no-restore` passes 1,438 tests, zero failed or skipped, including
checksum and private-corpus coverage. Formatting, code-map and diff checks pass.
Audits found no confirmed functional defect in these slices; the final audit
corrected obsolete capability statements in this status document. The reference
checkout is unchanged. Branch 4c still has outstanding world capabilities and
acceptance work; branch 4d has not started.

Explicit-region mission scripts can now choose their mission from the persisted
alien strategy table. Weighted order, empty-choice failure, live batch removal,
`useTable: false`, run history and reload are covered against the pinned
`strategic-mission-strategy` probe. Missing/invalid tables and batches that can
remove a later explicitly required table stop before mutation; removal bounds
count one possible removal per command and region. Region-table refresh and
implicit base-target mission selection remain for the next slice.

Strategy region selection and exhausted-table refresh now execute through the
mission scheduler. Refresh rebuilds weighted regions/missions and preserves run
and location histories. Base targeting without mission weights prunes missing
region tables, retains duplicate base regions and then chooses a table mission.
Pinned traces cover refresh and implicit-base pruning; fresh/cache composed
batches and transition reloads verify persistence. Preflight validates current
and possible refreshed selections without RNG or live mutation. Site scheduling,
non-space initialization and campaign monthly composition remain guarded.

Strategy audit correction: `AlienStrategy::removeMission` deletes even an
already-empty table. A prior removal must therefore guard a later explicit-region
fallback lookup of that table. New pinned deletion traces and fresh/cache red/green
regressions prove the batch now blocks before its first mission or RNG mutation.

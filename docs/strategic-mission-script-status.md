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

Supported scripts use explicit weighted mission, region and race timelines and create
ordinary space missions. Eligibility uses the shared live campaign triggers and the
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
enter site generation, base targeting, implicit strategy-table fallback, or non-space
objectives. Those paths and regional spawn/objective work belong to subsequent 4c
slices. Some choices that a later odds or conditional draw would skip are also
conservatively blocked. A 4,096-script and 100,000-mission bound keeps work finite.

`strategic-script-rules` captures the exact C++ weighted timeline methods and now
also captures `RuleMissionScript::getDelay`; `strategic-wave-chain` separately captures
mission countdown transitions. `strategic-mission-scripts.rul` composes these with
actual world rules. `StrategicMissionScriptTests` covers fresh/cache, mission creation,
selection RNG order, conditional and run history, table removal, reload, invalid
labels/links/delays, unavailable objectives and exhausted IDs.

Validation: root `dotnet test` passes 1,188 tests with zero failures or skips,
including checksum and private-corpus tests. Formatting and code-map verification pass.

Outstanding: arc scheduling, mission site/base/strategy fallback paths, tagged adhoc
scripts, actual initial/monthly hooks and score/funding calculations. This subsystem
does not claim integrated monthly campaign progression or branch 4c acceptance.

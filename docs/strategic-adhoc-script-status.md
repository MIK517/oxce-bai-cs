# Tagged adhoc mission-script subsystem

Branch 4c incremental slice. Pinned reference:
`4df3a5e571a1a4b5e8a46d3161fb2e21a2adba15`.

`GeoscapeEventState.cpp::eventLogic` calls
`GeoscapeState::determineAlienMissions(false, eventRule)` when an event has adhoc
mission-script tags. `GeoscapeState.cpp::determineAlienMissions` uses the ordered
`Mod::getAdhocScriptList`, filters each script by the event's tags, collects eligible
scripts before processing any command, and invokes the same `processCommand` used
by monthly mission scripts. Its inner tag loop breaks after the first script tag,
so a later script tag does not match by itself. Adhoc scheduling uses the current
month's score and funds; only new-month scheduling looks ahead for funding. Month
zero skips the score/funds criteria, as in the shared reference eligibility check.
`RuleEvent.cpp::load` and `RuleMissionScript.cpp::load` provide the two tag lists.

The runtime event projection now retains the immutable event tag list; content
compiler revision 27 invalidates older caches. The registered
`ICampaignWorldAdhocScripts` operation and internal `ScheduleAdhocWorldMissions`
fixture command filter by the first script tag and reuse the monthly mission
command executor. Empty event tags schedule nothing. Missing event rules block
before mutation. Ordered labels, odds, weighted choices, mission creation, run
history and strategy-table changes follow the monthly subsystem and persist
through save/reload. The shared preflight prevents unsupported objectives and
unsafe resource use before any RNG or mutation.

The `strategic-adhoc-scripts` manifest pins the public rule fixture and a probe
that inserts the exact level-zero C++ tag-filter block into controlled event and
script collaborators. Its captured rows cover first-tag matching, later-tag
exclusion, multiple event tags and empty tags. Distinct mission delays let
`StrategicAdhocScriptTests` compare actual scheduled order with the verified
rows in fresh and cached content. The composed fixture also covers ordered
conditions, run limits, score/funds criteria, save/reload and atomic blocking
of an unsupported site mission. Due event payloads are still guarded:
the future branch 6 event caller may invoke this operation only when payload
execution is implemented. Site/base/fallback mission modes and full campaign
hooks remain 4c work. This subsystem does not claim branch 4c closure.

Validation: root `dotnet test` passes 1,222 tests with zero failures or skips,
including checksum and private-corpus tests. Formatting, regenerated code-map and
diff checks pass.

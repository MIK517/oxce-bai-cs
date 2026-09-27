# Monthly event-script subsystem

Branch 4c slice, pinned reference `4df3a5e571a1a4b5e8a46d3161fb2e21a2adba15`.

## Reference and ownership

`GeoscapeState::determineAlienMissions` selects events after arcs and missions and
before base upgrades/evolution. Initial geoscape entry (`init`) and `time1Month`
call the full scheduler. Ordinary `time1Day` does not invoke event scripts. The
event payload caller can request tagged adhoc missions, not monthly event scripts.

This slice implements the event phase as the registered internal gameplay operation
`ICampaignWorldEventScripts`. `ScheduleMonthlyWorldEvents` is an internal command
for subsystem fixtures, dispatched through the existing campaign transaction gate.
Friend access is limited to the two test assemblies. It is not a public campaign/UI
action. The real initial/monthly caller is still deferred until the preceding arc
and mission phases exist; no time handler or new-campaign hook calls it yet.

The caller supplies already computed score and eligible funds. In the full reference
caller those include `SavedGame::getCurrentScore(month)` and, for a new month,
country funding plus nonnegative performance bonus minus base maintenance. Those
calculations and month advancement are not implemented or claimed by this slice.
The campaign month guard remains unchanged.

## Eligibility and selection

Eligibility preserves inclusive month/difficulty/score/funds bounds; score/funds are
ignored before month one. All seven trigger groups and both counter names are checked.
The mission marker uses `SavedGame::getLastId`: absent is zero, a present ID has a
minimum last ID of one. Nonpositive minimum bounds are inactive; only maximum -1 is
unbounded. Execution odds are drawn for every eligible script, including odds zero
and 100, before any event selection or spawning.

Campaign facts mirror `SavedGame::isItemObtained`, `isFacilityBuilt`,
`isSoldierTypeHired`, `locateRegion`, `locateCountry` and `isResearched`:

- Items count in base stores and craft item containers; transfers, worn armor,
  vehicles and installed weapons are excluded.
- Facilities count at build time zero, even if disabled. Soldiers must have arrived.
- Base regions/countries use the first matching saved region/country. Pact tests use
  the active pact flag, not a pending new pact. Debug mode satisfies every research ID.

Per script, choose the first ungenerated sequential event, then an ungenerated
weighted event, then a repeatable timeline event. Only afterward spawn the selected
events in that order. Thus the same event can appear three times in one command;
the next command sees its generated history. `SavedGame::wasEventGenerated` tests
key presence, including an imported zero-count key. Sequential/random missing rules
are required errors; missing repeatable selections are silently omitted.

Existing `ICampaignEventScheduling` owns timer RNG, repeated instance identity and
history changes. No notification consumer mutates state and no save schema changes
are needed. Save/reload fixtures include repeated instances, zero-count history,
interrupted cleanup and the unchanged due-payload guard.

## Intentional bounds and validation

Before the first RNG draw, preflight checks all potentially reachable required
references, timers and weighted totals, plus event/history capacity and generation
overflow. It bounds a phase to 4,096 eligible scripts. An unsafe timeline or weight
throws before mutation; unavailable rules/capacity/timers return a blocked result.
Preflight may conservatively reject a near-limit workload or an invalid possible
choice that randomness or an earlier script would otherwise suppress. Zero-odds
scripts cannot select events and do not validate unreachable choices. It does not
preview RNG, roll back state or execute payloads.

Sequential validation stops at the first ungenerated entry that no earlier eligible
nonzero-odds script can generate. Only earlier scripts' possible selections expose
later entries; choices from the current script enter that set after all three paths
are checked. This keeps an unavailable tail from blocking its valid head while still
blocking before mutation when an earlier script could expose the tail. The possible-ID
set conservatively combines mutually exclusive earlier choices rather than expanding
all combinations. Fresh/cache regression cases cover missing and invalid-timer tails,
duplicate entries, all three earlier-script selection paths, zero odds, same-script
selections and a newly reachable tail after save/reload.

`strategic-event-scripts` captures the exact C++ event processing loop,
`RuleEventScript::generate`, `WeightedOptions` methods and `wasEventGenerated` with
controlled rules/RNG/history. It does not claim whole-scheduler parity. Eligibility
is checked separately by `WorldScriptEligibilityTests` and live campaign fixtures in
`StrategicEventScriptTests`, including fresh/cache loading and persistence.

Validation for this slice: root `dotnet test` passes 1,168 tests with zero failures
or skips; formatting, code-map, diff and fixture checksum checks pass.

Still outstanding: arc/mission/adhoc scheduling and actual campaign scheduler hooks,
score/funding eligibility calculations, remaining world lifecycles/detection/hunting
and the final 4c acceptance gate. Event payloads, combat/deployment and integrated
monthly progression retain their branch 5/6 boundaries. Branch 4d is untouched.

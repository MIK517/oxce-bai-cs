# Strategic scheduling rule projections

Branch 4c prerequisite slice, reference `4df3a5e571a1a4b5e8a46d3161fb2e21a2adba15`.
Mission, adhoc, arc and event scripts now have immutable runtime families with
generation-scoped links. Compiler/cache revision 26 rebuilds the runtime catalog.
This slice supplies executable timeline/weighted selection. The subsequent
[monthly event-script subsystem](strategic-event-script-status.md) now consumes
event projections in isolated fixtures. The bounded
[monthly mission-script subsystem](strategic-mission-script-status.md) similarly
consumes mission projections; integrated campaign scheduling remains guarded.

## Reference inventory

- `Mod/RuleMissionScript.cpp`: constructor, `load`, `generate`, `getMissionTypes`,
  `getRegions`, `getAllMissionTypes`; also used for `adhocScripts`.
- `Mod/RuleArcScript.cpp`: constructor and `load`.
- `Mod/RuleEventScript.cpp`: constructor, `load`, `generate`.
- `Mod/Mod.cpp`: `loadAll` mission/adhoc objective validation; indexed script lists.
- `Savegame/WeightedOptions.cpp`: `choose`, `set`, `getNames`.
- `Geoscape/GeoscapeState.cpp`: `determineAlienMissions`, `processCommand` identify
  consumers, conditional labels, trigger groups, arc research, event selection and
  tagged adhoc invocation. Their campaign execution remains a subsequent slice.

## Owned data and defaults

| Projection | Fields and default behavior |
|---|---|
| Common eligibility | first month 0, last month -1, odds 100, difficulty 0..4, full signed 32-bit score and signed 64-bit funds ranges; mission variable/marker names and counter bounds |
| Triggers | research, items, facilities, soldier types, base regions/countries and pact countries; preserve each boolean and ID; map order is ordinal, unresolved references remain explicit |
| Mission/adhoc | variable name, label 0, target-base odds 0, max runs -1, repeat avoidance 0, delays 0, use-table true; conditionals and tags retain order and duplicates; mission/race/region timelines |
| Arc | maximum arcs -1; sequential research retains duplicates, random research uses ordinal weighted entries |
| Event script | sequential one-time events, random one-time events, repeatable event timeline, affects-game-progression false; legacy missionMinRuns/missionMaxRuns are overridden by counterMin/counterMax |

Mission/arc counter members are uninitialized in the pinned C++ constructors. The
port retains its existing typed defaults of 0/-1; these are deterministic port policy,
not a claim of reference-defined values. Configured bounds are projected exactly.

## Ordering and errors

Timelines append in load order, including inherited entries and duplicate months.
Generation scans backward for the first eligible entry; it does not sort months.
Mission/region listing instead falls back to the first entry before its first month
and preserves the reference signed comparison. Weight maps are ordinal and omit
zero entries; sequential lists, conditionals and tags are not deduplicated.

The reference permits an empty event timeline, producing no event and no RNG draw.
An empty mission generation timeline or generation before every timeline entry would
dereference an invalid iterator; the port intentionally rejects it before RNG.
Weighted totals above the reference integer RNG bound fail before RNG as well.

Mission and adhoc rules collect mission IDs across all months at link time and
reject a missing first ordinal mission or mixed site/non-site objectives, matching
Mod::loadAll. Later missing mission IDs retain nullable links: the reference skips
them during objective validation, leaving their handling to the eventual caller.
Site-only scripts have their derived SiteType flag set. Duplicate labels are retained:
their conditional/caller validation does not belong to the loader.
Arc and event references stay nullable so the eventual caller can preserve required
versus optional lookups (notably optional repeatable-event selection). Trigger IDs
also remain available even when they cannot resolve to handles.

No campaign save fields or user options change in this slice. Persistence coverage
is fresh/cache reconstruction; existing campaign save/reload tests remain applicable.
Counter history, generated-event history and mission state remain owned by existing
save-neutral snapshots. Monthly, event-payload and combat/deployment guards remain.

## Evidence

`strategic-script-rules` captures exact C++ selection methods with controlled rule
storage and RNG. It covers unsorted timelines, weight boundaries, pre-first-month
listing and empty event timelines. It does not claim YAML parsing or scheduler parity.
`StrategicScriptRuleTests` checks fresh/cache projections and the captured rows.
`StrategicScriptLinkingTests` covers repeated loads, trigger replacement, missing and
mixed mission rules, and optional event references. `WorldScriptSelectionTests`
covers duplicate timeline entries and unsafe/malformed selection without RNG changes.

# Phase 6 ownership and fixture ledger

Reference: `4df3a5e571a1a4b5e8a46d3161fb2e21a2adba15`. Implementation
branch: `codex/strategic-base-logistics`. This is a work ledger, not a completion claim.

| Behavior/state | Owner | Reference | Evidence/status |
|---|---|---|---|
| Ordered tick fallthrough and popup pause | Branch 1 | `GeoscapeState::timeAdvance` | `strategic-time` extracted-loop probe; handler internals excluded |
| Highest-trigger summary | Branch 1 | Existing C# event contract | Retain exclusive counts; execute effects before event publication |
| Tick eligibility and unsupported live state | Branch 1, broadened in each branch | Timed handlers, `SavedGame::load` | Port safety boundary; preserve-only imports remain readable |
| Inventory, money, research eligibility, capacities, purchase limits | Branch 1 | `PurchaseState`, `SellState`, `Base`, `SavedGame::addMonth` | Capture pending |
| Soldier identity, initial stats/name/armor, templates | Branch 1 | `Mod::genSoldier`, `Soldier`, `PurchaseState` | Capture pending; no delayed generation |
| Craft initialization, cargo/assignment effects, arrival checkup | Branch 1 | `Craft`, `Transfer`, purchase/sale/transfer states | Capture pending |
| Transfer quantities, cost/distance, time and mobile save fields | Branch 1 | `TransferItemsState`, `TransferConfirmState`, `Transfer` | Capture pending; ownership crosses base/transit containers |
| Facility editing, training, recovery, transformations, servicing | Branch 2 | Base/personnel/craft states, timed handlers | Deferred; action dependencies must be promoted before use |
| Research and production progression | Branch 3 | `ResearchProject`, `Production`, timed handlers | Deferred; completed research eligibility belongs in branch 1 |
| World entities, mission/arc/event scheduling and movement | Branch 4 | `GeoscapeState`, mission/target classes | Deferred; do not advance opaque entities |
| Interception and strategic deployment | Branch 5 | `DogfightState`, landing/deployment callers | Deferred |
| Monthly ledger, event consequences, strategic endings | Branch 6 | `MonthlyReportState`, `GeoscapeEventState`, `SavedGame` | Deferred; earlier monthly hooks must not run twice |
| Active battle, battle-derived personnel changes and debriefing | Phase 7 | Battlescape and debriefing | Preserve-only; tactical continuation unavailable |
| Full diaries/graphs/Ufopaedia presentation | Phase 8 | Corresponding UI states | Underlying strategic state belongs to its earlier owner |

Per-property defaults, widths, options, script dependencies and malformed-input cases
are added with each owning fixture. Unknown fields alone do not imply an unsafe action;
fields required by the action or its next tick do. Positive executable, blocked, and
inspect/preserve corpus results must be reported separately.

## Implemented prerequisites

`StrategicTimeFixtureTests` matches all 18 extracted-loop traces. Dispatcher unit tests
cover pause fallthrough, preflight without partial mutation, batching equivalence and
the empty million-tick allocation bound. `CampaignSaveRegressionFixtureTests` covers
soldier A -> transit -> B, reordered bases, repeated rewrites, deletion/recreation,
duplicate ownership and the guarded month boundary. Save-adapter/foundation unit tests
also cover legacy types and preservation after new-campaign emission.

Transfer commands and arrival effects are still pending at this prerequisite milestone.
See [ADR 0025](decisions/0025-strategic-time-and-mobile-save-ownership.md).

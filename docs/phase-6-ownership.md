# Phase 6 ownership and fixture ledger

Reference: `4df3a5e571a1a4b5e8a46d3161fb2e21a2adba15`. Implementation
branch: `codex/strategic-base-logistics`. This is an implementation/evidence ledger;
the final bounded acceptance is in [strategic logistics status](strategic-logistics-status.md).
Checkpoint notes below retain the scope and outstanding work at each implementation milestone.

| Behavior/state | Owner | Reference | Evidence/status |
|---|---|---|---|
| Ordered tick fallthrough and popup pause | Branch 1 | `GeoscapeState::timeAdvance` | `strategic-time` extracted-loop probe; handler internals excluded |
| Highest-trigger summary | Branch 1 | Existing C# event contract | Retain exclusive counts; execute effects before event publication |
| Tick eligibility and unsupported live state | Branch 1, broadened in each branch | Timed handlers, `SavedGame::load` | Port safety boundary; preserve-only imports remain readable |
| Inventory, money, research eligibility, capacities, purchase limits | Branch 1 | `PurchaseState`, `SellState`, `Base`, `SavedGame::addMonth` | Logistics/critical-sale fixtures; monthly reset in isolated handler |
| Soldier identity, initial stats/name/armor, templates | Branch 1 | `Mod::genSoldier`, `Soldier`, `PurchaseState` | Generation/piloting/template fixtures; no delayed generation |
| Craft initialization, cargo/assignment effects, arrival checkup | Branch 1 | `Craft`, `Transfer`, purchase/sale/transfer states | Starting/purchase/arrival and save-cycle fixtures |
| Transfer quantities, cost/distance, time and mobile save fields | Branch 1 | `TransferItemsState`, `TransferConfirmState`, `Transfer` | Extracted arithmetic and two-base fixtures; mobile ownership |
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

Item/staff quote, order, transfer and arrival commands are implemented in the working
branch. `CampaignLogisticsTests` covers atomic rejection, incoming capacity reservation,
minimum transfer cost, purchase-log persistence, and save/reload before and after arrival.
`StrategicLogisticsFixtureTests` checks extracted `RuleItem` price, `Base::storesOverfull`,
and `TransferItemsState::getDistance` arithmetic. The corresponding probe deliberately
stubs the script hook; actual VM input/event plumbing is covered separately by
`ScriptRuntimeFrameTests`. These results do not establish complete branch acceptance.

Soldier name-pool loading and constructor generation are implemented. Reference
sources inspected: `Mod/RuleSoldier.cpp`, `Mod/SoldierNamePool.cpp`,
`Savegame/Soldier.cpp`, `Mod/Mod.cpp::genSoldier`, and
`SavedGame::selectSoldierNationalityByLocation`. The public logistics fixture checks
fixed initial stats, bravery truncation, initial psi skill, female-name/callsign fallback,
look weights and ten-attempt duplicate handling. Scalar recruit templates apply after
generation, including stat merge sentinels, mana reroll, and nationality/name refresh.
Starting soldiers load literal stats instead; random starting soldiers select all types
before generation and do not apply the recruit template. Starting craft load their
supplied weapons/cargo instead of initializing purchased fixed weapons. Complex soldier
template payloads remain guarded where unsupported. Initial crew assignment, commendations,
and negative-capacity starting weapon removal are covered by the initialization checkpoint below.

Craft purchase/transfer/sale and soldier transfer/dismissal now own cargo refunds,
crew movement and assignment removal. Arrival runs `Craft::checkup` priorities and the
same tick's first refuel operation, including item-fuel consumption/refunds. Subsequent
service progression remains guarded until branch 2. Graph restoration validates crew
references across simultaneous incoming craft/soldier transfers.

Explicit campaign options retain the reference defaults (`storageLimitsEnforced=false`,
`canSellLiveAliens=false`, `autoCombatDefaultSoldier=true`). Unit scenarios check that
purchase storage limits remain active with enforcement disabled, craft transfer capacity
checks depend on the option, and recruit defaults precede template overrides. Transfer
alien containment requires a matching facility even when its occupancy limit is disabled.
Source: `Engine/Options.cpp`, `PurchaseState::increaseByValue`,
`TransferItemsState::increaseByValue`, `SellState` catalog construction and `Soldier`.

Runtime name-pool projections retain content hashes. Cache restoration checks referenced
pool bytes, including custom file extensions; the custom-extension mutation regression
passes. Further action/provider coverage and final cached/fresh corpus closure remain outstanding.

## Logistics interface integration

`--campaign-sdl` now runs a 640x400 indexed logistics client. `I` inspects stored and
incoming inventory; `B`/`S` open a stable purchase/sale quote; `T` selects a destination
before opening a transfer quote. Up/down selects rows, +/- changes quantities (Shift
changes ten), Enter requests confirmation, and Y submits once. Tab changes the selected
base. Escape cancels confirmation, closes the current screen, then exits from the main
view. Space advances one minute; Shift+Space requests one hour, subject to simulation
guards and arrival interruptions. F5 saves and F9 requests confirmation before loading
the supplied save path. A `-` destination disables save/load. There is no automatic
background time advance while an order is being edited.

The read-only stores query does not execute price hooks or capture the save graph.
Missing craft inventory is reported as an unavailable capacity calculation and blocks
dependent actions; unknown soldier assignment state prevents craft sale/transfer.
`CampaignLogisticsClientTests` exercises keyboard purchase confirmation/cancellation,
save/load in transit, exactly-once delivery, and mutation-free stores inspection.

The App resolves English labels from common/layered language files and composed
`extraStrings`; the font loader supports the installation's indexed `FONT_SMALL` images
and uses the asset-independent interface font when no font definition exists. Source
inspection: `Engine/Font.cpp::load`, `Language.cpp::loadFile/loadRule`. The staged 40k
font (8x9) and scientist label (`Adept`) have been rendered and visually inspected using
the public logistics scenario. This is not yet the full cached/fresh playable-corpus
acceptance gate. Main navigation/help text is currently English; broader language
selection and richer layout/notification review remain open integration work.

UI checkpoint validation: 605 solution tests passed with zero skips, including the
available private corpus. The older personnel-rule fixture now supplies the synthetic
`second.nam` file that its name-list replacement test retained. `fontName` and composed
extra strings are immutable runtime projections, independent of optional diagnostic
compatibility data, and the font override survives a compiled-cache hit (revision 7).

## Randomized recruit-template bonuses

Templates now own `previousTransformations`, fixed `transformationBonuses`, and weighted
`randomTransformationBonuses`/`transformationBonusesCount`. Choices are removed after
selection; empty or NUL rule names consume a choice without awarding a bonus. Existing
counts increment, zero weights do not draw, and the loop stops when options are exhausted.
Only the awarded counts are emitted, so a recruit cannot reroll on transit reload.
These payloads cover the previously guarded randomized templates used by 40 soldier
types in the staged 40k/Rosigma content. Other complex template payloads remain guarded.

`capture-strategic-logistics-reference.ps1` now inserts `WeightedOptions::choose/set` and
the `Soldier::load` random-bonus block verbatim into the probe. Parsed map input and an
injected minimum-choice RNG are collaborators; YAML parsing and RNG stream parity are
not claimed by this probe. Five captured count/draw/result traces match the C# path.
Purchase/save/arrival tests preserve both fixed and randomized results. Negative weights
and totals outside the C++ integer RNG call's valid range fail intentionally; possible
counter overflow and excessive generation work block orders before recruitment.
The runtime cache revision is now 8.

## Starting crew and commendations

Starting craft whose combined raw capacities are negative return every mounted launcher
and loaded clip to stores, retaining physical cargo. Automatic assignment runs when the
`randomSoldiers` key exists, even for zero recruits. It considers all small soldiers in
base order, retains the first legal transport as fallback, and prefers an eligible
interceptor with an unfilled pilot requirement. Capacity includes weapon bonuses,
existing soldiers and vehicles; soldier/armor groups and per-group limits are respected.
Large soldiers keep their existing assignment, matching the reference caller.

Random starting recruits receive `STR_MEDAL_ORIGINAL8_NAME` when defined. Commendation
name/noun/level now have gameplay ownership and survive save/reload; other diary fields
remain in the mobile source overlay. Pilot eligibility includes transformation and
commendation bonuses, armor stats, short-width addition and fixed stat minimums.
Vehicle size/space overrides are retained; missing dimensions use the vehicle-unit armor.
Runtime craft constraints, pilot minimum stats, armor stats, soldier bonuses and
commendations are immutable projections; compiler/cache revision is 9.

References inspected: `Mod::newSave`, `Craft::validateAddingSoldier`, capacity/space
and soldier/vehicle count helpers, `CraftWeapon::getClipsLoaded`, `Craft::load`,
`Vehicle::load/save`, `Armor::getTotalSize/getSpaceOccupied`,
`Soldier::getBonuses/prepareStatsWithBonuses/hasAllPilotingRequirements`,
`SoldierDiary::awardOriginalEightCommendation`, `SoldierCommendations::load/save`,
`RuleCommendations::getSoldierBonus`, and `UnitStats::obeyFixedMinimum`.
The public Veteran logistics fixture checks launcher/clip refunds, interceptor-first
assignment, transport capacity, awards and repeated save rewriting. Unit tests check
bonus deduplication/count semantics and short overflow before clamping. The 612-test
solution checkpoint passes, including private saves; final branch acceptance remains open.

## Critical storage sales

With storage enforcement enabled, critical overflow exposes incoming items and craft
cargo/equipment as sale stock. Sales consume base stock, stationed craft cargo/equipment,
then transfers in their existing order. Removing a launcher or loaded clip removes the
whole mounted component and returns unsold parts to base stores. Partial transfer removal
retains identity and hours; exhausted item transfers disappear. Sales must resolve storage
overflow before confirmation can complete. The planned inventory changes are checked on
an isolated state copy before publishing, including refund overflow and capacity checks.

References: `Base::getUsedStores/storesOverfullCritical`, `Craft::getTotalItemCount`,
and `SellState` catalog construction and `btnOkClick` cleanup lambdas. Public compatibility
scenarios cover stationed and incoming craft, clip refunds, partial transfer reduction,
save/reload and exactly-once arrival. Normal-sale regressions pass in the unit suite.
One explicit readiness boundary remains: removing mounted weapons with nonzero bonus
stats is blocked. The reference cleanup retains the craft's cached stats until reload;
branch 1 must not replace this with an immediate derived-stat recalculation. No stock,
funds or transfer changes are committed when that boundary is reached.

## Final branch validation

Fresh/cache action and eligibility checks pass for all three staged content families.
Three of 19 private saves can purchase and reload; eight active battles and eight active
research/production states are explicitly blocked. The full solution has 620 passing
tests and no skips. Generation preflight now rejects overflowing name/look weights and
possible empty callsign outcomes before consuming RNG. Craft bonus overflow disables
the purchase row before a mixed recruit/craft order can draw recruits. Bravery bounds
are checked after the reference integer division; initial psi skill does not draw a range.
The status document records measured allocations, UI controls and remaining guards.

See [ADR 0025](decisions/0025-strategic-time-and-mobile-save-ownership.md).

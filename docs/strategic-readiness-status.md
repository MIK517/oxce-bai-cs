# Strategic readiness status

Branch: `codex/strategic-base-readiness`. Date: 2026-09-08.

Phase 6 branch 2 implements the bounded base-and-force readiness slice. A campaign can
select a legal starting site, create a region-priced second base, build or dismantle
facilities, develop and equip personnel, edit craft loadouts, advance construction and
training, complete transformations, and service craft. These transitions use gameplay
commands and indexed UI actions and survive fresh/cache creation plus OXCE save reload.

## Implemented behavior

- Globe data loads from ruleset polygons or bounded `WORLD.DAT` records. Placement uses
  the reference spherical polygon test, texture ocean/fake-underwater flags, research
  gates, first-matching region cost, the base limit option, and compatible access lifts.
- Facility commands enforce the 6x6 grid, type/research/function restrictions, overlap,
  upgrades, construction reduction, build queues, connectivity, dismantling remainders,
  refunds, ammunition, capacity use and stable preservation keys. Daily construction
  resets replacement state at completion. Queries expose monthly maintenance components
  without applying the still-guarded monthly ledger.
- Personnel commands cover physical/psi training allocation, wounded training queues,
  recovery, armor stock exchange, craft assignment, pilot requirements and craft/armor
  group and capacity limits. Daily recovery preserves reference ordering and the daily
  psi option; the isolated monthly psi algorithm does not weaken the month guard.
- Soldier transformations project all typed fields. Commands validate research, base
  functions, rank, health, type, history, stats, commendations, items and funds; apply
  random/flat/percentage stat changes and bounds; update armor, rank, recovery, bonuses
  and history; select weighted events in reference RNG order; and use saved transfers
  for delayed soldiers or produced items.
- Craft commands edit removable weapons and vehicles, consume/refund stores, preserve
  fixed slots, reject capacity loss, and refresh mutable fuel/shield maxima. Critical
  sales can remove bonus weapons when the resulting loadout remains legal. Hourly
  repair/rearm/shield and facility-ammunition service plus half-hour refuelling preserve
  progress, shortage flags and reference handler ordering.
- The indexed client provides legal site selection, second-base creation, layout and
  facility actions, personnel training/assignment/armor actions, craft weapon/vehicle
  actions, readiness state, maintenance, save/load and time controls.

## Reference evidence

Normative revision: `4df3a5e571a1a4b5e8a46d3161fb2e21a2adba15`.

| Reference source | Covered behavior | Evidence |
|---|---|---|
| `Globe.cpp`, `BuildNewBaseState.cpp`, `ConfirmNewBaseState.cpp` | Polygon selection, land/base type and cost | `strategic-bases.rul`; fresh/cache scenario |
| `BaseView.cpp`, facility states, `Base.cpp`, `BaseFacility.cpp` | Placement, queue/connectivity, upgrades, refunds, capacities, maintenance, construction | base construction/save scenario and campaign tests |
| `Soldier.cpp`, training allocation and daily handlers | Wounds, recovery, training, transformation eligibility/stat changes | extracted recovery rows and deterministic tests |
| `Craft.cpp`, `CraftWeapon.cpp`, `Vehicle.cpp`, equipment states | Equipment, capacities and service lifecycle | 108 extracted rearm rows and service/loadout/sale scenarios |

`tools/capture-strategic-readiness-reference.ps1` verifies the pinned checkout, extracts
the actual C++ weapon and recovery methods, and builds the arithmetic oracle. It writes
only ignored project artifacts. The expected JSON contains no original game assets.

## Deliberate boundaries

Daily readiness advancement is enabled only while preserved world and active-project
guards permit it. Month boundaries remain blocked because funding, scoring, pacts,
mission scheduling and the monthly report belong to later Phase 6 branches. Airborne and
auto-patrolling craft, opaque live-world entities and active battles remain guarded.
Dead-soldier transformations await ownership of the memorial/death graph. Battle-earned
soldier changes and tactical equipment behavior remain Phase 7. Event selection is
implemented; applying event consequences belongs to the later strategic event pipeline.

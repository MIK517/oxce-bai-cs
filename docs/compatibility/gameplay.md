# Gameplay compatibility

Phase 6 branches 1 and 2 implement bounded strategic logistics and readiness through
headless commands and an indexed keyboard UI. Supported daily readiness advances while
monthly and preserved live-world state remain guarded.
See [branch 1 acceptance and limitations](../strategic-logistics-status.md).
See [the ownership ledger](../phase-6-ownership.md) and
[ADR 0025](../decisions/0025-strategic-time-and-mobile-save-ownership.md).

| Subsystem or scenario | Status | Reference source | Fixture | Notes |
|---|---|---|---|---|
| Battlescape coordinate storage and basic conversions | partial | `src/Battlescape/Position.h` | `core-position` | Signed 16-bit storage, tile/voxel conversion, remainder, and distance rounding are covered. |
| New campaign and starting base | compatible for foundation subset | `src/Mod/Mod.cpp` (`newSave`, `getStartingBase`), `src/Savegame/Base.cpp` | `campaign-foundation`; `runtime-rule-linking`; public and staged UFO/TFTD/40k scenarios | Deterministic campaign creation builds countries/regions and the difficulty-selected starting base from generation-linked rules, including initial funding, facilities, crafts, fixed/random soldiers, items, IDs, and script values. Starting-base placement is a validated command/event and the implemented state round-trips through OXCE YAML. |
| Time progression and calendar | compatible for bounded readiness | `GeoscapeState.cpp`, `GameTime.cpp` | `strategic-time`; readiness and dispatcher/calendar/soak tests | Hourly servicing/transfers, half-hour refuelling and daily construction/recovery/training run when preflight permits. Month boundaries and live world state remain guarded. |
| Globe, countries, and regions | partial | `src/Mod/RuleCountry.cpp`, `RuleRegion.cpp`; `src/Savegame/Country.cpp`, `Region.cpp` | `campaign-foundation`; `runtime-rule-linking`; private UFO/TFTD/Rosigma saves | Dense linked rules create area-bearing countries/regions; mutable funding, activity, pact state, histories, and base coordinates persist. Globe rendering/rotation, region queries, activity generation, and strategic simulation are not implemented. |
| Personnel and soldier progression | compatible for bounded strategic readiness | `RuleSoldier.cpp`, `Soldier.cpp`, training/transformation states | `strategic-logistics`; `strategic-readiness`; generation/piloting/recovery tests | Assignments, armor, recovery, physical/daily psi training and transformations are executable and saved. Monthly psi remains isolated; dead/memorial state and battle-earned changes remain deferred. |
| Bases, facilities, inventory, and transfers | compatible for bounded readiness | base placement/facility states, `Base.cpp`, `BaseFacility.cpp` | `strategic-logistics`; `strategic-bases`; sale/UI tests | Additional bases, construction, queues, upgrades, connectivity, dismantling, capacity and maintenance queries are executable. Active project accounting remains guarded. |
| Crafts and equipment | compatible for bounded strategic readiness | craft equipment states, `Craft.cpp`, `CraftWeapon.cpp`, `Vehicle.cpp`, `GeoscapeState.cpp` | logistics/readiness/service/loadout/sale tests | Weapon, vehicle and crew editing, mutable capacity/maxima and the service lifecycle are implemented. Airborne movement and auto-patrol require world simulation. |
| Research | not started | `src/Basescape/`, `src/Savegame/ResearchProject.cpp` |  | Eligibility and unlock order are mod-visible. |
| Manufacture | not started | `src/Basescape/`, `src/Savegame/Production.cpp` |  | Costs, materials, and completion timing. |
| Finance and monthly processing | partial histories and transactions | `GeoscapeState.cpp`, `SavedGame.cpp` | `campaign-foundation`; logistics/save tests | Initial funding and bounded histories persist. Purchases, sales and transfer fees update funds/accounting; monthly purchase-log reset is tested independently. Complete monthly funding, scoring, pacts and campaign failure remain guarded. |
| Alien missions and UFO movement | not started | `src/Geoscape/`, `src/Savegame/AlienMission.cpp`, `Ufo.cpp` |  | Compare eligibility and weights around randomness. |
| Detection and interception | not started | `src/Geoscape/` |  | Legal actions, ranges, and resolution. |
| Tactical map generation | not started | `src/Battlescape/`, `src/Mod/MapScript.cpp` |  | Deterministic fixtures should inject random choices. |
| Tactical pathfinding and movement | not started | `src/Battlescape/Pathfinding.cpp` |  | Costs, legality, doors, falling, and terrain. |
| Line of sight and voxel collision | not started | `src/Battlescape/TileEngine.cpp` |  | Gameplay-visible geometry is required compatibility. |
| Projectiles, explosions, and damage | not started | `src/Battlescape/` |  | Preserve rounding, armor sides, and damage types. |
| Morale, reactions, melee, and psi | not started | `src/Battlescape/` |  | Compare permissions before random outcomes. |
| Tactical AI and turn processing | not started | `src/Battlescape/` |  | Benchmark and scenario coverage required. |
| Mission objectives and debriefing | not started | `src/Battlescape/`, `src/Geoscape/` |  | Recovery and campaign transition. |

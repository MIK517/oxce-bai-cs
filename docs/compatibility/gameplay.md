# Gameplay compatibility

Phase 6 branch 1 implements bounded strategic logistics through headless commands and
an indexed keyboard UI. Ordered dispatch is covered by the extracted `strategic-time`
fixture; advancement stops before unsupported midnight processing and live state.
See [branch 1 acceptance and limitations](../strategic-logistics-status.md).
See [the ownership ledger](../phase-6-ownership.md) and
[ADR 0025](../decisions/0025-strategic-time-and-mobile-save-ownership.md).

| Subsystem or scenario | Status | Reference source | Fixture | Notes |
|---|---|---|---|---|
| Battlescape coordinate storage and basic conversions | partial | `src/Battlescape/Position.h` | `core-position` | Signed 16-bit storage, tile/voxel conversion, remainder, and distance rounding are covered. |
| New campaign and starting base | compatible for foundation subset | `src/Mod/Mod.cpp` (`newSave`, `getStartingBase`), `src/Savegame/Base.cpp` | `campaign-foundation`; `runtime-rule-linking`; public and staged UFO/TFTD/40k scenarios | Deterministic campaign creation builds countries/regions and the difficulty-selected starting base from generation-linked rules, including initial funding, facilities, crafts, fixed/random soldiers, items, IDs, and script values. Starting-base placement is a validated command/event and the implemented state round-trips through OXCE YAML. |
| Time progression and calendar | compatible for bounded logistics | `GeoscapeState.cpp`, `GameTime.cpp` | `strategic-time`; dispatcher/calendar/soak/allocation tests | Five-second ticks execute ordered handlers before event publication. Hour/day/month fallthrough and monthly purchase reset have isolated fixtures; real campaigns stop before unsupported midnight or live-state processing. Arrivals complete same-tick servicing before pausing subsequent ticks. Highest-trigger summaries and the empty-workload 16 KiB gate remain intact. |
| Globe, countries, and regions | partial | `src/Mod/RuleCountry.cpp`, `RuleRegion.cpp`; `src/Savegame/Country.cpp`, `Region.cpp` | `campaign-foundation`; `runtime-rule-linking`; private UFO/TFTD/Rosigma saves | Dense linked rules create area-bearing countries/regions; mutable funding, activity, pact state, histories, and base coordinates persist. Globe rendering/rotation, region queries, activity generation, and strategic simulation are not implemented. |
| Personnel and soldier progression | compatible for logistics subset | `RuleSoldier.cpp`, `SoldierNamePool.cpp`, `Soldier.cpp`, `SoldierDiary.cpp`, `Mod::newSave` | `strategic-logistics`; generation/piloting unit tests; fresh/cached private corpus | Recruitment creates stats, names, armor, scalar templates and randomized transformation bonuses before transit. Dismissal and transfer preserve ownership and refund armor where applicable. Initial crew assignment and original-eight awards are implemented. Training/recovery/promotion progression and equipment editing remain readiness work; unsupported template payloads block recruitment. |
| Bases, facilities, inventory, and transfers | compatible for logistics subset | `PurchaseState.cpp`, `SellState.cpp`, `TransferItemsState.cpp`, `Base.cpp`, `Transfer.cpp` | `strategic-logistics`; critical-sale fixtures; logistics/UI unit tests; private corpus | Stores, costs, funds, research/base-function eligibility, capacities, monthly purchase limits, staff hiring, sales and transfers are executable. Critical sales include cargo and incoming supplies. Arrivals persist and deliver once. Additional bases and construction remain branch 2; active project accounting is guarded. |
| Crafts and equipment | compatible for logistics subset | `RuleCraft.cpp`, `Craft.cpp`, `CraftWeapon.cpp`, `Vehicle.cpp` | `strategic-logistics`; craft/piloting unit tests; private corpus | Purchases initialize fixed weapons; sale/transfer owns crew, cargo and refunds. Arrival runs checkup and same-tick refuelling. Starting negative-capacity weapons unload before crew assignment. Later servicing and loadout editing remain branch 2. Critical removal of mounted bonus weapons is guarded pending mutable craft-stat ownership. |
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

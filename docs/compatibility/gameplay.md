# Gameplay compatibility

| Subsystem or scenario | Status | Reference source | Fixture | Notes |
|---|---|---|---|---|
| New campaign and starting base | not started | `src/Savegame/SavedGame.cpp`, `src/Mod/Mod.cpp` |  | First strategic vertical slice. |
| Time progression and calendar | not started | `src/Geoscape/GeoscapeState.cpp`, `src/Savegame/GameTime.cpp` |  | Fixed-step headless clock. |
| Globe, countries, and regions | not started | `src/Geoscape/`, `src/Savegame/Country.cpp`, `Region.cpp` |  | Separate simulation from presentation. |
| Personnel and soldier progression | not started | `src/Basescape/`, `src/Savegame/Soldier.cpp` |  | Include promotions, wounds, training, and diaries. |
| Bases, facilities, inventory, and transfers | not started | `src/Basescape/`, `src/Savegame/Base.cpp` |  | Validate capacity and adjacency rules. |
| Crafts and equipment | not started | `src/Basescape/`, `src/Savegame/Craft.cpp` |  | Include refueling, repair, and rearming. |
| Research | not started | `src/Basescape/`, `src/Savegame/ResearchProject.cpp` |  | Eligibility and unlock order are mod-visible. |
| Manufacture | not started | `src/Basescape/`, `src/Savegame/Production.cpp` |  | Costs, materials, and completion timing. |
| Finance and monthly processing | not started | `src/Geoscape/`, `src/Savegame/SavedGame.cpp` |  | Include scoring and campaign failure. |
| Alien missions and UFO movement | not started | `src/Geoscape/`, `src/Savegame/AlienMission.cpp`, `Ufo.cpp` |  | Compare eligibility and weights around randomness. |
| Detection and interception | not started | `src/Geoscape/` |  | Legal actions, ranges, and resolution. |
| Tactical map generation | not started | `src/Battlescape/`, `src/Mod/MapScript.cpp` |  | Deterministic fixtures should inject random choices. |
| Tactical pathfinding and movement | not started | `src/Battlescape/Pathfinding.cpp` |  | Costs, legality, doors, falling, and terrain. |
| Line of sight and voxel collision | not started | `src/Battlescape/TileEngine.cpp` |  | Gameplay-visible geometry is required compatibility. |
| Projectiles, explosions, and damage | not started | `src/Battlescape/` |  | Preserve rounding, armor sides, and damage types. |
| Morale, reactions, melee, and psi | not started | `src/Battlescape/` |  | Compare permissions before random outcomes. |
| Tactical AI and turn processing | not started | `src/Battlescape/` |  | Benchmark and scenario coverage required. |
| Mission objectives and debriefing | not started | `src/Battlescape/`, `src/Geoscape/` |  | Recovery and campaign transition. |

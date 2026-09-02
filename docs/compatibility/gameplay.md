# Gameplay compatibility

| Subsystem or scenario | Status | Reference source | Fixture | Notes |
|---|---|---|---|---|
| Battlescape coordinate storage and basic conversions | partial | `src/Battlescape/Position.h` | `core-position` | Signed 16-bit storage, tile/voxel conversion, remainder, and distance rounding are covered. |
| New campaign and starting base | rule projection complete; gameplay not started | `src/Mod/Mod.cpp` (`newSave`, `getStartingBase`), `src/Savegame/Base.cpp` | `runtime-rule-linking` | Starting facility, craft, soldier, item, random-soldier, time, and funding references are generation-linked. Mutable campaign creation and persistence begin in Slice 5. |
| Time progression and calendar | not started | `src/Geoscape/GeoscapeState.cpp`, `src/Savegame/GameTime.cpp` |  | Fixed-step headless clock. |
| Globe, countries, and regions | rule projection complete; gameplay not started | `src/Mod/RuleCountry.cpp`, `RuleRegion.cpp`; `src/Savegame/Country.cpp`, `Region.cpp` | `runtime-rule-linking` | Dense runtime rules preserve external IDs/provenance and link eager event/self-region references. |
| Personnel and soldier progression | creation-rule projection complete; gameplay not started | `src/Mod/RuleSoldier.cpp`; `src/Savegame/Soldier.cpp` | `runtime-rule-linking` | Starting soldier type, armor, skill, research eligibility, costs, group, promotion, and piloting fields are projected. |
| Bases, facilities, inventory, and transfers | creation-rule projection complete; gameplay not started | `src/Mod/RuleBaseFacility.cpp`, `src/Savegame/Base.cpp` | `runtime-rule-linking` | Starting facility costs/capacities, replacements, item costs, research, map, and indexed-resource slots are projected. |
| Crafts and equipment | creation-rule projection complete; gameplay not started | `src/Mod/RuleCraft.cpp`, `RuleItem.cpp`; `src/Savegame/Craft.cpp` | `runtime-rule-linking` | Starting craft/item capacities, costs, research, fuel item, fixed weapon, and compatible-ammo relationships are projected. |
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

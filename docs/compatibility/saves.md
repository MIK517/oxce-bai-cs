# Save compatibility

| Area | Status | Reference source | Fixture | Notes |
|---|---|---|---|---|
| Save metadata/header document | not started | `src/Savegame/SavedGame.cpp` |  | Version, mods, campaign name, and display metadata. |
| Campaign body document | not started | `src/Savegame/SavedGame.cpp` |  | Split into child rows as state models arrive. |
| Game time and campaign identifiers | not started | `src/Savegame/GameTime.cpp`, `src/Savegame/SavedGame.cpp` |  | Preserve integer ranges and ordering. |
| Countries, regions, and alien strategy | not started | `src/Savegame/Country.cpp`, `Region.cpp`, `AlienStrategy.cpp` |  | Strategic state. |
| Bases, facilities, storage, and transfers | not started | `src/Savegame/Base.cpp`, `BaseFacility.cpp`, `Transfer.cpp` |  | References and in-progress operations. |
| Soldiers, crafts, and equipment | not started | `src/Savegame/Soldier.cpp`, `Craft.cpp`, `ItemContainer.cpp` |  | Includes diaries and layouts. |
| Research, manufacture, and finance | not started | `src/Savegame/ResearchProject.cpp`, `Production.cpp`, `SavedGame.cpp` |  | Includes queued and completed state. |
| Alien missions, UFOs, and sites | not started | `src/Savegame/AlienMission.cpp`, `Ufo.cpp`, `MissionSite.cpp` |  | Preserve target references. |
| Active battlescape root | not started | `src/Savegame/SavedBattleGame.cpp` |  | Tactical save document and version handling. |
| Battle units and items | not started | `src/Savegame/BattleUnit.cpp`, `BattleItem.cpp` |  | IDs, ownership, inventories, and status. |
| Tiles, nodes, and environmental state | not started | `src/Savegame/Tile.cpp`, `Node.cpp`, `SavedBattleGame.cpp` |  | Large collections require explicit limits. |
| Script values | not started | `src/Savegame/SavedGame.cpp`, `SavedBattleGame.cpp` |  | Strategic and tactical persistence. |
| Unknown/forward-compatible fields | not started | persistence methods throughout `src/Savegame/` |  | Define preservation boundaries explicitly. |
| Original UFO/TFTD save import | not started | `src/Savegame/SaveConverter.cpp` |  | Binary input is untrusted. |
| Semantic save round trip | not started | `src/Savegame/SavedGame.cpp`, `SavedBattleGame.cpp` |  | Normalize rather than compare YAML whitespace. |
| Missing mods/rules and corrupt saves | not started | `src/Savegame/SavedGame.cpp` |  | Must produce actionable diagnostics. |

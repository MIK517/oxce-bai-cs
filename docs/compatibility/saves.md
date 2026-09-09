# Save compatibility

Phase 6 branch 1: purchase, recruitment, sale, transfer and arrival snapshots preserve
campaign-wide soldier/craft identity across base/transit containers, repeated rewrites
and deletion/recreation. Session options and monthly purchase logs persist.
See [branch 1 acceptance and limitations](../strategic-logistics-status.md).
See [the ownership ledger](../phase-6-ownership.md) and
[ADR 0025](../decisions/0025-strategic-time-and-mobile-save-ownership.md).

The normative implementation target is the pinned secondary C++ checkout at commit
`4df3a5e571a1a4b5e8a46d3161fb2e21a2adba15`. Ignored private fixtures currently cover
early geoscape and active-battlescape saves for vanilla UFO, vanilla TFTD, and Rosigma;
their provenance identifies OXCE Brutal 8.6 commit `ab5041e`. Treat them as required
backward/migration inputs, not as evidence that the pinned revision's complete save
schema or late-campaign state is covered. Capture fresh pinned-version and late-game
saves as the owning gameplay slices make those states executable.

Architecture note: [ADR 0008](../decisions/0008-gameplay-owned-state-and-save-adapters.md)
assigns mutable state, persistent identities, and save-neutral capture/restoration
contracts to `Oxce.Gameplay`. `Oxce.Savegames` owns the external OXCE schema, mappings,
versions, migrations, unknown-field sidecars, and file operations. Compatibility
fixtures cross this adapter boundary; runtime models must not be shaped for direct YAML
serialization.

| Area | Status | Reference source | Fixture | Notes |
|---|---|---|---|---|
| Save metadata/header document | compatible for implemented fields | `src/Savegame/SavedGame.cpp` | `campaign-foundation`; save-adapter unit tests; private UFO/TFTD/Rosigma saves | Campaign name, time, mod labels, ironman state, engine/version/build output, port campaign identity, and creation time load or emit through the two-document OXCE stream. Existing mod labels retain their display versions when the active IDs are unchanged. Other header fields survive through the opaque source overlay. |
| Campaign body document | partial strategic subset | `src/Savegame/SavedGame.cpp` | `campaign-foundation`; save-adapter unit tests; private UFO/TFTD/Rosigma saves | The implemented strategic graph restores transactionally through gameplay-owned snapshots. Unimplemented strategic and tactical nodes remain persistence-owned and survive overlay writes; they are not treated as executable state. |
| Game time and campaign identifiers | compatible for campaign foundation | `src/Savegame/GameTime.cpp`, `src/Savegame/SavedGame.cpp` | `campaign-foundation`; `CampaignFoundationTests`; private UFO/TFTD/Rosigma saves | Calendar fields, ending/month/day counters, RNG state, next-ID maps, deterministic derived IDs for legacy inputs, and port-owned stable campaign IDs round-trip with validated ranges. |
| Countries, regions, and alien strategy | partial | `src/Savegame/Country.cpp`, `Region.cpp`, `AlienStrategy.cpp` | `campaign-foundation`; save-adapter unit tests; private UFO/TFTD/Rosigma saves | Country funding/activity/pact state, country script values, and region activity restore through linked external rule IDs. Alien strategy remains opaque and unimplemented. |
| Bases, facilities, storage, and transfers | compatible for bounded readiness | `Target.cpp`, `Base.cpp`, `BaseFacility.cpp`, `Transfer.cpp` | foundation/logistics/base/readiness save tests | Base identity/type/coordinates, constructed and replacement facilities, stock, staff and nested transfers round-trip. Stable preservation keys distinguish facility deletion and recreation. Project accounting remains guarded. |
| Soldiers, crafts, and equipment | partial: logistics and servicing | `Soldier.cpp`, `SoldierDiary.cpp`, `Craft.cpp`, `Vehicle.cpp`, `ItemContainer.cpp` | `strategic-logistics`; `strategic-servicing`; save-adapter/unit fixtures; private UFO/TFTD/Rosigma saves | Identity, scalar personal state, stats, armor, assignments, transformation/commendation state, craft status/fuel/damage/shields, weapons, cargo and vehicle dimensions are owned. Service progress survives fresh/cache creation and source-overlay reloads. Unknown diary/equipment/script fields remain in mobile source overlays. Newly generated state is persisted before transit and cannot reroll during reload. |
| Research, manufacture, and finance | partial histories and logistics eligibility | `ResearchProject.cpp`, `Production.cpp`, `SavedGame.cpp` | save-adapter/logistics tests; private UFO/TFTD/Rosigma saves | Bounded financial histories, completed research and monthly purchase/hire logs round-trip. Purchase/sale accounting is executable. Projects and production queues remain preserved and block dependent operations until their accounting is implemented. Monthly simulation remains guarded. |
| Alien missions, UFOs, and sites | not started | `src/Savegame/AlienMission.cpp`, `Ufo.cpp`, `MissionSite.cpp` |  | Preserve target references. |
| Active battlescape root | preservation only | `src/Savegame/SavedBattleGame.cpp` | private active-battle UFO/Rosigma saves | The complete `battleGame` node survives strategic overlay writes, but no tactical state is parsed, validated, or published. Measure consistent snapshot allocation before introducing segmented capture. |
| Battle units and items | not started | `src/Savegame/BattleUnit.cpp`, `BattleItem.cpp` |  | IDs, ownership, inventories, and status. |
| Tiles, nodes, and environmental state | not started | `src/Savegame/Tile.cpp`, `Node.cpp`, `SavedBattleGame.cpp` |  | Large collections require explicit limits. |
| Script values | partial strategic subset | `src/Savegame/SavedGame.cpp`, `SavedBattleGame.cpp` | save-adapter unit tests; private UFO/TFTD/Rosigma saves | Validated `GeoscapeGame` and `Country` tag values restore through the compiled tag catalog. Other strategic owners and all tactical script values remain opaque. |
| Unknown/forward-compatible fields | partial | persistence methods throughout `src/Savegame/` | unknown-field overlay, atomic loaded-rewrite, and entity-mutation unit tests; private active-battle UFO/Rosigma saves | Unknown header/body fields and unimplemented nested state remain in `OxceSaveDocument`, never enter gameplay, and survive ordinary strategic round trips. New-campaign emission and loaded-campaign rewriting are separate public APIs; loaded rewrites require the opaque source document instead of accepting an optional sidecar. Countries/regions associate by external ID, crafts by type/ID and soldiers by globally unique ID (including legacy soldiers with omitted type), bases by persistent ID, and facilities by owning base plus type/position. Removed entities lose their opaque fields rather than transferring them to another entity. Unimplemented entity families still require an identity rule when their gameplay slice lands. |
| Original UFO/TFTD save import | not started | `src/Savegame/SaveConverter.cpp` |  | Binary input is untrusted. |
| Semantic save round trip | compatible for campaign foundation | `src/Savegame/SavedGame.cpp`, `SavedBattleGame.cpp` | `campaign-foundation`; 100-cycle unit soak; private UFO/TFTD/Rosigma saves | Capture, adapter mapping, linked staged restoration, gameplay validation, re-emission, and semantic comparison pass for the implemented strategic subset. Emission is byte-stable across repeated generated-save cycles; YAML whitespace is not the compatibility oracle. |
| Missing mods/rules and corrupt saves | compatible for implemented subset | `src/Savegame/SavedGame.cpp` | save-adapter and campaign-restoration unit tests | Missing active mods/rules, duplicate entity identities, invalid histories/coordinates/facility placement/script tags, non-sequence collection fields (including explicit null), oversized input, and cancelled writes fail intentionally without publishing a partial campaign or corrupting the existing file. |

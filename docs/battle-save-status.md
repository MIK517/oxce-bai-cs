# Battle save status

Phase 7 branch 2 (`claude/battle-save`), from `main` at `3c91029a` (PR #78, branch 1 merged). Plan:
[Phase 7 branch 2](phase-7-plan.md#branch-2--battle-saves); decisions: [ADR 0038](decisions/0038-battle-aggregate.md),
[ADR 0039](decisions/0039-battle-save-intentional-differences.md) and the plan's branch decisions.

## Progress

| Slice | State |
|---|---|
| S0 planning | Done (2026-10-25) |
| S1 save module and battle root | Done (2026-10-25) |
| S2 map | Done (2026-10-25) |
| S3 units | Done (2026-10-25) |
| S4 items | Done (2026-10-25) |
| S5 campaign integration | Not started |
| S6 acceptance and branch audit | Not started |

## S0 — Planning (2026-10-25)

Reference studied: `SavedBattleGame::load`, `save` and `loadMapResources`, `Tile::loadBinary`, `saveBinary`, `load`,
`save` and `serializationKey`, `Node::load` and `save`, `BattleUnit::load` and `save`, `AIModule::load` and `save`,
`BattleItem::load` and `save`, `SavedGame::load` and `save` (`battleGame`, the header), `Craft::load`
(`inBattlescape`), `BattlescapeGenerator::run` and `deployXCOM` (`setInBattlescape`, the soldiers' transformed and
replaced armor), `BriefingState` (`setMissionTarget`, `setMissionCraftOrBase`), `YamlNodeReader::useIndex`.

- The [Phase 7 plan](phase-7-plan.md#branch-2--battle-saves) splits the branch into S1–S6 and records its decisions:
  typed state with opaque leftovers on their owner, the classification of the port state the reference does not
  save, no save before the first turn, writing the reference's current form while reading the older ones, the
  reference quirks (duplicate keys, armor sides, old text tiles), generation-time values for the fields branch 1 left
  unmodelled, the load failure and skip classes, the port-only overlay, the strategic side of a battle, and how "the
  reference loads written saves" is checked by a conformance reader without a reference binary. Each slice row names
  the synthetic tests for the read paths the corpus does not cover.
- Of the [inherited refusals](phase-7-plan.md#inherited-refusals), "Active tactical battle is preserved; tactical
  continuation is unavailable." (`OxceSaveAdapter.ReadRestrictions`) and branch 1's `battle.save-unsupported`
  (`CampaignBattles.BattleSaveUnsupported`) change in S5: the first retires, the second narrows to a named restriction
  on saving before the battle's first turn, which branch 3 owns (the briefing and `startFirstTurn`).
- The plan was audited before this commit; the audit's findings (unsaved port state, the turn-0 save, the duplicate
  keys' read side, the mid-battle validation, the ammo and text-tile failure rules, tests for the uncovered read paths)
  are folded into the plan's decisions and slice table.
- Corpus: eight private active-battle saves, `fixtures/private/saves/{vanilla-ufo,vanilla-tftd,modded/rosigma}/battlescape/`
  (124–336 KB of `battleGame` each). All use binary tiles; Rosigma's also carry `enviroEffectsType` and root `tags`.

## S1 — Save module and battle root (2026-10-25)

Reference: `SavedBattleGame::load` (the root keys), `save`, the constructor's defaults, `initMap`,
`resetCurrentAmbienceDelay`; `BattlescapeGenerator::generateMap` (the ambience); `RuleTerrain::load` (`ambience`,
`ambientVolume`, `ambienceRandom`, `ambienceRandomDelay`); `ItemContainer::load` and `save`;
`ScriptValuesBase::loadBase` and `saveBase`; `YamlNodeReader::readVal` and `tryRead`; `DebriefingState`,
`BattlescapeState`, `BriefingState` and `AbortMissionState` (the deployment a battle's readers derive).

- `BattleSnapshot` (`Oxce.Gameplay/Battles`) is the persistence boundary: every root key of `SavedBattleGame::load` and
  `save`, as values, with the constructor's defaults. `BattleState.Capture` and `BattleState.Restore(snapshot, content,
  ufoInBattlescape)` cross it; generation-only state (the plan's table) is not captured. `Restore` builds an empty map
  of the saved size (`initMap`), derives the deployment (`DeriveDeploymentId`, as `DebriefingState` does: the alien
  custom mission's, else the mission type's, else the in-battle UFO's own; the other readers differ, ADR 0039), the armor transformations from the enviro effects, and `NextItemId` 1
  (no items yet). A map size outside `BattleMap.IsSupportedSize`, or a starting condition or enviro effects the content
  lacks, is a corrupted save.
- `BattleState` gains the root fields generation did not model: `strTarget`, `strCraftOrBase`, `nameDisplay`,
  `animFrame`, `undoUnit`, `reinforcementsMemory`, the VIP counters, `objectivesDestroyed`, `tuReserved`,
  `kneelReserved`, the ambience, the three toggles and the root's `BattleGame` script values.
- `OxceBattleSave` (`Oxce.Savegames`) reads a `battleGame` node into a snapshot and writes one over the loaded node
  (`OxceSaveAdapter.Overlay`, so unknown root keys and the later slices' keys survive). Reading follows the reference:
  `tryRead` and `readVal(default)` keys keep the default when absent and fail naming the key when present and
  unreadable (`tryReadVal` throws), `selectedUnit` is required; an unknown starting condition or enviro effects is
  none (silently in the reference), an unknown base item or tag is left out (logged in the reference), each with an
  `OXCE-BATTLE-SAVE-SKIPPED` warning the port adds. A tag of the `RuleList` value type is refused on read and write
  with `OxceBattleSave.UnsupportedTagType`, a named restriction branch 8 owns (the tactical script bindings); the
  strategic tags have the same gap. Writing follows `save`: the VIP block only with a survival percentage, the
  objective block only with objectives needed, the starting condition and enviro effects only when set (each removed
  from the source otherwise), `strCraftOrBase` double-quoted (it carries `\x01`), `baseItems` sorted, tags non-zero
  only. The campaign still preserves `battleGame` opaquely (S5 wires the module in).
- Generation sets the terrain's ambience as `generateMap` does: the looping sound, else the random sounds, their
  delay range and a first delay from a seedless stream that leaves the game's stream untouched; the volume either way
  ([ADR 0039](decisions/0039-battle-save-intentional-differences.md)). `RuntimeTerrain` (also a craft's or UFO's
  inline terrain) carries the ambience with `loadOffsetNode`'s rule: an index at or above BATTLE.CAT's shared sounds
  is offset into the declaring mod's range (`ResolvedResourceCatalog.SharedSoundCounts`, new, cached; the compiled
  cache's compiler revision is now 74). An `ambienceRandom` list drops its no-sound (-1) entries, a single value
  keeps it; `ambientVolume` is now read as a double (`RuleTerrain::_ambientVolume`).
- ADR 0039 records the battle save's intentional differences; S1's are the seedless stream, a reversed or overflowing
  delay range, the sound index's unchecked mod size limit, the skip warnings and the deployment's reader.
- The conformance reader starts in the tests (`BattleSaveConformance`): the root's reading rules, each citing its line
  of `SavedBattleGame.cpp`.

Tests: `BattleSaveRootTests` (a generated battle written, reread, restored and rewritten byte-stable, through the
conformance reader; the looping and random ambience and the unchanged game stream; the delay floor and a reversed
range; absent keys; the VIP and objective blocks present and absent; `startingConditionType` and `enviroEffectsType`
known, unknown and absent; an unknown root key and the later slices' keys kept in the source's order; the required
`selectedUnit`; wrong types naming their key, the `readVal(default)` keys included; the `\x01` craft line; `baseItems`
and tags known, unknown, repeated and not a mapping, a base item key that is no scalar; `RuleList` tags refused; the
derived deployment; a restore refusing a missing starting condition, enviro effects or tag; an overflowing delay; an
unsupported map size; the restored empty map).
`ResourceDescriptorResolverTests.TerrainAmbienceResolvesTheBattleSoundsAModDeclares` (declared and undeclared mod
sounds offset, a shared one and no sound kept, no-sound list entries dropped and a single one kept, the volume and
delays). `PrivateBattleSaveTests` (all eight corpus battles: the root reads without a skip, restores past turn 1 with a
derived deployment, rewrites over its source with semantic equality, passes the conformance reader, and rewrites
byte-stable a second time).


## S2 — Map (2026-10-25)

Reference: `SavedBattleGame::load` (lines 168-170, 180-244), `save` (the map keys), `loadMapResources`, `initMap`,
`calculateModuleMap`; `Tile::load`, `loadBinary`, `save`, `saveBinary`, `serializationKey`, `setMapData`, `isVoid`,
`getTUCost`, `isUfoDoorOpen`; `unserializeInt` and `serializeInt` (`SerializationHelper.cpp`); `Node::load`, `save`
and `Node()`; `Mod::getMapDataSet`, `MapDataSet::loadData` and `getObject`; `Position` and `std::pair` reading
(`Position.cpp`, `Yaml.h`), `from_chars(csubstr, char*)` (rapidyaml's `charconv.hpp`); `BattlescapeGenerator::loadRMP`
(dummy nodes), `TileEngine` and `DebriefingState` (the module map's pair order).

- `BattleSnapshot` gains the map: `MapDataSets`, `Tiles` (`BattleTileSnapshot`: each part's map data and set IDs, smoke,
  fire, the floor's and walls' discovered flags, the walls' door frames, the three `lastExploredBy` turns), `Nodes`
  (`BattleNodeSnapshot`, with `allocated` and `dummy`; no segment), the three block grids as saved, and `ModuleMap`
  (`BattleModuleSnapshot(Total, Remaining)`, the pair's first and second).
- `OxceBattleSave.Read` reads `tileTotalBytesPer`'s presence as the binary form: the key's sizes are one-character
  values (`readVal<char>`, so `"\x04"` is 4 and `4` would be 52), a missing size is 0 except `tileBoolFieldsSize` (1);
  sizes 1, 2 and 4 read as Uint8, Sint16 and Uint32-as-int, any other reads 0 and skips its bytes with a skip warning
  (the release build), a `lastExploredBy` size of 0 is not read; a repeated index takes the later record. Without it,
  the old text `tiles` are read over each other by position, with `discovered`'s quirk and the door keys' presence.
  A base defense reads `moduleMap`, or counts it without objects (all (-1, -1)); other missions neither read nor write
  it. Malformed input fails naming the key (ADR 0039 lists each case).
- `OxceBattleSave.Write` writes the reference's current key (29-byte records), the non-void tiles in index order,
  `totalTiles`, `binTiles`, the nodes (block style; `allocated` and `dummy` only when set) and, for a base defense, the
  module map, in `save`'s order; it removes a loaded source's text `tiles`.
- `BattleState.Restore` now takes a `BattleTerrainLoader`: every data set loads with its MCD patches (`loadData`), a
  part with an ID or set of -1 is empty, a set past `mapdatasets`, an object past its set, a set without its MCD file
  (the reference dereferences a null stream, whether or not a tile uses the set; the plan's "fails when used" is
  corrected) or a tile outside the map is a corrupted save, and the restored battle runs `Validate` (a violation is a corrupted save).
  `Capture` writes a tile unless it is void (no part, no smoke, no item on its ground).
- `BattleMap` holds the saved tile state (smoke, fire, wall door frames, the explored turns), restores discovered flags
  without `setDiscovered`'s spreading, keeps the block grids jagged as saved, and costs an open UFO door 0 time units
  (`getTUCost`). `BattleNode` keeps `Allocated` and a restore path for saved nodes, dummies included.
  `BattleState.BaseModules` is jagged (`BattleBaseModule.EmptyGrid`).
- ADR 0039 gains S2's entries: the text tiles' `discovered`, the key's other sizes, the reads outside the reference's
  memory (also a missing `binTiles` and a data set without its MCD file), the module map fallback, the block grids'
  and the module map's saved shape (with the branches whose readers must refuse a missing block), the restored map's
  validation, the animation offsets.
- The conformance reader checks the map keys: the grids, `mapdatasets`, the one-character sizes of 1, 2 or 4 bytes, a
  record that holds every field, `binTiles` of exactly `totalTiles` records, each index inside the map, the nodes'
  types and the module map's pairs; it runs on every written battle.

Tests: `BattleSaveMapTests` (a generated map written, conforming, reread, restored part by part and rewritten
byte-stable; the key and the write order; every binary field; a repeated record; no `tileBoolFieldsSize`; other sizes
and missing explored sizes; an unsupported size and a missing index size; eleven malformed binary cases; text tiles
with `discovered`, open doors, short ID lists and a repeated position, rewritten as binary; twelve malformed text
tiles; text tiles without the explored turns; a node that is no mapping; mappings where sequences are expected; an open
UFO door's cost and the frames written; sets and objects past their range, an empty half of a part, a
tile outside the map, smoke past 255; an unknown and an unnamed data set; MCD patches on restore; void tiles; nodes'
fields, defaults and written keys; nodes that break the map; malformed map keys; block grids' saved shape; a base
defense's module map with a lost module, and without one; the module map outside a base defense, as a scalar and with a
bad pair). `BaseDefenseMapTests` reads the jagged module map. `PrivateBattleSaveTests` now restores every corpus map
with the installation's terrain files and compares the whole map with its source.

## S3 — Units (2026-10-25)

Reference: `SavedBattleGame::load` (the units' passes, lines 247-304 and 354-362, the status sanity check), `save` (the
units), `loadMapResources` and `resetUnitTiles`, `createTempUnit`; `BattleUnit::BattleUnit` (both constructors),
`updateArmorFromSoldier`, `updateArmorFromNonSoldier`, `deriveSoldierRank`, `deriveHostileRank`, `deriveNeutralRank`,
`adjustStats`, `setRecolor`, `load`, `save`, `isOut`, `setTile`; `Armor`'s `findWithFallback`, `getFaceColor` and the
others, `ArmorMoveCost::load` and `save`; `AIModule::AIModule`, `load`, `save`, `setStartNode`; `BattleUnitStatistics`
and `BattleUnitKills` `load` and `save`; `read(UnitStats)`, `read(Position)`, the enum `from_chars`;
`YamlNodeReader::tryReadAs`, `readVal(default)` and `YamlNodeWriter::write` of a vector (no key when empty);
`SavedGame::getSoldier`; `Mod`'s `ai` section (`pickUpWeaponsMoreActively`, `pickUpWeaponsMoreActivelyCiv`);
`Options::autoCombatDefault*`; `BattlescapeGenerator::addAlien`, `addCivilian` and `deployCivilians` (`markAsVIP`).

- `BattleSnapshot.Units` holds `BattleUnitSnapshot`s: every key `BattleUnit::load` reads and `save` writes, the AI module
  (`BattleAiSnapshot`), `tempUnitStatistics` (`BattleUnitStatisticsSnapshot`, `BattleUnitKillSnapshot`), recolor, move
  costs, tags, the previous owner, and the port-only controller and hostile and neutral spotting. A key the save may
  leave out, which the load then takes from the constructor, is nullable; `Capture` fills it.
- `OxceBattleSave.Read` reads the first unit pass: `faction` and `id` are required (`readVal` without a default), so is a
  mapping to hold them, and `genUnitType` and `genUnitArmor` for a unit that is no soldier; a faction outside 0-2 fails
  (ADR 0039); `originalFaction` defaults to the faction. A unit whose rule or armor is gone is left out with a skip
  warning. Each key follows `tryRead` (a present unreadable value fails naming it); the first of repeated keys wins and
  the `Uint8` counters keep the low byte (the pointer-valued duplicates); `armor` keeps up to nine sides as saved;
  `spawnUnit` names a known unit or none, and only then are `respawn` and `spawnUnitFaction` read; unit tags are read
  as the root's (unknown skipped, `RuleList` refused). A status other than standing, dead, unconscious or ignored is
  reported (`OXCE-BATTLE-SAVE-UNIT-STATUS`), as the reference logs it. `oxcePortBattle.units` gives controllers
  (`controller: ai` or `player` with `playerSlot`) and `spottedBy` (1, 2); without an entry, the player's units get the
  local player and the others the AI.
- `OxceBattleSave.Write` writes `units` after `moduleMap` in `save`'s order and conditions, each unit over its source
  node by ID; the duplicated keys once (ADR 0039); `movementType` and each `moveCost` kind only when they differ from
  the armor's; `tempUnitStatistics` as `~` when empty; the load-only `isBrutal`, `isNotBrutal` and `isCheatOnMovement`
  removed. `oxcePortBattle` is written only for units whose controller or spotting differs from those defaults.
- `BattleState` shares the constructors between the generator and the load. `TempUnit` (createTempUnit) and the unit
  rule constructor: statistics with a hostile unit's difficulty adjustment, the armor's five sides (`adjustStats` on a
  hostile unit's), move costs and movement type, the unit rule's aggression, spawn unit, VIP flag and weapon pick-up
  (else the mod's `ai` default), the unified rank, the recolor from `BattleState.Seedless`, the starting condition's
  `bannedInNextStage` (prepareBannedFlag; the load passes none), and an AI module for a unit that is not the player's. `SoldierUnit`: the soldier's statistics with bonuses, the armor's sides plus the
  soldier bonuses' (`RuntimeSoldierBonusRule.Sides`), its rank, auto-combat flag and look's recolor. The rule
  projections gained what they read (`RuntimeBattleArmor` sides, move costs and colors, `RuntimeBattleUnitRule` rank,
  aggression, spawn unit, VIP flag and pick-up, `RuntimeStartingCondition.ForbiddenArmorsInNextStage`, the campaign
  rules' `AiPickUpWeaponsMoreActively` and `...Civilians`; the compiled cache's compiler revision is now 75).
  `StartNodeId` is the AI module's `fromNode` (the plan's table corrected); `markAsVIP` now adds to the rule's flag.
- `BattleState.Restore` takes a `BattleRestoreCampaign` (the campaign's soldiers, `CampaignState.BattleSoldier`, and its
  difficulty): a soldier unit is made from the soldier and its own armor (genUnitArmor is not read), any other from its
  unit rule and `genUnitArmor` without the enviro effects; `BattleUnit::load` then sets the saved values. The AI module
  is kept for a unit that is not the player's, dead or ignored, its nodes inside the battle's nodes or -1; the selected
  and undo units are resolved among the player's units (the saved one, else the first not out); previous owners name a
  loaded unit; `resetUnitTiles` puts every unit that is not out on its tile and makes the player's units visible. A
  missing soldier is a corrupted save; so are overlapping units, repeated IDs and a footprint outside the map (ADR
  0039). A soldier unit saved in other armor than the campaign soldier's is refused with
  `BattleState.SoldierBattleArmorRestriction`, a named restriction S5 retires (the campaign soldiers' battle armor). `Validate` refuses a unit
  that is out but stands on the map; a restored battle skips the generator's placement check (a Rosigma battle has a
  unit where `setUnitPosition` would refuse it).
- The conformance reader checks the units: required faction and id (and the generated units' type and armor), one of
  each key, the value types of every key, nine armor sides, six wounds, Uint8 recolor pairs, the move cost kinds, the AI
  module and the statistics.

Tests: `BattleSaveUnitTests` (generated units written, conforming, restored and rewritten stable; both constructors'
values for what the save leaves out; a hostile unit's unified rank; a missing soldier, with and without a campaign; a
unit whose rule or armor is gone; fifteen unreadable units naming their key and a unit that is no mapping; no
`originalFaction`; a mind-controlled alien with an AI node; the AI kept only for a standing non-player unit, out-of-range
nodes; the AI's target faction; the pointer-valued duplicates; armor sides as saved and a short list; every saved value
round-tripping; the flags a reload loses; an unknown spawn unit; the selected and undo units; previous owners; an invalid
status; units on and off the map, overlapping units, a repeated ID; `Validate` on an out unit standing on the map;
statistics and kills; unit tags; `oxcePortBattle` and five malformed port nodes; unknown unit keys and no `units` key
without units; a hostile unit's armor at two difficulties; the soldier constructor's rank, bonus armor (clamped at 0)
and look recolor; a soldier in unknown armor and its original armor; the S5 armor restriction; a dead soldier found;
the starting condition's banned flag at generation only; the mod's AI pick-up defaults; `markAsVIP` keeping the rule's
flag; a large unit's footprint outside the map). The root and map
tests restore with the campaign and compare with the loaded form (`BattleSaveUnitTests.Loaded`,
`AssertLoadedRewriteIsStable`). `PrivateBattleSaveTests` restores every corpus battle's units with the save's campaign
and compares the rewrite with its source with the duplicated keys and the dropped AI modules normalized; the corpus has
a mind-controlled alien with an AI node, player and dead units with one, and a panicking soldier.

## S4 — Items (2026-10-25)

Reference: `SavedBattleGame::load` (the item passes and the units' second pass, lines 306-397), `save` (the item keys),
`createItemForUnitSpecialBuiltin`, `initItem`; `BattleItem::BattleItem`, `load`, `save`, `setFuseTimer`, `setOwner`,
`setPreviousOwner`, `moveToOwner`, `setAmmoForSlot`, `needsAmmoForSlot`, `isSpecialWeapon`; `BattleUnit::setSpecialWeapon`
(with `updateFromSave`), `addLoadedSpecialWeapon`, `isIgnored`; `RuleItem::load` (`inventoryMoveCost`,
`fuseTriggerEvents`), `RuleItemFuseTrigger`; `Mod::getInventoryGround`; `YamlNodeWriter::write` of a vector (no key when
empty, the callback choosing the elements), `YamlNodeReader::operator[](size_t)`; `BattlescapeGenerator` (`setFuseTimer`
in `loadMAP`, the layouts and the pre-primed grenades, `sendItemsToLimbo`).

- `BattleSnapshot` gains `Items`, `ItemsSpecial`, `RecoverConditional` and `RecoverGuaranteed` (`BattleItemSnapshot`:
  every key `BattleItem::load` reads and `save` writes and those the load's passes read, with the rule values `save`'s
  conditions read: the rule's move cost, an inventory grid, rechargeable ammo, a medikit). A key the load takes from the
  constructor when absent is nullable; `Capture` fills it.
- `OxceBattleSave.Read` reads the four lists in the load's order. `type` and `id` are required (`readVal` without a
  default), the type first, so an item whose rule is gone is left out with a skip warning before its ID is read; an
  unknown `inventoryslot` is the ground (`STR_GROUND`; a corrupted save when the mods lack it) with a skip warning;
  `NULL` is none; only a ground item's `position` is read, x -1 being none; `inventoryMoveCost` counts only as a mapping
  holding `basePercent`; `ammoItemSlots`' first four entries are read, else `ammoItem`; `fuseEnabed` (sic); item tags
  as the root's (owner `BattleItem`, unknown skipped, `RuleList` refused). Each key follows `tryRead` (a present
  unreadable value fails naming it).
- `OxceBattleSave.Write` writes `items` and `itemsSpecial` after `units` (both whenever the battle has items, each a
  possibly empty sequence, as the reference's callback skips elements), `recoverGuaranteed` and `recoverConditional`
  after `currentAmbienceDelay` (only when not empty), each item over its source node by ID in `save`'s order and
  conditions: the units only when set, `inventoryMoveCost` only when it differs from the rule's, `inventoryX` and
  `inventoryY` only in an inventory grid, `position` only on a tile, `ammoqty` when non-zero or rechargeable, `ammoItem`
  and `ammoItemSlots` up to the last filled slot (-1 between), a medikit's quantities always, `fuseTimer` unless -1,
  the flags only when set.
- `BattleState` gains `ConstructItem` (the BattleItem constructor, shared with the generator: the rule's move cost and
  clip size, a medikit's quantities, the self-filled ammo slots), `Rules` (the battle's rules, for the capture's rule
  values) and `ConditionalRecoveredItems`; `BattleItem` gains the previous owner, corpse unit, inventory move cost, fuse
  enabled flag, `droppedOnAlienTurn` and script values. `BattleItem.SetFuseTimer` is `setFuseTimer` (the rule's
  `fuseTriggerEvents`: a primed fuse is enabled with the default behavior and neither trigger); the generator's map
  items, layout fuses and pre-primed grenades now go through it. `BattleState.MoveToOwner` is `moveToOwner` (an owner
  change makes the old owner, none included, the previous one), which the generator's `UnitEquipment.MoveToOwner` now
  calls. `RuntimeBattleItem` gained `InventoryMoveCostPercent` and `FuseTrigger` (the compiled cache's compiler revision
  is now 76).
- `BattleState.Restore` runs `RestoreItems` after the units. The first pass makes each item by its constructor and
  `BattleItem::load`, resolves `owner`, `previousOwner` and `unit` among the loaded units (`findUnitById`: none for a
  skipped unit), makes an owned item in no section a special built-in item of its owner (the first free of four slots; a
  fifth stays owned in none) and puts a ground item with a position on its tile; `NextItemId` is the highest ID (0 to
  start with) plus one. The units' second pass gives every unit that is neither dead nor ignored what
  `setSpecialWeapon(save, true)` gives (`UnitEquipment.RestoreSpecialWeapons`): with loaded special items, the armor's
  self-destruct item it lacks, in the first slot (the reference's index starts at 0; the item there stays owned, out of
  the slots); without any, the new unit's set. The generator's psi weapon check now reads the original faction, as the
  reference does. The second item pass loads `ammoItemSlots` (an ID naming no item is ignored), else `ammoItem` into
  slot 0 (an ID naming no item is a corrupted save), through `setAmmoForSlot` (only a weapon with ammo, in a slot it
  does not fill itself; the ammo leaves its tile, owner and section). Each list is then sorted by ID. A repeated ID, a
  ground position outside the map, an ID of `INT_MAX` and a special built-in firearm without a clip size (the reference
  throws) are corrupted saves.
- `Validate` checks a corpse's unit and a previous owner among the battle's units and both recovery lists against the
  battle's items and each other (checks the restore already guarantees, so they guard generated battles); a restored
  battle may hold an item held by nothing (a skipped unit's items, a ground item without a position, ammo nowhere),
  while a generated battle still needs exactly one holder per item. An owned item that also lies on a tile, one ammo
  item in two weapons and a weapon of the battle loaded with recovery-list ammo fail it (ADR 0039).
- `Capture`'s void test counts the ground items of both recovery lists too (`Tile::addItem` on load puts them in the
  tile's inventory), so their tiles are saved.
- ADR 0039 gains S4's entries: the corrupted-save cases, the skip warnings and the inventory order, the block style.
- The conformance reader checks the items: each list a sequence of mappings with a string `type` and an integer `id`,
  each ID once, the keys' value types, `inventoryMoveCost`'s `basePercent`, up to four `ammoItemSlots`, a ground
  position inside the map, and an `ammoItem` read without `ammoItemSlots` naming an item.

Tests: `BattleSaveItemTests` (generated items of four deployments written, conforming, restored and rewritten stable,
with the item counter and the units' special items; a loaded gun's clip, a rammer's special item in `itemsSpecial`, the
armory's fuses through `setFuseTimer`, the weapon pile in guaranteed recovery and the keys' order; every key round
tripping in `save`'s order; the constructor's values for what the save leaves out and `save`'s conditions; unknown,
`NULL`, ground, grid and hand slots with positions read or not, and one outside the map; owners, previous owners and
corpse units of skipped and missing units; four loaded special items and a fifth, an owned item in `items` moved to
`itemsSpecial`, the self-destruct item into the first slot, a new unit's set on load, dead and ignored units; ammo
slots over `ammoItem`, an unknown ammo slot ID, a self-filled slot, an item that takes no ammo, the older `ammoItem`
and one naming no item; an OXC `fuseTimer` with the default, proximity, throw and no-default triggers and `fuseEnabed`;
the recovery lists, the sort, the counter, negative IDs and a repeated ID; recovery-list items on the ground (their
tile saved) and owned (a special item, no inventory); an item whose rule is gone and a legacy `ammoItem` to it; 24
unreadable items naming their key; item tags; unknown item keys kept; `Validate`'s refusals (a clip with two holders, an
owned item on a tile, recovery-list ammo), an ID of `INT_MAX`, a special firearm without a clip size; the units'
second pass giving a mind-controlled psionic its psi weapon (not an originally neutral one) and a soldier saved without
special items the soldier rule's). The item rules give the soldiers (`RECRUIT`) a RAM. The root and map tests compare the
reread snapshot without its units and items (`BattleSaveItemTests.WithoutEntities`). `PrivateBattleSaveTests` now
restores and rewrites every corpus battle's items (semantic equality with the source, a byte-stable second rewrite).

## Next

S5 — campaign integration ([plan](phase-7-plan.md#branch-2--battle-saves)). Notes:

- `battleGame` restores into `CampaignBattles` through `OxceBattleSave.Read` and `BattleState.Restore(snapshot, content,
  terrain, ufoInBattlescape, BattleRestoreCampaign.From(campaign))`, and is written with
  `OxceBattleSave.Write(battle.Capture(), source)`. The "Active tactical battle is preserved" restriction
  (`OxceSaveAdapter.ReadRestrictions`) retires, and `battle.save-unsupported` (`CampaignBattles.BattleSaveUnsupported`)
  narrows to the named restriction on saving before the battle's first turn (branch 3 owns it).
- Retire `BattleState.SoldierBattleArmorRestriction`: `StartBattle` sets the soldiers' battle armor and
  `transformedArmor`/`replacedArmor` as `deployXCOM` does, and the restore takes the campaign soldier's armor.
- `StartBattle` sets the `inBattlescape` flags (`Craft`, `Ufo`, `MissionSite`, `Base`, `AlienBase`); the header's
  `mission`, `target`, `craftOrBase` and `turn` are written while a battle exists; the UFO in battle gives `Restore`'s
  `ufoInBattlescape`.
- `ReturnedToBase` and `TakenFromBase` are not saved; `StartBattle` applies them (the plan's table).
- Open from S4: a fifth loaded special item of one unit is not reported (the restore has no diagnostics sink), and an
  owned item of a recovery list keeps its owner without joining the unit's inventory (ADR 0039). The item tests' rules
  (`BattleSaveItemTests.ItemRules`) make the generated aliens rammers with a special built-in item.
- Gotchas carried over: write test files with LF (Python on Windows writes CRLF in text mode; use `newline='\n'` or
  the Edit tool); insert C# string literals with `\n` through the Edit tool or a script file written with the Write
  tool, not a Bash heredoc (a heredoc's Python sees `\\n` differently than expected, and some quote mixes fail to
  parse; a long heredoc with backticks can fail to parse at all); the test runner is Microsoft Testing Platform
  (`dotnet test --project <proj> --filter-class <full name>` or `--filter-method`). The battle test helpers
  `BattleStartTests.Requested`, `Started` and `Battle`, `BattleSaveRootTests.Generated`, `SharedContent`, `Parse`,
  `Emit`, `Normalized` and `Terrain`, `BattleSaveUnitTests.Loaded` and `AssertLoadedRewriteIsStable`,
  `BattleSaveItemTests.WithoutEntities`, and `BaseDefenseMapTests.Requested`, `Layout` and `MapOf` are internal for
  reuse. Rules added to the battle test rules must go into their existing lists (a second top-level `armors:` is
  ignored: the first key wins); see `BattleSaveUnitTests.UnitRules` and `BattleSaveItemTests.ItemRules`. A YAML integer
  of the port wraps like ryml's (`fire: 300` reads 44), so a wrong-type test needs a word or a sign; a YAML boolean
  accepts an integer (ryml's fallback). Restoring a generated battle needs `BattleRestoreCampaign.From(campaign)` for
  its soldiers.

# Battle save status

Phase 7 branch 2 (`claude/battle-save`), from `main` at `3c91029a` (PR #78, branch 1 merged). Plan:
[Phase 7 branch 2](phase-7-plan.md#branch-2--battle-saves); decisions: [ADR 0038](decisions/0038-battle-aggregate.md),
[ADR 0039](decisions/0039-battle-save-intentional-differences.md) and the plan's branch decisions.

## Progress

| Slice | State |
|---|---|
| S0 planning | Done (2026-10-25) |
| S1 save module and battle root | Done (2026-10-25) |
| S2 map | Not started |
| S3 units | Not started |
| S4 items | Not started |
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

## Next

S2 — map ([plan](phase-7-plan.md#branch-2--battle-saves)). Notes:

- Keys: `mapdatasets`, the tile key (`tileIndexSize` to `lastExploredByHostile`, `totalTiles`, `binTiles`) or the old
  text `tiles`, `nodes`, `moduleMap` (base defense only, with the `calculateModuleMap` fallback,
  `BattleMapGenerator.cs` has the port of it), `flattenedMapTerrainNames`, `flattenedMapBlockNames`,
  `reinforcementsBlocks`. Add them to `BattleSnapshot`, `OxceBattleSave.Read`/`Write` (in the reference's write order:
  `reinforcementsBlocks` and the flattened names go before `globalshade`, the map keys after `undoUnit`) and
  `BattleState.Restore`, which today builds an empty `BattleMap` of the saved size.
- The tile key sizes are one-character strings read as `char` (`tileIndexSize: "\x04"`); the port's YAML reader
  already decodes `\x04` to U+0004 and the writer emits it back (see `TheCraftLineKeepsItsControlCharacter`).
  Write them with `YamlScalarStyle.DoubleQuoted`, as `strCraftOrBase`.
- `loadMapResources` goes through `BattleTerrainLoader` (MCD patches); a restored battle then needs the terrain files
  (`campaign.BattleTerrainFiles`), so the module-level restore takes the loader. Lighting and field of view stay
  branch 4's.
- Extend `BattleSaveConformance` with the tile buffer rules (its size against `totalTiles` times `tileTotalBytesPer`,
  the index field) and run it on every written battle; `PrivateBattleSaveTests` already compares the whole
  `battleGame` node, so it shows each newly typed key.
- Add S2's quirk (old text tiles' `discovered`) to ADR 0039.
- S3 must resolve `selectedUnit` and `undoUnit` against the loaded units (`Restore` keeps the raw IDs today; the
  reference selects the first player unit that is not out even when the saved ID is -1, `SavedBattleGame.cpp:286-292`)
  and report the status sanity check.
- Gotchas carried over: write test files with LF (Python on Windows writes CRLF in text mode; use `newline='\n'` or
  the Edit tool); insert C# string literals with `\n` through the Edit tool, not a Bash heredoc; the test runner is
  Microsoft Testing Platform (`dotnet test --project <proj> --filter-class <full name>` or `--filter-method`). The
  battle test helpers `BattleStartTests.Requested`, `Started` and `Battle` are internal for reuse. A YAML boolean
  accepts an integer (ryml's fallback), so a wrong-type test needs a word.

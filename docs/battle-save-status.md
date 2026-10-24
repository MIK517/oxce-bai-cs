# Battle save status

Phase 7 branch 2 (`claude/battle-save`), from `main` at `3c91029a` (PR #78, branch 1 merged). Plan:
[Phase 7 branch 2](phase-7-plan.md#branch-2--battle-saves); decisions: [ADR 0038](decisions/0038-battle-aggregate.md)
and the plan's branch decisions.

## Progress

| Slice | State |
|---|---|
| S0 planning | Done (2026-10-25) |
| S1 save module and battle root | Not started |
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

## Next

S1 — save module and battle root ([plan](phase-7-plan.md#branch-2--battle-saves)). Notes:

- Where things are: `BattleState` and the `BattleUnit` class (`src/Oxce.Gameplay/Battles/BattleState.cs`), `BattleMap`,
  `BattleNode`, `BattleItem`, `CampaignBattles` (the save gate and the battle's lifetime). The strategic save is
  `src/Oxce.Savegames/Oxce/OxceSaveAdapter.cs`, with `OxceWorldSave.cs` and `OxceResearchDiarySave.cs` as the pattern
  for a separate module; `ReadScriptValues` reads `tags` through the compiled tag catalog (owners `BattleGame`,
  `BattleUnit`, `BattleItem` in the reference). The opaque `battleGame` is preserved by the overlay of the source
  document today; `ReadRestrictions` adds the preservation restriction.
- Root keys branch 1 already holds: size, mission type, turn, turn limit, chrono trigger, cheat turn, map script
  (`lastUsedMapScript`), alien custom deploy and mission, alien item level, depth, global shade, selected
  unit, starting condition, enviro effects, the three `ecEnabled*`, music, bughunt mode and minimum turn, VIP and
  objective settings, reinforcements, `BaseStorageItems` (`baseItems`), the base modules. Not modelled: `strTarget`,
  `strCraftOrBase`, `nameDisplay`, `animFrame`, `undoUnit`, `reinforcementsMemory`, `tuReserved`, `kneelReserved`,
  the ambience keys, `vipsSaved` and the other VIP counters, `objectivesDestroyed`, the three toggles, root `tags`.
  `DeploymentId` has no key: derive it on load as the reference's readers do (the plan's table of unsaved state);
  `TerrainId` is dropped.
- The reference writes the root keys unconditionally except the VIP block (only when `vipSurvivalPercentage > 0`),
  the objective block (only when `objectivesNeeded`), `startingConditionType` and `enviroEffectsType` (only when set)
  and `moduleMap` (base defense only). `selectedUnit` is read without a default.
- Values are written in odd forms the reader must accept: the tile key sizes as one-character strings
  (`tileIndexSize: "\x04"`, read as `char`), and `strCraftOrBase` with a control character
  (`"CRAFT> \x01SKYRANGER-1"`). The port's YAML reader must keep both byte-exact.
- The reference quirks (duplicate `turnsSinceSpotted`, `turnsLeftSpottedForSnipers` and `turnsSinceStunned` with
  pointer values such as `0x4fc5f294`; nine `armor` sides with uninitialized values such as `-522133280`; old text
  tiles' `discovered`; `fuseEnabed`) are decided in the plan's branch decisions; S1 writes the ADR (an amendment of
  ADR 0038 or a new one) that the later slices add their lines to.
- The conformance reader (S6) must cite the C++ for each rule it applies, so it does not only confirm the port's own
  writer. S1 can start it with the root keys.
- Gotchas carried over from branch 1: write test files with LF (Python on Windows writes CRLF in text mode; use
  `newline='\n'` or the Edit tool); insert C# string literals with `\n` through the Edit tool, not a Bash heredoc; the
  test runner is Microsoft Testing Platform (`dotnet test --project <proj> --filter-class <full name>` or
  `--filter-method`).

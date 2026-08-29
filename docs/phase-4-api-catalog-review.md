# Phase 4 exact API catalog review

Review date: 2026-08-29.

## Outcome

The pinned reference engine now supplies the executable declaration catalog used by the
managed scripting compiler. A synthetic probe constructs all eight public `ModScript`
registration groups and captures `ScriptParserBase::logScriptMetadata` after template
and helper expansion. The normalized result contains:

- 60 concrete parser instances across the `unit`, `item`, `bonuses`, `skill`, `country`,
  `ufo`, `craft`, and `soldier` groups;
- 755 unique name-and-signature overloads with exact parser membership;
- 132 named integer constants;
- 94 emitted type forms, including writable registers, reference/editable-reference
  forms, custom tags, text, `Position`, null pointers, and the `__` separator;
- zero unresolved declarations.

Stable binding IDs start at 10000 and are assigned after ordinal sorting by operation
name and complete emitted parameter signature. The compact canonical fixture is embedded
as the single runtime catalog source; the managed loader validates its declared counts,
unique IDs, types, parser values, memberships, and zero-unresolved invariant. This avoids
a second generated declaration file that could drift from the reviewed ledger.

## Capture method

The probe links the registration-owning sources needed to instantiate the public parser
groups. Gameplay provider bodies are never called while metadata is emitted. The capture
therefore permits unresolved provider symbols for this metadata-only executable; ordinary
behavioral probes remain strict. The wrapper pins the reference revision, bounds all
additional sources to that checkout, captures raw logs, and normalizes them into canonical
JSON.

The authoritative registration sources exercised are:

- `src/Engine/Script.cpp`, `Script.h`, and `ScriptBind.h`;
- `src/Mod/Mod.cpp`, `ModScript.h`, `Armor.cpp`, `Unit.cpp`, `RuleCountry.cpp`,
  `RuleCraft.cpp`, `RuleInventory.cpp`, `RuleItem.cpp`, `RuleResearch.cpp`,
  `RuleSkill.cpp`, `RuleSoldier.cpp`, `RuleSoldierBonus.cpp`, `RuleStatBonus.cpp`,
  `RuleUfo.cpp`, and `RuleUnit.cpp`;
- `src/Savegame/BattleItem.cpp`, `BattleUnit.cpp`, `Country.cpp`, `Craft.cpp`,
  `SavedBattleGame.cpp`, `SavedGame.cpp`, `Soldier.cpp`, `Tile.cpp`, and `Ufo.cpp`;
- `src/Ufopaedia/StatsForNerdsState.cpp`.

The committed evidence is `script-api-catalog`. Regenerate an ignored comparison with
`tools/capture-script-api-reference.ps1`; updating the expected file remains an explicit
review action.

## Managed boundary

Catalog parser definitions now expose typed outputs and inputs. The compiler can construct
a parser directly by its exact catalog name, allocate custom/reference parameter layouts,
rewrite a dotted value receiver to its declared custom type, match writable/reference
arguments, and recognize `__` as the separator pseudo-type. A declared gameplay call still
compiles without a provider and fails intentionally as a missing runtime capability, as
required by ADR 0013.

This branch declares APIs; it does not implement gameplay providers. Separator-driven
nested call expansion, full text/reference host-frame storage, exact overload scoring in
all mixed pointer cases, and compilation of every preserved mod script remain acceptance
work for the content-closure branch and later owning gameplay slices.

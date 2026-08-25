# Phase 3 status

## Mod-loading foundation

The Phase 3 mod-loading foundation is implemented for directory- and ZIP-backed mods.
It discovers `metadata.yml`, applies OXCE defaults, retains the reference version
comparison rules, rejects malformed or duplicate metadata, removes missing-master and
cyclic dependency chains, filters enabled mods for the selected master, expands
transitive master chains, and produces deterministic resource and ruleset load order
with structured diagnostics.

Single-mod ZIPs use root `metadata.yml`; multi-mod ZIPs use the reference layout with
explicit top-level directory entries and one `metadata.yml` immediately below each.
Entries are opened lazily through the virtual-file API. Archive entry count, path depth,
path length, compressed size, per-entry expanded size, and total expanded size are
bounded. Unsafe rooted, traversal, empty-segment, drive, and URI-like paths are skipped.
Archives are discovered before directories, so the reference's first-discovered
duplicate-ID behavior is retained.

Top-level masters now map every `loadResources` name from explicitly ordered external
resource roots. For each name the first matching ZIP and first matching directory are
mapped, rulesets are ignored, loose files override the archive, earlier resource names
override later names, and the owning mod overrides all external layers. A missing or
unusable required resource removes the master and its dependent chain.

Persisted activation reconciliation removes missing entries, moves masters before
add-ons, adds newly discovered mods disabled, honors a still-available preferred master,
repairs multiple enabled masters, and selects `xcom1`, then `xcom2`, then the first
available master when automatic selection is required. The resulting state feeds the
load planner directly.

The planner now requires a composition-provided `ModEngineIdentity` and enforces
`requiredExtendedEngine` plus the reference four-component
`requiredExtendedVersion` comparison. It does not hard-code the reference C++ build's
identity into the platform-independent mod library.

The executable synthetic fixture is under `fixtures/public/mods/load-order`. It covers a
master that depends on another master, an add-on, a mod for a different master, resource
overlay provenance, and descending ruleset filename order. Compatibility tests also scan
every top-level directory with `metadata.yml` in the optional `fixtures/private/mods`
corpus without committing private mod data. Unit fixtures create bounded single- and
multi-mod archives and external directory/archive layers at runtime from synthetic text.

The first generic ruleset-composition slice is also implemented. Typed rule families
register their section name and identity key, and the composer replays the compatible
file order into an unresolved named-rule registry. It implements ordinary
update-or-create declarations plus `new`, `override`, `update`, `delete`, and `ignore`,
preserves reference insertion/deletion ordering, records creation and update provenance,
rejects conflicting or missing main nodes, requires one mapping document per ruleset,
and bounds total operations and nested `refNode` mappings. Surviving rules retain their
ordered YAML operation history so typed loaders—not a speculative generic map merge—own
property-level semantics. The executable fixture is under
`fixtures/public/mods/rule-operations`.

Authoritative C++ references inspected for this slice:

- `src/Engine/ModInfo.cpp`: `ModInfo::ModInfo`, `ModInfo::load`,
  `normalizeModVersion`, `compareVersions`, `ModInfo::canActivate`, and
  `ModInfo::isParentMasterOk`.
- `src/Engine/FileMap.cpp`: `scanModDir`, `checkModsDependencies`, `drop_mods`,
  `FileMap::setup`, `VFS::push_back`, and the layer/ruleset aggregation helpers.
- `src/Engine/Options.cpp`: `refreshMods`, `updateMods`, and `getActiveMods`.
- `src/Engine/CrossPlatform.cpp`: `parseVersion` and `isHigherThanCurrentVersion`.
- `src/Mod/Mod.cpp`: `Mod::loadAll` and `Mod::loadMod`; the latter sorts each mod's
  rulesets by descending full path immediately before parsing.
- `src/Mod/Mod.cpp`: `Mod::loadFile`, `Mod::loadRule`, `loadRuleInfoHelper`, and
  `refNodeTestDeepth` for named-rule dispatch, lifecycle markers, index ordering, soft
  failures, and the 64-level `refNode` guard.
- Individual `src/Mod/Rule*.cpp::load` methods, including `RuleItem::load`, confirm that
  property merging and `refNode` application belong to typed loaders rather than a
  universal YAML merge.

## Typed-loader core

The typed-loader core is implemented. Its central registry matches all 38 C++
`loadRule`-dispatched sections and separately classifies the eight non-generic special
sections by replacement, merge, append, or append/delete behavior. The default ruleset
composer uses that registry while still allowing isolated synthetic families in tests.

Typed family loaders replay surviving rule operations in source order, create immutable
ordered sections with creation/last-update provenance, and require each family to opt
into `refNode` recursion exactly where its C++ loader does. Property readers preserve
missing-value defaults, expose the owned YAML conversions, bound input property nodes,
report unconsumed keys as errors, and distinguish deliberately deferred script fields
with their own structured diagnostic. Composed and typed capabilities are reported
separately; later slices advance linking, resource resolution, and script compilation.

`Oxce.FixtureTool dump-rules` emits a bounded, deterministic `composed`-stage JSON dump
with section identities, rule order, operation kinds, semantic YAML nodes, and normalized
source provenance. The `typed-rule-replay` fixture captures family-owned recursive
inheritance plus scalar preservation and sequence/map replacement from the pinned C++
reference. It is intentionally a loader-contract fixture rather than the first concrete
game rule family.

Additional authoritative C++ references inspected for this slice:

- `src/Mod/Mod.cpp`: `Mod::loadResourceConfigFile`, `Mod::loadFile`, and `Mod::loadRule`
  for the complete section registry, identity keys, lifecycle dispatch, and operation
  order.
- `src/Mod/RuleItemCategory.cpp`: `RuleItemCategory::load` for family-owned recursive
  `refNode` application, missing-value preservation, and property replacement.
- `src/Engine/Yaml.h` and the pinned rapidyaml integration for generic scalar,
  sequence, and map reads used by the executable oracle probe.

## Presentation and resource declarations

The first concrete typed-content slice is implemented. It loads immutable interface,
music, sound-definition, custom-palette, and cutscene rules with their reference
defaults and family-specific incremental behavior. Interface `refNode` recursion and
element-by-ID updates are owned by the interface loader; music fields preserve missing
values; sound ranges and sound lists append; palette maps replace; and cutscene video,
audio, and slide lists append while `useUfoAudioSequence` resets on every operation as
in the reference engine.

The special `extraStrings`, `extraSprites`, and `extraSounds` sections are also composed.
Plural strings flatten to suffixed keys and later language entries overlay earlier
values. Sprite declarations retain per-mod provenance, legacy `typeSingle`/`fileSingle`
defaults, append by type, and delete a type's accumulated declaration list. Sound
declarations append with provenance. Explicit sprite, sound, palette, and sound-catalog
paths can be checked against the platform-neutral virtual-file catalog with structured
missing-resource diagnostics.

Metadata-selected resource-configuration rulesets are also replayed into a separate
preload sound-definition view before the complete ruleset view. This preserves the
reference distinction needed to configure original CAT loading without hiding the
file's later ordinary ruleset replay.

This slice deliberately does not claim the linked or resources-resolved capability.
Interface sound references preserve scalar or `{index, mod}` ownership, but their final
offset depends on the target sound set's shared range plus each mod's reserved offset.
That calculation and decoded resource publication belong to the reference-ordered link
and resource pass after the remaining set declarations exist.

The executable `presentation-rules` fixture was captured from the pinned C++ YAML
semantics. It covers recursive interface element merge, music defaults, cutscene
append/reset behavior, plural-string flattening, and sprite delete/re-add defaults.

Additional authoritative C++ references inspected for this slice:

- `src/Mod/RuleInterface.cpp` and `.h` for recursive inheritance, element defaults and
  merge behavior, background/music fields, and `GEO.CAT` sound references.
- `src/Mod/RuleMusic.cpp`, `RuleVideo.cpp`, `SoundDefinition.cpp`, and
  `CustomPalettes.cpp` for defaults and append/replacement behavior.
- `src/Mod/ExtraStrings.cpp`, `ExtraSprites.cpp`, and `ExtraSounds.cpp` for special
  section composition, legacy aliases, file/folder declarations, and mod ownership.
- `src/Mod/Mod.cpp`: `loadOffsetNode`, `loadSpriteOffset`, `loadSoundOffset`,
  `loadResourceConfigFile`, and `loadExtraResources` for offset validation and resource
  load ordering.
- `src/Language/Language.cpp`: `Language::loadRule` for language-specific overlay.

## Deliberate remaining Phase 3 work

Phase 3 closes through four explicit gates rather than treating metadata discovery as
a fully loaded mod:

1. **Typed loading infrastructure (foundation complete):** extend the core with each
   concrete family's property contract while retaining source provenance, explicit
   consumed/deferred keys, and bounded typed loading.
2. **Typed content:** resource/interface/localization declarations are complete;
   campaign-start, strategic, tactical, terrain/deployment, mission/event, and global
   rule families remain.
3. **Link and resource resolution:** publish immutable rule catalogs only after the
   reference-ordered cross-link, validation, derived-rule, cache, and sorting passes
   complete. Resource declarations remain platform-neutral descriptors.
4. **Corpus closure:** compare normalized typed-rule dumps, load the bundled standard
   mods, and audit at least one large script-heavy private mod with stable manifests.

Content loading reports distinct `composed`, `typed`, `linked`,
`resources-resolved`, and `scripts-compiled` capabilities. Phase 3 must preserve and
diagnose script-bearing nodes without silently claiming that they compile; compatible
script compilation and execution remain the Phase 4 gate.

OXCE metadata does not define general multi-dependency or conflict collections. Its
compatibility relationship is the single `master` chain plus optional required master
version. Port-specific dependency/conflict schema extensions remain deferred so they do
not become an accidental compatibility requirement.

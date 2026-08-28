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

## Campaign-start rules

The campaign-start typed slice is implemented for countries, extra globe labels,
regions, and base facilities. Geographic degrees are converted to radians at load time,
inverted latitude bounds are normalized, country and region area append/reset behavior
is retained, mission zones replace as a collection, and mission weights update by key
with zero removing an entry. Country script nodes and dynamically registered script
values are retained as explicit Phase 4 deferrals rather than discarded.

Facilities retain reference constructor defaults, recursive `refNode` application,
legacy `size`, editable `!add`/`!remove` name collections, mod-owned sprite and sound
references, incremental build/refund item costs, creation-derived `listOrder`, storage
and craft positions, replacement relationships, and other campaign-visible capacity and
cost fields. Creation ordinals now live in the generic unresolved rule model, including
gaps left by deleted rules; this also supplies compatible default ordering to later
craft, item, armor, research, and manufacture loaders. Facility vertical map-level
definitions are preserved with an explicit deferral to the terrain/deployment slice.

Starting-base templates are preserved as immutable YAML mappings and use the reference
shallow overlay: keys in a later template replace earlier values while missing top-level
keys are inherited. Difficulty-specific templates fall back to the default. Starting
time, difficulty, funding, personnel costs and timing, transfer-cost factors, campaign
research gates, base-function requirements, defeat thresholds, and base/operation name
parts retain their reference defaults and per-file updates.

Internal facility validation reports missing replacement/build-over references,
destroyed-facility size mismatches, invalid replacement layouts, missing map names,
out-of-area storage tiles, missing global destroyed-facility references, and the
128-name base-function limit with source provenance where available. This catalog stays
at the typed capability: event, research, item, sprite, and sound targets cannot be fully
linked until their owning slices are loaded.

The executable `campaign-start-rules` fixture covers geographic conversion and merge,
weighted mission updates, deleted-rule ordering gaps, facility defaults, shallow
starting-base overlay, and partial starting-time/global updates against the pinned C++
reference semantics.

Additional authoritative C++ references inspected for this slice:

- `src/Mod/RuleCountry.cpp` and `.h` for country/globe-label defaults, areas, base
  functions, event links, and script envelopes.
- `src/Mod/RuleRegion.cpp` and `.h`, plus `src/Savegame/WeightedOptions.cpp`, for area
  reset, mission-zone conversion, mission weights, and region selection values.
- `src/Mod/RuleBaseFacility.cpp` and `.h`, plus `src/Mod/MapScript.h`, for facility
  defaults, editable properties, incremental costs, derived ordering, and after-load
  validation.
- `src/Mod/Mod.cpp`: `RuleListOrderedFactory`, `loadBaseFunction`, `loadNames`,
  `loadUnorderedNames`, the global `loadFile` campaign fields, and new-game setup.
- `src/Engine/Yaml.cpp`: `YamlNodeReader::emitDescendants` for shallow template overlay.
- `src/Savegame/GameTime.cpp` and `src/Menu/NewGameState.cpp` for starting-time defaults
  and difficulty-specific starting-base selection.

## Item rules

The item family is implemented as a separate prerequisite slice because its declaration
contract is larger than the craft, UFO, research, and manufacture families combined.
Items retain constructor defaults, recursive `refNode` application, deletion-aware
creation/list ordering, strategic purchase/recovery fields, inventory dimensions and
restrictions, medikit and AI values, targeting/spawn configuration, and environmental
restrictions. Editable research, category, base-function, inventory-section, recovery,
zombie-unit, and compatible-ammo collections preserve replacement, `!add`, and
`!remove` behavior.

All four ammo slots retain compatible names and load/unload costs. Aimed, auto, snap,
and melee actions retain ranges, accuracy, shot counts, ammo-slot bounds, optional ammo
overrides, six-component costs, flat/percentage flags, and configuration names. Global
use, mind-control, panic, throw, prime, and unprime costs preserve nullable component
semantics. Battle-type assignment applies the reference side effects for craft-equipment
visibility, psi requirements/range/dropoff/targeting, fuse defaults, melee ammo use, and
corpse damage. Damage and melee-damage declarations retain predefined-type identity plus
ordered `blastRadius` and alteration overlays; an untyped default profile retains the
reference `RuleDamageType` constructor values.

Sprite, sound, animation, transparency, and preview indexes retain the declaring mod ID
until shared resource ranges can resolve their final offsets. Internal validation covers
zero-clip weapon ammo declarations, item spawn/ammo references, and recovery
transformation restrictions. Research, unit, inventory, item-category, built-in damage,
and resource links remain deferred, so the catalog deliberately reports only the typed
capability. Stat-bonus, item-event, and dynamically registered script nodes are preserved
with Phase 4 deferral diagnostics.

The executable `item-rules` fixture covers constructor and battle-type defaults,
recursive inheritance, editable collection updates, deleted-rule ordering gaps, action
costs, damage overlays, and partial update behavior against the pinned C++ reference
semantics.

Additional authoritative C++ references inspected for this slice:

- `src/Mod/RuleItem.cpp` and `.h` for constructor defaults, property order, battle-type
  side effects, action/ammo parsing, and after-load invariants.
- `src/Mod/RuleDamageType.cpp` and `.h` for default damage profiles and alteration keys.
- `src/Mod/RuleStatBonus.cpp` and `src/Mod/ModScript.h` for stat-bonus and item-script
  envelopes retained for Phase 4.
- `src/Mod/Mod.cpp`: editable name/map loaders, base functions, nullable names, and
  sprite/sound/transparency offset ownership.

## Equipment and production rules

The equipment/production slice implements typed item-category, weapon-set,
craft-weapon, craft, UFO, research, manufacture, and manufacture-shortcut declarations.
It preserves constructor defaults, recursive `refNode` updates, deletion-aware list
ordering, editable sequence/map behavior, fixed craft weapon-slot expansion, craft and
UFO stat overlays, UFO size normalization and blob limits, research protected-topic
groups, incremental weighted events, manufacture product defaults, random products,
and shallow spawned-soldier template overlays.

Relationship validation covers category replacement, weapon-set items, craft-weapon
launcher/clip and ammunition invariants, fixed craft weapons and refuel items, research
graphs and the requirements/cost invariant, manufacture inputs/outputs and positive
time, and manufacture-shortcut sources. These checks consume the item catalog from the
previous slice while keeping the catalog capability at `typed`; final immutable linking
and manufacture-shortcut derivation remain part of the reference-ordered closure pass.

The executable `equipment-production-rules` fixture covers rule deletion and ordering
gaps, editable inventory-order and required-item updates, craft weapon-slot expansion,
UFO normalization and capping, research event removal/addition, and manufacture maps
against the pinned C++ reference semantics. Terrain, deployment, pilot, resource-offset,
event, personnel, and script links are retained or explicitly deferred to their owning
slices.

Additional authoritative C++ references inspected for this slice:

- `src/Mod/RuleItemCategory.cpp` and `.h`, and `src/Mod/RuleWeaponSet.cpp` and `.h`,
  for defaults, editable collections, and item links.
- `src/Mod/RuleCraftWeapon.cpp` and `.h`, and `src/Mod/RuleCraft.cpp` and `.h`, for
  weapon/craft defaults, stat overlays, slot expansion, resource ownership, derived
  capacities, and after-load invariants.
- `src/Mod/RuleUfo.cpp` and `.h` for constructor defaults, size normalization, blob
  limits, root/race stat overlays, and marker/sound ownership.
- `src/Mod/RuleResearch.cpp` and `.h`, `src/Mod/RuleManufacture.cpp` and `.h`, and
  `src/Mod/RuleManufactureShortcut.cpp` and `.h` for editable graphs/maps, protected
  free research, weighted events, shallow soldier overlays, and shortcut declarations.
- `src/Savegame/WeightedOptions.cpp`, `src/Engine/Yaml.cpp`, and `src/Mod/Mod.cpp` for
  incremental weights, shallow overlay, creation ordinals, editable loaders, link order,
  and manufacture-shortcut derivation.

## Personnel and tactical-unit rules

The personnel/tactical slice implements typed inventories, armors, skills, soldier
types, battlescape units, soldier bonuses, soldier transformations, and commendations.
It preserves each family's creation-order increment, recursive `refNode` updates,
editable collections and maps, signed 16-bit unit-stat behavior, nonzero stat merging,
inventory hand identity, armor movement and damage modifiers, armor-size immunity side
effects, shallow spawned-soldier overlays, skill cost/flat declarations, weighted
built-in weapon sets, transformation stat bounds and events, and commendation criteria.

Relationship validation covers inventory movement-cost targets; armor corpse, item,
soldier, research, commendation, and bonus references; skill items and bonuses; soldier
armor, special-weapon, and skill links; unit armor, spawn, recovery, and built-in weapon
links; transformation inputs and outputs; and commendation research, unit, and bonus
links. The validation result also publishes the reference-derived armor eligibility and
unique storage-item caches without claiming that the typed catalog itself is fully
linked.

The executable `personnel-tactical-rules` fixture covers deletion-aware 10-, 100-, and
1-step ordering, inventory hand/cost data, armor stat and movement overlays, skill mode
normalization, soldier stat-cap fallback and name-pool reset, unit built-in weapon
weights, soldier bonuses, transformation event removal, and commendation criteria
against the pinned C++ reference semantics. Sound/sprite indexes retain declaring mod
ownership. Name-pool file decoding, layer surface/resource verification, stat-bonus
expressions, tactical event scripts, and dynamic script values remain explicitly
deferred to resource closure or Phase 4.

Additional authoritative C++ references inspected for this slice:

- `src/Mod/RuleInventory.cpp` and `.h` for slot/cost replacement, hand identity, and
  10-step deletion-aware ordering.
- `src/Mod/Armor.cpp` and `.h`, plus `src/Mod/RuleStatBonus.cpp`, for armor defaults,
  nullable immunities, size effects, movement costs, damage/stat overlays, resource
  ownership, relationship checks, and eligibility/storage caches.
- `src/Mod/RuleSkill.cpp` and `.h`, and `src/Mod/RuleItem.h`, for target/battle-type
  normalization, nullable six-component costs, flat flags, and item/bonus links.
- `src/Mod/RuleSoldier.cpp` and `.h`, `src/Mod/Unit.cpp` and `.h`, for signed unit stats,
  merge semantics, one-step soldier ordering, shallow templates, weighted weapons,
  resource ownership, height limits, and after-load invariants.
- `src/Mod/RuleSoldierBonus.cpp` and `.h`, `src/Mod/RuleSoldierTransformation.cpp` and
  `.h`, and `src/Mod/RuleCommendations.cpp` and `.h` for bonus stats, transformation
  bounds/events, editable requirements, award criteria, and cross-links.
- `src/Mod/Mod.cpp`, `src/Engine/Yaml.cpp`, and `src/Savegame/WeightedOptions.cpp` for
  editable loaders, shallow overlays, reference link order, derived caches, and
  incremental weights.

## Terrain and deployment rules

The terrain/deployment slice implements terrain declarations, map-script command lists,
MCD patches, alien races, environmental effects, starting conditions, and alien
deployments. Keeping these families together makes their references executable in one
catalog: terrains and deployments select map scripts and environmental effects;
deployments select terrains, races, and starting conditions; and map scripts contain
the commands that make those selections observable.

The slice preserves constructor defaults, recursive `refNode` updates, editable
collections, special-section replacement/merge behavior, command-specific map-script
defaults and validation, and declaring-mod ownership for resource offsets. It adds
relationship diagnostics and derived lookups without advancing beyond the `typed`
capability. Map/MCD binary decoding, resource-offset resolution, random selection,
battlescape execution, and script-language compilation remain owned by later resource,
Phase 4, or Phase 5 work.

The public layered fixture and pinned C++ oracle cover terrain
updates, map-script replacement and deletion, MCD-patch merging, alien-race weights,
environment and starting-condition overlays, deployment defaults, and cross-family
references. Malformed commands, bounded collection inputs, and missing relationship
targets require focused unit coverage.

Authoritative C++ references for this slice:

- `src/Mod/RuleTerrain.cpp` and `.h`, `src/Mod/MapBlock.cpp` and `.h`, and
  `src/Mod/MapScript.cpp` and `.h` for terrain defaults, map-block updates, command
  parsing, and map-script selection.
- `src/Mod/MCDPatch.cpp` and `.h`, `src/Mod/MapData.cpp` and `.h`, and
  `src/Mod/MapDataSet.cpp` and `.h` for MCD patch declarations and their later
  application to decoded map data.
- `src/Mod/AlienRace.cpp` and `.h`, `src/Mod/RuleEnviroEffects.cpp` and `.h`, and
  `src/Mod/RuleStartingCondition.cpp` and `.h` for weighted races, environmental
  conditions, editable restrictions, and after-load links.
- `src/Mod/AlienDeployment.cpp` and `.h` for deployment defaults, nested deployment
  data, map and environment selection, mission transitions, and tactical parameters.
- `src/Mod/Mod.cpp`, `src/Mod/LoadYaml.h`, and `src/Savegame/WeightedOptions.cpp` for
  section dispatch, special-section composition, editable helpers, link order, and
  incremental weights.

## Mission, event, and ufopaedia rules

The mission/event slice implements UFO trajectories, alien mission definitions, arc scripts,
event scripts, geoscape events, recurring and ad-hoc mission scripts, and ufopaedia
articles. Grouping these families closes the strategic-content graph in one CI run:
mission scripts select alien missions, races, and regions; alien missions select UFOs,
trajectories, and deployments; event scripts select events; and articles link the
resulting content to player-visible reference material.

The slice preserves constructor defaults, recursive `refNode` updates, weighted
timeline behavior, shallow spawned-soldier overlays, mission-script invariants,
ufopaedia replacement/deletion and list ordering, and lossless script-bearing nodes.
Relationship checks cover the already typed campaign, item, equipment, personnel,
terrain, and deployment catalogs without claiming script compilation or full immutable
link closure.

The layered public fixture and pinned C++ oracle cover trajectory
replacement, mission weights and side effects, arc/event selections, mission-script
weight timelines and validation, event overlays, and ufopaedia update/deletion.
Malformed waypoint/article/script shapes and missing cross-family references require
focused unit coverage.

Authoritative C++ references for this slice:

- `src/Mod/UfoTrajectory.cpp` and `.h`, and `src/Mod/RuleAlienMission.cpp` and `.h`
  for trajectory defaults, mission waves, weighted timelines, and retaliation effects.
- `src/Mod/RuleArcScript.cpp` and `.h`, `src/Mod/RuleEventScript.cpp` and `.h`, and
  `src/Mod/RuleMissionScript.cpp` and `.h` for strategic-script defaults, trigger maps,
  weighted selections, recurrence constraints, and ad-hoc reuse.
- `src/Mod/RuleEvent.cpp` and `.h` for event defaults, item/personnel payloads,
  research links, and shallow spawned-soldier templates.
- `src/Mod/ArticleDefinition.cpp` and `.h` for article type dispatch, shared and
  type-specific properties, page inheritance, deletion, and list ordering.
- `src/Mod/Mod.cpp`, `src/Engine/Yaml.cpp`, and `src/Savegame/WeightedOptions.cpp` for
  section order, special ufopaedia composition, shallow overlay, link/sort passes, and
  incremental weights.

## Phase 3 corpus closure

The final Phase 3 branch will add an aggregate content loader and a bounded normalized
typed manifest. It will load all family catalogs, run local and cross-family validation
in reference dependency order, and advance to `linked` only when the complete typed
graph is valid. Resource resolution and script compilation remain independent gates.

Closure acceptance requires deterministic whole-catalog manifests, all public mod
fixtures loading through the aggregate path, optional private mod-corpus coverage, and
a benchmark for aggregate load plus manifest generation. ADR 0012 records why closure
wraps the existing family catalogs instead of creating a duplicate reflection-shaped
compatibility model.

## Deliberate remaining Phase 3 work

Phase 3 closes through four explicit gates rather than treating metadata discovery as
a fully loaded mod:

1. **Typed loading infrastructure (foundation complete):** extend the core with each
   concrete family's property contract while retaining source provenance, explicit
   consumed/deferred keys, and bounded typed loading.
2. **Typed content:** resource/interface/localization, campaign-start, item,
   equipment/production, personnel/tactical, terrain/deployment, and mission/event
   declarations are complete; only focused global-table closure remains.
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

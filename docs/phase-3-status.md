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

## Deliberate remaining Phase 3 work

Phase 3 closes through four explicit gates rather than treating metadata discovery as
a fully loaded mod:

1. **Typed loading infrastructure:** register every supported section and identity
   key, replay property-specific inheritance from unresolved operation histories,
   retain source provenance, diagnose unconsumed keys, and bound typed loading.
2. **Typed content:** implement resource/interface/localization, campaign-start,
   strategic, tactical, terrain/deployment, mission/event, and global rule families.
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

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

## Deliberate remaining Phase 3 work

- Implement generic ruleset parsing/merging, resource configuration, typed rules, and
  rule linking. The completed mod-loading slice produces their compatible input order.

OXCE metadata does not define general multi-dependency or conflict collections. Its
compatibility relationship is the single `master` chain plus optional required master
version. Port-specific dependency/conflict schema extensions remain deferred so they do
not become an accidental compatibility requirement.

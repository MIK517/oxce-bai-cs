# Phase 3 status

## Mod-loading foundation

The first Phase 3 vertical slice is implemented for directory-backed mods. It discovers
`metadata.yml`, applies OXCE defaults, retains the reference version comparison rules,
rejects malformed or duplicate metadata, removes missing-master and cyclic dependency
chains, filters enabled mods for the selected master, expands transitive master chains,
and produces deterministic resource and ruleset load order with structured diagnostics.

The executable synthetic fixture is under `fixtures/public/mods/load-order`. It covers a
master that depends on another master, an add-on, a mod for a different master, resource
overlay provenance, and descending ruleset filename order. Compatibility tests also scan
every top-level directory with `metadata.yml` in the optional `fixtures/private/mods`
corpus without committing private mod data.

Authoritative C++ references inspected for this slice:

- `src/Engine/ModInfo.cpp`: `ModInfo::ModInfo`, `ModInfo::load`,
  `normalizeModVersion`, `compareVersions`, `ModInfo::canActivate`, and
  `ModInfo::isParentMasterOk`.
- `src/Engine/FileMap.cpp`: `scanModDir`, `checkModsDependencies`, `drop_mods`,
  `FileMap::setup`, `VFS::push_back`, and the layer/ruleset aggregation helpers.
- `src/Engine/Options.cpp`: `refreshMods`, `updateMods`, and `getActiveMods`.
- `src/Mod/Mod.cpp`: `Mod::loadAll` and `Mod::loadMod`; the latter sorts each mod's
  rulesets by descending full path immediately before parsing.

## Deliberate remaining work

- Discover single- and multi-mod ZIP containers and provide stream-backed virtual-file
  entries. Directory-backed mods are the only discovery source in this slice.
- Map top-level masters' `loadResources` directories/archives. The field is parsed with
  the reference top-level-master restriction, but external layers are not resolved yet.
- Reconcile persisted activation settings and automatic master selection. The planner
  currently accepts the already-selected active master and ordered activation list.
- Enforce `requiredExtendedEngine` and `requiredExtendedVersion` against application
  build identity. The metadata is retained; composition does not yet expose a stable
  engine-identity service.
- Implement generic ruleset parsing/merging, resource configuration, typed rules, and
  rule linking. This slice only produces their compatible input order.

OXCE metadata does not define general multi-dependency or conflict collections. Its
compatibility relationship is the single `master` chain plus optional required master
version. Port-specific dependency/conflict schema extensions remain deferred so they do
not become an accidental compatibility requirement.

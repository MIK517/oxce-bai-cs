# ADR 0023: Hot runtime content and versioned campaign capabilities

Status: accepted, 2026-09-04.

## Context

`RuntimeContent` retained both dense gameplay projections and the complete typed Phase 3
catalog. The catalog retained known compatibility fields, deferred YAML, source
provenance, and a second runtime compatibility sidecar. Gameplay and save restoration
used only dense runtime rules, compiled scripts, tags, resource descriptors, and stable
external IDs. On staged 40k/Rosigma content, retaining both representations kept about
168.6 MiB alive before the strategic and tactical models had begun to grow.

The experimental managed-extension API also exposed one combined campaign access
interface. Adding personnel, inventory, craft, transfer, research, and tactical APIs to
that object would make unrelated extensions depend on one monolithic contract.

The compatibility lifetime references remain `src/Mod/Mod.cpp` (`Mod::loadMod`,
`Mod::loadAll`, `loadModData`, and `loadExtraResources`), `src/Mod/ModScript.h`, and
`src/Engine/Script.cpp`/`.h` (`fileLoad`, `load`, and `endLoad`). The reference engine
retains executable rule/script state after loading; this decision changes port ownership,
not mod-visible data or ordering.

## Decision

`RuntimeContent` is the hot content-generation root. It owns only content capabilities,
dense runtime rule projections and generation handles, resource descriptors, compiled
scripts and event plans, tags, initial script values, and the parsed-file count. It does
not expose or retain `Phase3ContentCatalog`, deferred YAML, or rule provenance.

`ContentCompatibilityData` is the explicit cold sidecar. It owns the typed Phase 3
catalog and deferred-rule compatibility entries. `ContentSnapshot` always carries this
sidecar while build and audit work is active. `InstallationContentLoader` drops it by
default and returns it only when `RetainCompatibilityData` is requested. The compiled
content cache reconstructs the cold graph to relink runtime rules, then follows the same
retention policy. Semantic audits continue to normalize the cold catalog before release.

Managed-extension API `0.2` replaces the combined campaign access interface with an
`ExtensionCampaignCapabilities` bundle. Query, command, and committed-event contracts
have stable IDs and independent `1.0` capability versions. Query and command operations
use separate narrow interfaces; the bundle is explicit rather than a generic capability
or service locator. The manifest API range still rejects binary-incompatible `0.1`
extensions before loading.

Future campaign features receive new narrowly grouped capability contracts or a new
version of the affected capability. They do not grow the existing interfaces without a
version change. Gameplay remains the validator and single transaction writer.

## Consequences

- The staged 40k/Rosigma production measurement retains 30.2 MiB for hot runtime
  content, 138.4 MiB (82.1%) below the preceding 168.6 MiB baseline.
- Fresh semantic output remains 9,591,410 bytes with SHA-256
  `C52A5EA199A378EA557ACD9E86B87977DB107A81C1A92817ACF421B2C579B02D`.
- Tooling that needs typed compatibility rules must retain the explicit sidecar; routine
  gameplay and extensions cannot accidentally reach it.
- Cache payload size and startup allocation are not reduced by this ownership change,
  because reconstruction still needs the cold graph. It becomes collectible after
  publication.
- Extension API `0.2` is intentionally binary-incompatible with experimental `0.1`;
  manifests must declare a range containing `0.2` and extensions must recompile.
- A GC regression test proves that runtime content cannot keep compatibility, parsed, or
  composed graphs alive.

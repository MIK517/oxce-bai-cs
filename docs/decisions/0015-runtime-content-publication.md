# ADR 0015: Publish runtime content separately from build and audit state

## Status

Accepted for post-Phase-4 Slice 1.

The later hot/cold split in
[ADR 0023](0023-hot-runtime-content-and-versioned-capabilities.md) moves linked typed
catalogs and deferred compatibility data out of `RuntimeContent`; this ADR's separation
of build/audit roots remains in force.

## Context

The Phase 4 `ContentSnapshot` published `Phase3ContentBuild`. That object retained the
complete parsed-document catalog and composed rule-operation history after typing,
linking, and script compilation had finished. Tooling needed those graphs to reproduce
the semantic manifest, but normal gameplay did not. Script compilation also rebuilt a
complete indexed API catalog per source file solely to vary cumulative tags and
`RuleList.current`.

The full 40k/Rosigma baseline retained roughly 300 MiB by the audit measurement and
allocated roughly 2.45 GiB during construction. More importantly, the API did not make
the intended lifetime visible: application code could not publish content without also
publishing build and audit graphs.

The reference engine loads rules file by file against one shared `ScriptGlobal`,
updates current-file/current-mod state during loading, and clears parser lookup state
at `ScriptGlobal::endLoad`. It retains compiled rule/script data rather than a general
parsed ruleset catalog.

## Decision

Use three explicit content owners:

- `ContentBuildSession` owns parsed documents, composed operations, and stage
  measurements while loading.
- `RuntimeContent` is the normal immutable publication root and owns linked typed
  catalogs, scripts, event plans, tags, and initial values.
- `ContentAuditArtifact` is opt-in, owns parsed/composed audit inputs, and is disposable
  after normalization or inspection.

`ContentSnapshot` is a build result rather than the runtime root. Callers handle its
diagnostics and publish only `ContentSnapshot.Content`. Tooling requests audit
retention explicitly through `ContentSnapshotOptions`.

Deferred rule properties live under `RuleCompatibilityData`; other remaining YAML
structures must be assigned to named compatibility fields and the inventory in
`docs/content-lifetime.md`. A later projection replaces each with a bounded immutable
runtime representation or an explicitly retained compatibility sidecar.

Script declaration indexes remain in one shared `ScriptApiCatalog`. Immutable scopes
add file-visible constants and are cached by cumulative tag count and current mod
identity. Source-file mappings and scopes are compilation-only.

## Consequences

- Normal runtime content cannot reach `RulesetDocumentCatalog` or
  `UnresolvedRuleCatalog`; a collection test verifies released audit roots.
- Audit tools retain full semantic-manifest compatibility and must dispose their
  artifact when finished.
- Runtime-only retained memory can be measured without diagnostics or audit buffers.
- File-scoped script behavior remains explicit while shared parser/binding/type indexes
  are not rebuilt hundreds of times.
- Some structured YAML remains intentionally owned by named compatibility fields until
  resource, gameplay, or presentation projections consume it; additions require an
  inventory update.
- `ContentSnapshot` remains as a compatibility-friendly build-result name, so callers
  must follow the documented publication rule rather than retaining it indefinitely.

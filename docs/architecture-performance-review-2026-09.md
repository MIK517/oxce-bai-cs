# Architecture and performance review, September 2026

Review date: 2026-09-04.

The current architecture is healthy and does not need a wholesale rewrite. Compatibility
behavior is generally isolated in the correct projects, project dependencies are enforced,
untrusted inputs are bounded, published content is immutable, and rendering, audio, script
execution, and archive access have useful measurements. The highest-value improvements are
compatibility guardrails and startup compilation rather than changes to the measured runtime
hot paths.

This review used the following priorities, in order: compatibility with original OXCE assets
and saves, runtime performance, extensibility, asset-loading performance, runtime size, and
source readability.

## Findings

### Make loaded-save preservation mandatory

`OxceSaveAdapter.Emit` and `WriteAtomic` accept an optional source document. A caller can load
an existing campaign and later emit it without the opaque source sidecar, silently discarding
unknown or not-yet-implemented OXCE state. New-save creation and loaded-save rewriting should
be distinct APIs. Rewriting a loaded campaign must require its persistence-owned source;
source-less emission should remain available only through an explicitly named new-save path.

Reference files: `src/Savegame/SavedGame.cpp` and `src/Savegame/SavedBattleGame.cpp`.

### Use document identity instead of a physical path as compiler identity

Content script compilation stores per-document API scopes in a case-insensitive dictionary
keyed by `SourcePath`. Case-sensitive filesystems can contain two ordered ruleset documents
whose paths differ only by case. Those documents must not share scope accidentally. Assign a
stable ordered identity to each parsed ruleset document, carry it into operation provenance,
and reserve paths for diagnostics.

Reference files: `src/Engine/FileMap.cpp`, `src/Engine/FileMap.h`, and `src/Mod/Mod.cpp`.

### Remove catalog churn from script compilation

`ScriptTagCatalogBuilder.Build()` materializes arrays and dictionaries before every document
scope lookup, even when the equivalent scope is already cached. Binding selection also filters
and allocates arrays repeatedly. The builder should expose a monotonic revision so the scope
cache can be checked before materialization; parser-group bindings should be pre-indexed.

The recorded large-mod warm startup is about 7.2 seconds. Script compilation contributes about
3 seconds, and the full content build allocates about 1.9 GiB. This work should precede more
complex parsing parallelism.

### Add a versioned compiled-content cache

After source identity and compilation allocation are corrected, persist immutable compiled
rules, scripts, and runtime descriptors. The cache key must include the format and engine
versions, target compatibility version, ordered active mods, exact input identities and strong
hashes, API/tag revisions, and relevant limits. Cache mismatch or corruption must fall back
atomically to a fresh build, and an audit path must be able to compare cached and fresh semantic
digests.

### Separate hot runtime content from cold compatibility state

`RuntimeContent` retains both the complete typed Phase 3 catalog and dense runtime projections.
Typed rules also retain deferred YAML and provenance. Before the strategic and tactical graphs
grow substantially, publish a hot runtime representation and a cold compatibility catalog.
Unknown nodes, source provenance, and stable identities remain available without duplicating
all known fields indefinitely. Retained memory should be reported by rule family.

Resolved on 2026-09-04: `RuntimeContent` retains only dense execution data; typed rules,
deferred YAML, and provenance moved to explicit `ContentCompatibilityData`. Staged
40k/Rosigma retained runtime memory fell from 168.6 MiB to 30.2 MiB.

### Evolve campaign and extension APIs by capability

Campaign commands, extension commands, and extension event mapping currently use separate
central switches and mirrored public DTOs. Upcoming personnel, inventory, transfer, facility,
research, manufacture, and tactical slices would turn these into multi-file change points.
Keep validation and transactions centralized, but group operations into versioned feature
capabilities and registered internal handlers. Extension contracts should expose stable IDs and
narrow snapshots rather than mirror the complete internal domain graph.

The extension-facing portion was resolved in API `0.2`: campaign query, command, and
event contracts now have stable IDs and independent capability versions. Internal
handler registration remains part of the next owning strategic slices rather than a
generic framework added ahead of consumers.

### Close Unicode and case canonicalization before durable caching

The managed virtual path implementation uses invariant lowercase, while reference behavior is
mediated by platform-specific Unicode handling. Capture reference fixtures for non-ASCII case
mappings, normalization forms, case-only collisions, and loose versus archived paths. Retain
both original spelling and the canonical lookup key, and document whether compatibility is
target-platform-specific or follows one portable definition.

Reference files: `src/Engine/FileMap.cpp` and `src/Engine/Unicode.cpp`.

### Defer resource-cache concurrency changes until streaming needs them

The decoded-resource cache locks on every hit and can perform duplicate concurrent decodes.
Current hit measurements are already good, so redesign is not justified yet. Before background
streaming, add contended and large-decoder workloads. If those expose a problem, use per-key
single-flight decode and a segmented or sampled recency policy. Decoder and variant identity
should also become strongly typed.

### Report compatibility at capability granularity

Phase-level completion labels can be mistaken for full rule-family or gameplay compatibility.
Milestones should distinguish loader-core closure, declaration-schema compatibility, runtime
semantic compatibility, and save round-trip compatibility. Compatibility matrices remain the
authoritative gate for each vertical slice.

### Simplify typed-loader construction and measure distribution profiles

`TypedRuleFamilyLoader` forces some derived loaders to implement a construction method that can
only throw. Make unresolved-rule construction the primary contract and provide an ID-only helper.
For distribution size, measure framework-dependent and self-contained publication separately.
Do not enable trimming or Native AOT by default until YAML and managed-extension loading have
dedicated published-application tests.

## Planned branches

The work is grouped so each branch has one coherent acceptance boundary and can be pushed once,
avoiding CI runs for intermediate commits.

1. `codex/compatibility-preservation-guardrails`
   - Record this review.
   - Separate new-save creation from loaded-save rewriting and require opaque source preservation.
   - Introduce stable ruleset document identity for compilation scope ownership.
   - Add compatibility fixtures/tests and update the save/ruleset matrices.
2. `codex/script-compilation-allocation-reduction`
   - Cache scopes by tag revision before materializing catalogs.
   - Pre-index parser-group bindings and extend stage/allocation benchmarks.
3. `codex/versioned-content-cache`
   - Record the cache format and invalidation decision.
   - Implement atomic persistent compiled-content caching with fresh-build audit coverage.
4. `codex/runtime-capability-shaping`
   - Split hot runtime projections from cold compatibility/provenance storage.
   - Introduce versioned campaign/extension capabilities alongside the existing v1 surface.
   - Add retained-heap and extension compatibility coverage.

Resource-cache concurrency and publish-size changes remain measurement-gated follow-ups rather
than unconditional feature branches.

## Recommended order

Complete compatibility preservation and source identity first, reduce compiler allocations
second, then establish cache performance on stable identities. Reshape runtime ownership and
extension capabilities before broad strategic gameplay makes those contracts expensive to
change. Leave renderer, mixer, prepared script VM, archive pooling, and existing project
boundaries unchanged unless new measurements show a regression.

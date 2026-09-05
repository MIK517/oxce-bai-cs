# ADR 0022: Versioned compiled-content reconstruction cache

Status: accepted, 2026-09-04.

## Context

The production installation bootstrap rebuilt the complete typed catalog, resource
descriptor graph, script programs, event plans, and runtime projections on every
launch. On the staged 40k/Rosigma installation this took about 7.6 seconds and allocated
about 1.9 GiB after the script-allocation work. The data is deterministic for an ordered
mod plan and a fixed set of compatibility/compiler contracts, so repeating all build
stages is unnecessary when those inputs have not changed.

The cache must not become a second compatibility model. Reference ordering and content
semantics remain defined by `src/Mod/Mod.cpp` (`Mod::loadMod`, `Mod::loadAll`,
`loadModData`, and `loadExtraResources`) in the C++ engine and by the normal fresh-build
fixtures in this port. Cache entries are untrusted local input and may outlive binaries,
mods, or configured resource limits.

## Decision

`Oxce.Mods.Bootstrap` owns a versioned compiled-content cache used only by
`InstallationContentLoader`. The default location is
`<installation>/user/cache/compiled-content`; callers can choose a different directory
or disable it. A miss or rejected entry always falls back to the existing fresh build.

The invalidation key covers:

- the cache/compiler, ruleset-normalization, manifest, and script-API revisions;
- the participating assembly module identities and runtime architecture;
- engine identity, ordered active mods, content limits, and resource index settings;
- content-relevant mod metadata and ordered layer/resource identities; and
- the identity and SHA-256 content digest of every ordered ruleset input; and
- the effective VFS identities, uncompressed lengths, and first four bytes (or the
  complete shorter prefix) of TAB/CAT inputs used to derive shared resource counts.

The resource dependency correction on 2026-09-05 increments the compiler revision to
`2`, invalidating older keys without changing the reconstruction format. Resolution
and key construction share one inventory and metadata reader. SHA-256 covers every
metadata byte consumed by shared-count resolution; it does not hash entire media
payloads. A same-size TAB width or CAT directory-offset change therefore invalidates
the compiled indexes. ZIP inputs report uncompressed length from the live archive
entry, since the decompression stream itself does not support `Length`.

Explicit shared-count options suppress their asset dependency. Sound candidates use
the same preferred/fallback selection as resolution. Before composition, the key
conservatively includes those selected CAT headers even when configured `soundDefs`
later suppress them. This can cause a harmless extra miss. An I/O failure while probing
optional metadata bypasses caching for that load; fresh resolution decides whether the
input is actually required and must fail. Missing entries and changes in VFS membership
or ordering also affect identity. Resource inputs must remain stable during a load,
as with the existing ruleset hash/parse boundary; live installation updates are unsupported.

Pixel/audio payload bytes beyond this metadata remain excluded: they are not part of
the compiled graph, and the resource runtime opens the current winning VFS entry when
decoding. Tests prove that irrelevant payload and shadowed/fallback byte changes do not
force recompilation while shared-count changes match a fresh build.

The stored image has a fixed identity header followed by a source-generated JSON
reconstruction contract compressed with gzip. The header rejects stale keys before
decompression. The payload retains the linked typed catalog, compatibility/deferred
YAML nodes, compiled scripts, tags, event plans, initial values, resource descriptors
and indexes, and build diagnostics. It does not persist generation-scoped handles or
runtime projections. A hit creates a fresh content generation, rebuilds handles and
indexes, and runs the runtime rule linker again before publication.

Reads and writes have configurable compressed and expanded size bounds. Restored
script/resource counts and capabilities are checked against the current options.
Malformed, stale, oversized, or error-bearing images are rejected. Publication writes
a unique temporary file and atomically replaces the single current image; cache I/O
failure cannot turn a successful fresh content build into a startup failure.

## Consequences

The starting-personnel correction increments the compiler revision again to `3`.
Scientists and engineers are now projected from the selected starting-base YAML into
runtime content. The persisted cold catalog already contains these fields, so no payload
format change is required; older keys rebuild, and cache hits rerun the same updated
projection as fresh builds. `StartingPersonnelFixtureTests` verifies creation and save
round trips from both freshly built and restored content.

- Warm staged 40k/Rosigma startup is about 31.3% faster and allocates about 50.1% less
  than a fresh production load; publishing the first compressed image is slower and is
  an explicit one-time tradeoff.
- Cached and fresh content share the same public `RuntimeContent` contract. Cache hits
  report empty build-stage measurements because the skipped stages were not executed.
  Installation results additionally expose `StartupMeasurements`: non-overlapping
  elapsed-time and calling-thread allocation samples for discovery/planning, key
  construction, cache read/deserialization, resource restoration, rule relinking,
  runtime publication, fresh build, and cache write as applicable. Failed attempts
  retain completed/failed stage samples; an absent stage was not attempted, rather
  than measured at zero. Total includes orchestration and measurement overhead.
- Compatibility audits continue to call `ContentSnapshotBuilder` directly and therefore
  bypass the cache. The semantic manifest remains the fresh-build oracle.
- Format evolution requires an explicit revision change or a participating assembly
  identity change; old entries fail closed and are replaced after a fresh build.
- Virtual-path canonicalization has its own explicit key revision under
  [ADR 0024](0024-portable-virtual-path-canonicalization.md), so lookup-policy changes
  cannot reuse an image built with older resource identities.
- The cache currently keeps one image per configured directory. Switching active mod
  sets replaces it rather than allowing unbounded per-profile cache growth.

The startup-efficiency follow-up uses reverse layer lookups for the bounded shared
resource dependency inventory. This preserves `VirtualFileCatalog` last-layer-wins
semantics without constructing its complete resource/directory indexes solely for
metadata fingerprints. It changes neither cache input identity nor persistence format;
the loose/ZIP precedence and cached/fresh regression scenarios remain the acceptance gate.

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
- the identity and SHA-256 content digest of every ordered ruleset input.

Resource payload bytes are deliberately excluded. The cached graph contains resource
identities and declarations, not decoded pixels or audio; the resource runtime still
opens and decodes the current winning VFS entry. Changes to resource membership or
ordering invalidate the graph, while replacement bytes at the same logical identity are
observed without recompiling rules and scripts.

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

- Warm staged 40k/Rosigma startup is about 31.3% faster and allocates about 50.1% less
  than a fresh production load; publishing the first compressed image is slower and is
  an explicit one-time tradeoff.
- Cached and fresh content share the same public `RuntimeContent` contract. Cache hits
  report empty build-stage measurements because the skipped stages were not executed.
- Compatibility audits continue to call `ContentSnapshotBuilder` directly and therefore
  bypass the cache. The semantic manifest remains the fresh-build oracle.
- Format evolution requires an explicit revision change or a participating assembly
  identity change; old entries fail closed and are replaced after a fresh build.
- The cache currently keeps one image per configured directory. Switching active mod
  sets replaces it rather than allowing unbounded per-profile cache growth.

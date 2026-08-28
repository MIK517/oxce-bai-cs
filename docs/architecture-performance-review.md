# Architecture and performance review

Review date: 2026-08-29.

The Phase 2 asset foundation and Phase 3 typed-content closure are healthy: the release
solution builds without warnings, the public compatibility corpus passes, and the
project boundaries match the documented dependency graph. The port is not yet a game
engine, however. `Oxce.Scripting`, `Oxce.Gameplay`, and `Oxce.Savegames` are foundations
for the next vertical slices rather than compatibility-complete implementations.

This review records work that is cheapest to complete before those projects depend on
the current bootstrap APIs. Optimizations remain subject to controlled before/after
measurement; compatibility behavior is not traded for lower startup time or memory.

## Findings and required corrections

### Error state must not depend on retained diagnostic messages

`Phase3ContentCatalog` currently decides whether it may publish `linked` by searching a
bounded `DiagnosticCollector` snapshot. The collector retains at most 10,000 messages.
A corpus with at least 10,000 warnings followed by an error can therefore drop the error
and incorrectly publish `linked`. Large accepted corpora already approach that boundary.

Diagnostic retention and diagnostic state are separate responsibilities. A bounded
collector may continue to retain representative messages, but capability gates must use
an unbounded summary of counts and maximum severity. Regression coverage must place an
error after the retention limit.

### Parse each ruleset once per content build

The aggregate Phase 3 load invokes each family catalog independently. Each catalog
reopens and parses the complete ruleset sequence, while campaign settings, presentation
specials, terrain specials, Ufopaedia entries, and resource configuration add more
passes. Typed-manifest generation composes the input again for its semantic digest.
This makes startup cost scale with both input size and the number of typed consumers.

Introduce a content-build session with these stages:

1. Open and parse each ordered ruleset input once.
2. Dispatch named and special sections in reference load order.
3. Publish one unresolved/composed input shared by typed family loaders.
4. Type and link immutable rule catalogs.
5. Resolve resources and compile scripts through independent capability gates.
6. Release parse-only state after all preserved/deferred nodes and audit data have an
   explicit owner.

Family loaders remain independently testable through overloads that accept composed
sections. Convenience entry points may create a session, but aggregate loading must not
call those entry points repeatedly.

### Avoid repeated linear lookup inside large YAML mappings

The compatibility DOM preserves entry order, duplicate keys, complex keys, aliases, and
source spans. Those semantics remain authoritative. Scalar-key lookup is currently a
linear scan, and typed loaders perform one lookup for almost every known property. Large
rules can therefore approach quadratic lookup work.

Evaluate a lazy scalar-key index, preferably above a small mapping-size threshold. The
index must preserve the first matching entry for `TryGet`, retain ordered duplicates for
`GetAll`, and never replace the ordered entry collection. Keep the change only when the
representative YAML and content benchmarks demonstrate a worthwhile improvement.

### Make presentation work observable and suppressible

The SDL host currently converts and uploads the complete indexed frame on every host
iteration, including unchanged menus and paused states, and creates a new pin handle for
each upload. Before gameplay UI depends on that contract, add an explicit frame or
palette revision/invalidation signal. A later measured implementation may write directly
to a locked streaming texture, retain pinned staging storage, use dirty rectangles, or
use a palette-lookup shader. Indexed 8-bit data remains the canonical software surface.

### Split aggregate benchmarks by content stage

The existing Phase 3 benchmark combines load, validation, and manifest emission on a
small public fixture. Retain that end-to-end workload, but also measure parse/compose,
typing/linking from precomposed input, and manifest emission separately. Optional private
benchmarks should cover complete `xcom1` and script-heavy community-mod chains. Record
elapsed time, allocations, peak live memory where practical, parsed bytes, and parse-pass
count.

## Architecture recommendations for upcoming phases

- Publish a single immutable content snapshot assembled by an explicit staged build
  pipeline. Consumers receive typed rules and stable IDs, not mutable builders.
- Resolve resources to lightweight descriptors and stable handles. Decode small sprites
  lazily, stream music and video, and use bounded caches keyed by source identity and
  decoder parameters. Do not decode an entire installation at startup.
- Keep script execution in a compact VM with instruction arrays, numeric binding IDs,
  register arrays, and a content-scoped symbol pool. Keep debug/source-map data separate
  from the hot execution representation and never use runtime C# code generation.
- Favor flat tile and voxel storage plus reusable pathfinding workspaces for battlescape
  hot paths. Do not introduce a general ECS without scenario profiling that justifies it.
- Add gameplay, scripting, persistence, resources, and minimal presentation together in
  vertical slices. Save adapters remain external to gameplay and restore transactionally.
- Do not adopt `FrozenDictionary`, Native AOT, broad pooling, unsafe code, or GPU-only
  compatibility paths without measured end-to-end evidence.

## Current asset boundary

Indexed images, palettes, original containers, terrain data, fonts, and FLI/FLC decoding
have compatible foundations. Remaining resource work includes final sprite/sound offset
publication, lazy decoded-resource ownership, digital music decoding, MIDI/AdLib
synthesis, streaming playback, and the documented FLAC/MOD backlog. A modern internal
representation is allowed, but every asset and ruleset accepted by the targeted OXCE
branch must remain usable without pre-conversion.

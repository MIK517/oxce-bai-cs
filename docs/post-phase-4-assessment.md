# Post-Phase-4 architecture and performance assessment

Assessment date: 2026-08-30.

## Executive assessment

The compatibility foundations are healthy. The solution builds in Release without
warnings, all 436 public and locally available tests pass, and the pinned compatibility
fixtures cover the implemented YAML, file, asset, typed-rule, rendering, audio, and
scripting behavior. The complete local `xcom1` -> `40k` -> `40k_ROSIGMA_edits` corpus
parses 512 ruleset files and reaches `scripts-compiled` with the expected warnings and
no errors.

The port is not yet a playable engine. `Oxce.Gameplay` and `Oxce.Savegames` contain
only their project boundaries, resource resolution has not advanced the content
snapshot to `resources-resolved`, and declared script bindings do not yet have their
strategic or tactical providers. The next work therefore establishes runtime ownership
and hot-path contracts rather than adding broad save DTOs or a large translated state
model.

This assessment supersedes the forward-looking portion of the
[2026-08-29 architecture and performance review](architecture-performance-review.md).
That earlier review remains the record for the single-parse content, bounded diagnostic,
YAML lookup, and revision-driven presentation corrections already implemented.

## Verified strengths

- Project references are acyclic and enforce the documented dependency direction.
- Compatibility-facing formats are owned adapters rather than reflection-serialized
  runtime models.
- Rulesets are parsed once per aggregate content build.
- Content capability gates distinguish composition, typing, linking, resource
  resolution, and script compilation.
- The script compiler accepts the complete available mod corpus against an auditable
  catalog without depending on gameplay providers.
- Indexed 8-bit surfaces remain the canonical compatibility representation while SDL
  presentation is isolated and revision-driven.
- File, ZIP, YAML, media, script, and diagnostic inputs have explicit bounds.
- Deterministic fixtures and injected timing/randomness abstractions provide a good
  basis for headless scenario tests.

## Current compatibility boundary

The original static resource formats are substantially covered, but asset compatibility
is not complete end to end. Shared sprite/sound offset publication, decoded-resource
ownership, digital music decoding, MIDI/AdLib synthesis, streaming, scheduling, and
gameplay playback integration remain. The reference engine itself rejects general
images that do not decode to 8-bit indexed surfaces, so retaining that restriction is
compatible rather than a port limitation.

Script syntax, catalog declarations, content compilation, core scalar execution, tags,
and event composition are implemented. Text/reference host frames, recursive calls,
mission/arc execution, and the player-visible behavior of strategic and tactical
bindings remain with their gameplay owners. No gameplay subsystem should be marked
compatible merely because its scripts compile.

All save compatibility rows and nearly all gameplay rows remain unimplemented. The
next save work must be shaped by gameplay-owned identities, invariants, and
capture/restoration contracts rather than by a speculative translation of the complete
external schema.

## Findings that should be corrected before gameplay expands

### Separate build/audit state from runtime content

`ContentSnapshot` currently owns `Phase3ContentBuild`, which retains the complete
parsed `RulesetDocumentCatalog` and composed `UnresolvedRuleCatalog`. Typed rules also
retain deferred YAML nodes. Consequently, a successful large content build keeps
parse-time and audit-time object graphs alive for the runtime session.

Introduce explicit lifetimes:

1. `ContentBuildSession` owns parsed documents, operation history, builders, temporary
   indexes, and compilation scopes.
2. `RuntimeContent` owns only immutable linked rules, compiled scripts, resolved
   resource descriptors, required compatibility sidecars, and a bounded diagnostic
   summary.
3. `ContentAuditArtifact` optionally owns normalized operation history and manifest
   inputs for diagnostic tools.

Unknown or deferred nodes must have an explicit consumer before parse state is released.
Resource- and script-bearing nodes survive only until their owning stage has converted
them to durable descriptors or programs. Audit tooling may request retention, but the
normal game path must not retain it by default.

### Use lightweight file-scoped script environments

The script content builder reconstructs a complete `ScriptApiCatalog` for each source
file to vary visible tags and `RuleList.current`. Each construction recopies and
revalidates the shared binding, parser, and type indexes. A large installation therefore
retains hundreds of near-identical catalogs.

Keep one immutable base declaration catalog and add persistent or interned overlays for
file-visible constants, tags, and the current mod index. Cache overlays by their tag
revision and mod identity. Compilation behavior and file-order visibility remain
unchanged.

### Compact the script runtime before it becomes a hot path

The VM is semantically useful but allocates an execution object, register array, binding
dictionary, output definitions, diagnostics, trace list, result collections, and output
dictionary for each call. Each instruction is also an object containing a separate
operand array and source span. Tactical scripts may execute per unit, item, tile,
projectile, or drawn sprite, so this representation should not become the gameplay ABI.

Use packed instruction values plus flat operand and optional source-map tables. Resolve
per-program binding metadata once. Add a reusable execution context and a span-based
success path that writes outputs into caller-owned storage and returns a small status
value. Preserve the allocating result API as a tool/test convenience, and allocate
diagnostics or traces only when requested or when execution fails.

Text/reference frames, recursion, and nested host calls must use the same bounded frame
ABI rather than a separate implementation added later.

### Resolve external IDs into content-scoped runtime handles

Typed rule catalogs correctly preserve stable external string IDs, but many known
properties and relationships remain string-keyed dictionaries or unresolved index/mod
pairs. This is suitable for compatibility loading and audit output, not for repeated
gameplay access.

Each gameplay slice should compile the relevant compatibility rules into a strongly
typed runtime projection backed by dense content-scoped arrays and `RuleHandle<T>`-like
values. External IDs and provenance remain available for saves, diagnostics, scripts,
and extensions. Save files never persist numeric runtime handles. Unknown/deferred
properties remain separate from known hot fields.

Do not introduce a general ECS. Flat tactical collections, stable entity IDs, and
reusable pathfinding/visibility workspaces are the appropriate initial design.

### Establish decoded-resource ownership and bounded caching

Add a platform-neutral resource-runtime boundary that resolves extra sprite/sound
offsets and maps rules to immutable typed resource descriptors. Decode small/high-use
assets lazily into size-aware bounded caches; stream music and video; allow deliberate
preload sets; and key caches by content generation, source identity, path, and decoder
parameters.

ZIP-backed entry access currently opens and indexes the archive for every entry stream.
Measure repeated cold/warm resource access and, if confirmed, introduce a bounded
archive-handle lease or pool without allowing one long-lived stream to block unrelated
loads. Directory and ZIP behavior must remain identical at the virtual-file boundary.

Whether this boundary is a new `Oxce.Resources` project or a service composed from
existing projects is an architectural decision to record before implementation.

### Deliver gameplay, persistence, scripting, and presentation vertically

The first runtime slice should cover new-campaign creation, calendar/time, and the
starting base. It includes the necessary rule projections, gameplay state and
invariants, script providers/events, save header/body fields, transactional restoration,
headless commands, semantic fixtures, and the smallest useful presentation.

Gameplay begins as a deterministic single-writer simulation. Extensions and
presentation receive read-only views or snapshots and submit validated commands.
Persistent entity IDs are distinct from content rule handles. Save adapters preserve
external field names and eligible unknown fields without putting YAML into gameplay.

### Expand measurements around runtime behavior

Add controlled benchmarks for:

- retained heap after a full content build and after audit state release;
- file-scoped script-environment construction;
- script core execution, host calls, events, recursion, and failure paths;
- directory/ZIP cold and warm resource access, decoding, cache hits, and eviction;
- save capture, YAML mapping, staged restoration, and atomic writing;
- later pathfinding, visibility, AI turns, and representative frame construction.

Successful non-traced scalar script execution should aim for zero managed allocation.
Resource and save targets require explicit supported-hardware and memory budgets before
they become gates.

### Defer speculative optimizations

The mixer currently locks while filling a callback buffer and recomputes gain/pan in
the inner loop. Polygon filling allocates scratch intersections, and indexed-to-RGBA
conversion is scalar. These are legitimate measurements once real application workloads
exist, but they do not justify redesign ahead of the runtime-content, script, resource,
and gameplay work.

Continue to defer a general ECS, indiscriminate pooling, `FrozenDictionary`, Native AOT,
unsafe hot paths, parallel gameplay mutation, dirty rectangles, SIMD conversion, and a
palette shader until controlled end-to-end evidence supports them.

## Validation result

On 2026-08-30, `dotnet build Oxce.slnx --configuration Release --no-restore` completed
with zero warnings and errors. `dotnet test Oxce.slnx --configuration Release
--no-build` passed all 436 tests with no failures or skips.

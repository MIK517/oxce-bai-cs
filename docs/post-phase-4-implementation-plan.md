# Post-Phase-4 architecture implementation plan

Plan date: 2026-08-30.

This plan implements every recommendation in the
[post-Phase-4 assessment](post-phase-4-assessment.md). It is deliberately ordered so
runtime ownership and compatibility fixtures stabilize before gameplay depends on
bootstrap representations. Each delivery branch remains independently reviewable and
must satisfy the repository's required compatibility workflow.

## Delivery principles

- Preserve external IDs, schemas, defaults, ordering, error behavior, script semantics,
  resource precedence, and save meaning even when internal representations change.
- Make structural performance changes only with before/after measurements on the same
  machine and unchanged corpus.
- Keep a convenient allocating/tooling API where a separate non-allocating runtime API
  is introduced.
- Do not publish a capability until its complete diagnostic stream is error-free.
- Use the local full installation as ignored acceptance evidence, never as committed
  copyrighted fixture content.
- Add gameplay in executable vertical slices that include rules, state, scripts,
  persistence, headless scenarios, and only the presentation needed to operate them.

## Local full-installation corpus

The currently available source is `D:\Games\OpenXCom\WH40kRosigma\`. A future staging
tool will copy the minimum complete `xcom1`, `40k`, `rosigma`, metadata, ruleset, and UFO
resource roots needed by normal discovery into `artifacts/private-install/`. The
destination is already ignored by Git.

The staging command must:

1. Accept an explicit source path and never embed that machine-specific path in tests.
2. Copy into a new or explicitly selected directory under `artifacts/`; it must not
   merge into an unrelated existing corpus.
3. Emit a manifest containing relative paths, sizes, SHA-256 hashes, selected mod IDs,
   engine executable hash/version when available, and staging time.
4. Exclude executables, DLLs, logs, user configuration, saves, screenshots, and
   unrelated standard mods unless a workload explicitly requires them.
5. Support validation-only and refresh modes without deleting broad directories.
6. Let benchmarks and installation audits discover the staged root through an argument
   or task-specific environment variable and skip clearly when it is absent.

Raw mod and original-game content remains local. Only aggregate counts, normalized
semantic hashes, commands, environment metadata, and performance results may be
committed after review.

## Slice 1 — Runtime content ownership and measurement

Suggested branch: `codex/runtime-content-ownership`.

### Work

- Add retained-memory instrumentation and benchmarks for public and private content.
- Introduce explicit build-session, runtime-content, and optional audit-artifact types.
- Move parsed documents and composed operation history out of the default published
  runtime object.
- Inventory every retained deferred/structured YAML node and assign it to resource
  resolution, script compilation, a compact compatibility sidecar, or audit-only state.
- Replace per-source complete script API catalogs with shared base indexes and
  lightweight immutable file scopes.
- Preserve the existing typed manifest and full-install semantic hash.
- Update content capability and lifetime documentation.

### Reference and fixtures

- Inspect `src/Mod/Mod.cpp`, `src/Mod/ModScript.h`, `src/Engine/Script.cpp`, and the
  owning rule loaders for file visibility and after-load retention behavior.
- Add a fixture with tags changing between files, multiple mods, `RuleList.current`,
  deferred resource/script properties, duplicates, and a requested audit artifact.
- Run the staged 512-file installation three times and compare typed manifests.

### Exit gate

- Normal runtime content no longer references `RulesetDocumentCatalog`,
  `UnresolvedRuleCatalog`, or unowned YAML nodes after all required stages finish.
- Audit mode still reproduces the current normalized output.
- Parsed-file count, diagnostic severity, script count, event plans, tag visibility,
  initial values, and full-install manifest hash are unchanged.
- Before/after startup allocation, retained heap, elapsed time, and catalog/scope counts
  are recorded.

## Slice 2 — Compact script program and reusable execution frames

Suggested branch: `codex/compact-script-runtime`.

### Work

- Add VM benchmarks before changing representation.
- Replace instruction objects and per-instruction operand arrays with packed immutable
  instruction and operand storage.
- Move source spans and optional trace metadata into side tables.
- Resolve binding metadata into compact per-program slots once.
- Add bounded reusable scalar/text/reference frame storage and a span-based execution
  API; keep the existing result API as an adapter.
- Implement nested host frames, recursion depth, text/reference lifetime, and provider
  failure semantics through this ABI.
- Ensure writable arguments and output commit behavior remain transactional on failure.

### Reference and fixtures

- Inspect `src/Engine/Script.h`, `Script.cpp`, and `ScriptBind.h` for frame layout,
  recursive execution, text/reference values, and failure behavior.
- Extend `script-core`, API, event, and value fixtures with nested calls, recursion,
  text/reference arguments, missing providers, and limit failures.
- Compile and execute representative scripts from the staged full installation where
  providers can be synthetic without weakening semantics.

### Exit gate

- Existing compiler/VM fixtures and full-install compilation remain unchanged.
- Successful non-traced binding-free scalar execution allocates zero managed bytes in
  the benchmark harness.
- Host-call and event execution have recorded before/after time and allocation results.
- Trace/source diagnostics retain stable operation and source identity.

## Slice 3 — Resource resolution, ownership, and caches

Suggested branch: `codex/resource-runtime-foundation`.

### Work

- Record an ADR selecting the resource-runtime project/service boundary and dependency
  direction.
- Resolve extra sprite and sound declarations, shared offsets, palettes, fonts, terrain,
  interfaces, music, and video to immutable descriptors and typed handles.
- Advance `resources-resolved` only after all required references and bounds validate.
- Add lazy decoding, explicit preload groups, size-aware cache budgets, eviction, cache
  telemetry, and content-generation invalidation.
- Stream music/video and keep small decoded images/sounds cacheable.
- Benchmark repeated ZIP entry access and implement bounded archive leasing/pooling only
  if the measurement justifies it.
- Extend the resource browser through the same runtime service; do not add a parallel
  loader.

### Reference and fixtures

- Inspect `src/Mod/Mod.cpp`, `ExtraSprites.cpp`, `ExtraSounds.cpp`, `SoundDefinition.cpp`,
  `RuleMusic.cpp`, `FileMap.cpp`, and relevant surface/set loaders.
- Capture synthetic offset and override fixtures spanning multiple mods and VFS layers.
- Audit representative images, sounds, terrain, music, and video from the staged
  installation without committing content.

### Exit gate

- The public resource fixture reaches `resources-resolved` with exact descriptor and
  offset output.
- Missing, invalid, and oversized resources fail intentionally with provenance.
- Cold/warm directory and ZIP benchmarks, cache hit/eviction tests, and retained-memory
  measurements pass.
- No entire installation is decoded at startup and no mandatory pre-conversion exists.

## Slice 4 — Runtime rule projections and stable handles

Suggested branch: `codex/runtime-rule-linking`.

### Work

- Define content-generation-scoped typed rule handles and dense immutable family
  storage without exposing numeric handles to saves or external schemas.
- Compile the first strategic slice's compatibility rules into strongly typed runtime
  projections with direct known fields and resolved rule/resource/script references.
- Keep external IDs and provenance available for diagnostics, scripts, saves, and
  extension views.
- Separate unknown/deferred data from known hot properties.
- Add stale-handle/content-generation validation in debug and restoration paths.
- Establish reusable flat collection and scratch-workspace patterns for later tactical
  systems; do not add a general ECS.

### Reference and fixtures

- Inspect the exact C++ rule types and `afterLoad` sites used by campaign creation,
  countries/regions, facilities, crafts, items, personnel, and starting-base settings.
- Add fixtures that cover missing IDs, self/cyclic links where legal or illegal,
  load-order replacement, mod provenance, and save-facing ID round trips.

### Exit gate

- The first strategic runtime projection performs no string lookup for already-linked
  relationships during normal rule evaluation.
- Equivalent external IDs and normalized semantics match the compatibility catalog.
- Handles cannot be confused across content generations or rule families.
- Benchmark results compare compatibility-model lookup with linked runtime access.

## Slice 5 — First persisted strategic vertical slice

Suggested branch: `codex/campaign-foundation-slice`.

### Work

- Implement gameplay-owned campaign identity, game time/calendar, countries/regions,
  campaign creation, and the starting base with explicit invariants.
- Define validated commands and domain events; run the simulation with a deterministic
  single writer and injected random/clock sources.
- Install every script provider and event source used by this slice.
- Add save-neutral capture/restoration contracts to gameplay.
- Implement the corresponding OXCE save header/body adapters, mod/rule validation,
  script values, eligible unknown-field sidecars, stable writing, and recoverable atomic
  replacement.
- Restore transactionally: parse/bound, allocate IDs, populate, resolve, restore script
  values, validate, and publish.
- Provide a headless command/scenario path and the smallest presentation needed to
  inspect and operate the slice.

### Reference and fixtures

- Inspect `SavedGame.cpp`, `GameTime.cpp`, `Country.cpp`, `Region.cpp`, `Base.cpp`,
  `BaseFacility.cpp`, campaign-start code, and each installed binding/event source.
- Capture new-campaign and starting-base scenarios before implementation.
- Obtain representative OXCE saves for the targeted branch; keep private saves ignored
  and commit only derived redistributable fixtures when permitted.

### Exit gate

- Vanilla and staged modded campaigns can be created headlessly with compatible initial
  state and legal commands.
- The implemented state loads from representative C++ saves, saves, reloads, and
  compares semantically without loss.
- Missing mods/rules, corrupt values, identity collisions, invalid references, and
  interrupted writes fail intentionally.
- Save capture/restoration latency, allocation, and file size are recorded before any
  copy-on-write or segmented capture design is considered.

## Slice 6 — Runtime performance hardening and platform coverage

Suggested branch: `codex/runtime-performance-hardening` after representative workloads
exist; individual measured fixes may instead accompany their owning vertical slices.

### Work

- Measure audio callback duration and control-thread contention. If justified, precompute
  voice gain/pan and move control changes through a bounded command queue or immutable
  voice snapshot.
- Measure polygon scratch allocation and introduce reusable caller/workspace storage if
  it is material.
- Measure frame conversion, uploads, and UI invalidation before considering SIMD, dirty
  rectangles, locked textures, or a palette shader.
- Add Windows native SDL smoke validation alongside Linux and macOS.
- Add long-run cache, repeated-save, script-event, and simulation-soak tests.

### Exit gate

- Every optimization includes a controlled baseline, preserved compatibility fixtures,
  and an updated performance record.
- Audio callback overruns, unbounded cache growth, frame-loop idle work, and repeated
  save degradation have explicit tests or measurements.
- No platform-only optimization becomes the compatibility implementation.

## Continuing strategic and tactical delivery

After the first persisted slice, Phase 6 continues personnel/inventory/craft/transfers,
research/manufacture, alien missions/interception, and monthly processing as the same
end-to-end unit. Phase 7 applies the identical pattern to map generation, movement,
visibility, damage, AI, objectives, and tactical saves. Each slice expands rule
projections, providers, state, save fields, scenarios, resource use, and benchmarks
together.

## Documentation and matrix work in every slice

Every branch must update:

- the relevant compatibility matrix rows and fixture names;
- authoritative C++ files inspected;
- the implementation-plan status and capability boundary;
- benchmark workloads and retained result metadata when performance changes;
- ADRs for consequential ownership, dependency, ABI, cache, threading, or extension
  decisions.

## Decisions required before or during implementation

The following user/product decisions are intentionally not assumed by this plan:

1. Whether to create a new `Oxce.Resources` production project or keep resource-runtime
   composition inside the existing project graph.
2. Minimum supported hardware and acceptable targets for startup time, steady retained
   memory, resource cache size, save latency, and tactical frame time.
3. Whether the first strategic slice should expose only headless/CLI operation or also
   include a minimal SDL UI.
4. The exact targeted OXCE save version/commit and which representative campaign and
   active-battle saves may be used privately.
5. Whether a versioned managed-extension abstractions assembly is required with the
   first gameplay slice or should wait until gameplay contracts have matured.
6. Which production dependency policy is acceptable for OGG, MP3, FLAC, MOD,
   MIDI/soundfont, and AdLib decoding/synthesis, including managed versus native
   libraries and redistribution expectations.
7. Whether the staged private installation should be a complete self-contained copy or
   a smaller copied corpus plus references to original resource roots.
8. Whether performance work should be delivered as the foundational slices above before
   campaign behavior, or interleaved to reach the first playable screen sooner.

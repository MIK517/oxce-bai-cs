# Testing strategy

The reference C++ engine has little automated behavioral coverage, so the port must
create its own executable specification while features are understood.

## Test layers

### Unit tests

Use focused tests for arithmetic, coordinates, parsing helpers, merge operations,
script instructions, path costs, damage calculations, and serialization decisions.
Every discovered edge case gets a named regression test.

### Fixture tests

Fixture tests feed external data to one subsystem and compare a stable semantic result:

- YAML input -> normalized node tree, diagnostics, and source locations.
- Resource file -> dimensions, palette indices, records, links, or decoded sample hash.
- Script -> tokens, typed IR, diagnostics, execution trace, and final variables.
- Save -> external representation -> staged gameplay restoration -> semantic snapshot
  -> save through the external adapter -> reload -> equivalent snapshot.

Do not assert emitted whitespace unless the reference parser requires it.

### Differential tests

Where practical, run the same fixture through the reference engine and this port, then
compare normalized results. Reference output must include its commit hash. Never let a
silent reference-engine update rewrite expected data.

Random behavior can be compared by one of these methods:

1. Supply fixed decisions or a scripted random source to both harnesses where possible.
2. Compare pre-random eligibility, ranges, weights, state transitions, and invariants.
3. Compare distributions statistically only when individual outcomes are irrelevant.

Different next random outcomes are acceptable; different available actions or formulas
are not.

### Scenario tests

Headless scenarios exercise player-visible flows:

- Start campaign, advance time, detect/intercept a UFO, resolve consequences.
- Build facilities, transfer inventory, research, manufacture, and monthly accounting.
- Generate a battle, deploy units, move/pathfind, attack, end turns, and debrief.
- Save/reload at strategic and tactical checkpoints.
- Run scenarios under representative vanilla and community mods.

Scenarios compare semantic snapshots: rule IDs, positions, ownership, stats, inventories,
events, legal actions, and outcomes with random-specific fields masked when appropriate.
Save scenarios also verify that invalid graphs are never published, stable identities
survive round trips, forward/cyclic references resolve deliberately, and eligible
unknown fields remain attached only to surviving entities. These tests cross the
gameplay-owned capture/restoration boundary defined by ADR 0008; they do not serialize
runtime objects directly.

### Presentation tests

Use exact indexed-buffer assertions for software primitives and resource decoders. For
complete screens use structural assertions and tolerant snapshots; UI and final pixels
are allowed to differ. Always test that gameplay-relevant information is present.

The path-scoped `SDL validation` workflow publishes `Oxce.App` and stages a
checksum-pinned SDL 3.4.10 native library beside it. Linux and macOS build the pinned
source; Windows uses the official checksum-pinned x64 development archive. Linux must
report X11 under Xvfb, macOS must report Cocoa, and Windows must report the dummy
backend. Every platform must present at least one indexed frame, suppress unchanged
frame uploads, and complete the dummy-audio callback smoke. A macOS dummy-video
fallback is diagnostic only and deliberately leaves the job failed.

### Robustness and performance

Fuzz or property-test binary readers, YAML adapters, script parsing, and save loading.
Set explicit limits for file size, decompression, recursion, collections, and scripts.
Maintain representative performance budgets for mod loading, save loading, pathfinding,
AI turns, and frame allocations. Measure consistent strategic and tactical snapshot
latency and allocation before adopting segmented capture, visitors, or copy-on-write.
`ScriptVmBenchmarks` keeps allocating adapters and prepared scalar, host, and event
frames side by side. A repeated unit fixture enforces zero managed allocation for a
prepared, successful, non-traced binding-free scalar program; BenchmarkDotNet records
the broader host and event costs. Long-run unit workloads additionally exercise
bounded cache churn, 100 stable save/reload cycles, 10,000 prepared script-event
executions, 200,000 deterministic campaign ticks, and concurrent mixer control updates
across 10,000 callbacks. These are regression/soak checks, not substitutes for
statistical microbenchmarks or full-install acceptance.

## Fixture layout

```text
fixtures/
  public/          # Redistributable synthetic or freely licensed inputs
  expected/        # Normalized expected output plus reference metadata
  private/         # Ignored locally: original game data and non-redistributable mods
  manifests/       # Hashes and instructions for obtaining external corpora
```

Each expected fixture records:

- Fixture schema version.
- Reference engine commit and build options.
- Mod/version list.
- Input hashes.
- Normalization/masking rules.
- Expected diagnostics or semantic output.

Do not commit original X-COM data. Prefer tiny synthetic files that isolate a format
feature; use manifests and local hashes for end-to-end tests requiring owned assets.

## Test and fixture runners

Unit and compatibility tests use centrally pinned xUnit v3 packages on Microsoft
Testing Platform v2. Coverlet provides cross-platform coverage. The separate
`Oxce.FixtureTool` command-line application validates manifests and supports hashing,
canonical JSON normalization, YAML semantic-tree normalization, and semantic comparison
outside the test runner. `audit-typed-install` accepts an isolated mod root, an owned
external-resource root, an active master ID, and a manifest destination.
`audit-content-install` accepts a complete installation root, master ID, add-on ID, and
destination; it discovers the installation's standard and user mod trees and runs the
durable content snapshot through script compilation. Both commands report the content
stage, parsed-file and diagnostic counts, and manifest size. The content audit also
reports attempted and retained scripts, event plans, tags, initial values, and the first
errors so complete private installations can be checked without copying or committing
their assets.

`measure-content-install` uses the same discovery and activation path but publishes
only `RuntimeContent` across an isolated build boundary. Its retained-memory result
therefore excludes diagnostics, parsed documents, composed operation history, audit
normalization, and manifest buffers. Use the audit and measurement commands together;
the former is the compatibility oracle and the latter is the runtime ownership probe.

`measure-cached-content-install` exercises the production loader and reports cache
status, elapsed time, current-thread allocation, content counts, and cache size. Pass
`-` as its cache directory for a fresh no-cache comparison. Cache tests require a new
generation on restore and compare scripts, runtime rules, diagnostics, scoped script
visibility, and retained deferred YAML. Corrupt or mismatched images must rebuild rather
than weakening startup. `audit-content-install` deliberately bypasses the cache and
remains the semantic compatibility oracle.

`campaign-scenario` is the headless acceptance path for the first persisted strategic
slice. It uses the same structured, cancellable installation bootstrap as the
application, creates a deterministic campaign, places the starting base, advances time,
atomically writes a save, reloads it, and emits a compact JSON result. Use `-` when there
is no add-on:

```powershell
dotnet run --project tools/Oxce.FixtureTool --configuration Release --no-build -- campaign-scenario artifacts/private-install xcom1 - artifacts/campaign-foundation/xcom1.sav
dotnet run --project tools/Oxce.FixtureTool --configuration Release --no-build -- campaign-scenario artifacts/private-install xcom2 - artifacts/campaign-foundation/xcom2.sav
dotnet run --project tools/Oxce.FixtureTool --configuration Release --no-build -- campaign-scenario artifacts/private-install 40k 40k_ROSIGMA_edits artifacts/campaign-foundation/rosigma.sav
```

When private fixtures are present, compatibility tests round-trip every supplied
vanilla UFO, vanilla TFTD, and 40k/Rosigma `.sav`/`.asav` through the implemented
snapshot while retaining opaque fields. CI skips this owned-data check when those
ignored fixtures are absent.

Installation-bootstrap unit tests cover the complete stable progress sequence,
mid-composition cancellation without publication, missing-installation failures, and
normalization of the command-line no-add-on marker. Staged UFO, TFTD, and Rosigma
campaign scenarios exercise the production runtime loader. Content audit and retained
runtime measurement exercise the shared plan builder while preserving their own build
and GC lifetimes.

For owned full-install acceptance, `tools/stage-private-install.ps1` copies the
complete data trees into an isolated destination below `artifacts/`, writes per-file
sizes and SHA-256 hashes, and validates the published corpus. Its default destination
is `artifacts/private-install`; `-ValidateOnly` rechecks every file without consulting
the source data, and `-Refresh` replaces only the explicitly selected staged directory
after a new copy validates. The source installation is never modified.

```powershell
.\tools\stage-private-install.ps1 -SourceRoot D:\path\to\owned-install
.\tools\stage-private-install.ps1 -ValidateOnly
```

After a Release build, `tools/capture-private-content-baseline.ps1` runs the complete
content audit in separate processes and records timing, allocation, conservative
retained-managed-memory and process working-set measurements under the ignored
`artifacts/baselines/` directory. The installation and output roots can be overridden
explicitly. A staged corpus has already been fully read for hashing, so the first run
is labelled `first-process`, not cold-cache; a controlled cold-cache result must be
captured separately.

```powershell
dotnet build Oxce.slnx --configuration Release --no-restore
.\tools\capture-private-content-baseline.ps1 -Runs 3
```

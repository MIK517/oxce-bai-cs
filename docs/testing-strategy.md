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

The path-scoped `SDL validation` workflow builds the pinned SDL source inside each hosted
runner, publishes `Oxce.App`, and stages the native shared library beside the app before
execution. Linux must report the X11 backend under Xvfb; macOS must report Cocoa. Both
must present at least one indexed frame and complete the dummy-audio callback smoke. A
macOS dummy-video fallback is diagnostic only and deliberately leaves the job failed.

### Robustness and performance

Fuzz or property-test binary readers, YAML adapters, script parsing, and save loading.
Set explicit limits for file size, decompression, recursion, collections, and scripts.
Maintain representative performance budgets for mod loading, save loading, pathfinding,
AI turns, and frame allocations. Measure consistent strategic and tactical snapshot
latency and allocation before adopting segmented capture, visitors, or copy-on-write.

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

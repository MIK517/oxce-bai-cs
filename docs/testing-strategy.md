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
- Save -> normalized semantic snapshot -> save -> reload -> equivalent snapshot.

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

### Presentation tests

Use exact indexed-buffer assertions for software primitives and resource decoders. For
complete screens use structural assertions and tolerant snapshots; UI and final pixels
are allowed to differ. Always test that gameplay-relevant information is present.

### Robustness and performance

Fuzz or property-test binary readers, YAML adapters, script parsing, and save loading.
Set explicit limits for file size, decompression, recursion, collections, and scripts.
Maintain representative performance budgets for mod loading, save loading, pathfinding,
AI turns, and frame allocations.

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
canonical JSON normalization, and semantic comparison outside the test runner.

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

## Bootstrap runner

The initial test projects are console runners so the empty skeleton has no unresolved
package choice. In Phase 0, select a maintained .NET 10 test framework, pin versions
centrally, enable coverage, and keep the compatibility fixture runner callable both
from the test framework and from the command line.


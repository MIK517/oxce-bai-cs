# Phase 0 status

This document records evidence against the bootstrap and specification-harness
deliverables in `implementation-plan.md`. It is not a substitute for the compatibility
matrices.

## Completed

- .NET 10 SDK requirements are pinned in `global.json`.
- Nullable analysis, warnings-as-errors, deterministic builds, and recommended SDK
  analyzers are enabled repository-wide.
- xUnit v3, Microsoft Testing Platform v2, and Coverlet are centrally pinned.
- Unit and compatibility tests are discoverable through `dotnet test`.
- Cobertura coverage generation is exercised locally.
- `dotnet format --verify-no-changes` is available as the formatting gate.
- Windows, Linux, and macOS CI jobs are defined.
- GPL-3.0-or-later license text and attribution are present.
- The six required compatibility matrices and a generated C++ source inventory exist.
- Fixture manifest schema version 1, hashing, canonical JSON normalization, manifest
  inspection, and semantic comparison are implemented and tested.
- A redistributable `tool-self-test` fixture exercises the .NET fixture pipeline.
- The `core-position` fixture is captured by compiling a synthetic probe against the
  pinned C++ `src/Battlescape/Position.h` and comparing normalized output with .NET.
- GitHub Actions builds, formats, and tests the repository on hosted Windows, Linux,
  and macOS runners. The first complete matrix passed on 2026-08-11.

## Continuing inventories and performance baselines

- Expand broad source inventory candidates into individual rule keys, script APIs,
  save nodes, formats, options, and gameplay scenarios as exact loaders are inspected.
- The standalone BenchmarkDotNet suite now measures VFS scanning/catalog construction,
  path normalization/layered lookup, YAML parsing/mapping lookup, indexed blits/frame
  conversion, and mixer throughput. See `performance-baselines.md`. Add tactical
  pathfinding and AI-turn scenarios only when those executable gameplay paths exist.

## Current exit-gate assessment

The build, hosted CI matrix, fixture tooling, and executable C++ oracle path are
operational. The Phase 0 exit gate is **complete**. Detailed inventories and meaningful
performance baselines continue with the phases that implement the measured behavior.

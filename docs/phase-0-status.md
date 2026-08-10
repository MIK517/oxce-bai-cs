# Phase 0 status

This document records evidence against the bootstrap and specification-harness
deliverables in `implementation-plan.md`. It is not a substitute for the compatibility
matrices.

## Completed locally

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

## Remaining before the Phase 0 exit gate

- Run the hosted CI matrix after a repository remote is intentionally configured.
- Add a narrow capture hook or probe to a pinned C++ reference build and commit the
  first `cpp-reference` fixture. The harness self-test deliberately does not claim to
  be a C++ oracle.
- Expand broad source inventory candidates into individual rule keys, script APIs,
  save nodes, formats, options, and gameplay scenarios as exact loaders are inspected.
- Establish meaningful performance baselines when mod loading, tactical pathfinding,
  AI turns, and indexed drawing operations exist. Recording placeholder timings before
  those operations exist would provide false assurance.

## Current exit-gate assessment

The local build/test/tooling portion is operational. Phase 0 is **partial**, because CI
has not run on hosted operating systems and no executable C++ oracle fixture has yet
flowed through the comparison pipeline.

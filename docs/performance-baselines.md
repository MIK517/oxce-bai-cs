# Performance baselines

`benchmarks/Oxce.Benchmarks` is the controlled microbenchmark suite for operations that
are expected to become hot as compatibility coverage grows. It uses BenchmarkDotNet
with memory diagnostics and deterministic synthetic inputs; private assets and mods are
not required.

## Current benchmark workloads

| Area | Workload |
|---|---|
| VFS scanning | Recursively scan 5,000 files distributed across 50 directories. |
| Catalog construction | Finalize eight layers of 2,000 resources each, with overlapping virtual paths. |
| Path handling | Normalize 1,024 mixed-case backslash paths, perform mixed hit/miss catalog lookups, and compare legacy per-layer normalization with one-time slice normalization. |
| Finalized maps | Compare `Dictionary` and `FrozenDictionary` construction, allocation, and mixed hit/miss lookup at 2,000 and 16,000 entries. |
| YAML | Parse a 1,000-entry ruleset-shaped mapping and repeatedly look up keys near its end plus missing keys. |
| Phase 3/4 content | Compose, type, dependency-validate, compile defaults/tags/events/rule scripts/stat bonuses, and normalize the bundled script-content fixture through the aggregate snapshot. |
| Indexed rendering | Opaque 320x200 blit, transparent 64x80 sprite blit, and 640x400 indexed-to-RGBA conversion. |
| Audio | Mix 1,024 stereo frames from sixteen looping 48 kHz stereo voices. |

Setup constructs files, layers, YAML, surfaces, palettes, clips, and output buffers
outside the measured operations. Batched benchmarks declare their operations-per-invoke
count, so reported time and allocation columns remain per logical operation.

The aggregate Phase 3 workload is retained as an end-to-end startup signal, but it must
not be the only content measurement. The architecture review requires separate
parse/compose, type/link-from-composed-input, and manifest workloads plus parse-pass
instrumentation. Optional private runs should exercise complete `xcom1` and large
community-mod chains. See
[`architecture-performance-review.md`](architecture-performance-review.md).

## Running and comparing results

Build validation, including CI, compiles the benchmark project as part of `Oxce.slnx`
but does not execute it. Shared CI machines are unsuitable for performance gates.

List or smoke-test the discovered benchmarks:

```powershell
dotnet run --project benchmarks/Oxce.Benchmarks -c Release --no-build -- --list flat
dotnet run --project benchmarks/Oxce.Benchmarks -c Release --no-build -- --job Dry --filter "*VirtualPathLookupBenchmarks*"
```

For a comparison used to justify an optimization, use the same machine, SDK, runtime,
power profile, command, and idle-process conditions for the before and after commits:

```powershell
dotnet run --project benchmarks/Oxce.Benchmarks -c Release --no-build -- `
  --job Short --artifacts artifacts/benchmarks/<commit-or-label>
```

Record the commit, CPU, OS, .NET SDK/runtime, power profile, and command with retained
results. BenchmarkDotNet records the main runtime and hardware details in its output.
`artifacts/` is ignored; results should only be committed when they support a documented
decision and the environment metadata accompanies them. A dry job validates execution
but is not statistical performance evidence.

## Item 7 VFS evaluation

Measured on 2026-08-20 from the item 7 candidate based on `c094c28`, using the
documented `Short` job on Windows 11, a Ryzen 9 7940HS, .NET SDK 10.0.302, and .NET
runtime 10.0.10. These short runs guide the implementation decision; they are not CI
performance gates.

Normalizing a requested path once before probing all eight VFS layers reduced layered
slice lookup from 678.76 ns and 1,752 B to 178.18 ns and 296 B per lookup. This is a
74% time reduction and an 83% allocation reduction in the synthetic workload. The
public layer lookup continues to normalize untrusted caller input; only the catalog's
internal canonical-path route bypasses repeated normalization.

Removing per-directory sorting left the required final whole-layer ordinal sort in
place. The scan workload changed from 114.59 ms and 4.49 MB to 119.30 ms and 4.38 MB.
The short-run timing difference is not evidence of a speedup, but the intermediate
arrays and sorts are eliminated and deterministic catalog order remains covered by
tests and the C++-derived VFS fixture.

`FrozenDictionary` is not adopted for VFS catalogs at this stage. The evaluation found:

| Entries | Dictionary build | Frozen build | Dictionary lookup | Frozen lookup |
|---:|---:|---:|---:|---:|
| 2,000 | 8.61 us / 65,456 B | 134.01 us / 172,872 B | 9.68 ns | 4.84 ns |
| 16,000 | 100.26 us / 491,237 B | 1.59 ms / 1,430,418 B | 10.34 ns | 6.62 ns |

Freezing made construction about 16 times slower and allocated 2.6-2.9 times as much
for a 3.7-4.8 ns lookup saving. Even the conservative replacement-cost comparison
requires roughly 26,000 lookups at 2,000 entries or 400,000 at 16,000 entries to repay
construction time; converting the already-built mutable catalog would cost more. Keep
`Dictionary` until end-to-end profiling demonstrates enough repeated lookups in a
long-lived finalized catalog. Reconsider freezing the resolved catalog first, rather
than every individual layer.

The Phase 3 lazy ZIP-source implementation was checked against the same directory scan
workload on 2026-08-20. Its specialized loose-file representation measured 93.74 ms and
4.38 MB, retaining the item 7 allocation level; ZIP-only state does not enlarge ordinary
directory entries. Short-run timing remains informational rather than a performance
gate.

## 2026-08-29 content-build and YAML evaluation

Measured on Windows 11 with a Ryzen 9 7940HS, .NET SDK 10.0.302, runtime 10.0.10,
BenchmarkDotNet 0.15.8, and the documented `Short` job. The before content result is
from documentation commit `b8e6d56`; the after result is from the single-parse candidate
on `codex/architecture-performance-foundations`.

Parsing every ordered ruleset once and sharing the resulting immutable document catalog
across composition, typed family loading, special sections, and manifest normalization
reduced the public Phase 3 end-to-end workload from 3.198 ms and 3.62 MB to 424.285 us
and 485.98 KB. That is approximately a 7.5-times throughput improvement and an 87%
allocation reduction on the fixture. The after benchmark also separates the stages:

| Stage | Mean | Allocated |
|---|---:|---:|
| Parse ruleset documents | 218.344 us | 290.30 KB |
| Compose parsed documents | 8.413 us | 33.15 KB |
| Load and validate aggregate catalog | 422.549 us | 417.05 KB |
| Normalize a prebuilt manifest | 57.053 us | 70.03 KB |

The parsed-file count is exposed by the build result and asserted by tests, so future
aggregate consumers cannot silently reintroduce multiple parse passes without changing
observable instrumentation.

Phase 4 retains the parsed document catalog in the durable snapshot and adds
`LoadValidateAndCompileScripts` beside the earlier stage measurements. It uses the
public `script-content` fixture and covers the seven defaults plus global tags/events,
rule scripts, initial values, and legacy stat conversion. No timing is recorded here:
the benchmark is compiled in CI and dry-run validated locally, while statistical
baselines must follow the controlled-machine procedure above.

A lazy ordinal scalar-key index for mappings with at least 16 entries reduced the
representative repeated lookup from 2.891 us and 32 B to 8.450 ns and zero allocation,
approximately 342 times faster. Representative parse allocation increased from
4,329,992 B to 4,338,000 B (8,008 B, or 0.18%). Parse timing changed from 3.155 ms to
3.391 ms, but the short-run confidence intervals are broad and overlap, so this is not
evidence of a parse regression. The index preserves the ordered entries, first-value
`TryGet` behavior, and duplicate enumeration through `GetAll`; the measured lookup gain
justifies retaining it.

The SDL host now uses an explicit presentation revision and keeps its RGBA staging
buffer pinned for the run. An unchanged frame performs no indexed-to-RGBA conversion,
texture upload, clear, copy, or present operation. This is a structural elimination of
the entire idle-frame workload rather than a faster implementation of it; native SDL
smoke validation remains part of the platform validation workflow.

### Private 40k/Rosigma installation acceptance

The same candidate was checked against the user-owned complete
`xcom1` -> `40k` -> `40k_ROSIGMA_edits` chain on 2026-08-29. The audit consumed 512
ruleset files sourced from the installation and mapped its declared external `UFO`
resource directory. Both candidate runs reached `linked`, reported the expected 8,818
warnings and zero errors, and emitted the same 9,274,826-byte typed manifest with SHA-256
`6153996A44CFB42DD39FEAB6B56FD1BB7A55530FB2CF8A14B3B9655AFFE3728C`.

As a direct behavioral and wall-clock comparison, documentation commit `b8e6d56` was
run against the identical isolated input. Its two process-level runs took 14.593 and
14.869 seconds; the candidate took 4.888 and 4.918 seconds. The averages are 14.731
versus 4.903 seconds, approximately a 3.0-times end-to-end improvement. All four output
manifests were byte-identical. These timings include process startup, mod/resource
discovery, parsing, composition, typing, validation, and manifest writing. They are
acceptance evidence rather than a BenchmarkDotNet performance gate, but they confirm
that the synthetic-fixture improvement carries over to the intended large mod chain.

## Dependency review

BenchmarkDotNet 0.15.8 is pinned centrally. It is the current stable release at the time
of adoption, is MIT-licensed, actively maintained, and is used only by the executable
benchmark project. It adds no production runtime, mod-compatibility, deployment, or
Native AOT dependency. Version upgrades should be deliberate because harness changes
can affect comparisons across historical results.

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
| Path handling | Normalize 1,024 mixed-case backslash paths and perform 1,024 mixed hit/miss catalog lookups. |
| YAML | Parse a 1,000-entry ruleset-shaped mapping and repeatedly look up keys near its end plus missing keys. |
| Indexed rendering | Opaque 320x200 blit, transparent 64x80 sprite blit, and 640x400 indexed-to-RGBA conversion. |
| Audio | Mix 1,024 stereo frames from sixteen looping 48 kHz stereo voices. |

Setup constructs files, layers, YAML, surfaces, palettes, clips, and output buffers
outside the measured operations. Batched benchmarks declare their operations-per-invoke
count, so reported time and allocation columns remain per logical operation.

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

## Dependency review

BenchmarkDotNet 0.15.8 is pinned centrally. It is the current stable release at the time
of adoption, is MIT-licensed, actively maintained, and is used only by the executable
benchmark project. It adds no production runtime, mod-compatibility, deployment, or
Native AOT dependency. Version upgrades should be deliberate because harness changes
can affect comparisons across historical results.

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
| Indexed rendering | Opaque 320x200 blit, transparent 64x80 sprite blit, six-point polygon fill, and 640x400 indexed-to-RGBA conversion. |
| Audio | Mix 1,024 stereo frames from sixteen looping 48 kHz stereo voices, both uncontended and while a control thread continuously changes bus gain. |
| Content lifetime | Compare normal runtime publication with explicit audit-artifact retention for the public script-content fixture. |
| Script VM | Compare the allocating result adapter with prepared scalar, host-call, and three-program event frames. |

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

The portable Unicode canonicalization slice was measured on 2026-09-04 on the same
Ryzen 9 7940HS host, .NET SDK 10.0.302, and runtime 10.0.10. The established ASCII
normalization workload measured 102.4 ns and 376 B per operation, compared with the
earlier 90.9 ns and 376 B baseline. A new representative Unicode workload measured
343.6 ns and 216 B. The compatibility path remains a small part of discovery and content
startup; ASCII inputs bypass rune mapping, while non-ASCII inputs receive scalar
validation and the pinned-reference-compatible mapping.

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

### Phase 4 full-installation script acceptance

The merged Phase 4 result and its corrective candidate were audited on 2026-08-30
against the same user-owned installation with `audit-content-install`. The corrective
run parsed 512 ruleset files, attempted 3,875 scripts, retained 3,536 compiled script
artifacts, composed 31 event plans, registered 580 tags, and validated 13,651 initial
values. It reached `scripts-compiled` with the installation's expected 8,818 warnings
and zero errors. Three process-level runs emitted byte-identical 9,591,410-byte typed
manifests with SHA-256
`C52A5EA199A378EA557ACD9E86B87977DB107A81C1A92817ACF421B2C579B02D`.

The corrective audit closed failures in unique-best editable-pointer overload scoring,
catalog list-loop lowering, custom-type operations that share scalar core names, the
hidden typed `set` binding, and file-scoped `RuleList.current`. The manifest hash differs
from the Phase 3 acceptance above because later content work changed normalized output;
the earlier result remains the historical performance comparison baseline.

### Campaign time-event retention

Campaign time advancement originally retained one `CampaignTimeTrigger` per five-second
tick. The maximum command therefore created a one-million-element event array even
though consumers only needed aggregate notification and deterministic ordering. The
campaign now accumulates a fixed-size six-counter summary and can replay the ordered
sequence from its previous time and tick count through a struct enumerator. A warmed
one-million-tick unit scenario must allocate no more than 16 KiB on the calling thread.
This is a regression gate for retained memory, not a timing benchmark; per-tick gameplay
and script actions will continue to execute synchronously in the authoritative loop.

### Post-Phase-4 staged-corpus baseline

The self-contained staging workflow was exercised on 2026-08-30 against the same owned
40k/Rosigma installation. The staged corpus contains 29,926 files and 2,648,645,693
bytes across `common`, `standard`, `UFO`, `TFTD`, and `user/mods`; an independent
validation pass checked every size and SHA-256 hash. Raw content and the detailed
manifest remain ignored under `artifacts/private-install/`.

Three separate Release audit processes ran on Windows 11 build 26200, Ryzen 9 7940HS,
16 logical processors, x64, and .NET SDK 10.0.302. The two labelled warm-process runs
had these medians:

| Measurement | Median |
|---|---:|
| Process elapsed, including discovery, audit normalization, and manifest write | 9.135 s |
| Content snapshot build | 7.617 s |
| Allocation during content snapshot build | 2.452 GiB |
| Managed bytes retained over the pre-build baseline | 299.9 MiB |
| Peak process working set | 657.2 MiB |

All runs parsed 512 files, attempted 3,875 scripts, retained 3,536 artifacts, reported
8,818 expected diagnostics with zero errors, and reproduced the 9,591,410-byte semantic
manifest with SHA-256
`C52A5EA199A378EA557ACD9E86B87977DB107A81C1A92817ACF421B2C579B02D`.

This capture does not claim a cold-cache number because staging validation reads every
file immediately beforehand. The retained-managed figure is a conservative process
delta and can include build diagnostics still rooted by the audit call; therefore it
establishes that content itself is below the 512 MiB budget, not its exact exclusive
size. The content-build median alone exceeds the 5 s warm-startup target before later
resource resolution exists, and the roughly 2.45 GiB transient allocation confirms
that runtime ownership and build-session separation should remain the first
optimization slice.

### Runtime-content ownership result

Slice 1 was measured on 2026-09-01 with the same staged corpus, host, x64 environment,
and .NET SDK 10.0.302. Each of three samples used a semantic-audit process followed by
a separate runtime-only measurement process. The two warm samples produced these
medians:

| Measurement | Pre-slice baseline | Slice 1 |
|---|---:|---:|
| Audit process elapsed | 9.135 s | 9.272 s |
| Audit content-build elapsed | 7.617 s | 7.279 s |
| Runtime build allocation | 2.452 GiB conservative audit measurement | 1.883 GiB |
| Retained managed bytes | 299.9 MiB conservative audit delta | 156.1 MiB runtime-only |
| Source-file/API scopes | one full catalog per source | 510 file views / 50 shared scopes |

The allocation change is approximately a 23% reduction. The retained figures have
different boundaries: the earlier result included diagnostics and audit state, while
the new runtime-only command deliberately drops those objects before its forced-GC
measurement. The new number is the repeatable publication baseline, not a claim that
all of the apparent 48% difference came from runtime content.

No wall-clock improvement is established. The audit-process boundary was about 1.5%
slower while the audited build was about 4.4% faster than the earlier run. The runs
occurred on different days without a pinned power or thermal state, so neither change
is evidence of a regression or speedup. Stage measurements on the runtime-only warm
runs were approximately 2.345 s parse, 0.106 s compose, 2.152 s type/link, and 3.039 s
script compilation. Those boundaries identify parsing and script compilation as the
next profiling targets.

All three audits retained the established 512 files, 3,875 attempted scripts, 3,536
artifacts, 31 event plans, 580 tags, 13,651 initial values, 8,818 warnings, and zero
errors. Their 9,591,410-byte manifests were byte-identical with SHA-256
`C52A5EA199A378EA557ACD9E86B87977DB107A81C1A92817ACF421B2C579B02D`.
Audit-mode retained memory remained approximately 300 MiB, demonstrating that the
normal runtime reduction comes from releasing ownership rather than weakening the
audit oracle.

### Compact script-runtime result

Slice 2 added `ScriptVmBenchmarks` before changing the runtime representation. On
2026-09-01, the pre-change Dry smoke job reported 1,336 B for scalar execution, 1,504 B
for scalar host-call execution, and 5,520 B for a three-program event chain through the
only available result API.

After introducing packed programs and prepared execution frames, the same six-workload
Dry job on the Ryzen 9 7940HS, .NET SDK 10.0.302, and .NET runtime 10.0.10 reported:

| Workload | Allocated | Dry elapsed |
|---|---:|---:|
| Result adapter, scalar | 2,168 B | 1.243 ms |
| Result adapter, host call | 2,256 B | 1.225 ms |
| Result adapter, three-program event | 7,824 B | 2.454 ms |
| Prepared scalar frame | 0 B | 236.7 us |
| Prepared host frame | 0 B | 245.2 us |
| Prepared three-program event frame | 0 B | 324.7 us |

The allocating adapter now includes construction of a bounded frame and intentionally
remains a convenience API; gameplay hot paths must use the positional frame API. The
zero-allocation result is also asserted over repeated scalar execution in the unit
suite. Dry timings include cold-launch and single-iteration effects, so they establish
workload operation and allocation boundaries but are not statistical speedup evidence.

The staged Rosigma audit still compiled 512 files and 3,875 attempted scripts into
3,536 artifacts with 8,818 warnings and zero errors. Its normalized 9,591,410-byte
manifest retained SHA-256
`C52A5EA199A378EA557ACD9E86B87977DB107A81C1A92817ACF421B2C579B02D`.

### Resource-runtime result

Slice 3 was measured on 2026-09-02 on the same Ryzen 9 7940HS host, .NET SDK
10.0.302, and .NET runtime 10.0.10. The staged 40k/Rosigma audit reached
`scripts-compiled`, resolved 24,623 final immutable descriptors after overrides, and retained the established
512 files, 3,875 attempted scripts, 3,536 script artifacts, 8,818 warnings, and zero
errors. Across the two warm processes, resource resolution had a 203.2 ms median and
allocated 32.1 MiB; the complete build had a 6.753 s median and retained 159.2 MiB of
runtime content over its pre-build baseline. No resource body was decoded during startup.

The `ResourceRuntimeBenchmarks` ShortRun used one 256 KiB payload through directory and
compressed-ZIP VFS sources:

| Workload | Mean | Allocated |
|---|---:|---:|
| Directory cold load into an empty runtime | 345.9 us | 849.3 KiB |
| ZIP cold load into an empty runtime | 349.8 us | 1,239.2 KiB |
| Directory warm cache hit | 32.7 ns | 0 B |
| ZIP warm cache hit | 26.3 ns | 0 B |

ZIP cold time was about 1.1% above directory cold time. Although opening an archive
allocated more, the normal repeated path is the allocation-free decoded cache hit.
This result does not justify adding an archive pool with shared lifetime, locking, and
file-handle complexity. The benchmark remains in-tree so that decision can be revisited
if future streaming or uncached workloads show a material difference.

### Runtime-rule-linking result

Slice 4 was measured on 2026-09-02 on the same Ryzen 9 7940HS host, .NET SDK
10.0.302, and .NET runtime 10.0.10. The `RuntimeRuleLinkingBenchmarks` ShortRun used
the public multi-mod strategic fixture and allocated no managed memory per operation:

| Workload | Mean | Relative to compatibility lookup |
|---|---:|---:|
| Compatibility-model string lookup | 6.073 ns | 1.00 |
| Runtime-model boundary string lookup | 4.987 ns | 0.82 |
| Pre-linked direct rule access | 1.256 ns | 0.21 |
| Pre-linked relationship plus external-ID access | 2.289 ns | 0.38 |

The intended gameplay path is pre-linked direct access; string lookup remains available
at external boundaries. The nanosecond-scale fixture is a controlled representation
comparison, not an end-to-end gameplay forecast.

The three-process staged 40k/Rosigma capture reached `runtime-linked` with 12,542
projected rules/scripts, 24,623 resource descriptors, 512 parsed files, 3,875 attempted
scripts, 3,536 script artifacts, 8,818 warnings, and zero errors. Across the two warm
runs, runtime linking had a 125.1 ms median and allocated 12.6 MiB. Complete build time
was 7.189 s and runtime content retained 165.5 MiB over the pre-build baseline, about
6.3 MiB above the Slice 3 result. All three semantic manifests remained byte-identical
at 9,591,410 bytes with SHA-256
`C52A5EA199A378EA557ACD9E86B87977DB107A81C1A92817ACF421B2C579B02D`.

### Campaign capture and save result

Slice 5 was measured on 2026-09-03 on the same Ryzen 9 7940HS host, .NET SDK
10.0.302, and .NET runtime 10.0.10. `CampaignSaveBenchmarks` uses the supplied early
40k/Rosigma save and its retained opaque YAML sidecar:

| Workload | Mean | Allocated |
|---|---:|---:|
| Save-neutral capture | 12.98 us | 14.06 KiB |
| OXCE YAML overlay emit | 241.81 us | 515.55 KiB |
| YAML parse, link, validate, and restore | 1.361 ms | 1,761.91 KiB |

The representative capture is small enough that segmented or copy-on-write snapshots
are not justified yet. YAML parsing dominates allocations, but save operations are not
a frame-time path. Revisit the design after personnel, inventory, missions, and the
tactical graph are modeled; preserve this benchmark so growth is visible.

### Runtime performance-hardening result

Slice 6 was measured on 2026-09-03 on the same Ryzen 9 7940HS host, .NET SDK
10.0.302, and .NET runtime 10.0.10. ShortRun results use 1,024-frame stereo output at
48 kHz, so the buffer duration is 21.33 ms:

| Workload | Mean | Allocated | Derived callback share |
|---|---:|---:|---:|
| Mix sixteen looping stereo voices | 110.886 ns/frame | 0 B | 113.55 us/callback; 0.53% |
| Same mix while another thread continuously changes bus gain | 120.302 ns/frame | 0 B | 123.19 us/callback; 0.58% |

Even deliberately continuous control activity adds only about 8.5% to mixer time and
leaves the callback far below the 25%-of-buffer target. This does not justify replacing
the current synchronization with a bounded command queue or immutable voice snapshots.
The contention benchmark and a 10,000-callback deadlock/liveness test remain in-tree so
that decision can be revisited when decoding and streaming voices are integrated.

The indexed rendering baseline measured a changed 640x400 conversion at 280.937 us
with zero allocation. A six-point polygon initially measured 6.245 us and 72 B per
call. Caller-provided scratch storage plus stack/pooled convenience storage reduced the
same workload to 6.192 us and zero allocation without changing raster output. The
conversion result is well below the 4 ms presentation target and does not justify SIMD,
dirty rectangles, locked textures, or a palette shader at this stage.

The pinned SDL 3.4.10 Windows x64 dummy-backend smoke presented one changed 160x100
frame in 2.560 ms and suppressed 123 unchanged presentation attempts during its
two-second run. This is a local integration observation rather than a percentile.
Runtime diagnostics and all three native CI jobs now expose changed-frame duration and
idle suppression, allowing representative UI workloads to supply the eventual p95.

Long-run regression tests cycle a three-entry resource working set 10,000 times under
an eight-byte cache budget, emit/load a stable save 100 times, execute a prepared
three-program script event 10,000 times with zero managed allocation, and compare two
same-seed campaigns after 200,000 five-second ticks. These tests establish boundedness,
stability, and determinism; they are not throughput benchmarks.

### Script-compilation allocation result

The September compiler-allocation slice was measured on 2026-09-04 on the same Ryzen 9
7940HS host, .NET SDK 10.0.302, and .NET runtime 10.0.10. Tag scopes now use a monotonic
catalog revision and materialize only on a scope-cache miss. Initial-value validation
uses builder indexes directly. Binding candidates are indexed once by name and parser
group, and ordinary/list overload selection uses one pass without temporary score and
winner arrays.

The public `script-content` ShortRun comparison produced:

| Version | Mean | Allocated |
|---|---:|---:|
| Before | 827.2 us | 802.52 KiB |
| After | 840.3 us | 778.83 KiB |

Allocation fell by 23.69 KiB, or 2.95%. The timing confidence intervals overlap, so
this workload establishes no speedup or regression. An attempted per-scope constant
index increased allocation to 963.31 KiB and was removed before publication.

The staged 40k/Rosigma comparison used the preceding runtime-rule-linking capture as
its baseline and the same three-process audit/runtime harness. Warm medians were:

| Measurement | Before | After | Change |
|---|---:|---:|---:|
| Script compilation | 2,433.1 ms | 1,926.1 ms | -20.8% observed |
| Script compilation allocation | 816.7 MiB | 717.7 MiB | -12.1% |
| Complete content build | 7,188.9 ms | 6,165.1 ms | -14.2% observed |
| Complete build allocation | 1,942.1 MiB | 1,852.9 MiB | -4.6% |
| Retained runtime content | 165.5 MiB | 168.6 MiB | +3.1 MiB |

The retained increase includes the shared parser-group binding index and remains well
inside the 512 MiB budget; it is accepted in exchange for about 99 MiB less transient
script allocation. Wall-clock changes are observations rather than hard gates because
the captures are not interleaved. All runs retained 512 files, 3,875 attempted scripts,
3,536 artifacts, 580 tags, 13,651 initial values, 8,818 warnings, and zero errors. The
50 API scopes caused 51 tag-catalog materializations including final publication, and
all semantic manifests remained byte-identical at 9,591,410 bytes with SHA-256
`C52A5EA199A378EA557ACD9E86B87977DB107A81C1A92817ACF421B2C579B02D`.

### Compiled-content cache result

The versioned-cache slice was measured on 2026-09-04 on the same Ryzen 9 7940HS host,
.NET SDK 10.0.302, and .NET runtime 10.0.10. Each value below is the median of three
separate production-loader processes against the staged 40k/Rosigma installation,
except the one-time cache publication row:

| Production load | Elapsed | Current-thread allocation | Change from fresh |
|---|---:|---:|---:|
| Cache disabled, fresh median | 7,811.5 ms | 1,888.6 MiB | baseline |
| Stale rejection and cache publication | 11,621.3 ms | 2,078.2 MiB | +48.8% elapsed; +10.0% allocation |
| Warm cache-hit median | 5,366.8 ms | 943.0 MiB | -31.3% elapsed; -50.1% allocation |

The gzip image plus identity header is 19,037,803 bytes (18.16 MiB). All runs published
512 parsed files, 3,536 script artifacts, 24,623 resource descriptors, and 3,387
compatibility entries.
The cache persists the cold compatibility graph but deliberately recreates generation
handles and runtime-rule projections, so the remaining warm cost is not merely file I/O.
The stale identity was rejected from the fixed header before decompression. The slower
publication run is accepted because caching is optional, atomic, and amortized
over later launches; a no-compression experiment produced a 231 MiB image and was
rejected. Wall-clock values remain local observations rather than CI gates.

### Resource dependency correction validation (2026-09-05)

After adding bounded TAB/CAT metadata fingerprints, four separate Release
`measure-cached-content-install` processes exercised the staged 40k/Rosigma installation
with an isolated cache. The publication run took 13,867.3 ms; subsequent hits took
7,585.6, 7,267.9, and 7,333.6 ms (median 7,333.6 ms). Median current-thread allocation
was 961.7 MiB. Each load published 512 parsed files, 3,536 scripts, 24,623 descriptors,
and 12,542 runtime rules/scripts. The cache image was 19,037,799 bytes.

These are current integration observations, not a controlled before/after benchmark:
the earlier September baseline was collected in a different session. They establish
successful cache publication/restoration on the representative installation, not a
performance improvement. Warm startup remains above the 5-second target and belongs
to the planned startup-measurement follow-up. The new dependency probes read at most
four logical bytes plus length metadata per selected shared-count input; VFS construction
and archive opening still have costs. Raw outputs remain under ignored
`artifacts/cache-resource-dependency-20260905/run-*.json`.

### Startup stage attribution and metadata lookup (2026-09-05)

The startup/CI branch measured the same staged 40k/Rosigma install on the existing
Windows host with SDK 10.0.302. The instrumented baseline and candidate each used
one excluded publication/rebuild process and three separate warm-hit processes. No
build or test run overlapped these measurements. Raw JSON is retained under ignored
`artifacts/startup-stages-20260905/{baseline,candidate}-*.json`.

| Stage | Baseline median | Candidate median | Baseline allocation | Candidate allocation |
|---|---:|---:|---:|---:|
| Discovery and planning | 773.1 ms | 680.2 ms | 50,108,232 B | 50,108,232 B |
| Cache key | 182.5 ms | 116.9 ms | 16,511,536 B | 7,847,720 B |
| Cache read, decompress, deserialize | 6,204.1 ms | 5,834.6 ms | 915,615,120 B | 914,915,032 B |
| Resource restoration | 70.8 ms | 57.2 ms | 9,611,888 B | 9,611,888 B |
| Runtime rule linking | 173.3 ms | 144.8 ms | 13,427,952 B | 13,427,952 B |
| Runtime publication | 19.3 ms | 17.4 ms | 418,000 B | 418,000 B |

The change replaces full VFS catalog construction during key hashing with reverse
layer lookups for the bounded metadata inventory. It removes 8,663,816 B (52.5%) of
median key-stage allocation. Overall observed warm median changed from 7,435.4 ms
to 6,874.3 ms, but this is a short sequential sample without confidence intervals:
unchanged stages also ran faster, so the whole timing difference cannot be attributed
to this change. The allocation reduction is the primary acceptance evidence.

All runs published 512 input files, 3,536 scripts, 24,623 resource descriptors, and
12,542 runtime rules/scripts. Cache read/deserialization is about 85% of candidate
startup; the 5-second target remains open. Further work should profile converters and
the cold reconstruction graph before considering parallel loading or a new cache format.
See [startup/CI status](startup-ci-efficiency-status.md) for scope and CI deferrals.

### Hot runtime-content retention result

The hot/cold content-partitioning slice was measured on 2026-09-04 on the same Ryzen 9
7940HS host, .NET SDK 10.0.302, and .NET runtime 10.0.10. Three separate
`measure-content-install` processes loaded staged 40k/Rosigma content and returned only
`RuntimeContent` across a non-inlined boundary. The retained-byte results were
31,676,376, 31,676,376, and 31,707,096; the 31,676,376-byte median is 30.2 MiB after
full collection:

| Retained content | Before | After | Change |
|---|---:|---:|---:|
| Production `RuntimeContent` | 168.6 MiB | 30.2 MiB | -138.4 MiB; -82.1% |

The hot root still contains 12,542 runtime rules/scripts, 24,623 resource descriptors,
3,536 compiled script artifacts, 580 tags, and 13,651 initial values. The complete typed
catalog, 3,387 deferred compatibility entries, YAML nodes, and rule provenance are now
owned by `ContentCompatibilityData` and are collectible after publication. Median build
time was 7.036 s and build allocation remained about 1.81 GiB; this slice targets
steady-state size rather than construction work. The fresh semantic manifest remained
9,591,410 bytes with SHA-256
`C52A5EA199A378EA557ACD9E86B87977DB107A81C1A92817ACF421B2C579B02D`.

### Windows x64 publish-size baseline

The distribution profiles were measured on 2026-09-04 with .NET SDK 10.0.302 on
Windows 10.0.26200 x64. `tools/measure-windows-publish-size.ps1` performed Release
RID-specific publishes with trimming, single-file publication, and Native AOT explicitly
disabled. These raw `dotnet publish` directories include the default portable PDB files.
The staged profile adds the checksum-pinned SDL 3.4.10 x64 library to a copy of the
self-contained output.

| `win-x64` profile | Files | Uncompressed | Optimal ZIP |
|---|---:|---:|---:|
| Framework-dependent | 32 | 5,776,074 bytes (5.51 MiB) | 1,976,790 bytes (1.89 MiB) |
| Self-contained | 219 | 85,965,530 bytes (81.98 MiB) | 38,765,994 bytes (36.97 MiB) |
| Staged self-contained with SDL3 | 220 | 88,770,266 bytes (84.66 MiB) | 39,969,597 bytes (38.12 MiB) |

SDL3 contributes 2,804,736 bytes before compression. Compare future packaging work with
the same profile and symbol policy; framework-dependent and self-contained totals are not
interchangeable. Cross-platform distribution measurements are deferred until those hosts are
available for release packaging.

## Dependency review

BenchmarkDotNet 0.15.8 is pinned centrally. It is the current stable release at the time
of adoption, is MIT-licensed, actively maintained, and is used only by the executable
benchmark project. It adds no production runtime, mod-compatibility, deployment, or
Native AOT dependency. Version upgrades should be deliberate because harness changes
can affect comparisons across historical results.

# Startup measurement and CI efficiency

Date: 2026-09-05. Branch baseline: `3ece8ef`.

The third review branch adds startup attribution, removes a measured allocation in
cache-key construction, and cancels superseded pull-request validation. The 5-second
warm-start target is not declared complete.

## Measurement boundary

`InstallationContentLoadResult.StartupMeasurements` separates synchronous startup
stages from the existing fresh content-build measurements. Cache reading combines file
reads, decompression, JSON deserialization, and initial envelope validation: it does not
pretend to isolate I/O from conversion CPU. Resource restoration, runtime relinking,
and runtime publication are measured separately. Discovery and mod planning share one
sample. Fresh build and optional cache writing are separate stages on misses.

Wall times use the monotonic stopwatch; allocations use calling-thread counters.
Stage scopes close on failure/cancellation and are recorded once. Absent stages were
not attempted. Total covers orchestration and instrumentation overhead, and may exceed
the sum of stages. No threshold is asserted from noisy wall-clock tests.

The fixture tool emits named `startupStages` and `startupTotal` objects alongside its
existing output. Run separate processes against the same staged installation, excluding
publication/stale-rebuild runs from warm-hit medians. Raw baseline and candidate results
are retained in ignored `artifacts/startup-stages-20260905/`.

## Measured change

The instrumented baseline attributes most warm startup to cache read/deserialization.
Key construction also built a complete VFS catalog to look up only seven TAB sets and
two CAT fallback groups. It now searches layers in reverse order with the existing
layer path-normalization API. The latest layer wins, preserving `VirtualFileCatalog`
semantics and effective-resource identity without allocating full directory indexes.

Reference inspected: `src/Engine/FileMap.cpp` `_merge_resources` and layer merging at
pinned C++ commit `4df3a5e571a1a4b5e8a46d3161fb2e21a2adba15`. Existing public loose/ZIP,
shadowed-resource, preferred/fallback, explicit-override, malformed-input, and
cached/fresh scenarios are reused unchanged as the compatibility gate. No serializer,
cache format, resource fingerprint, gameplay, or script semantics change is introduced.

Results are recorded in [performance baselines](performance-baselines.md). The narrow
allocation reduction does not resolve the dominant deserialization cost. A compact
cache representation or converter optimization needs its own attribution and corpus
comparison; this branch does not add speculative parallel loading.

## CI policy

Both workflows use workflow name, event name, and PR number as the PR concurrency key.
Cancellation is enabled only for `pull_request`. For main pushes and manual dispatch,
the fallback is the unique run ID. This is deliberate: merely disabling cancellation
on a shared main group would still allow newer pending runs to replace older pending
runs. The behavior follows [GitHub's concurrency documentation](https://docs.github.com/en/actions/how-tos/write-workflows/choose-when-workflows-run/control-workflow-concurrency).

The three-platform build/test matrix, separate coverage job, and three-platform native
SDL validation remain intact. No push triggers or required-check names change. A final
candidate should still be pushed once after local validation; cancellation saves work
when an additional candidate is needed, not a reason to push partial work.

Compiled SDL artifact caching was considered and deferred. The workflow uses moving
hosted runner images and builds against system libraries; a reusable native artifact
requires compiler, image, architecture, source checksum, build-option, and system-library
identity plus real cross-platform runner evidence. No current runner-duration comparison
was collected in this local task. Supersession cancellation is the supported saving for
this branch; it does not weaken native smoke validation or claim a measured runner-minute
reduction. Workflow YAML is parsed locally and expressions reviewed against the official
documentation; hosted execution is verified when the PR runs.

Local validation: Release build passed with zero warnings/errors; all 572 unit and
compatibility tests passed with none skipped; formatting verification and both workflow
YAML parses passed.

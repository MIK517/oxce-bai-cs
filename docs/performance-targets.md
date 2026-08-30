# Runtime performance targets

Target policy date: 2026-08-30.

These are initial engineering budgets for the post-Phase-4 runtime work. They protect
architecture from unbounded retention or hot-path allocation; they are not permission
to weaken compatibility. Controlled baselines may revise a number when representative
evidence shows it is unrealistic, but revisions must record the workload, hardware,
and reason.

## Supported system class

The project targets an average modern household desktop or laptop rather than the old
hardware supported by the original engine. The initial reference class is:

- a maintained x64 or Arm64 desktop operating system;
- a mainstream 2020-or-newer processor with at least four physical cores and eight
  logical processors;
- 16 GiB of system memory;
- SSD storage;
- an SDL3-supported graphics device capable of ordinary desktop composition.

The current development host's Ryzen 9 7940HS is the controlled comparison machine
already used by `performance-baselines.md`; it is intentionally not the minimum system.
Measurements on it establish regressions and relative improvements. Release-level
budgets should also be sampled on hardware near the reference class before becoming
hard gates.

## Initial budgets

| Area | Initial target on the reference class | Measurement boundary |
|---|---:|---|
| Full staged `xcom1` -> `40k` -> `rosigma` cold startup | <= 10 s | Process start through linked rules, compiled scripts, and resolved resource descriptors; no bulk decode. |
| Same warm startup | <= 5 s | Identical corpus after OS filesystem cache warm-up. |
| Runtime content metadata retained after build | <= 512 MiB | Rules, scripts, descriptors, IDs, provenance, and required sidecars; excludes decoded-resource cache and diagnostic/audit tooling. |
| Default decoded-resource cache | <= 512 MiB | Size-accounted cache with eviction; streaming buffers have separate explicit bounds. |
| Normal total managed steady state before a battle | <= 1.5 GiB | Modded campaign with default cache; excludes native driver allocations and optional audit artifacts. |
| Strategic save capture and write | <= 500 ms | Representative implemented mid-sized campaign, including durable atomic replacement. |
| Strategic save load and transactional restore | <= 1 s | Parse through validated publication. |
| Active-battle save capture/write or load/restore | <= 2 s | Representative large tactical state once Phase 7 supplies the workload. |
| Successful non-traced scalar script invocation | 0 B allocated | VM call including outputs through the runtime API; tool/result adapters are excluded. |
| Interactive frame construction | p95 <= 8 ms | Main-thread state snapshot to indexed frame on a representative busy screen. |
| Presentation conversion/upload | p95 <= 4 ms | Changed 640x400 indexed frame through SDL present path; unchanged frames remain suppressed. |
| Audio callback work | p99 <= 25% of buffer duration | Maximum supported effect voices plus music/ambient on the reference class. |

The startup and memory budgets are deliberately generous relative to the current
content-only measurements. They allow a large scripted installation while still
detecting retained YAML graphs, duplicated catalogs, unbounded caches, or eager decode.
The cache budget must be configurable downward and upward; eviction correctness cannot
depend on the default.

Tactical simulation, pathfinding, visibility, and AI budgets will be added with their
first representative scenarios. AI turns may legitimately exceed one display frame,
but input, rendering, and audio must remain responsive while bounded background or
incremental work proceeds.

## Dependency and redistribution policy

Media dependencies are selected per format for the most convenient compatible
redistribution without compromising licensing or package size. Evaluation order is:

1. Correct decoding/synthesis for the complete required corpus and malformed-input
   behavior.
2. GPL-compatible licensing and permission to redistribute all required binaries/data.
3. Maintained Windows, Linux, and macOS support without host-wide installation.
4. Bounded operation, deterministic testability, and acceptable security history.
5. Self-contained deployment size and operational simplicity.
6. Native AOT implications, which remain informative rather than a current gate.

A managed implementation is preferred when compatibility and maintenance are equal.
A project-local native dependency is acceptable when it materially improves format
coverage, correctness, redistribution, or size. System-installed codecs and mandatory
external tools are not acceptable runtime dependencies. Each selected library requires
the package review mandated by `AGENTS.md` and an ADR when it establishes a lasting
runtime/deployment policy.

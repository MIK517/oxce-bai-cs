# ADR 0014: Dedicated resource runtime

## Status

Accepted and implemented in post-Phase-4 Slice 3.

## Context

`Oxce.Mods` owns virtual-file precedence, resource declarations, mod provenance, and
the staged content build. `Oxce.Formats` owns bounded decoders, `Oxce.Rendering` owns
indexed software representations, and `Oxce.Engine` owns playback and presentation
orchestration. Putting decoded-resource lifetime and caches into any one of those
projects would either mix compatibility parsing with runtime policy or create an
undesired dependency from gameplay to presentation/media implementations.

Phase 5 must resolve the reference engine's shared sprite/sound offsets, publish honest
`resources-resolved` evidence, decode assets lazily, stream large media, and bound
retained decoded data. It also needs one reusable path for the application, resource
browser, tests, and future UI.

## Decision

Create a dedicated `Oxce.Resources` production project.

`Oxce.Mods` continues to own compatibility-facing declarations and resolves them into
lightweight immutable descriptors: source identity, virtual path, mod/layer provenance,
shared offsets, dimensions or format hints when known, and typed content-generation
handles. Reaching `resources-resolved` proves that these descriptors and offsets are
valid; it does not require decoding the installation.

`Oxce.Resources` references `Oxce.Core`, `Oxce.Formats`, `Oxce.Mods`, and
`Oxce.Rendering`. It owns decoded-resource loading, size-aware bounded caches, preload
groups, streaming sources, cache telemetry, archive access policy, and invalidation by
content generation. `Oxce.Engine` and tools consume this runtime. `Oxce.Gameplay` does
not reference it and cannot depend on decoded surfaces, audio clips, or filesystem
state.

Platform-neutral immutable audio clip/stream contracts needed by resource loading will
move to or be defined in `Oxce.Resources`; engine playback consumes those contracts.
SDL device and rendering details remain in `Oxce.Platform.Sdl`.

The application composes rule content and its matching resource runtime. Numeric
resource handles are never written to saves or exposed as stable external identifiers.
External paths/IDs and provenance remain available for diagnostics and extensions.

## Dependency direction

```text
App -> Engine -> Resources -> Mods -> Scripting -> Core
Resources -> Formats -> Core
Resources -> Rendering -> Core
Engine -> Gameplay -> Mods
Platform.Sdl -> Engine
Platform.Sdl -> Rendering
```

`Resources` may use `Rendering`; `Rendering` must not reference `Resources`. `Mods`
must not reference `Resources`. This prevents cycles and keeps parsing/linking usable
without decoded runtime assets.

## Consequences

- There is one platform-neutral asset lifetime and cache policy shared by the game and
  tools.
- Gameplay remains headless and resource-implementation independent.
- Content can validate resource identity/offset compatibility without decoding every
  asset.
- Moving the audio data contract requires a focused migration, but avoids an
  `Resources` -> `Engine` -> `Resources` cycle.
- Cache and archive pooling decisions remain measured implementation choices rather
  than part of this project-boundary decision.

## Implemented policy

`ResourceRuntime` uses a size-accounted LRU with a 512 MiB default decoded budget and
a 128 MiB per-entry ceiling. Loads are lazy unless a caller supplies an explicit
preload group. Music and video descriptors are streaming-only and cannot enter the
decoded cache. Handles carry a content-generation ID; stale handles are rejected and
generation invalidation releases all retained entries. Cache telemetry reports hits,
misses, loads, evictions, oversized rejections, retained bytes, and entry count.

A 2026-09-02 ShortRun benchmark found 256 KiB directory and ZIP cold loads at 345.9 us
and 349.8 us respectively; warm cache hits were 32.7 ns and 26.3 ns with no allocation.
The roughly 1% cold-time difference does not justify archive leasing/pooling. ZIP
entries continue to own an archive per uncached stream, avoiding shared archive
lifetime and contention. Revisit only if a representative workload demonstrates a
material uncached-entry bottleneck.

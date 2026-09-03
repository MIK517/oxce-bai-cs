# Runtime performance-hardening status

Review date: 2026-09-03.

## Outcome

Post-Phase-4 Slice 6 is complete. The branch adds controlled mixer-contention and
polygon workloads, measures the complete changed-frame SDL presentation boundary,
counts unchanged-frame suppression, removes ordinary polygon scratch allocation, and
adds bounded long-run regression tests for caches, saves, scripts, simulation, and
audio control concurrency.

The results deliberately leave the mixer synchronization and full-frame RGBA path
unchanged. Their measured costs are comfortably below current budgets, so command
queues, immutable voice snapshots, SIMD conversion, dirty rectangles, locked textures,
and palette shaders would add complexity without demonstrated benefit. Polygon scratch
was the only measured allocation in scope and now uses caller-provided, stack, or
bounded pooled storage while retaining the allocating-convenience-shaped entry point.

Windows now participates in the native `SDL validation` workflow. The staging script
downloads the official SDL 3.4.10 Visual C++ archive, verifies SHA-256
`E2B336B10B037934AF98308027410732EF7B22F2C6697D58092AA1C209FAE7D7`, and copies only
the x64 runtime beside the published app. Linux and macOS continue to build the same
pinned release from source.

The minimal presentation deferred by the campaign foundation also lands here.
`CampaignOverviewClient` is an engine-owned indexed view over gameplay commands and
snapshots: clicking the globe places the starting base, Space advances one minute,
and unchanged host ticks do not revise the frame. `Oxce.App --campaign-sdl` composes it
with real runtime-linked installation content and optionally writes a compatible save
on exit. No SDL or persistence type enters `Oxce.Gameplay`.

## Evidence

- Ryzen 9 7940HS / .NET 10.0.10 ShortRun: sixteen-voice mixing measured 113.55 us per
  1,024-frame callback normally and 123.19 us under continuous gain-control contention,
  with zero managed allocation in both cases.
- A changed 640x400 indexed-to-RGBA conversion measured 280.937 us with zero managed
  allocation.
- Six-point polygon fill changed from 6.245 us / 72 B to 6.192 us / 0 B.
- The local pinned Windows SDL dummy smoke presented one changed 160x100 frame in
  2.560 ms, suppressed 123 unchanged presentation attempts, and completed managed-mixer
  audio streaming.
- The Release solution build and all 481 locally available unit, public compatibility,
  and private-corpus tests pass.

Full commands, workload boundaries, caveats, and decisions are retained in
[performance baselines](performance-baselines.md). Native runner success remains a CI
gate; the local dummy result does not replace X11, Cocoa, or hosted Windows execution.

## Reference implementation considered

This slice changes no gameplay, asset, save, or script compatibility semantics. The
performance boundaries remain based on the already captured reference behavior in:

- `src/Engine/Surface.cpp` and `src/Engine/ShaderDraw.h` for indexed primitive output;
- `src/Engine/Game.cpp`, `src/Engine/Sound.cpp`, and `src/Engine/Music.cpp` for callback
  ownership and playback-visible mixer behavior;
- `src/Engine/Timer.cpp` and `src/Engine/Timer.h` for independently paced simulation;
- `src/Mod/Mod.cpp` and `src/Savegame/SavedGame.cpp` for the real-content and repeated
  persistence workloads.

The campaign view is intentionally a redesigned presentation of the already pinned
campaign commands and snapshots. It adds no new compatibility rule and therefore does
not need a new C++ oracle fixture.

## Next boundary

The next strategic slice should close the TFTD runtime projection blocker, then add
personnel, inventory, craft, transfers, facilities, and finance end to end with their
save fields, script providers/events, headless scenarios, and operable UI actions.

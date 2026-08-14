# ADR 0002: Indexed rendering with an SDL3 platform boundary

- Status: accepted for desktop implementation; deployment validation remains ongoing
- Date: 2026-08-12

## Decision

Keep an owned, platform-independent 8-bit indexed surface and palette model. Use SDL3
behind `Oxce.Platform.Sdl` for windowing, input, audio integration, and final frame
presentation. Do not expose SDL handles or structs to higher projects.

## Rationale

Original assets, palette shading, transparency, and much OXCE drawing logic are naturally
indexed. Preserving this model is simpler and safer than prematurely converting all code
to an RGBA sprite engine, while final presentation can still use modern textures/shaders.

## Consequences

- Software rendering stays headless and fixture-testable.
- UI and GPU presentation can evolve independently.
- Native calls use a small source-generated `LibraryImport` surface and safe lifetime
  wrappers rather than an external C# binding package.
- [SDL 3.4.10](https://github.com/libsdl-org/SDL/releases/tag/release-3.4.10) is the
  first validated runtime. The official Windows VC archive has SHA-256
  `e2b336b10b037934af98308027410732ef7b22f2c6697d58092aa1c209fae7d7`.
- SDL's zlib license is compatible with this project's GPL-3.0-or-later distribution.
- Windows x64 window creation, indexed-frame presentation, resize/letterboxing, and the
  event loop have been exercised. SDL3 events are translated into engine-owned input
  values before crossing the platform boundary. Linux, macOS, audio output, and packaged
  native deployment remain separate validation work.
- A future Android host may reuse the platform boundary, but desktop data compatibility
  and feature completion take priority and mobile UI may differ substantially.

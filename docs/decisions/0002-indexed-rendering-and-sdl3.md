# ADR 0002: Indexed rendering with an SDL3 platform boundary

- Status: proposed; validate in Phase 2 spike
- Date: 2026-08-06

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
- The SDL3 C# binding/native deployment choice must be pinned and reviewed in the spike.


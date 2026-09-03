# Phase 2 closure audit

This audit records evidence against the original-asset and indexed-rendering foundation
in `implementation-plan.md`. Detailed behavior remains tracked in the compatibility
matrices.

- Audit date: 2026-08-17
- Reference implementation: `oxce-bai` C++ checkout
- Managed test inventory at closure: 246 tests in two assemblies

## Deliverable assessment

| Deliverable | Evidence | Assessment |
|---|---|---|
| Indexed surfaces, palettes, drawing, and text | `indexed-surface-operations`, `xcom-palettes`, sprite-font tests, private UFO/TFTD palettes | complete |
| SCR/DAT, SPK, BDY, PCK/TAB, and general indexed images | public compatibility fixtures plus private PNG/GIF/BMP/LBM and UFO/TFTD/40k/Rosigma corpus tests | complete for the declared indexed formats |
| CAT, MAP, RMP, MCD, LOFT, fonts, palettes, and identified original containers | bounded unit tests, C++ fixtures, and private corpus tests | complete for Phase 2 |
| Owned-installation resource browser | `PrivateResourceBrowserTests` builds non-empty geoscape/battlescape previews for UFO and TFTD through `VirtualFileCatalog` | exit gate met |
| SDL3 indexed presentation and input | Required checksum-pinned Windows, Linux X11, and macOS Cocoa CI smokes | foundation complete |
| Audio boundary and mixer decision | ADR 0006, managed-mixer fixtures, SDL3 dummy-device smokes on all three desktop systems | foundation complete |
| Music decoder and synthesizer direction | ADR 0007 and supplied-corpus inventory | strategy complete; playback intentionally deferred |

Exact indexed output is covered by executable C++ fixtures for deterministic formats.
Malformed inputs and allocation limits are tested in each implemented parser. The
ignored private tests additionally exercise owned assets without placing copyrighted
material in Git.

## Explicitly deferred work

These entries do not block the Phase 2 foundation, but they remain partial in the
matrices and must not be described as compatible:

- Ruleset-driven sound-set and music discovery belongs to Phase 3 typed rules and later
  gameplay integration.
- OGG/MP3/FLAC/MOD music decoding, MIDI and AdLib synthesis, and actual music playback
  follow ADR 0007 in Phase 8. OGG, MP3, MIDI, and AdLib are demonstrated requirements of
  the supplied corpus; FLAC and MOD remain explicit reference-format backlog.
- FLI/FLC playback scheduling, audio synchronization, and skipping remain Phase 8; the
  bounded container/frame/audio decoding layer is complete.
- Physical-device validation and release packaging remain release work. CI validates
  real desktop window backends and SDL's dummy audio device, not runner hardware.
- ZIP-backed mod layers and exact Unicode filesystem case behavior are Phase 3 file/mod
  system work, not original-format decoder gaps.

## Artifact review

Original game data and private corpus material remain ignored. The local SDL 3.4.10
runtime is retained because the Windows smoke helper consumes it, and project-local
.NET/NuGet caches remain useful for repeatable offline validation. C++ oracle build
directories are reproducible but retained for likely fixture maintenance. The obsolete
SDL_image 1.2 source probe and one bootstrap comparison output were removed at this
milestone.

## Closure

Both Phase 2 exit criteria are met. The phase is **complete as a foundation**, with the
deferred integration and playback work above still represented as partial rather than
being hidden by the phase boundary. Phase 3 is the next implementation phase.

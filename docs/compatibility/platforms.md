# Platform compatibility

| Platform | Architecture | Status | Fixture or CI job | Notes |
|---|---|---|---|---|
| Windows | x64 | partial | `windows-latest` CI; manual SDL 3.4.10 window and dummy-audio smokes | Build/tests plus the indexed-window event loop, resize/letterboxing, event translation, presentation, and managed-mixer-to-SDL stream callback pass. Physical-device smoke and packaging remain. |
| Linux | x64 | partial | `ubuntu-latest` CI | Managed build/tests pass; SDL runtime and packaging smoke remain. |
| macOS | arm64 hosted runner | partial | `macos-latest` CI | Managed build/tests pass; SDL runtime and packaging smoke remain. |

## Cross-platform behavior requiring fixtures

| Area | Status | Reference source | Fixture | Notes |
|---|---|---|---|---|
| Path separators and normalization | partial | `src/Engine/FileMap.cpp` | `vfs-layered-catalog`; unit tests | Slash normalization and unsafe path rejection are implemented; Unicode cases remain. |
| Case-sensitive resource lookup | compatible | `src/Engine/FileMap.cpp` | `vfs-layered-catalog` | Case-insensitive virtual lookup passes on Windows and case-sensitive CI filesystems. |
| Unicode paths | not started | startup and file-map code |  | Include mod and save paths. |
| Endianness assumptions | partial | binary resource loaders | `binary-endian-primitives`; `indexed-screen-codecs` | Explicit primitives and SPK command words are covered; each future codec still requires fixtures. |
| Input coordinates and desktop quit shortcuts | partial | `src/Engine/Action.cpp`, `src/Engine/CrossPlatform.cpp` | `input-audio-semantics`; unit tests | Reference logical/surface pointer transforms match. SDL3 keyboard, text, pointer, wheel, resize, focus, minimize/restore, close, and platform quit events translate to engine-owned values. Controller/touch mapping remains. |
| SDL3 native loading | partial | SDL platform spike | manual `--sdl-smoke` | SDL 3.4.10 loads and runs a resizable, letterboxed indexed-window event loop on Windows x64. Native handles remain inside `Oxce.Platform.Sdl`; Linux/macOS native smoke and packaging remain. |
| Audio device and mixer boundary | partial | `src/Engine/Game.cpp`, `src/Engine/Sound.cpp`, `src/Engine/Music.cpp` | `input-audio-semantics`; `managed-mixer`; unit tests; SDL3 dummy-device smoke; ADR 0006 | PCM clips, playback/bus contracts, headless output, nonlinear reference volume, owned stereo mixing, loops, gain/pan, fixed-point rate conversion, pause/resume, reserved-bus voice policy, SDL3 signed-16 stereo stream output, and graceful device-open failure are implemented. Linux/macOS native and Windows physical-device smokes remain. |
| Self-contained deployment | not started | packaging configuration |  | Native AOT is not a bootstrap requirement. |

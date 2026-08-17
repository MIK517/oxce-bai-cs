# Platform compatibility

| Platform | Architecture | Status | Fixture or CI job | Notes |
|---|---|---|---|---|
| Windows | x64 | partial | `windows-latest` CI; manual SDL 3.4.10 window and dummy-audio smokes | Build/tests plus the indexed-window event loop, resize/letterboxing, event translation, presentation, and managed-mixer-to-SDL stream callback pass. Physical-device smoke and packaging remain. |
| Linux | hosted runner architecture | partial | `ubuntu-latest` CI; path-scoped `SDL validation / Linux X11` | Managed build/tests pass. The native smoke builds checksum-pinned SDL 3.4.10, stages it beside the published app, and requires X11 indexed presentation plus dummy-audio streaming. Physical display/audio and release packaging remain. |
| macOS | hosted runner architecture | partial | `macos-latest` CI; path-scoped `SDL validation / macOS Cocoa` | Managed build/tests pass. The native smoke builds checksum-pinned SDL 3.4.10, stages it beside the published app, and requires Cocoa indexed presentation plus dummy-audio streaming. An offscreen fallback provides diagnostics but intentionally cannot make the job pass. Physical display/audio and release packaging remain. |

## Cross-platform behavior requiring fixtures

| Area | Status | Reference source | Fixture | Notes |
|---|---|---|---|---|
| Path separators and normalization | partial | `src/Engine/FileMap.cpp` | `vfs-layered-catalog`; unit tests | Slash normalization and unsafe path rejection are implemented; Unicode cases remain. |
| Case-sensitive resource lookup | compatible | `src/Engine/FileMap.cpp` | `vfs-layered-catalog` | Case-insensitive virtual lookup passes on Windows and case-sensitive CI filesystems. |
| Unicode paths | not started | startup and file-map code |  | Include mod and save paths. |
| Endianness assumptions | partial | binary resource loaders | `binary-endian-primitives`; `indexed-screen-codecs` | Explicit primitives and SPK command words are covered; each future codec still requires fixtures. |
| Input coordinates and desktop quit shortcuts | partial | `src/Engine/Action.cpp`, `src/Engine/CrossPlatform.cpp` | `input-audio-semantics`; unit tests | Reference logical/surface pointer transforms match. SDL3 keyboard, text, pointer, wheel, resize, focus, minimize/restore, close, and platform quit events translate to engine-owned values. Controller/touch mapping remains. |
| SDL3 native loading | compatible | SDL platform spike | Windows manual `--sdl-smoke`; path-scoped Linux/macOS SDL validation | SDL 3.4.10 loads from the published-app directory and runs the indexed-window event loop through Windows, X11, and Cocoa backends. CI asserts the selected backend, linked SDL version, renderer creation, ticks, and presented frames. Native handles remain inside `Oxce.Platform.Sdl`; release packaging remains separate. |
| Audio device and mixer boundary | partial | `src/Engine/Game.cpp`, `src/Engine/Sound.cpp`, `src/Engine/Music.cpp` | `input-audio-semantics`; `managed-mixer`; unit tests; cross-platform SDL3 dummy-device smoke; ADR 0006 | PCM clips, playback/bus contracts, headless output, nonlinear reference volume, owned stereo mixing, loops, gain/pan, fixed-point rate conversion, pause/resume, reserved-bus voice policy, SDL3 signed-16 stereo stream output, and graceful device-open failure are implemented. Dummy-device native streaming is validated on Windows, Linux, and macOS; physical-device smokes remain. |
| Self-contained deployment | not started | packaging configuration |  | Native AOT is not a bootstrap requirement. |

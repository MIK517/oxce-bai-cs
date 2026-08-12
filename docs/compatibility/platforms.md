# Platform compatibility

| Platform | Architecture | Status | Fixture or CI job | Notes |
|---|---|---|---|---|
| Windows | x64 | partial | `windows-latest` CI; manual SDL 3.4.10 smoke | Build/tests and indexed-frame window presentation pass. Packaging, input, and audio remain. |
| Linux | x64 | partial | `ubuntu-latest` CI | Managed build/tests pass; SDL runtime and packaging smoke remain. |
| macOS | arm64 hosted runner | partial | `macos-latest` CI | Managed build/tests pass; SDL runtime and packaging smoke remain. |

## Cross-platform behavior requiring fixtures

| Area | Status | Reference source | Fixture | Notes |
|---|---|---|---|---|
| Path separators and normalization | partial | `src/Engine/FileMap.cpp` | `vfs-layered-catalog`; unit tests | Slash normalization and unsafe path rejection are implemented; Unicode cases remain. |
| Case-sensitive resource lookup | compatible | `src/Engine/FileMap.cpp` | `vfs-layered-catalog` | Case-insensitive virtual lookup passes on Windows and case-sensitive CI filesystems. |
| Unicode paths | not started | startup and file-map code |  | Include mod and save paths. |
| Endianness assumptions | partial | binary resource loaders | `binary-endian-primitives`; `indexed-screen-codecs` | Explicit primitives and SPK command words are covered; each future codec still requires fixtures. |
| SDL3 native loading | partial | SDL platform spike | manual `--sdl-smoke` | SDL 3.4.10 loads and presents on Windows x64. Native handles remain inside `Oxce.Platform.Sdl`; Linux/macOS remain. |
| Self-contained deployment | not started | packaging configuration |  | Native AOT is not a bootstrap requirement. |

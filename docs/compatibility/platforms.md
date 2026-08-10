# Platform compatibility

| Platform | Architecture | Status | Fixture or CI job | Notes |
|---|---|---|---|---|
| Windows | x64 | partial | `windows-latest` CI definition; local build/tests | SDL and packaging not started. |
| Linux | x64 | not started | `ubuntu-latest` CI definition | CI must run after the repository is hosted. |
| macOS | arm64/x64 hosted runner | not started | `macos-latest` CI definition | SDL and packaging not started. |

## Cross-platform behavior requiring fixtures

| Area | Status | Reference source | Fixture | Notes |
|---|---|---|---|---|
| Path separators and normalization | not started | `src/Engine/FileMap.cpp` |  | External identifiers use stable normalized paths. |
| Case-sensitive resource lookup | not started | `src/Engine/FileMap.cpp` |  | Test Windows and case-sensitive filesystems. |
| Unicode paths | not started | startup and file-map code |  | Include mod and save paths. |
| Endianness assumptions | not started | binary resource loaders |  | Codecs must use explicit endian operations. |
| SDL3 native loading | not started | SDL platform spike |  | No native handles may leak into domain projects. |
| Self-contained deployment | not started | packaging configuration |  | Native AOT is not a bootstrap requirement. |

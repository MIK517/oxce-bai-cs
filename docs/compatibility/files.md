# File and resource catalog compatibility

| Area | Status | Reference source | Fixture | Notes |
|---|---|---|---|---|
| Explicit-endian binary primitives | compatible | `src/Engine/CatFile.cpp`, `src/Savegame/SaveConverter.cpp` | `binary-endian-primitives` | Bounded readers and writers cover signed and unsigned integers plus IEEE floating point in both byte orders. |
| Resource lookup and layer precedence | compatible | `src/Engine/FileMap.cpp` (`VFSLayer::insert`, `VFSLayerStack::_merge_resources`) | `vfs-layered-catalog` | Lookup is case-insensitive; the last entry in a layer and the highest layer win. |
| Vertical resource slices | compatible | `src/Engine/FileMap.cpp` (`VFSLayerStack::get_slice`) | `vfs-layered-catalog` | Results retain a nullable slot for every layer from lowest to highest priority. |
| Virtual folder unions | compatible | `src/Engine/FileMap.cpp` (`VFSLayer::insert`, `VFSLayerStack::_merge_vdirs`) | `vfs-layered-catalog` | Lists contain immediate resource basenames, exclude rulesets, and are emitted deterministically. |
| Ruleset separation and order | compatible | `src/Engine/FileMap.cpp` (`isRuleset`, `VFSLayerStack::push_back`) | `vfs-layered-catalog` | `.rul` matching is case-insensitive and order follows entries within layers, then layer priority. |
| Filesystem directory layers | partial | `src/Engine/FileMap.cpp` (`ls_r`, `VFSLayer::mapPlainDir`) | unit tests | Recursive physical-file discovery is bounded and deterministic. Reparse points and unsafe virtual paths are rejected; exact Unicode case behavior still needs a reference fixture. |
| ZIP-backed layers and prefixes | not started | `src/Engine/FileMap.cpp` (`VFSLayer::mapZip`, `mapZipFileRW`) |  | Must bound compressed/uncompressed sizes and preserve archive entry provenance. |

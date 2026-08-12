# Original asset compatibility

| Format or area | Status | Reference source | Fixture | Notes |
|---|---|---|---|---|
| 8-bit indexed surface model | partial | `src/Engine/Surface.h`, `src/Engine/Surface.cpp` | unit tests | Zero-initialized storage, dimensions, row addressing, 256-color palettes, and RGBA presentation conversion are implemented. Drawing operations remain. |
| Palettes and palette blocks | partial | `src/Engine/Palette.h`, `src/Engine/Palette.cpp` | unit tests | The 256-entry RGBA model exists; original palette-file decoding and shade blocks remain. |
| SCR/DAT raw indexed images | compatible | `src/Engine/Surface.cpp` (`rawCopy`, `loadRaw`, `loadScr`) | `indexed-screen-codecs` | Copies visible bytes, leaves short-image tail pixels unchanged, and ignores overscan bytes. Dimensions remain caller-owned as in the reference surface. |
| SPK images | compatible | `src/Engine/Surface.cpp` (`Surface::loadSpk`) | `indexed-screen-codecs` | Transparent/literal word runs and unknown command words match. Truncated commands fail intentionally instead of synthesizing zero bytes. |
| BDY images | compatible | `src/Engine/Surface.cpp` (`Surface::loadBdy`) | `indexed-screen-codecs` | Literal and repeated runs preserve reference row clipping. Truncated runs fail intentionally. |
| PCK/TAB sprite sets | not started | `src/Engine/SurfaceSet.cpp` |  | Includes TAB width variants and offsets. |
| General image formats | not started | `src/Engine/Surface.cpp`, image-library call sites |  | PNG and other mod-provided formats. |
| CAT containers | not started | `src/Engine/CatFile.h`, `src/Engine/CatFile.cpp` |  | Validate offsets and entry sizes. |
| GM CAT music | not started | `src/Engine/GMCat.h`, `src/Engine/GMCat.cpp` |  | Catalog extraction and playback-visible content. |
| MAP terrain maps | not started | `src/Battlescape/Map.cpp`, map loading call sites |  | Dimensions and tile topology. |
| RMP routes/nodes | not started | map generation and route loading call sites |  | Node links and reserved fields. |
| MCD terrain records | not started | `src/Mod/MapDataSet.cpp` |  | Signedness and record-size validation. |
| LOFTEMPS voxel data | not started | `src/Mod/MapDataSet.cpp` |  | Validate record count and references. |
| Fonts | not started | `src/Engine/Font.cpp`, `src/Engine/DosFont.h` |  | Bitmap metrics and localization behavior. |
| Sounds and sound sets | not started | `src/Engine/Sound.cpp`, `src/Engine/SoundSet.cpp` |  | CAT/WAV and mixer-visible behavior. |
| Music and AdLib/MIDI | not started | `src/Engine/Music.cpp`, `src/Engine/AdlibMusic.cpp` |  | Backend strategy requires an ADR. |
| FLC video | not started | `src/Engine/FlcPlayer.cpp` |  | Frame timing may differ; decoded content must remain equivalent. |

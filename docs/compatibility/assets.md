# Original asset compatibility

| Format or area | Status | Reference source | Fixture | Notes |
|---|---|---|---|---|
| 8-bit indexed surface model | partial | `src/Engine/Surface.h`, `src/Engine/Surface.cpp` | unit tests | Zero-initialized storage, dimensions, row addressing, 256-color palettes, and RGBA presentation conversion are implemented. Drawing operations remain. |
| Palettes and palette blocks | compatible | `src/Engine/Palette.h`, `src/Engine/Palette.cpp`, `src/Engine/State.cpp`, `src/Mod/Mod.cpp` | `xcom-palettes`; private UFO/TFTD corpus | RGB6-to-RGBA scaling, index-zero transparency, 768-byte blocks with six-byte separators, short-file zero initialization, 16-color block offsets, and bounded palette replacement match. The indexed model does not need the reference SDL duplicate-black workaround because palette indices are never remapped by RGB equality. |
| SCR/DAT raw indexed images | compatible | `src/Engine/Surface.cpp` (`rawCopy`, `loadRaw`, `loadScr`) | `indexed-screen-codecs` | Copies visible bytes, leaves short-image tail pixels unchanged, and ignores overscan bytes. Dimensions remain caller-owned as in the reference surface. |
| SPK images | compatible | `src/Engine/Surface.cpp` (`Surface::loadSpk`) | `indexed-screen-codecs` | Transparent/literal word runs and unknown command words match. Truncated commands fail intentionally instead of synthesizing zero bytes. |
| BDY images | compatible | `src/Engine/Surface.cpp` (`Surface::loadBdy`) | `indexed-screen-codecs` | Literal and repeated runs preserve reference row clipping. Truncated runs fail intentionally. |
| PCK/TAB sprite sets | compatible | `src/Engine/SurfaceSet.cpp`, `src/Engine/SurfaceSet.h`, `src/Engine/Surface.cpp` | `pck-tab-sprites`; private UFO/TFTD/40k/Rosigma corpus | Sequential frame decoding, leading transparent rows, transparent runs, overscan clipping, no-TAB single frames, and 16/32-bit TAB frame counts match. Malformed streams fail intentionally; decoded output is bounded. Two-byte one-frame TAB files used by 40k/Rosigma are handled deterministically instead of relying on the reference loader's incomplete four-byte probe. |
| General image formats | not started | `src/Engine/Surface.cpp`, image-library call sites |  | PNG and other mod-provided formats. |
| CAT containers | compatible | `src/Engine/CatFile.h`, `src/Engine/CatFile.cpp` | `cat-entries`; private UFO/TFTD/mod corpus | Stored sizes are ignored and entry bounds come from adjacent valid offsets; embedded name chunks remain part of entry data. Malformed tables, descending offsets, and out-of-range entries fail intentionally instead of being skipped or producing unsigned sizes. Entry count and total input size are bounded. |
| GM CAT music | not started | `src/Engine/GMCat.h`, `src/Engine/GMCat.cpp` |  | Catalog extraction and playback-visible content. |
| MAP terrain maps | not started | `src/Battlescape/Map.cpp`, map loading call sites |  | Dimensions and tile topology. |
| RMP routes/nodes | not started | map generation and route loading call sites |  | Node links and reserved fields. |
| MCD terrain records | not started | `src/Mod/MapDataSet.cpp` |  | Signedness and record-size validation. |
| LOFTEMPS voxel data | not started | `src/Mod/MapDataSet.cpp` |  | Validate record count and references. |
| Fonts | not started | `src/Engine/Font.cpp`, `src/Engine/DosFont.h` |  | Bitmap metrics and localization behavior. |
| Sounds and sound sets | not started | `src/Engine/Sound.cpp`, `src/Engine/SoundSet.cpp` |  | CAT/WAV and mixer-visible behavior. |
| Music and AdLib/MIDI | not started | `src/Engine/Music.cpp`, `src/Engine/AdlibMusic.cpp` |  | Backend strategy requires an ADR. |
| FLC video | not started | `src/Engine/FlcPlayer.cpp` |  | Frame timing may differ; decoded content must remain equivalent. |

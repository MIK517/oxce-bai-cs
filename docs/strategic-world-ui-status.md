# Strategic world UI status (branch 4d)

Branch `claude/strategic-world-ui`; plan and decisions in
[the 4d slices](phase-6-world-integration-plan.md#branch-4d-slices-approved-2026-10-11).
Reference: `4df3a5e571a1a4b5e8a46d3161fb2e21a2adba15`. This document records each slice's
evidence once; the plan's Progress section links here.

## S1 — UI session and world entry (2026-10-11)

`CampaignUiSession` carries typed readiness, research/production and world queries. The
SDL host builds it with `ForCampaign`, so those queries come from the campaign while
commands keep flowing through the extension session; previously the readiness and
research/production views were unreachable in the SDL host. Placing the starting base and
loading a save issue `EnterCampaignWorld`, matching `GeoscapeState::init`'s first-entry
block (`monthsPassed == -1` with a named first base); the command is idempotent. Tests:
`CampaignLogisticsClientTests` (extension-wrapped session, entry on placement and on load).

## S2 — Geoscape content and resources (2026-10-11)

- Subdivided extra sprites (`subX`/`subY`) now resolve one descriptor per cell in
  `ExtraSprites::loadSurfaceSet` order, with the cell rectangle in
  `ResolvedResourceDescriptor.SubFrame`; before, only the first cell resolved. This makes
  the standard mods' 27×3 `GlobeMarkers` strip usable. A run of cells outside the mod's
  reserved range, or a missing image, is reported once for the declaration (the reference
  stops at the first bad frame). The catalog also records each mod's sprite offset
  (`ModData::offset`). Compiler/cache revision 56.
- `RuntimeGlobe` projects `polylines` (`Mod/Polyline.cpp`) and the globe colours
  (`countryColor`, `cityColor`, `baseColor`, `lineColor`, `oceanPalette` as a block,
  `oceanShading`) with `Mod::Mod` defaults; later layers override named fields. Malformed
  polylines or colours fail like malformed polygons (strict: even coordinate count, range
  checks, colour 0–255, ocean block 0–15).
- Craft `marker` is projected; craft and UFO markers above the nine shared frames are offset
  into the declaring mod's range (`Mod::getOffset(marker, 8)`), declared or not.
  Deployment `markerIcon` keeps its declared number: the typed rule has no declaring mod, so
  a non-master mod's custom site/alien-base marker above 8 is a named restriction.
- Cities are not projected separately: `RuleRegion::getCities` derives them from named
  point mission areas, which the runtime regions already carry.
- `GeoscapeAssets` loads `PAL_GEOSCAPE`, `GEOGRAPH/TEXTURE.DAT` (32×32 frames) and the mod
  `TEXTURE.DAT`/`GlobeMarkers` frames through the resource service. A whole-image frame takes
  the image's own size (`Surface::loadImage`); a cell copies from the image and leaves pixels
  outside it transparent.

Tests: `ResourceDescriptorResolverTests` (cells, offsets, one report for an invalid run),
`InstallationContentLoaderTests.SubdividedSpriteCellsAndModOffsetsSurviveCompiledCache`,
`RuntimeGlobeProjectionTests`, `GeoscapeAssetsTests`; the owned-installation check
`PrivateGeoscapeAssetsTests` (UFO, TFTD, Rosigma) runs with the private corpus.

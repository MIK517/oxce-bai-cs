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

## S3 — Globe geometry and targets (2026-10-11)

`GlobeView` ports `Globe.cpp`'s orthographic projection (`polarToCart` with its flooring,
`cartToPolar` with longitude kept in [0, 2π) and off-globe points rejected where the
reference yields NaN and ignores the click), `pointBack`, the six `setupRadii` zoom radii
(0.45–3.60 × height), `setZoom`'s texture detail offset, `rotate`'s speed scaling by
`geoScrollSpeed` and zoom, and `targetNear` (`NEAR_RADIUS` 25 reference pixels, scaled to
the 2× frame). `GlobeTargets` orders selectable markers as `Globe::getTargets` does: each
placed base followed by its airborne craft (a base at exactly (0, 0) is skipped with its
craft, and so is a craft at its base with no destination), then UFOs, waypoints, sites and alien bases; it re-finds a selection by stable
identity (UFOs by unique id). Intentional differences: only detected sites are selectable
(the reference also lets an undetected site be clicked, revealing it), and a destroyed UFO is
not selectable during the instant before its removal; both are recorded in the S11 ADR.
Longitude normalisation keeps the reference loops for ordinary values and reduces a huge
(untrusted saved) centre first so it cannot hang. Tests: `GlobeViewTests`.

## S4a — Globe surface rendering (2026-10-11)

`GlobeRenderer` draws in `Globe::draw` order: the ocean disc (`radius + 20`, offset one
reference pixel), land polygons culled and projected as `Globe::cache` does (re-projected only
when the view moves) and filled with `IndexedSurface.FillTexturedPolygon`, a port of SDL_gfx
`texturedPolygonMT` (16.16 scanline spans, reversed spans swapped, screen-space texture tiling,
colour 0 skipped as the surface colour key, so ocean shows through), the
texture detail level `polygon texture + Globe::_zoomTexture`, then `CreateShadow` over every
pixel and the markers. `GlobeShading` ports `GlobeStaticData`'s terminator gradient and noise
decomposition, `getShadowValue`, ocean and land shading (`isOcean` honours `oceanShading`) and
`getSunDirection` with `GameTime::getDaylight` and the optional seasons. The noise table uses a
fixed local seed (the reference seeds it from the clock), so only its distribution matters.
Markers follow `Target::getMarker` and `Globe::drawMarkers` order (bases except one at (0, 0),
waypoints, sites with default frame 5, alien bases only with a marker icon, UFOs by status with
defaults 2/3/4, airborne craft with default 1), are centred, scaled to the frame, and shift one
colour while the 100 ms blink is off (cities excepted; a shade wrapping to 0 is not drawn, as
on the reference's colour-keyed marker layer). Repeat frames allocate nothing. Radar circles,
flight paths and the detail layer follow in S4b.
Tests: `GlobeRendererTests`, `GlobeSceneTests`.

## S4b — Radar, flight and detail layers (2026-10-11)

The world query now carries what `Globe::drawRadars`/`drawFlights` read: each base's largest
completed facility radar below 10,000 nautical miles, each craft's radar range with weapon
bonuses, and for UFOs the hunter-killer, hyper-wave and hunting flags, radar range (rule plus
mission race bonus) and destination (an unresolved anonymous UFO waypoint keeps its recorded
position). `drawEnemyRadarCircles` and `extraGlobeLabels` are projected (compiler/cache revision
57). `GlobeScene` collects radar circles (bases 48 segments, airborne craft, hunter-killer UFOs
per `drawEnemyRadarCircles`, discovered alien bases with a detection range, 24 segments), flight
paths (airborne craft to their destination, hunting UFOs; the reference meeting point is disabled)
and labels (countries from zoom 2 in their label colour, extra labels from their own zoom, cities —
named point mission areas — and base names from zoom 3). `GlobeRenderer` shades radar and path
segments from the unshaded globe onto their own layer (`XuLine`, `drawGlobeCircle`, `drawPath`),
including the destination-selection craft range circle, then composes the shadowed globe, that
layer, borders (`LINE_COLOR`, zoom 1+), labels (`Text` palette shift: colour plus glyph value,
centred in the reference text box) with city markers, and the markers. The globe options
`globeRadarLines`, `globeFlightPaths` and `globeDetail` toggle the layers. Labels use the small
font at its own size on the 2× frame and are not clipped to the reference text box. Country and
city labels come from the campaign's own countries and regions (the query lists them), as the
reference visits the saved game's lists. Still to come with base placement (S8): the hover radar
circles drawn while choosing a new base site. Tests: `GlobeRendererTests`, `GlobeSceneTests`,
`StrategicWorldGlobeQueryTests`.

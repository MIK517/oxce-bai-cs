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

## S5 — Geoscape shell (2026-10-11)

`GeoscapeClient` is the SDL campaign's top screen: the globe in a 512×400 area and the clock,
funds and six speed buttons beside it. `GeoscapeTimeController` ports
`GeoscapeState::timeAdvance`: every `geoClockSpeed` step (80 ms) the selected speed owes 1, 12,
60, 360, 720 or 17,280 ticks, executed as `AdvanceCampaignTime` chunks that carry the selected
speed and spread over host frames within a 12 ms budget (one-day speed is otherwise far slower
than a frame; gameplay is unchanged because every chunk keeps the speed context). A tick raising
notifications ends the step like a popup's `_pause`; a refused tick halts the clock with its
reason until a speed is selected. Time runs only while no popup, confirmation or base screen is
open and the world has been entered: `GeoscapeState::init`'s entry runs when the client opens and
again when the base screens close (where the first base is placed until S8). Popups queue and show
one at a time (`GeoscapeState::popup`, rotation stops); `GeoscapeNotifications` maps each campaign
notification to the reference popup text (formatted with its arguments, for example the item,
craft and base of a refuel or rearm shortage, which the service events now carry), its buttons
with their `keyOk`/`keyCancel` bindings and its `timerReset` behaviour (UFO and site detection
reset on Centre, alien base discovery on OK, low fuel, craft errors, UFO landing and hunting
alerts on "OK – 5 seconds" bound to Enter, research completion when queued;
others never). Production pops up only when a project ends, as `time1Hour` does. Scheduling a
strategic event (`SavedGame::spawnEvent`) shows nothing; its popup belongs to the event firing. A timer reset
changes only the speed: a halted clock stays halted until a speed is chosen. Save, load and
unexpected time failures report in the status line (the clock halts after a time failure) instead
of ending the session; the base screens cannot advance time while the geoscape hosts them.
Known difference: the reference resets the speed inside `time1Day` when research completes, so
that tick's five-second hunting retarget already sees five-second speed; here the reset applies
from the next tick, because each command's preflight fixes its speed context.
Keyboard: 1–6 speeds, arrows rotate (10 ms steps scaled by zoom), +/- and the wheel zoom,
Tab detail, R radar lines, `.`/`,` cycle front-facing targets (Tab is the reference detail key),
Enter centres and describes the selection, B base screens, F5 quick save, F9 load after
confirmation, Esc quit; right click centres, middle drag scrolls (`geoDragScrollButton`), left
click selects one target or lists several (`MultipleTargetsState`). Interception, destination and
craft screens follow in S6; the popups' Intercept, Redirect and Go-to-base buttons with them.
Tests: `GeoscapeTimeControllerTests`, `GeoscapeClientTests`.

## S6a — Wing dispatch and classified refusals (2026-10-11)

`DispatchCraftWing` launches one to four craft at a target or a new waypoint as one command, as
`InterceptState` and `ConfirmDestinationState::btnOkClick` do: the first craft leads and the
others follow the leader or fly to the same destination. Every check runs before any craft
changes, in the reference order: launch readiness, armor of every craft, the destination, cargo
of every craft, pilots of every craft and waypoint ids. A leader that is its own target (only
possible while it is airborne, since grounded craft are not globe targets) goes on patrol and the
wing follows it. Refusals (`CampaignActionBlocked`) now carry a stable `Code` and, where the
reference has one, a `TextKey` (for example `STR_ARMOR_NOT_ALLOWED_ONBOARD`,
`STR_TOO_MANY_ITEMS_ONBOARD`, `STR_NOT_ENOUGH_PILOTS`), classified by `WorldCommandReasons`; the
single-craft commands use the same table, whose texts are shared constants with the commands so
a rewording cannot silently unclassify a refusal. Classification covers world craft commands and
UFO contact actions; other campaign refusals (feature restrictions, ended campaigns, time
refusals, logistics) stay `unclassified` until their screens need codes. `STR_NOT_ENOUGH_PILOTS`
takes the craft's pilot count as its argument, which the UI reads from the craft rule. Port
restriction: with follow-leader off, a follower cannot be the wing's target (the reference would
let it chase itself). Only craft that fly to a base target must belong to it; followers fly to
the leader. The world query now describes craft for the craft screen (fuel, damage, shield, speed,
weapons with ammunition, crew, low fuel, mission complete, auto-patrol, the refusal reasons for
dispatch and for patrol or recall, saturated rather than thrown for out-of-range bonuses), and
each target carries the classified reason its action is unavailable.
Tests: `StrategicWorldWingTests`.

## S6b — Craft, interception and destination screens (2026-10-11)

The geoscape's craft and target screens are popups pushed above queued notifications
(`GeoscapeScreens` builds them; `GeoscapeClient` runs their buttons). Screens opened from one
another share a group, so a launch closes them together as the reference pops its state stack, and
a cancel returns to the screen below.
- `InterceptState` (Intercept button, I key, or a click on a base for that base's craft and a Go
  to base button): every craft with status, base and weapons/soldiers/vehicles, paged six rows at
  a time. A craft that
  cannot start (`allowStart`) is listed but refuses with its reason in the status line;
  Shift+click adds or removes up to three wing members, a plain click launches with that craft
  leading — to `ConfirmDestinationState` when a target is given, otherwise to destination choice.
- `SelectDestinationState`: the globe takes the next left click; targets near it (the leader
  excluded) open `MultipleTargetsState` when several, an empty spot becomes a new waypoint. Time
  and queued notifications wait, as in the reference. A grounded leader's return range
  (`Craft::getBaseRange`, now in the world query) is drawn while radar lines are on. Esc or the
  sidebar Cancel ends the choice.
- `ConfirmDestinationState`: the target, the leader's ETA as `oxceShowETAMode` asks
  (`GeoscapeClientSetup.ShowEtaMode`; 0, the reference default, shows none; 1 none for moving
  targets; 2 always) and, for a wing, the follow-wing-leader toggle (off when going after a flying UFO). OK
  executes `DispatchCraftWing`; a refusal closes the screens and shows the reference text
  (`TextKey`, `STR_NOT_ENOUGH_PILOTS` with the craft's pilot requirement) or the port reason.
- `GeoscapeCraftState` (click an airborne craft): status, base, speed, fuel, damage, shield,
  weapons by the rule's slot labels (`weaponStrings`, now projected) with rounds, crew (hidden
  when the craft carries none) and ETA;
  Return to base, Select new target, Patrol/Auto patrol and Cancel. The commands are hidden for a
  craft returning on low fuel or after its mission.
- `TargetInfoState` (waypoint, mission site, alien base) and the UFO screen offer Intercept. The
  detection popups gain Intercept (timer reset and centring, `InterceptState` above them);
  `CraftPatrolState` gains Redirect (Enter), which opens the craft screen; items arriving
  (Enter), construction and production gain Go to base / Allocate manufacture (timer reset), which
  open the base screens on that base.
Unnamed craft are labelled `STR_CRAFTNAME` (type and id) on the globe, as `Craft::getDefaultName` does.
Known differences: craft altitude, maintenance times in the intercept list, distance sorting
(`oxceGeoSortCraftByDistanceToTarget`), intercept row right-click (centre or craft info), `Ctrl`
ignore on UFO alerts, the dogfight "tailing" status and the "Go to last known UFO position" craft
screen (with the lost-target notifications, S7) are not shown. Allocate manufacture opens the base
screens rather than `ManufactureState`, and the random-production summary (which hides that
button) is not shown. Button and row layout is the popup layout, not the reference windows: six
intercept rows per page instead of `oxceInterceptTableSize` 8, at most 16 entries in the
several-targets choice instead of a scrolling list, and no title strip over the globe while a
destination is chosen.
Tests: `StrategicWorldGeoscapeScreensTests`, `GeoscapeScreensTests`, `GeoscapeNotificationsTests`,
`GeoscapeClientTests`.

## S7 — Notification screens (2026-10-11)

- `UfoDetectedState`: the alert and a globe click on a UFO show its size, altitude
  (`STR_GROUNDED` when landed), heading (`STR_NONE_UC` unless flying) and speed, and with
  hyper-wave decoding its craft type, race, mission and zone (the world query now carries these as
  `CampaignWorldUfoDetails`). Intercept and Centre reset the speed and centre on it. The
  reference's Ctrl+Cancel "Ignore" is a separate Ignore button running `IgnoreUfoContact`; it is
  not offered once the contact is ignored, nor for crash sites (the command takes flying and landed
  contacts).
- A craft whose pursued UFO left radar opens `GeoscapeCraftState` with the UFO's last position
  (`CraftTargetLost.OffersLastPosition`): the status reads `STR_INTERCEPTING_UFO` and Cancel becomes
  "Go to last known UFO position" (Esc), which sends the craft to a new waypoint there. As in
  `GeoscapeState::time5Seconds` this includes UFOs hidden as they were destroyed (trajectory end of
  an interrupted mission, expired crash sites), while the craft heads home; a retaliation assault
  run that landed or was destroyed, a visible destroyed or ignored UFO, and removed sites or bases
  show nothing: the craft returns or patrols.
- `AlienBaseState`: the title names "country, region", the region alone, or unknown, from the
  first country and region containing the base in saved order, using the deployment's
  `alienBaseDiscoveredMessage` (with the marker name) when it has one.
The other alerts (UFO lost, site detected and lost, low fuel, craft ready and service shortages,
waypoint reached, landing and hunting alerts, items arriving, construction, production, research,
training, time refusals) keep their S5/S6 texts and buttons.
Known differences: "Go to last known UFO position" runs `DispatchCraftToWaypoint`, so it applies
that command's armour, cargo and pilot checks and cancels auto-patrol, where the reference simply
sets the destination; the large "Redirect craft" caption of that screen is not shown. Research
completion has no "View reports" button (no Ufopaedia yet), training
completion names soldiers by id, `STR_AIRBORNE` for underwater UFOs over water is not applied, and
UFO alert sounds are not played.
Tests: `GeoscapeNotificationsTests`, `StrategicWorldGlobeQueryTests`.

## S8 — Base placement on the globe (2026-10-11)

A new game starts in `BuildNewBaseState` for the starting base (no Cancel): time and the base
screens wait until a land site is clicked and named (the SDL window now enables text input).
Further bases start from N on the geoscape or N in the base screens (which close and hand over to
the globe), with Esc or the sidebar Cancel to stop; at the base limit or before the
`newBaseUnlockResearch` the request is refused at once (`QueryNewBaseRestriction`), as
`BasescapeState` hides the button. While a site is chosen the radar lines are on and the pointer carries the radar preview
(`Globe::setNewBaseHover`, refreshed on a 50 ms hover tick): every facility type's radar range
around it and, with `globeAllRadarsOnBaseBuild` (`GeoscapeClientSetup.AllRadarsOnBaseBuild`,
default on), around each placed base instead of its own radar.
A click on water or a forbidden fake-underwater texture shows `STR_XCOM_BASE_CANNOT_BE_BUILT`;
otherwise the starting base goes to `BaseNameState`, and a new base to `ConfirmNewBaseState`
(cost and area from the first region containing the site, now carried by `CampaignBaseSite`),
whose OK refuses with `STR_NOT_ENOUGH_MONEY` when the funds fall short (and with a message when
the base type has no access lift), then `BaseNameState` (typed text, Backspace, Enter or Esc to
accept a name that is not blank; the digit that pressed OK is not typed), the access lift choice
when the base type offers several (`PlaceLiftState`), `CreateCampaignBase`, and the new base's
screens. The globe stops rotating while a name is typed.
`CampaignOverviewClient`, the foundation's minimal view, is retired.
Known differences: the access lift is placed at the grid centre instead of a clicked position,
`baseNamesFirst/Middle/Last` random default names are not offered, `customInitialBase` (a
lift for the starting base) is not supported, the hover preview leaves out zero and repeated
radar ranges (the same pixels), costs use a comma thousands separator instead of a non-breaking
space, names are limited to 40 characters and a blank name is refused, and a custom
`GeoscapeClientSetup.OpenBases` factory keeps its own new-base behaviour.
Tests: `GeoscapeClientTests`, `StrategicWorldBasePlacementTests`.

## S9 — Globe view in saves and quick save/load (2026-10-12)

Saves now own SavedGame's `globeLon`, `globeLat` and `globeZoom`: `OxceSaveWriteOptions.GlobeView`
writes them (a new save without one writes the reference defaults 0, 0, 0; a rewrite without one
keeps the loaded values) and `OxceSaveDocument.GlobeView` reads them (absent keys are 0). The SDL
campaign saves the geoscape's current centre and zoom, and a load (F9, or `--campaign-sdl ...
--load` to start from the save) restores them through `CampaignUiSession.GlobeView`, as `Globe`'s
constructor does; a larger zoom is clamped to the closest view and a negative one wraps to it, as
`Globe::setZoom(size_t)` does. A quick save during a step spread over frames saves the ticks done
so far and the step keeps running. A load clears every screen, choice, selection, rotation and owed
step of the old campaign, takes the save's view (0, 0, 0 without one) and returns the clock to
five-second speed. A failed load keeps the running campaign; `--load` reports a missing or
unreadable save instead of failing with a trace. Selections that lose their
target after time runs are dropped on refresh (S3/S5).
Tests: `StrategicWorldGlobeViewSaveTests`.

## S10 — Scripted scenarios and SDL smoke (2026-10-12)

- `StrategicWorldGeoscapeScenarioTests` plays a public campaign through the client's inputs: the
  player clicks a detected landing, intercepts it with the ship and confirms; time runs at one
  minute speed with every alert answered, while the driver checks that time never moves under a
  popup or a choice. The crewless ship waits over the landing (no `ConfirmLandingState`), and when
  the UFO lifts off the interception needs strategic combat (branch 5): the clock halts with the
  reason in a popup and stays halted. The ship is recalled from its craft screen, time resumes
  until it is home, and quick save and load continue the same campaign with the same view.
- `PrivateGeoscapeScenarioTests` (owned installation; the user's Windows run) loads every early
  UFO, TFTD and ROSIGMA save with the real globe resources, opens and closes the screen of every
  target, runs a game day with the alerts answered, and checks quick save and load.
- `Oxce.App --campaign-sdl-smoke <installation> <master> <add-on|->` runs an unattended geoscape
  window (`ScriptedLoopClient` replays speed, interception, detail, zoom and target keys for eight
  seconds) and prints the SDL driver, frame and campaign-time diagnostics. SDL3 is not available on
  the Linux build VM, so it runs on Windows; a public synthetic installation with globe resources
  does not exist, so the smoke needs an owned installation.
- Found by the private drive: UFO, landing, crash, site, alien base and waypoint names showed their
  raw language keys. Names now follow `Target::getName` (`GeoscapeScreens.TargetLabel`: a custom
  name, or the marker key formatted with the marker id), the world query uses the reference marker
  keys for alien bases (`markerName_`) and waypoints (`STR_WAY_POINT_`), and `UfoContactLost`
  carries the marker id for a UFO already off the radar.
Tests: `StrategicWorldGeoscapeScenarioTests`, `PrivateGeoscapeScenarioTests`, `ScriptedLoopClientTests`,
`GeoscapeNotificationsTests`.

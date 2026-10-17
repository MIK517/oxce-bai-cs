# 0032: Geoscape UI intentional differences

Status: accepted for Phase 6 branch 4d.

## Problem

Branch 4d puts the reference geoscape (`Globe`, `GeoscapeState` and the craft, target,
interception, destination, notification and base-placement states) on top of headless world
commands. Some reference behaviour depends on state mutated inside a UI handler, on windows the
port does not have yet, or on work that does not fit in one host frame. Each such place needs one
recorded choice so later branches do not rediscover it as a defect. The slice evidence is in
[strategic world UI status](../strategic-world-ui-status.md); this record lists the decisions.

## Decision

The geoscape follows the reference by default. These differences are intentional:

**Commands go through headless validation.** A UI action never mutates state the command did not
validate. Consequences:
- Refusals carry a stable code and the reference `STR_` key where one exists (decision D1); port-only
  guards keep English text. Logistics, research and time refusals stay unclassified until their screens
  need codes.
- A wing is one atomic command (D2): every check of every craft runs before any craft changes.
  With follow-leader off a follower cannot be the wing's target (the reference would let it chase
  itself).
- "Go to last known UFO position" runs `DispatchCraftToWaypoint`, so it applies that command's armour,
  cargo and pilot checks and cancels auto-patrol; the reference only sets the destination.
- Transferring an airborne craft is a named restriction (D3): the reference lists one only with
  `canTransferCraftsWhileAirborne`, which is not supported. Selling one is refused as in `SellState`.
  Both rows stay visible with the reason, where the reference leaves them out.

**Time advance is chunked under a frame budget (D5).** Each `geoClockSpeed` step's ticks run as
`AdvanceCampaignTime` chunks carrying the selected speed, within 12 ms per host frame. Gameplay is
unchanged; one-day speed runs slower in wall time than the reference's one day per 80 ms when the
world is busy (see [performance baselines](../performance-baselines.md)). A tick fixes its speed
context in preflight, so research completion's speed reset applies from the next tick rather than
inside `time1Day`.

**Selection uses the published world.** Targets follow `Globe::getTargets` (grounded craft are not
targets), except that only detected mission sites are selectable (the reference lets an undetected
site be clicked, which reveals it) and a destroyed UFO is not selectable during the instant before its
removal.

**Robustness over failure.** Save, load and unexpected time failures report in the status line and
halt the clock instead of ending the session; a failed load keeps the running campaign. A huge
(untrusted) saved globe longitude is reduced before the reference normalisation loops so it cannot
hang. A malformed run of sprite cells is reported once per declaration, where the reference stops at
the first bad frame. The globe noise table uses a fixed seed instead of the clock, so frames are
reproducible.

**Content projection.** As in the reference, craft and UFO markers above the nine shared frames are
offset into the declaring mod's range. Deployment `markerIcon`, however, keeps its declared number
because the typed rule has no declaring mod; a non-master mod's custom site or alien-base marker above
8 is a named restriction.

**One 640×400 frame with popups (D4).** The globe is the reference layout at 2×. Screens are popups
over the globe instead of reference windows: six interception rows per page instead of
`oxceInterceptTableSize`, at most 16 entries in the several-targets choice, UFO "Ignore" as its own
button instead of Ctrl+Cancel, no title strip while a destination is chosen and no "Redirect craft"
caption. Labels use the small font at its own size on the 2× frame and are not clipped to the
reference text box. Allocate manufacture opens the base screens rather than `ManufactureState`.
Port-only keys: `.`/`,` cycle front-facing targets (the reference has no target-cycling key), Enter
centres and describes the selection, B opens the base screens and N starts base placement.

**Base placement.** The access lift is placed at the grid centre instead of a clicked position;
`baseNamesFirst/Middle/Last` default names are not offered; `customInitialBase` (a lift for the
starting base) is not supported; names are limited to 40 characters and a blank name is refused;
costs use a comma thousands separator; the hover preview omits zero and repeated radar ranges (the
same pixels); a custom `GeoscapeClientSetup.OpenBases` factory keeps its own new-base behaviour.

**Not shown yet** (Phase 8 polish or later branches): craft altitude, maintenance times and distance
sorting in the interception list, intercept row right-click, the dogfight "tailing" status,
research "View reports" (no Ufopaedia), soldier names in training completion, `STR_AIRBORNE` for
underwater UFOs over water, alert sounds and the random-production summary.

**Branch 5 windows (S8).** Interception windows are text panels in four grid slots (names, distance,
status, mode and minimize buttons, weapon toggles); the craft and UFO sprites, range bar and weapon
icons are Phase 8 polish, and minimized windows are icons at the globe's lower left. Base defense
resolves its rounds in one step after Start Firing or Skip Firing instead of animating them. The
campaign's end and a battle request get plain-text port windows (no defeat cutscene, no tactical
combat yet). A queued landing confirmation whose craft is heading home or has no destination is
closed rather than shown, where the reference shows it and throws "No mission available!" on Yes
(plan D4). The Cydonia button sits under Cancel in the destination sidebar. After missiles damage a base whose stores no longer fit,
the window's OK opens the base screens instead of forcing the reference's sell screen.

## Consequences

Each listed difference has a test or a named refusal, so a change in either direction is visible.
Branch 5 added interception combat and landing (`ConfirmLandingState`) with the windows above. Phase 8 owns visual parity of windows, fonts and sounds. Removing an item
from this record requires the reference behaviour and its fixture.

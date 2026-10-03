# UFO hunting and escort integration

Branch 4c. Pinned reference: 4df3a5e571a1a4b5e8a46d3161fb2e21a2adba15.

The strategic-hunting capture inserts Craft::getHunterKillerAttraction,
Ufo::insideRadarRange and GeoscapeState::ufoHuntingAndEscorting's selection
fragment verbatim. Controlled collaborators supply effective capacities, craft
eligibility and distances. Ninety-six attraction rows and fourteen selection
rows pin both hunt modes, strict saved-order comparisons, original-target ties,
ignored/undetectable exclusions, zero radar and inclusive range.

The gameplay-owned selection operation preserves those rules. Arithmetic outside
signed 32-bit range and zero divisors fail intentionally. Five focused tests
verify captured outputs and malformed inputs. Live retargeting and movement
remain guarded until the next two slices connect this operation. Combat remains
branch 5; branch 4d is untouched and the headless gate remains open.

The retarget slice adds reference destination backup, hunt speed, escort speed and
waypoint restoration operations. Captured transitions retain the original waypoint
across hunt-to-escort changes and leave speed unchanged on restoration. Explicit
selected speed controls the five-second/one-minute fast path; ten-minute retargeting
is always due. Mod::hunterKillerFastRetarget defaults true and survives fresh/cache
projection. The redundant-HK-alert option defaults true, honors fixed user options
and persists through the save adapter. Compiler revision 33 invalidates stale caches.
Eleven focused tests pass. The next slice connects live retargeting and movement.

Live imported hunters and escorts now use registered ten-minute retargeting after
base hunting, plus the explicit slow-speed fast path before five-second movement.
Retargeting honors projected fuel returns, preserves original waypoints, allocates
contact markers and publishes gameplay-owned hunt notifications. Alert suppression
does not prevent revelation or targeting. Movement follows craft/UFO positions in
reference list order; snapshot capture resolves final target coordinates without
changing movement. Deleted escort targets restore their original waypoint.

Combat arrivals stop atomically before mutation. Retargeting involving same-tick
grounded auto-patrol relaunches or airborne craft transfers retains specific
forecast guards. Escort chains/cycles and malformed originals fail intentionally.
Deletion scans are bounded before nested work. Future hunter wave creation remains
guarded until spawn/retarget forecasts compose; this slice enables loaded actors.
The headless gate remains open and branch 4d is untouched.

# Mission-site half-hour expiry and associated UFO lifecycle

Branch 4c incremental slice. Pinned reference:
`4df3a5e571a1a4b5e8a46d3161fb2e21a2adba15`.

Hourly discovery now runs at the end of `GeoscapeState::time1Hour`, after
production and before the half-hour site loop. One hidden site is revealed in
saved order, with a minimum remaining hour; other hidden sites gain an hour.
Postponement overflow stops before the tick. The discovery notification pauses
following ticks while retaining current-tick fallthrough. Expiry, activity, UFO
reactivation and craft-arrival forecasts account for the timer changes.
The probe inserts the actual hourly discovery block; fresh/cache tests cover
queue order, minimums, maximum timers, reload and post-hour creation.

`GeoscapeState::time30Minutes` processes mission sites after UFO activity/detection
and before scheduled events. `GeoscapeState::processMissionSite` subtracts 1800
seconds when at least 1800 remain; a site at exactly 1800 becomes zero and
expires only on the following half-hour. A due site with followers remains if
its mission does not force despawn. Every processed site adds alien activity:
deployment points while it remains, or the despawn penalty when removed.

The `strategic-site-timer` C++ probe inserts the timer/follower and score
statements and `SavedGame` custom-counter methods verbatim. The runtime handler
covers ordinary countdown, followed hold, simple unlinked expiry and forced
expiry with craft followers. Forced expiry returns those craft to base before
movement and emits one lost-site alert carrying the marker key, site ID and
optional custom name so the client can format the reference name after the site
is removed. Combined UFO/site
activity overflow is preflighted. Fresh/cache tests check score, timer,
save/reload, forced follower return, named and unnamed alerts and atomic guards.
Site despawn applies all six counter fields in reference order, including
duplicate names, missing-key defaults and the one-based floor. The shared
campaign counter operation persists them in the save's `ids` map and preflights
combined overflow, including possible same-day research increments from
completing topics and their free rewards. Unrelated research rules do not block
the tick. The preflight also credits guaranteed same-day research decrements in
catalog order before site expiry; it does not credit a later base's project if
an earlier base may remove that topic. This follows the pinned
`GeoscapeState::time1Day/time30Minutes` and `SavedGame` counter paths.

Associated `IGNORE_ME` UFOs now wait without movement, shield recharge, detection,
base scans or ground-timer consumption. A due site releases its linked UFO after
the despawn score and counters. `AlienMission::ufoLifting` assigns very-low
altitude and the current trajectory speed, retains the stored timer and landing
ID, and awards positive mission points except for base/infiltration objectives.
The subsequent ten-minute base scan and five-second movement/shield handlers run
in the same tick. Site-objective UFOs can continue ordinary airborne outbound
waypoints; special ground arrivals and operation-base paths remain guarded.

The `strategic-world-lifecycle` probe now extracts the site-release block and
`AlienMission::addScore` infiltration filter alongside `ufoLifting`, `Ufo::think`
and `Ufo::setAltitude`. Its added rows cover held/due sites, retained timers and
score/base/infiltration/site objectives without changing the four prior traces.
`StrategicWorldSiteUfoTests` checks fresh/cache transition reloads, deletion of
sites/UFOs/missions, follower-held sites, hidden-contact loss, immediate terminal
departure, same-tick base scans and combined activity overflow. Interception and
invalid shield state stop before site counters or any other tick state mutate.
Negative/overflowing lift speeds allow dormant waiting but stop at release.
Ordinary landed-UFO preflight and both takeoff paths share the same allocation-free
speed validation. Fresh/cache regressions cover a negative effective maximum
hidden by a negative percentage, effective-maximum overflow and trajectory-speed
overflow, before a half-hour tick can change the clock, RNG, site counters or activity.
Repeated loaded-save rewrites preserve unknown surviving UFO fields and remove
the deleted site's sidecar and link; UFO deletion then removes its sidecar too.
Duplicate site links fail intentionally; a due link to an active UFO is guarded.
UFO follower repair, despawn research and instant-event consequences remain
guarded before mutation.

The branch 5 landing/deployment and branch 6 event-payload and integrated month
guards remain in place. This does not close branch 4c.

Validation: root `dotnet test --no-restore` passes 1,268 tests with zero failures or skips,
including fixture checksums and all 19 staged saves under fresh/cache content.
Formatting, regenerated code-map and diff checks pass. The thirty-minute corpus
horizon is now nine executable, two blocked and eight tactical preservation-only;
Rosigma Early advances all 360 ticks rather than stopping at its site at tick zero.

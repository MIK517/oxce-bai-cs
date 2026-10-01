# Mission-site half-hour timer and simple expiry

Branch 4c incremental slice. Pinned reference:
`4df3a5e571a1a4b5e8a46d3161fb2e21a2adba15`.

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
the tick. UFO
followers, despawn research, instant-event consequences and associated-UFO
reactivation still stop before mutation.

The branch 5 landing/deployment and branch 6 event-payload and integrated month
guards remain in place. This does not close branch 4c.

Validation: root `dotnet test --no-restore` passes 1,242 tests with zero failures or skips,
including fixture checksums and all 19 staged saves under fresh/cache content.
Formatting, regenerated code-map and diff checks pass. The thirty-minute corpus
horizon is now nine executable, two blocked and eight tactical preservation-only;
Rosigma Early advances all 360 ticks rather than stopping at its site at tick zero.

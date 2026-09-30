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
statements verbatim. The runtime handler covers ordinary countdown, followed
hold, simple unlinked expiry and forced expiry with craft followers. Forced
expiry returns those craft to base before movement and emits one lost-site
alert. Combined UFO/site activity overflow is preflighted. Fresh/cache tests
check score, timer, save/reload, forced follower return and atomic guards.
UFO followers, despawn research and counters, instant-event consequences and
associated-UFO reactivation still stop before mutation.

The branch 5 landing/deployment and branch 6 event-payload and integrated month
guards remain in place. This does not close branch 4c.

Validation: root `dotnet test --no-restore` passes 1,234 tests with zero failures or skips,
including fixture checksums and all 19 staged saves under fresh/cache content.
Formatting, regenerated code-map and diff checks pass. The thirty-minute corpus
horizon is now nine executable, two blocked and eight tactical preservation-only;
Rosigma Early advances all 360 ticks rather than stopping at its site at tick zero.

# Alien-base hunting timers and eligibility

Pinned reference `4df3a5e571a1a4b5e8a46d3161fb2e21a2adba15`:
`GeoscapeState::time10Minutes/baseHunting/updateActiveCrafts`,
`Craft::isDestroyed/isIgnoredByHK`, `RuleCraft::isUndetectable`,
`AlienDeployment::generateHuntMission`, `WeightedOptions::choose`.

A registered ten-minute handler advances hunting timers after craft fuel and UFO
base detection. Grounded, destroyed, undetectable, completed-mission and low-fuel
craft do not qualify. Distance is strict; reference craft ordering, detection
rolls and empty weighted choices are preserved. Empty choices continue to later
craft without resetting the timer. The craft `undetectable` runtime projection
preserves its default false; compiler revision 32 invalidates stale caches.

Preflight uses the existing pure fuel transition and daily base-deletion proof.
It allows inactive/not-due hunts, safe empty choices and delayed mission creation.
A first wave that may spawn in the immediate think pass blocks before tick mutation. Missing/current-month timelines, weight limits, timer
overflow and bounded craft checks fail intentionally. Destroyed-craft cleanup and
combat remain guarded by their existing owners.

The pinned `strategic-alien-base-hunt-timer` probe inserts exact C++ methods with
asset-free collaborators. Focused unit and fresh/cache integration tests cover
all trace rows, after-fuel eligibility, daily deletion, guarded transitions,
resource limits and reload over 72 hours. Branch 4c remains open; 4d is untouched.

Servicing composition: due hunts also preflight possible auto-patrol relaunches
and arriving auto-patrol craft before half-hour/hourly handlers can mutate state.
When launch fuel/servicing outcomes are uncertain, candidate eligibility is
conservative. A fresh/cache regression reproduced a post-mutation hunt exception
from half-hour refuelling and now verifies atomic blocking instead.

Final audit corrected an obsolete persistence test that still expected the blanket
hunting guard. It now verifies the due branch-6 event boundary, with a separate
composed movement/timer reload regression. Destroyed-craft coverage verifies the
real whole-tick removal guard rather than synthesizing a timer result for a tick
that the port intentionally cannot execute.

Final validation for these three cycles: root `dotnet test --no-restore` passes
1,478 tests, zero failed or skipped, including checksum and private-corpus tests.
Formatting, regenerated code-map and diff checks pass. The pinned C++ checkout
is unchanged. Branch 4c remains incomplete and branch 4d has not started.

Delayed hunting mission creation is now owned. Exact baseHunting and initialization
countdown captures show source region (one remap), mission ID, optional race and site
area, source-base linkage, timer reset and first-success craft-loop exit. The
new mission receives one immediate think countdown after the half-hour handler,
including at midnight. First timers below 120 minutes retain an immediate-wave
guard. Mission IDs/population are reserved together with daily base generation.
Fresh/cache tests cover shortest supported delay, RNG ordering and transition reload.
## Immediate airborne hunting creation (2026-10-07)

Existing bases now preflight reachable ordinary score/supply waves at the actual
ten-minute hunting caller. Eligible-craft forecasts and source/region/race proofs
feed the common identity/population/wave checks before mutation. Pure birth
candidates are excluded from earlier half-hour activity and detection. Execution
shares the bounded mission think operation, appends the mission before thinking
and updates its live counters at each immediate step. Initial ground states,
hunters and unsupported immediate objectives retain a precise lifecycle guard.

Reference: pinned `GeoscapeState::baseHunting/time10Minutes/time30Minutes`,
`AlienMission::start/think/spawnUfo`; verified hunting-generation creation/countdown
and world-wave fixtures. Fresh/cache tests cover ten-minute versus half-hour birth,
no premature score/detection, source linkage, timer reset, transition reloads and
atomic UFO-identity exhaustion. The prior eligibility/refusal regressions now use
an explicit unsupported hunter rule; all 145 alien-base cases pass.

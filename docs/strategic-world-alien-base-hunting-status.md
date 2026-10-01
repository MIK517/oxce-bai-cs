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
It allows inactive/not-due hunts and safe empty choices. A possible nonempty
mission blocks before tick mutation until hunting mission creation and immediate
waves are integrated. Missing/current-month timelines, weight limits, timer
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

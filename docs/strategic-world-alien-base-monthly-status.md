# Alien-base upgrades and race evolution

Branch 4c incremental slice, pinned to
`4df3a5e571a1a4b5e8a46d3161fb2e21a2adba15`.

The registered `ICampaignWorldAlienBaseMonthly` operation implements the final
monthly phase of `GeoscapeState::determineAlienMissions`. The isolated
`AdvanceMonthlyWorldAlienBases` command exercises the operation. Its future caller
must run it after arc, mission and event scheduling, with the advanced campaign
month. Initial-campaign/non-monthly calls and month zero do nothing, including no
RNG. The campaign month guard remains installed; this slice does not enable
integrated monthly accounting or branch 6 effects.

Upgrades use the base's age, not the campaign month, and preserve the reference's
reverse timeline scan and ordinal weighted choices. Signed age subtraction then
unsigned conversion preserves future start months. Empty timelines and zero-weight
totals do not draw; empty, missing and current-deployment choices leave the base
unchanged. A nonempty timeline with no applicable age fails intentionally before
mutation, defining the reference's invalid iterator dereference as an input error.

Changing deployment retains identity, coordinates, name, discovery/battle flags,
generation count and pact country. The source's `resetAlienBaseAgeAfterUpgrade` or
destination's `resetAlienBaseAge` resets the start month. `AlienBase::setDeployment`
resets the hunting timer to the new deployment's maximum frequency. A defined
`upgradeRace` changes only the base race; missing races are ignored.

All upgrades run before any evolution. Evolution uses the resulting deployment
and campaign month, scans the stable descending-month tuple list, ignores missing
destination races, takes the first applicable transition and restarts the scan.
Self-transitions count as successes. The reference throws after its hundredth
successful transition even if the chain would then terminate. Preflight evaluates
every reachable upgrade outcome without RNG and rejects this error before any
base changes. The operation bounds population to 100,000 and total timeline,
choice, evolution and reference-repair checks to one million. Overflowing weight totals and negative
new hunting timers fail intentionally.

Reference object pointers keep mission and follower links attached to the upgraded
base even when its marker changes. Gameplay repairs saved mission/base, craft
destination and UFO destination references to the new marker. It does not change
existing mission/UFO races. Reachable collisions with other base/site markers or
reserved target names are guarded before mutation, preventing save reload from
redirecting a reference. Collision reservations are conservative: they also retain
each base's original marker during the operation.

`AlienBaseSnapshot.PreservationKey` retains a stable sidecar identity through
deployment changes. Legacy bases derive it from their original deployment and ID;
the save adapter emits `oxcePortEntityKey`, which the reference ignores. Loaded
rewrites match this key and ID, consuming each source once. Unknown fields survive
upgrades and repeated rewrites; deleted bases remain absent. Duplicate keys are
rejected. This extends the existing event sidecar-key mechanism and ADR 0008 without
putting YAML in gameplay. Existing reference save fields own all lifecycle state;
the immutable rule projections are unchanged, so the compiler/cache revision stays 30.

The `strategic-alien-base-monthly` capture inserts the exact monthly upgrade and
evolution loops, `attemptAlienRaceEvolution`, `generateAlienBaseUpgrade`,
`AlienBase::setDeployment` and `WeightedOptions::choose` into asset-free
collaborators. Twenty-four traces cover ignored calls, thresholds, future start
months, both reset flags, unknown/self/empty choices, upgraded race evolution,
stable ties, missing races, chains, loop failures and the finite 99/100-transition
boundary. The malformed future-timeline
case is tested only in the port because the reference dereferences an invalid
iterator. Debug-log formatting and exact RNG streams are not acceptance targets.

`StrategicWorldAlienBaseMonthlyTests` covers fresh/cache oracle comparisons,
mutation-free guards, target-link/marker changes, transition reloads, legacy and
repeated unknown-field rewrites, deletion, duplicate keys and population limits.
The generation ID and sight work-limit tests now construct distinct bases instead
of copying the first base's preservation key, retaining their original assertions.

Daily generation/activity, hunting, self-destruction and linked deletion retain
their existing ownership and guards. Actual monthly scheduler composition and
the remaining headless acceptance work are still open. Branch 4c is incomplete;
branch 4d remains untouched.

Validation checkpoint: all 1,376 root tests pass with zero failures or skips,
including 18 monthly base cases, fixture checksum and private-corpus tests.
Formatting verification, regenerated code-map validation and `git diff --check`
pass. The pinned reference checkout is unchanged. Private continuation horizons
remain nine executable through 360 ticks, two blocked at 125/220 ticks and eight
active-battle preservation-only imports; this subsystem slice does not claim
campaign-level monthly or branch 4c acceptance closure.

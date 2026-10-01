# Alien-base activity and discovery

Branch 4c incremental slice, pinned to reference
`4df3a5e571a1a4b5e8a46d3161fb2e21a2adba15`.

Owned behavior follows `GeoscapeState::time1Day` after the player-base loop:
deployment points accrue to the first matching region and country, including
negative points. Non-generating supply checks choose their weighted mission
before checking its limit and frequency. Exhausted limits skip the frequency
roll; zero frequency still consumes that roll. Empty choices skip it. Weights
use the reference ordinal map order. Undefined nonempty choices and excessive
weight totals stop before the affected tick consumes RNG or publishes state.

Facility discovery follows those checks. Distance truncates to reference units,
range is exclusive, and a zero configured chance uses integer distance scaling.
Construction completed earlier in the day is visible; disabled facilities still
qualify. An initially discovered base skips facility checks, but discovery during
the pass does not stop subsequent facilities or player bases. Each success emits
an `AlienBaseDiscovered` notification. Current-tick fallthrough is preserved.

`time10Minutes` patrol discovery executes immediately after each craft's fuel
transition, before UFO base scanning. Returning craft have a destination and do
not qualify. Installed weapon sight bonuses apply even when disabled. Range is
inclusive and the floating percentage truncates to an integer. Known bases still
consume the roll. Patrol discovery is silent. Invalid effective sight totals
stop before fuel mutation. No new save fields are needed: discovered state and
base identity already belong to gameplay and the save adapter.

The `strategic-alien-base-activity` capture inserts the exact daily activity,
facility detection, patrol detection, `XcomDistance` and `WeightedOptions::set/load`
blocks into an asset-free probe. Collaborators supply distances, containment and
parsed weight patches, record chances, and succeed
on positive chances; they do not establish RNG stream parity, geometry itself,
mission creation, hunting, deletion or upgrades. Gameplay tests exercise real
geometry/RNG, fresh/cache projections, notifications, construction and fuel order,
daily/half-hour combined score overflow, reload/rewrite, unknown-field conservation
and deleted base sidecars. A passive base advances 72 hours without a blanket
world guard. Compiler/cache revision is 30.
The preflight bounds daily scoring/selection and facility/patrol cross products
to one million candidate checks, intentionally stopping excessive populations
before RNG or fuel changes. This is a port resource limit, not a reference rule.

Inspected sources also include `AlienDeployment::chooseGenMissionType`,
`WeightedOptions::choose`, `Craft::getCraftStats`, `AlienBase::load/save`,
`GenerateSupplyMission`, `GeoscapeState::baseHunting`, and
`SavedGame::clearLinksForAlienBase`. Base mission creation remains a daily guard
when a nonempty valid choice could generate. Self-destruct deployments retain a
daily guard until same-day research, mission interruption, target repairs and pact
cleanup are implemented together. Positive hunting range retains a ten-minute
guard until hunting eligibility, timers and generated missions are owned.
Monthly upgrades/evolution remain behind the campaign month boundary. These are
explicit remaining 4c capabilities; this slice does not close its headless gate.
Branch 4d remains untouched.

Audit fixes preserve `genMission` weighted entries through partial overrides and
`refNode` inheritance; zero weights remove entries and childless patches leave
existing entries intact. The extended pinned probe checks edit/add/remove order,
empty choices and totals. Fresh/cache regressions ensure inherited generating
missions still block before their daily tick mutates time, activity or RNG.

The shared `Target::getDistance` formula now clamps its cosine to the valid
`acos` interval. This defines the numerically rounded coincident/antipodal cases
where the reference's raw formula produces NaN; it does not change the formula
inside that interval or hide NaN from invalid input. Unit tests cover both
endpoints. Fresh/cache daily sight tests cover the audited near-coincident pair
with and without a sight facility, verify complete daily progression and reload
the resulting discovery state without an exception after partial mutation.

Validation checkpoint: all 1,311 root tests pass with no skips, including 35
tests in the new activity/discovery class and fixture checksum validation.
Formatting verification, regenerated code-map validation and `git diff --check`
pass. Fresh/cache classifications of the 19 private saves remain nine executable
through the 360-tick horizon, two blocked at mission spawning (125 and 220 ticks),
and eight active-battle preservation-only imports. These horizons establish
bounded continuation, not unrestricted campaign acceptance.

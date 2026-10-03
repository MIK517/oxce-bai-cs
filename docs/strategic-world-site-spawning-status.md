# Direct mission-site generation

Branch 4c; pinned reference `4df3a5e571a1a4b5e8a46d3161fb2e21a2adba15`.

Explicit marked deployment waves now execute the direct-site branch of
`AlienMission::think` and `spawnMissionSite`. Area selection uses the mission's
spawn zone, or trajectory zone zero when the spawn zone is -1, preserving area
list order and the draw even for one selectable area. A selected mission area
skips that draw. Wave deployment overrides mission site type. Coordinate draws,
marker allocation and duration draws retain reference order; race, texture and
city persist. Sites begin undetected, and the same half-hour pass subtracts 1800
seconds and awards deployment points after all existing sites.

Finite immediate chains reserve sites, shared marker IDs and combined activity
before time or RNG changes. Invalid coordinates, exhausted/colliding IDs,
populations above 100,000 and work above one million checks stop intentionally.
Zero-hour durations remain guarded at their immediate despawn consequences;
overflowing duration values are refused. Mission cleanup retains its existing
UFO and retaliation-link checks.

Evidence: `strategic-site-spawn` inserts the reference direct selection and site
creation statements into controlled asset-free collaborators, excluding only
diagnostic logging. Eight focused tests cover explicit override, selected areas,
three-site immediate chains, fresh/cache creation reloads and atomic limits.
The captured terrain and player-base rows establish the next two slices.
Terrain fallback now follows `Texture::getRandomDeployment`: an explicit mission
site type precedes the texture table; a singleton is chosen without RNG even at
zero weight; multiple choices use ordinal weights and one integer draw. Empty
and all-zero multi-entry tables return no deployment and still advance the wave.
Only reachable choices require valid rules, duration and ID capacity. Missing
textures and overflowing weight totals fail before tick mutation. Activity ranges
reserve the possible positive and negative score for one selected deployment,
including suppressed outcomes. Eighteen focused tests pass, including captured
choices, weighted tails, cache reconstruction, reload and malformed input.
The probe now inserts the actual Texture method; its empty-rule lookup collaborator
matches `Mod::getRule`'s null-name early return.
Player-base targeting preserves regional base list order, skips the extra draw
for a single eligible base, and suppresses creation when no base qualifies.
It replaces coordinates and city while retaining the selected area's texture.
Twenty-three focused tests pass, including the captured base choices, reload,
unreachable invalid durations and maximum-integer score forecasts.
UFO-to-site conversion remains guarded. Hourly discovery now runs after production,
before half-hour spawning, and reveals one existing site in saved order. The
reference minimum-hour timer and postponement of other hidden sites are forecast
before mutation. Its notification interrupts subsequent ticks while the current
tick continues. Newly created sites wait for the next hourly pass.
Site despawn research/event payloads and branch 5 deployment remain guarded.

The reference checkout is read-only and unchanged. This is incremental 4c coverage;
branch 4c acceptance remains open and branch 4d is untouched.

The combined audit confirmed and fixed idle mission-preflight allocations and
missing hourly discovery. A failing regression also confirmed that a weighted
null deployment could incorrectly provide negative-score headroom for a later
wave; null outcomes now remain uncertain. Deployment-table scans count toward
the work limit even when every rule is unreachable. Empty marker namespaces
retain a precise guard because campaign ID allocation requires a nonempty key.
Twenty-nine creation tests cover these corrections and existing-site score order;
hourly discovery uses the expanded `strategic-site-timer` capture and reload tests.

Final root validation passes all 1,605 tests without failures or skips, including
checksums, private corpus and allocation budgets. An unchanged rendering allocation
test failed once during an earlier full run, then passed in isolation and in two
subsequent full runs; its strict assertion remains unchanged. Code-map and diff
validation and formatting verification pass. Useful captures remain; obsolete
object files were removed.

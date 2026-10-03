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
Terrain fallback, player-base targeting and UFO-to-site conversion remain guarded.
Site despawn research/event payloads and branch 5 deployment remain guarded.

The reference checkout is read-only and unchanged. This is incremental 4c coverage;
branch 4c acceptance remains open and branch 4d is untouched.

# Selected objective UFO conversion to mission sites

Branch 4c incremental slices. Pinned reference:
`4df3a5e571a1a4b5e8a46d3161fb2e21a2adba15`.

Reference ownership: `Savegame/AlienMission.cpp` (`ufoReachedWaypoint`,
`spawnMissionSite`, `addScore`), `Geoscape/GeoscapeState.cpp` (`time5Seconds`),
`Savegame/Ufo.cpp`, `Savegame/MissionSite.cpp` and `Mod/Texture.cpp`.

A selected objective wave converts when its next waypoint is ground and its
current waypoint is in the mission's spawn zone. The reference chooses the next
waypoint twice before conversion. Site coordinates come from the selected area,
or the UFO's arrival position when `objectiveOnTheLandingSite` is set. Mission
points are awarded at the arrival position except for infiltration. Site
deployment follows the mission rule then the area's texture, without a wave
deployment override. Marker allocation precedes duration selection. The site is
revealed immediately and interrupts subsequent ticks. A destroyed UFO ends the
current flight/craft pass and retains its live count until next-tick cleanup.

The expanded `strategic-site-spawn` probe inserts the actual conversion body and
site creation method, with controlled score and empty craft-follower callbacks.
Its conversion rows cover coordinate replacement, custom deployment and respawn
flags. Existing reference captures separately pin the actual mission score filter
and waypoint selection. Fresh/cache tests compare canonical captured creation,
check score, notifications, save/reload, delayed UFO cleanup and atomic ID/score
guards. Existing selected-waypoint scenarios now continue through conversion.

This first slice enables conversion on ticks below the half-hour trigger. Combined
counter forecasts and craft follower
transfer retain specific guards. The branch 5 landing/interception and branch 6
payload/month boundaries remain. The headless gate is open; branch 4d is untouched.

The second slice preserves the UFO's effective `missionCustomDeploy`, including
race overrides, as optional site metadata. Missing custom rules produce no custom
deployment, matching the reference's optional Mod lookup. This metadata does not
replace the site's marker, duration or strategic deployment. Captured custom rows,
race/missing-rule cases and transition reloads cover the behavior; existing save
fields are reused without changing the external schema.

The third slice links a converted site to its originating UFO when
`respawnUfoAfterSiteDespawn` is enabled. The UFO remains dormant with its live
mission count and prepared next destination. The arrival still recharges shields
and continues current-tick fallthrough, matching the flying switch branch;
subsequent dormant ticks do neither. Site expiry uses the existing lift capability
to reactivate it. All captured respawn/custom/coordinate combinations and
fresh/cache creation-to-expiry reloads pass. Null deployment outcomes still
destroy the UFO instead of creating a dangling dormant link.

The batch audit reproduced a false landing guard when a respawn-enabled mission
had no reachable terrain deployment. Preflight now records that suppressed
outcome for the craft and activity forecasts: the UFO is terminal and the later
craft/UFO pass is skipped. The regression confirms the tick proceeds atomically,
creates no site or link, and preserves the skipped craft state. Additional cases
check shield recharge and later-UFO movement only on dormant conversions. Compound
half-hour conversion, craft follower transfer and damaged ground outcomes remain
guarded; this batch does not close the headless gate.

Final validation: 1,644 root tests pass with zero failures or skips, including
checksums, private corpus and allocation checks. Formatting verification,
code-map and diff checks pass. The pinned reference checkout remains clean.

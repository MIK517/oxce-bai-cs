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
counter forecasts, custom deployment, dormant respawn links and craft follower
transfer retain specific guards. The branch 5 landing/interception and branch 6
payload/month boundaries remain. The headless gate is open; branch 4d is untouched.

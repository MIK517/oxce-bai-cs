# Research-driven alien-base deletion

Branch 4c slice, reference `4df3a5e571a1a4b5e8a46d3161fb2e21a2adba15`.

`GeoscapeState::time1Day` self-destruction runs after mission interruption and before
base scoring, supply generation and discovery. Completed research is checked without
debug shortcuts. Guaranteed same-day research and lookup discoveries are included in
the pure forecasts; uncertain rewards/disablers stop before the daily tick.

`SavedGame::clearLinksForAlienBase` clears associated mission links. Space missions
continue; Earth operations interrupt and zero-live missions clean up in the same
half-hour fallthrough. Live Earth-operation interruption remains guarded until its
combined UFO forecasts are supported. Surviving space UFOs retain their mission.
Lost craft destinations use the existing pursuit return behavior. Missing destruction
research and excessive link-cleanup work fail before mutation. Removed bases do not
score, generate or discover, and are excluded from the shared activity overflow forecast.

The mod's `allowCountriesToCancelAlienPact` setting defaults to false, as in
`Mod` constructor/load, and now projects through campaign settings and cache revision
31. When enabled, the first matching country uses `Country::setCancelPact` semantics:
clear the pending pact and set cancellation only for an already signed pact. The
signed flag remains intact for the guarded monthly caller. Existing save fields
persist these flags; stable base sidecars cannot resurrect deleted entities.

The extracted `strategic-alien-base-cleanup` capture pins the exact daily deletion,
link clearing and country cancellation blocks with eight traces. Deterministic
fresh/cache tests cover oracle comparisons, lookup research, generation/scoring
suppression, followers, uncertainty, live-operation boundaries and repeated loaded
rewrites. Campaign month progression, tactical deletion and branch 4d remain outside
this slice. Branch 4c remains incomplete.

Validation: 1,392 root tests pass with no failures or skips, including nine cleanup
cases, fixture checksum checks, private-corpus classification and allocation gates.
Formatting, code-map and diff checks pass; the pinned C++ checkout is unchanged.

Post-commit audit repair: `Target::~Target` returns craft during base deletion,
before terminal UFO arrival can skip craft movement. Two failing fresh/cache
reproductions confirmed the stale destination. Immediate follower repair now
runs in the deletion handler and is charged to the work budget. The extended
reference capture pins two destructor traces; reload regression covers the
terminal-arrival interaction.

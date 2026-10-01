# Crashed UFO visibility, expiry and cleanup

Branch 4c, reference `4df3a5e571a1a4b5e8a46d3161fb2e21a2adba15`.

The half-hour handler now expires crashed UFOs after mission processing and before
craft refuelling, as in `GeoscapeState::time30Minutes`. Timers at least 1,800 seconds
lose 1,800; shorter timers mark the UFO destroyed without changing its remaining
timer or detection flag. `Ufo::think` reveals remaining crashed UFOs in the five-second
pass without radar RNG or an alert. A timer exactly zero then clears detection and
marks the UFO destroyed, matching `GeoscapeState::time5Seconds`. Crashes do not
move or recharge shields. Original speed, crash identity and mission linkage persist.

Shared pure forecasts include both timer paths and visibility before craft arrival
checks. A surviving crash still stops before landing/deployment when required;
an expired one returns craft through the existing target-loss handling. Cleanup
releases the mission live count; the following half-hour pass removes finished
missions. A terminal airborne UFO still skips the remaining five-second handlers
in reference order. Tactical crash state remains guarded. Inconsistent live counts,
linked mission-site deletion and excessive cleanup work stop before tick mutation.
Cleanup forecasts use allocation-free predicates and bound checks to one million.

The `strategic-crashed-ufo` probe extracts the exact Ufo::think method, half-hour
expiry loop and five-second crashed branch. Twenty-four traces cover zero,
fractional-half-hour, threshold and longer timers with hidden/visible contacts.
Fresh/cache gameplay checks use all rows, reload before and after transitions,
verify unchanged RNG/scores/shields/positions, and cover followers, live landing,
malformed state, work limits and unknown-field conservation/deletion. A composed
72-hour scenario reloads before expiry and continues through mission cleanup.
Existing save fields own the state; compiler/cache revision remains 31.

This completes strategic continuation for already crashed UFOs; creating crashes
through interception and landing into battlescape remain branch 5. Broader mission
spawning, hunting/escorts, detection scripts and campaign scheduler composition
remain 4c work. Branch 4c is incomplete; 4d remains untouched.

Validation: all 1,414 root tests pass with no failures or skips, including 13
crash cases, fixture checksums, private-corpus classification and allocation gates.
Formatting, code-map and diff checks pass. The reference checkout is unchanged.

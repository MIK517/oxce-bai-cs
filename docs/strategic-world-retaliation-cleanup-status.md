# Retaliation mission linkage and completion

Branch 4c, reference `4df3a5e571a1a4b5e8a46d3161fb2e21a2adba15`.

Player bases now own `RetaliationMissionId` in save-neutral state. The adapter
reads/writes `retaliationMissionUniqueId`, matching `Base::load/save`. Restore
resolves only existing retained missions; stale IDs disappear. Rewrites preserve
unrelated unknown fields while explicitly removing cleared links, preventing
sidecars from restoring a deleted relationship. No compiler revision is needed.

The half-hour mission loop implements `GeoscapeState::time30Minutes`'s finished
retaliation cleanup before removing missions. Every matching player-base link is
cleared, while the independent `retaliationTarget` flag is unchanged. Daily research
interruption can complete a zero-live retaliation mission in this same pass.
Retaliation spawning, combat and base-defense transitions retain their branch 5
boundaries. Mission deletion still rejects linked UFOs with inconsistent live counts.
Cleanup scans are bounded to one million checks before mutation.

The reference excludes instant retaliation from this pointer cleanup. A malformed
base link to a finishing non-retaliation mission therefore stops before deletion,
rather than preserving the reference's dangling pointer. Loading and rewriting such
links remains supported until that unsafe transition. Missing mission links resolve
to null exactly as the reference does.

The `strategic-retaliation-cleanup` C++ capture extracts both the Base::load link
block and the half-hour repair block. Eight cleanup traces and three load traces
cover completion/objectives, unrelated links and missing IDs. Fresh/cache tests
compare those rows, reload around transitions and repeat unknown-field rewrites.
The malformed live-UFO case verifies atomic rejection. Branch 4c is incomplete;
4d is untouched.

Validation: all 1,402 root tests pass with zero failures or skips, including seven
new compatibility cases, checksum/private-corpus and allocation checks. Formatting,
code-map and diff checks pass.

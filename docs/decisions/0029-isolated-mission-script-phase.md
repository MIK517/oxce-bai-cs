# ADR 0029: Isolated mission-script scheduling phase

Status: accepted, 2026-09-28.

## Decision

Register monthly mission selection as an internal gameplay operation. Expose only an
internal fixture command through the existing campaign dispatcher while the reference
arc phase and full month caller are unfinished. It accepts already calculated score
and funds, uses the world-owned strategy and mission snapshot, and leaves the campaign
month guard intact.

The operation gathers eligible scripts once, then evaluates conditional labels and
odds in reference order. Before the first RNG draw it checks all potentially reachable
choices and capacity. Unsupported creation paths block the whole transaction, so a
later site/base operation cannot leave earlier missions partially scheduled. This is a
conservative boundary; it can reject an unsafe possible script that a later random
draw would have skipped. The future complete caller must run arcs before this phase
and event scripts afterward.

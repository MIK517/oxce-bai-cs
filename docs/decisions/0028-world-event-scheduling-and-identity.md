# ADR 0028: World event scheduling and sidecar identity

Status: accepted, 2026-09-26.

## Decision

Research and manufacturing invoke a registered internal gameplay operation while the
campaign transaction is held. Scheduling is not performed by notification consumers.
The existing requested-event notification remains feedback; scheduling already happened.
Internal operations are distinct from public read-only queries and command dispatch.

`SavedGame::spawnEvent`, `addGeneratedEvent` and `GeoscapeEvent::think` at pinned
revision `4df3a5e571a1a4b5e8a46d3161fb2e21a2adba15` define history and timing. The
`strategic-event` extracted-method oracle covers rounding, the sixty-minute minimum,
repeated generation and finished countdowns. Payload execution is excluded from this oracle.

Generated-event counts belong to the gameplay world snapshot and external
`generatedEvents` map. Preserve entries even when their rule was removed. Scheduled
instances use `oxcePortEntityKey`, extending the existing entity-sidecar convention.
Native imports get a legacy occurrence key once; the key survives subsequent movement,
countdown changes, deletion of other instances and rewriting. Newly generated events
use rule plus persisted generation count. Never fall back to a deleted occurrence for
a keyed new event. Reject duplicate keys and inconsistent generated identities.
Unkeyed synthetic snapshots retain the existing state/occurrence overlay fallback.

The reference reader ignores `oxcePortEntityKey`; it is preservation metadata, not a
new reference event identity. Full C++ reload is not established by these tests.

## Deferred effects and safety

Branch 6 retains event payload effects. Countdown progression and research-interrupted
cleanup execute now. A due, non-interrupted event blocks the whole next tick before
mutation. If same-day research could invalidate the interruption, preflight conservatively
blocks rather than predicting script/random outcomes or rolling back effects.

Producer preflight bounds timers, generation counts and pending population before time
or random state changes. Pending events and history entries are limited to 100,000 each.
This is a resource limit on untrusted inputs, not a reference gameplay rule. Finite
producer choice bounds may conservatively reject near-limit workloads.

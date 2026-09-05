# ADR 0025: Strategic time dispatch and mobile save ownership

Status: accepted, 2026-09-05.

## Decision

Execute timed effects during advancement, under the campaign's single writer.
`CampaignTimeDispatcher` performs preflight before committing a tick and dispatches
highest trigger down to five seconds. Popup pauses finish this fallthrough and stop
the next tick. The published summary remains exclusive highest-trigger counts, so
existing consumers must not replay it as mutation. The extracted C++ loop fixture
checks all trigger categories with hour/month pause requests. Empty-workload tests
retain the one-million-tick 16 KiB allocation ceiling.

Production campaigns stop before unsupported daily/monthly processing. Loaded active
battles and known live systems produce save-neutral capability restrictions. These
restrictions describe preserved state; raw YAML remains in Savegames. Broaden the
guards only as the corresponding systems become executable. Isolated dispatcher tests
can cross every boundary without claiming a complete campaign can do so.

Soldier/craft sidecars are indexed campaign-wide across bases and transfer containers.
Transfer IDs are assigned once at import, never rematched by mutable list position,
and emitted as `oxcePortTransferId`. `oxcePortEntityKey` distinguishes a surviving
entity from a newly constructed entity reusing its external ID. Legacy imports receive
deterministic keys; new campaign entities receive campaign-scoped keys. Empty keys on
explicitly reconstructed snapshots carry no right to an old entity's sidecar.

These metadata fields are port-owned and ignored by the reference load methods. They
do not replace OXCE soldier IDs, `(craft type, ID)`, or the reference transfer schema.
Reading port output back through C++ may discard the metadata; subsequent import
establishes a new preservation context. Emission does not mutate live campaign state
or the source document. Duplicate ownership is rejected before publication/emission.

## Consequences

Calendar-only advances that previously skipped missing gameplay now stop explicitly.
Save inspection and unknown-field conservation remain available. Transfer snapshots
model ownership and delivery fields; this prerequisite does not by itself implement
purchase, recruitment, servicing, or a complete transfer command lifecycle.

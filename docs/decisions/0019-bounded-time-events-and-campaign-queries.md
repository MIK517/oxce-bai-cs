# ADR 0019: Bounded time events and campaign queries

Status: accepted, 2026-09-03.

## Context

The campaign foundation returned one retained trigger value for every five-second tick
in an advance command. The command limit bounded the array, but the maximum command
still allocated one million enum entries while holding the campaign writer lock. The
minimal presentation client also called the persistence capture API for every redraw,
copying histories, identities, script values, and opaque-facing state that it never
used.

Future gameplay event dispatch must retain reference trigger precedence and execute in
simulation order. Presentation and future managed extensions need read-only state, but
must not acquire persistence snapshots or mutable campaign entities as their routine
query API.

## Decision

Time advancement continues to process every five-second tick synchronously under the
single campaign writer lock. It now accumulates six fixed counters and publishes a
constant-size `CampaignTimeTriggerSummary`. `CampaignTimeAdvanced.Triggers` exposes a
struct enumerable that deterministically replays the exact ordered trigger sequence
from the previous time and tick count without retaining an array. Gameplay actions and
script events introduced by later slices will execute in the primary tick loop; replay
is an observation facility, not deferred simulation.

`Oxce.Gameplay` also publishes separate `ICampaignQuery` and
`ICampaignCommandTarget` ports. The first query returns a purpose-built campaign
overview containing scalar totals and base/facility presentation data, including
linked facility extents. `CampaignState` produces the view while holding the same gate
used by commands and persistence capture. `Oxce.Engine` depends on those two ports and
no longer requests `CampaignSnapshot` during input or redraw.

## Consequences

- Retained time-event memory is independent of the number of advanced ticks; the
  command's existing one-million-tick work bound remains.
- Exact trigger order remains available without a hot-path allocation. Consumers that
  enumerate it explicitly pay linear CPU cost, bounded by the command limit.
- Later state-changing time handlers belong inside the authoritative advance loop and
  can use the same trigger switch without changing the compact public event.
- Presentation copies only the current fields it consumes. Persistence capture remains
  the durable, complete implemented-state boundary and may evolve independently.
- Query and command capabilities can be granted separately to presentation clients or
  trusted extensions.

# ADR 0018: Campaign state and OXCE save overlay

Status: accepted, 2026-09-03.

## Context

The first strategic runtime slice needs mutable state without making OXCE's YAML
layout the domain model. At the same time, saves contain much more state than this
slice can validate. Rewriting only known fields into a new document would discard
forward-compatible, tactical, and mod-owned data.

## Decision

`Oxce.Gameplay` owns the campaign aggregate, invariants, commands, events, stable
identity, and save-neutral immutable snapshots. A campaign accepts one command writer
at a time. Time and randomness are injected; the stateful random source is captured
and restored with the snapshot. Restore constructs and validates an unpublished graph
against generation-scoped runtime rule handles, and restores the caller's random
state if validation fails.

`Oxce.Savegames` owns the two-document OXCE YAML adapter. It parses bounded input,
validates the master and active mod IDs, translates external rule IDs at the boundary,
and restores script values through the compiled tag catalog. The parsed YAML document
is an opaque persistence sidecar. On write, known fields and implemented nested
entities are overlaid onto that document while unknown fields and unimplemented
collections remain intact. New saves use deterministic field order. File publication
uses a flushed same-directory temporary file followed by replacement with a backup.

Capture initially performs straightforward immutable copies. Copy-on-write,
segmentation, and mutable persistence DTOs are rejected until representative
measurements show a need.

## Consequences

- Gameplay has no YAML or `Oxce.Savegames` dependency.
- External saves and rules keep stable string IDs; runtime numeric handles never leak.
- Existing active-battle and later-strategy nodes survive strategic round trips even
  before this port interprets them.
- Unknown data is preserved structurally, not made available to gameplay as an
  unvalidated extension surface.
- A source sidecar is required for lossless preservation of fields outside the
  implemented subset; a new save can only contain state this port knows how to emit.
- The initial snapshot and YAML DOM allocate. Their measured costs are recorded and
  remain the baseline for later performance work.

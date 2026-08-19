# ADR 0008: Gameplay-owned state with external save adapters

- Status: accepted
- Date: 2026-08-19

## Context

The engine must load OXCE campaign and active-battlescape saves, preserve compatible
semantics and unknown information where possible, and write stable saves. At the same
time, gameplay state must remain efficient, enforce invariants, support deterministic
headless scenarios, and expose narrow views to future trusted C# extensions.

Two ownership models were considered:

1. Put campaign and battle state in a small common project referenced by both gameplay
   and persistence.
2. Let gameplay own runtime state and use an external persistence adapter.

A common model simplifies references but tends to become a persistence-shaped shared
data model. It invites public mutation, partially initialized entities, serializer
concerns, and broad plugin coupling. These costs conflict with gameplay compatibility
and long-term extensibility more than explicit adapter mapping does.

## Decision

Use gameplay-owned runtime state with external save adapters.

- `Oxce.Gameplay` owns mutable campaign and battle state, invariants, stable persistent
  identities, and save-neutral capture/restoration contracts.
- `Oxce.Savegames` references those contracts and owns OXCE save field names, defaults,
  versions, migrations, object/reference encoding, unknown-field preservation,
  diagnostics, and atomic file replacement.
- `Oxce.Gameplay` does not reference `Oxce.Savegames`.
- The application composes loading, restoration, capture, and writing.
- Raw YAML nodes, persistence DTOs, reflection serializers, and save-specific public
  setters do not enter gameplay state.

Loading is a staged transaction:

1. Parse the bounded external representation.
2. Validate structural limits and required mod/rule availability.
3. Create restoration data and stable identities.
4. Populate values and resolve object, rule, and script references.
5. Ask gameplay to validate the completed semantic graph.
6. Publish the state only after the complete graph is valid.

Saving captures a consistent gameplay snapshot, maps it to the compatibility schema,
reattaches eligible unknown data using stable entity identities, emits bounded YAML,
and replaces the destination atomically. The initial implementation may use ordinary
snapshots. Large tactical state will be optimized only after allocation and latency
measurements justify segmented capture, visitors, or copy-on-write.

Unknown external fields remain in a persistence-owned sidecar keyed by document,
semantic entity kind, and persistent identity. Gameplay does not carry raw YAML. The
save adapter decides whether preserved data still belongs to an entity that survives
at the next write.

## Consequences

- Gameplay models can optimize their representation and enforce invariants independently
  of YAML shape.
- Save code contains explicit mapping and may require updates when semantic gameplay
  state changes.
- Restoration APIs must deliberately support valid mid-campaign and mid-battle states
  that ordinary gameplay commands cannot construct.
- Object identity, forward/cyclic references, script state, and unknown-field ownership
  require dedicated compatibility fixtures.
- Save inspection tools depend on the gameplay contracts or operate on the external
  compatibility representation; this decision does not create a second shared domain
  model solely for tooling convenience.
- Future C# extensions use separate, versioned, read-only contracts and never persistence
  DTOs or mutable gameplay entities.

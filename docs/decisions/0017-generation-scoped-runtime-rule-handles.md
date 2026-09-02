# ADR 0017: Generation-scoped runtime rule handles

Status: accepted, 2026-09-02.

## Context

Compatibility loaders must preserve string IDs, provenance, deferred data, and OXCE
merge semantics. Gameplay should not repeatedly traverse those dictionary-oriented
models or keep unresolved relationship strings in normal rule evaluation. Numeric
slots cannot become save or mod schema because their values change with content order
and generation.

## Decision

`Oxce.Mods` publishes a `RuntimeRuleCatalog` for each successful content generation.
Each projected family owns a dense immutable array and a one-time external-ID index.
`RuleHandle<TFamily>` identifies an array entry by family and content generation; its
numeric slot is internal. Public APIs convert external IDs to handles at boundaries and
handles back to external IDs for diagnostics and persistence.

Relationships that the reference engine links eagerly become typed handles. Runtime
lookup relationships retain both the external ID and an optional handle, so missing
mod-defined targets preserve reference semantics without forcing repeated lookup for
targets that do exist. Resource slots similarly retain their OXCE set/index identity
and an optional resolved override handle. Rule-owned compiled scripts are indexed into
the same generation.

Known campaign-start fields live in direct projections. Deferred compatibility fields
remain in a separately named sidecar and are not mixed into hot records. Starting-base
facility, craft, soldier, item, and random-soldier references are compiled to handles;
gameplay state and save representations remain later concerns.

Reusable algorithms may use `RuleHandleScratch<TFamily>`, an explicitly owned pooled
buffer. This is the initial scratch-workspace pattern; it is not a general ECS or a
global mutable cache.

## Consequences

- Gameplay can evaluate pre-linked relationships with family-safe array access and no
  string lookup or per-operation allocation.
- Handles fail when used with another content generation or family.
- Saves, scripts, diagnostics, and extension views continue to use stable external IDs.
- Runtime projections add retained memory beside the compatibility catalog until later
  gameplay slices demonstrate which compatibility views can be released.
- Every newly projected relationship must be classified as eager or runtime lookup
  from the exact C++ `afterLoad`/consumer behavior.

# ADR 0003: Native AOT is not a bootstrap requirement

- Status: accepted
- Date: 2026-08-06

## Decision

Use normal self-contained .NET 10 deployments first. Preserve reasonable AOT hygiene,
but evaluate Native AOT only after YAML, scripting, SDL, and diagnostics dependencies
are stable.

## Consequences

- Early design is not constrained by trimming issues before compatibility is proven.
- Runtime C# compilation is still disallowed for OXCE scripts; the owned VM remains
  portable and auditable.
- AOT readiness becomes a measured deployment work item, not an assumed property.


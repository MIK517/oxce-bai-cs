# ADR 0012: Phase 3 content closure

## Status

Implemented.

## Context

Phase 3 now has typed catalogs for every named rule family in the reference loader and
custom composition for the special presentation, terrain, and ufopaedia sections.
Those catalogs deliberately report only `typed`: each family can validate local and
known cross-family relationships, but callers have no single operation that performs
the reference-ordered validation pass or proves that the complete strategic content
graph is linkable.

The composed dump is useful for diagnosing rule replay, but it cannot detect a typed
default, merge, or relationship regression. Corpus tests also need a stable result that
does not serialize implementation-specific object graphs or filesystem-specific paths.

## Decision

Add one aggregate Phase 3 content loader that constructs every typed catalog, runs
validation in reference dependency order, and publishes the `linked` capability only
when no error diagnostic remains. It retains each family catalog rather than copying
rules into a second model. Resource resolution and script compilation remain separate
capabilities and are not implied by linking.

Add a bounded typed-catalog manifest for corpus comparison. The manifest records schema
version, capability stage, ordered family counts and identifiers, source-normalized
provenance, deferred-property counts, and a deterministic semantic digest of the
composed input. It intentionally avoids reflection serialization and remains an audit
manifest rather than a save or runtime data format.

The closure gate will load every bundled public mod fixture and each available private
mod corpus root. Private corpus absence skips the optional test; malformed available
content fails with diagnostics. A benchmark measures aggregate composition, typing,
validation, and manifest generation as one operation.

## Consequences

- Callers get one honest `linked` result without weakening the independent
  `resources-resolved` or `scripts-compiled` gates.
- Family catalogs remain independently testable and do not acquire reverse dependency
  edges merely for closure.
- Manifest schema changes require an explicit version change and fixture update.
- Cross-cutting global tables not yet needed by a typed family remain preserved by the
  composed catalog and are listed as focused follow-up work rather than silently
  treated as linked gameplay state.

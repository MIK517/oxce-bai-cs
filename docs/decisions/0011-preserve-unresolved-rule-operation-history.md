# ADR 0011: Preserve unresolved rule operation history

- Status: accepted
- Date: 2026-08-25

## Context

OXCE does not perform one universal recursive YAML merge. `Mod::loadRule` first applies
named-rule lifecycle operations, while each typed `Rule*.cpp::load` method then decides
how individual scalar, list, map, and `refNode` properties update an existing object.
Flattening YAML mappings before typed loading would erase ordering and make replacement
semantics invented by this port part of the compatibility model.

## Decision

Compose rulesets into an ordered unresolved registry. The registry implements the
generic lifecycle shared by reference rule families: update-or-create declarations,
conditional `new`, `override`, and `update`, `delete`, `ignore`, stable insertion order,
source provenance, and bounded `refNode` shape validation.

Each surviving rule retains its ordered immutable YAML operation history. Typed rule
loaders replay that history and own property-level inheritance, replacement, deletion,
defaults, and linking. Rule section names and identity keys are registered explicitly by
typed slices instead of inferred from arbitrary top-level YAML.

## Consequences

- Generic named-rule behavior is implemented once without inventing a universal map
  merge algorithm.
- Creation and last-update provenance remain available for diagnostics and mod tooling.
- Deleting and recreating a rule discards its old operation history and moves it to the
  end of its rule-family index, matching the reference loader.
- The unresolved DOM remains alive until typed construction completes; large-mod memory
  use must be benchmarked before deciding whether histories can be compacted safely.
- Property-level collection semantics and cross-rule resolution remain explicit work in
  each typed vertical slice.

# ADR 0001: Compatibility-first port

- Status: accepted
- Date: 2026-08-06

## Decision

Treat OXCE mods/scripts, saves, original assets, and player-visible rules as compatibility
contracts. Build vertical slices with fixtures rather than mechanically translating the
C++ source tree.

Exact RNG streams, pixels, and UI layout are not compatibility contracts.

## Consequences

- The C++ implementation remains an oracle throughout development.
- Compatibility adapters and matrices are first-class artifacts.
- Cleaner internals are allowed, but observable quirks remain until deliberately reviewed.
- Initial progress may look slower than bulk translation but integration risk is reduced.


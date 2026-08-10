# Compatibility matrices

These matrices are the scope ledger for the targeted C++ branch. Status values are:

- `not started`: inventoried, with no compatible implementation yet.
- `partial`: some behavior is implemented and covered, but the entry is not complete.
- `compatible`: required behavior is demonstrated by named fixtures.
- `intentionally differs`: an accepted difference is documented by an ADR and fixture.

An entry cannot become `compatible` without a fixture and exact reference source
location. Broad entries are split into individual keys, operations, or scenarios while
their owning vertical slice is investigated. Empty fixture cells therefore represent
work still to be specified, not implicit support.

The current reference baseline is upstream commit
`67d1a6b69a7f6d8fdc377b4c3ed1d4be38514530`. Local build-only changes are isolated on
the C++ checkout's `local-windows-x64-docker-build` branch.

`reference-inventory.json` is a deterministic source inventory from the local reference
checkout. Refresh it from a conventional sibling checkout with:

```powershell
.\tools\update-reference-inventory.ps1
```

Use `-ReferenceRoot` when the checkout lives elsewhere. Candidate rule keys are a broad
source scan and must be confirmed against exact loader functions before being promoted
to compatibility entries.

- [YAML and rulesets](yaml-rulesets.md)
- [Scripting](scripting.md)
- [Saves](saves.md)
- [Original assets](assets.md)
- [Gameplay](gameplay.md)
- [Platforms](platforms.md)

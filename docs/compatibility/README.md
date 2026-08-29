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

The current reference baseline is commit
`4df3a5e571a1a4b5e8a46d3161fb2e21a2adba15`. Capture scripts refuse to regenerate
expected output from any other C++ revision.

`reference-inventory.json` is a deterministic source inventory from the local reference
checkout. Refresh it from a conventional sibling checkout with:

```powershell
.\tools\update-reference-inventory.ps1
```

Use `-ReferenceRoot` when the checkout lives elsewhere. Candidate rule keys are a broad
source scan and must be confirmed against exact loader functions before being promoted
to compatibility entries.

`script-inventory.json` is the Phase 4 source ledger for core operations, limits,
`ScriptRegister` definitions, binding/constant name candidates, parser types, and
persistent script-value owners. Schema version 2 also records exact foundational type
encoding, event limits, macro-defined operations, and direct registrations. Refresh it
against the same pinned checkout with:

```powershell
.\tools\update-script-inventory.ps1
```

Binding inventory entries are candidates. The separately captured `script-api-catalog`
fixture is the reviewed, template-resolved declaration source; declaration compatibility
does not claim that later gameplay providers are implemented.

- [YAML and rulesets](yaml-rulesets.md)
- [Files and resource catalog](files.md)
- [Scripting](scripting.md)
- [Saves](saves.md)
- [Original assets](assets.md)
- [Gameplay](gameplay.md)
- [Platforms](platforms.md)

# Compatibility fixtures

Fixtures are small executable specifications for external OXCE behavior. Paths in a
manifest are relative to the repository root and use forward slashes.

- `public/` contains redistributable synthetic or freely licensed inputs.
- `expected/` contains normalized semantic output and reference metadata.
- `manifests/` describes inputs, hashes, normalization, and expected output.
- `private/` is ignored and reserved for original game data and non-redistributable mods.

Manifest schema version 1 is described by `manifest.schema.json`. A C++ oracle fixture
uses reference kind `cpp-reference` and must record the full reference commit. Fixtures
used to test the harness itself use `tool-self-test`; they do not claim C++ parity.

From the repository root:

```text
dotnet run --project tools/Oxce.FixtureTool -- inspect fixtures/manifests/bootstrap-json.json
dotnet run --project tools/Oxce.FixtureTool -- normalize fixtures/public/bootstrap/canonical-json.input.json
dotnet run --project tools/Oxce.FixtureTool -- compare fixtures/expected/bootstrap/canonical-json.expected.json <actual.json>
```

The first executable C++ oracle probe covers battlescape coordinates. On Windows with
Visual Studio C++ tools installed, capture it with:

```powershell
.\tools\capture-position-reference.ps1
```

The Phase 1 YAML oracle covers multiple documents, nulls, duplicate mappings, anchors,
aliases, and merge keys:

```powershell
.\tools\capture-yaml-reference.ps1
.\tools\capture-yaml-scalars-reference.ps1
.\tools\capture-yaml-containers-reference.ps1
```

The scripts discover the conventional sibling checkout and the categorized
`C#/...` plus `CPP/...` layout. Otherwise pass `-ReferenceRoot` or set the portable
`OXCE_REFERENCE_ROOT` environment variable.

# Content build and runtime lifetime

Status date: 2026-09-02.

Content loading has three explicit ownership domains. A caller must publish
`RuntimeContent`, not the build result or build session.

| Owner | Lifetime | Contents |
|---|---|---|
| `ContentBuildSession` | Load only | Parsed `RulesetDocumentCatalog`, composed `UnresolvedRuleCatalog`, typed catalog, and parse/compose/type-link measurements. |
| `RuntimeContent` | Content generation | Dense runtime rule projections and handles, immutable resolved-resource descriptors/handles, compiled script programs and event plans, tag definitions, initial script values, capabilities, and the parsed-file count. |
| `ContentCompatibilityData` | Build, cache reconstruction, or explicit tooling lifetime | Linked typed catalogs, deferred YAML, source provenance, and deferred-rule compatibility entries. Normal production publication drops this cold sidecar. |
| `ContentAuditArtifact` | Optional tool/audit lifetime | Parsed documents and composed operation history required for semantic manifests and composition inspection. It is explicitly requested and disposable. |

`ContentSnapshot` is the build result: it carries the publishable `RuntimeContent`, the
cold `ContentCompatibilityData`, diagnostics, measurements, counts, and an optional
audit artifact. Keeping the build result is not required to keep runtime content alive.
Normal application composition must retain only `ContentSnapshot.Content` after handling
diagnostics. Audit tools may
request `ContentSnapshotOptions.RetainAuditArtifact`, normalize their output, and then
dispose the artifact to release parse and composition graphs.

## Structured compatibility-node inventory

Raw YAML is not a general runtime domain model. Nodes that remain after publication
are limited to compatibility structures whose schema has not yet reached its owning
runtime projection:

| Current owner | Structured data | Planned consumer/disposition |
|---|---|---|
| `RuleCompatibilityData` on each typed rule | Deferred and unknown rule properties with provenance | Retained only by `ContentCompatibilityData`; gameplay projections contain known hot fields and stable external IDs. |
| `CampaignStartSettings` compatibility catalog | Difficulty-specific starting-base mappings | Facility, craft, soldier, item, random-soldier, and time/funding fields now have bounded runtime projections. Remaining base/template fields stay compatibility-only until their gameplay owner lands. |
| Manufacture, soldier, unit, and event rules | Spawned-soldier templates and soldier stat-string structures | Phase 4 rule projections and Phase 5 campaign/personnel creation. |
| Terrain and map-script rules | Map-block item/fuse payloads, tunnel/vertical-level payloads, deployment/reinforcement/event structures, and alien-race evolution | `Oxce.Resources` terrain descriptors and later tactical mission generation. |
| Ufopaedia rules | Type-specific rectangle/layout structures | Presentation/resource projection. |

These nodes are reachable through named compatibility fields or `RuleCompatibilityData`
only while `ContentCompatibilityData` is retained. They are not reachable from
`RuntimeContent`, a parsed-document root, or a composed operation list on the normal
runtime path. Each later projection must replace its raw node with a bounded immutable
representation or keep it in the cold sidecar. Adding an unclassified `YamlNode` field
to hot runtime content is a lifetime-contract change and requires updating this inventory.

## Script API scopes

The reference engine owns one shared `ScriptGlobal`, mutates file-visible constants as
files load, and releases parser lookup state at `endLoad`. The port mirrors those
semantics with immutable views:

- `ReferenceScriptApiCatalog` owns the shared bindings, parser definitions, types, and
  their indexes.
- `ScriptApiCatalog.CreateScope` adds file-visible constants while reusing the shared
  arrays and indexes.
- Content compilation reuses one scope for a `(visible tag count, current mod)` state
  and maps each source file to that scope.
- Scopes exist only during compilation; compiled programs retain only bindings and
  instructions required at runtime.

This preserves cumulative tag visibility and file-scoped `RuleList.current` without
constructing a complete indexed API catalog for every ruleset file.

## Measurements

Every build records elapsed time and current-thread allocation for parse, compose,
type/link, resource-resolution, script-compilation, and runtime-rule-linking stages. `audit-content-install` retains audit state
long enough to emit and hash the semantic manifest. `measure-content-install` runs in a
separate process and returns only `RuntimeContent` across a non-inlined build boundary,
so its post-GC retained measurement excludes build diagnostics, parsed documents,
composed history, cold compatibility data, and audit normalization buffers. The staged
40k/Rosigma hot-runtime result is 30.2 MiB retained.

The normative C++ lifetime and file-scope references inspected for this boundary are:

- `src/Mod/Mod.cpp`: `ModScriptGlobal`, `Mod::loadMod`, and `Mod::loadFile`;
- `src/Mod/ModScript.h`: parser ownership against shared `ScriptGlobal`;
- `src/Engine/Script.cpp`: `ScriptGlobal::fileLoad`, `load`, and `endLoad`;
- `src/Engine/Script.h`: `ScriptGlobal` parser, tag, event, and current-file storage.

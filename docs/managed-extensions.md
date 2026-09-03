# Managed extensions

Managed extensions are trusted, manually installed .NET assemblies. They add optional
engine behavior; they do not replace OXCE rulesets or scripts. The accepted ownership,
compatibility, persistence, and deferral policy is in
[ADR 0021](decisions/0021-versioned-managed-extensions.md).

## Installation

Each extension occupies one immediate child directory of the installation's
`extensions` directory. The directory contains `extension.json`, the entry assembly,
and any private managed or native dependencies. The initial manifest schema is:

```json
{
  "schemaVersion": 1,
  "id": "vendor.extension",
  "version": "1.0.0",
  "entryAssembly": "Vendor.Extension.dll",
  "entryType": "Vendor.Extension.EntryPoint",
  "minimumApiVersion": "0.1",
  "maximumApiVersionExclusive": "0.2"
}
```

IDs are case-sensitive and contain at most 128 ASCII letters, digits, dots, hyphens,
or underscores. Entry assemblies must remain inside their extension directory. The
entry type is a public, concrete `IManagedExtension` with a public parameterless
constructor. The current experimental host API is `0.1`; compatibility uses the
half-open interval declared by the manifest.

The host scans at most 256 immediate directories and reads manifests no larger than
64 KiB. It loads each extension into its own non-collectible assembly-load context,
while sharing `Oxce.Extensions.Abstractions` with the application so contract type
identity remains stable. Extensions and dependencies remain loaded until process exit.

## Current capabilities

An extension can implement `ICampaignExtension` to receive a copied campaign overview,
submit the currently implemented validated campaign commands, and observe the resulting
committed events. Event callbacks cannot submit another command. Unknown future
gameplay events are not exposed until an explicit abstraction projection is added.

Exceptions at lifecycle, campaign, or state callbacks disable the extension and emit
structured `EXT` diagnostics. This protects host control flow, but it is not a sandbox:
trusted in-process code can still block, terminate, or inspect the process.

## Save-neutral state

`IManagedExtensionState` captures and restores an extension-owned structured tree. The
tree supports null, Boolean, signed 64-bit whole number, finite number, text, list, and
ordinal map values. Documents are deterministic by extension ID and map key. Default
limits are 256 records, depth 32, 100,000 total nodes, 10,000 entries per collection,
256 KiB per string, and 1 MiB encoded JSON.

Records carry stable extension ID, extension version, positive state-schema version,
and `requiredForContinuation`. Missing optional records are preserved; missing required
records prevent successful restoration. Extensions interpret and migrate their own
schema versions.

The JSON codec is an internal semantic/fixture representation, not the eventual OXCE
save shape. Encoding the same document below a namespaced OXCE YAML root and taking a
transactionally consistent campaign-plus-extension snapshot are deferred until a real
extension owns persistent gameplay state. Extensions will not receive YAML nodes.

## Deferred API

The tactical AI provider waits for tactical identities, immutable decision views,
validated actions, and deterministic decision context. Craft-mission hooks wait for
the craft and geoscape-mission slice. UI, content-generation, telemetry, dependency
graphs, package management, hot reload, unloading, and process isolation remain
demand-driven additions.

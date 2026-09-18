# ADR 0020: Installation planning and runtime bootstrap

Status: accepted, 2026-09-03.

## Context

Application and fixture-tool entry points independently discovered the `standard` and
`user/mods` trees, mapped external resources, built a catalog and activation plan,
collected diagnostics, built content, checked `RuntimeLinked`, and formatted failures.
The copies had already diverged around the no-add-on `-` marker and made it easy for a
new entry point to retain build-only state or report a different startup failure.

Loading a representative mod installation is also long enough that future interactive
startup needs cancellation and meaningful progress. A token checked only before and
after the complete build would not make directory scanning, rule processing, resource
resolution, or script compilation responsive.

## Decision

`Oxce.Mods.Bootstrap` owns the OXCE installation layout and activation bootstrap.
`InstallationPlanBuilder` discovers both mod trees and returns a structured plan result.
`InstallationContentLoader` builds and publishes only `RuntimeContent`, together with
measurements, bounded diagnostics, and a structured failure when planning or content
publication fails. The `-` command-line marker is normalized at request construction and
never becomes a mod activation.

The runtime loader reports stable coarse stages: discovery, planning, parsing,
composition, type/link, resource resolution, script compilation, runtime linking, and
completion. Cancellation is checked within directory traversal, per ruleset and rule
operation, resource declaration expansion, script/rule compilation, and runtime-family
linking. The API remains synchronous; hosts decide whether to run it on a worker thread.

The SDL campaign entry point and the headless campaign scenario use the complete runtime
loader. Audit and retained-memory measurement commands reuse installation planning but
keep direct control of `ContentSnapshotBuilder`, audit artifacts, GC observations, and
diagnostic release so the bootstrap abstraction does not invalidate their measurements.

## Consequences

- Startup policy, activation normalization, diagnostics, and runtime publication have
  one production implementation.
- Callers can distinguish cancellation, invalid requests, discovery failures, planning
  failures, and incomplete content builds without parsing exception text.
- Normal application startup cannot request or retain an audit artifact through this
  API.
- Progress is intentionally stage-based; percentages and per-file UI are deferred until
  measured startup work provides dependable weighting.
- Parallel parsing, persistent compiled-content caches, and hot reload remain separate
  performance work and are not implied by this extraction.

## Addendum, 2026-09-18

Shared `common` resources belong to the installation, not to a mod. Discovery maps them
once from the external resource roots (`ModDiscoveryResult.CommonLayers`), a catalog keeps
one layer per ID across the mod trees it was built from, and every plan created from that
catalog maps them below all mod layers, matching `FileMap::setup`, which calls
`VFS::map_common` before pushing any mod. Masters that declare no `loadResources` therefore
see `common` as well; previously only mods with external resources carried it.
Catalogs built from a single discovery result carry those layers automatically. Combining
mod candidates from several scans requires the caller to supply the installation's common
layers explicitly, so omitting them cannot silently change the load plan.

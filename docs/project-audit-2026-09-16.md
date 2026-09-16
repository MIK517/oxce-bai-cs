# Project audit, 2026-09-16

Branch: `audit/full-review-2026-09`, based on `main` at `cdb7e27`.
Reference engine: `oxce-bai` at `4df3a5e` (the pinned fixture commit).

## Outcome

The architectural foundation holds up. Project boundaries are enforced by tests, lower
layers do not depend on SDL or serializers, untrusted inputs are bounded, and runtime
content is immutable. No redesign is warranted.

The audit found nine concrete defects. Eight are fixed on this branch with regression
tests. The ninth is a performance fix. Six were in the newest strategic slice, even though
that slice had already been audited twice. The remaining gaps are listed below. They are
larger decisions that need an owner, so they were not patched here.

Validation on the branch head (Linux, SDK 10.0.401): build with no warnings, 537 unit
tests pass, 153 public compatibility tests pass, and `dotnet format --verify-no-changes`
passes. The `Private*`, `Phase3ContentCorpusTests` and `ModLoadingFixtureTests` classes
still need a run on Windows with the private corpus.

## Tooling added

- `tools/generate-code-map.py` writes `docs/code-map.md`. The map lists every project with
  its references, namespaces and public types (with the first XML-doc sentence), and the
  shared and nested test helpers. It also shows which tests and benchmarks use each fixture
  manifest, each expected output without a manifest, and each public mod fixture. The map
  has no line numbers, so ordinary edits do not make it stale. `--check` fails when the map
  is out of date, and the Linux CI job now runs it.
- `EnvClaude/dotnet/oxce-test.sh format` runs the CI formatting check inside the VM. This
  file is outside the repository.

The first map already surfaced one finding: the `campaign-foundation` manifest is only
verified by `FixturePipelineTests`. `CampaignFoundationFixtureTests` reads its expected
file directly. Seven `mods/*-rules` oracles and four `savegames/strategic-*` oracles have
no manifest at all, so their input hashes are not pinned.

## Fixed defects

| # | Area | Defect | Reference |
| --- | --- | --- | --- |
| 1 | Gameplay | Zero-cost manufacture stopped with `STR_NOT_ENOUGH_MONEY` whenever funds were negative. | `RuleManufacture::haveEnoughMoneyForOneMoreUnit` accepts `cost <= 0`. |
| 2 | Gameplay | Transfers created by research or production, produced craft and spawned soldiers took IDs from the counters only. The save adapter numbers the transfers of reference saves from 1 without writing a counter. The first transfer created after loading such a save therefore reused an ID, and the captured campaign could no longer be restored. | Port invariant; `SavedGame::getId`. |
| 3 | Formats | The YAML writer left scalars unquoted when they had leading or trailing whitespace or ended in `:`. It also kept single quotes around values with line breaks, and wrote C1, DEL, U+2028/2029 and BOM characters raw. A base named ` Alpha` lost its space when reloaded, and one named `Base:` made the save unparseable. | YAML 1.1/1.2 scalar rules. |
| 4 | Savegames | The adapter rejected a save that repeated a topic under `discovered`. | `SavedGame::load` accepts repeats. |
| 5 | Scripting | `pow` (and `sqrt`, sine and cosine waves) converted double to int with .NET's saturating conversion. For example, `pow 2 31` gave `INT_MAX`, where the reference x64 build gives `INT_MIN`. | `Script.cpp` assigns `std::pow` to `int` (cvttsd2si). |
| 6 | Gameplay | Zero-cost topics were discovered automatically without checking the base that finished the research: whether the topic was already running there, whether the needed item was in stock, and whether the required base functions existed. The protected-unlock check also ran after re-enables instead of before. | `SavedGame::addFinishedResearch` and `getAvailableResearchProjects(base)`. |
| 7 | Gameplay | A finished topic whose protected unlock was still waiting on requirements showed "already complete", so it could not be researched again. An extra exemption for repeatable topics was also dropped. | `getAvailableResearchProjects` (keep-if-rewards rule). |
| 8 | Mods/App (performance) | Each load rebuilt the layered file index up to five times: once for resource resolution, twice for runtime linking and twice during cache restore. The SDL command also rediscovered the whole installation just to get that index. `ModLoadPlan.VirtualFiles` now builds it once, and `InstallationContentLoadResult.VirtualFiles` publishes it with the content. | — |
| 9 | Savegames | Counted maps were written in dictionary mutation order: items, ids, rule status, purchase log and random production output. They are now written sorted by key. | `ItemContainer::save`, `std::map`. |

Each fix has a test that fails without it. This was checked explicitly for #1 and #2.

## Architecture review

**What holds**

- The dependency graph is acyclic and tested. `architecture.md` now lists every enforced
  edge.
- Gameplay owns its state, and saves go through an adapter that overlays an opaque source
  document. This model worked well: every save fix above stayed inside the adapter.
- Content has a hot runtime part and a cold compatibility part. The versioned compiled
  cache is keyed on ruleset bytes, TAB/CAT headers and `.nam` hashes, and it rereads the
  globe and name pools when it restores. No stale-cache path was found.
- Codecs bound their allocations by pixel, byte, entry and record limits. `CatArchive` and
  the image and FLC decoders reject malformed offsets before allocating.
- The script VM matches the reference semantics for division by zero, `muldiv`,
  `offsetmod`, waves and shades. `pow` was the only mismatch (#5).

**Assumptions that did not hold, or still need a decision**

1. *The port is stricter than the reference.* `VirtualPath` throws on `.`, `..` and empty
   path segments, and it maps `\` to `/`. The reference only lowercases the path, so the
   same lookups simply miss. `CatArchive` rejects entries outside the file, while
   `CatFile` skips them, which shifts later indexes. Both divergences are safe, but mods
   that load in OXCE can fail here. Decide per case, and record the decision in ADR 0024
   or ADR 0010.
2. *Invalid UTF-8 is decoded as Windows-1252* (the reader and `OxceSaveAdapter.Decode`),
   and the writer then re-encodes it as UTF-8. The reference keeps the raw bytes, so
   rewriting a Latin-1 save changes its bytes.
3. *Extension state is not persisted.* `ExtensionStateJsonCodec` and
   `ManagedExtensionHost.CaptureState` exist, but the save path in `CampaignSdlCommand`
   does not call them. Extension state marked `RequiredForContinuation` is therefore lost
   on save, which contradicts ADR 0021.
4. *`CampaignState` is becoming a monolith*: about 4.5k lines of partial classes behind one
   lock and one command switch. The 2026-09-04 review already recommended per-capability
   handlers, and world simulation (Phase 6 branch 4) is the last cheap point to introduce
   them.
5. *UI assets read `common/Language` from disk directly* (`CampaignUiAssets`) instead of
   through the virtual file system, even though the architecture maps `common` into it.
6. *Loaded-save identity depends on port-only keys* (`oxcePortEntityKey`,
   `oxcePortTransferId`). When the reference engine rewrites a save, those keys disappear
   and matching falls back to legacy keys. This is by design, but the save matrix does
   not mention it.

## Performance notes (not changed)

- `ItemPrice` rebuilds its binding set and LINQ pipelines on every call, and a logistics
  quote calls it for every item. This is only noticeable when price scripts exist.
- `OpenZipEntry` reparses the ZIP central directory on every open. Measure this for large
  archived mods before adding pooling.
- `AddFinishedResearch` scans all research rules for each queued topic. This is fine
  today. Revisit it if monthly or world ticks start calling research availability often.

## Suggested next steps

1. Run the private corpus classes on Windows against this branch.
2. Add manifests for the eleven oracles without one, and make
   `CampaignFoundationFixtureTests` use `LoadVerifiedManifest`.
3. Decide items 1–3 above before Phase 6 branch 4. Item 3 blocks any extension that needs
   to save state.

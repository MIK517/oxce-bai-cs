# Project review and sequential repair plan

Review date: 2026-09-05. Reviewed HEAD: `0fd0a4c` on `main`.

## Outcome

Two reproducible compatibility defects remain in the current foundation. Correct them
before treating Phase 5 as a reliable starting point for broader strategic gameplay.
The existing project boundaries, indexed rendering model, prepared script execution,
and hot/cold content ownership do not currently justify a redesign.

This is a review and proposed delivery plan, not an implementation of the fixes.
No branches, pull requests, CI runs, or production-code changes were made for this review.

## Findings

### P1: Compiled cache ignores asset bytes used to resolve resource indexes

Location: `src/Oxce.Mods/Bootstrap/CompiledContentCache.cs:154-159`.

`ComputeKey` hashes ruleset contents, but other layer entries contribute only their
canonical and physical paths. `ResourceDescriptorResolver.AddKnownSharedCounts` reads
TAB lengths/headers and CAT headers to determine shared sprite/sound counts. Those
counts affect whether a declared index is shared or receives a mod offset. Cache
restoration reuses persisted resource indexes rather than rerunning resource resolution.

Reproduction using an isolated copy of `runtime-rule-linking`:

1. Add an empty `runtime-master/GEOGRAPH/BASEBITS.TAB` before the first load.
2. Load the master and add-on through `InstallationContentLoader` with caching enabled.
3. Replace that TAB with four zero bytes, keeping the same filename and all rulesets.
4. Compare another cached load with a load using `Cache.Enabled = false`.

Observed: cache status `Hit`, cached runtime sprite index `1000`, fresh index `0`.
This demonstrates stale behavior after an asset-only update, independently of RNG or
rendering differences. Sound indexes use the same incomplete identity boundary.

Fix: include strong content identities for every resource input consumed during content
compilation, using the effective VFS winner and explicit dependency tracking or a
complete bounded dependency inventory. Alternatively rerun affected resolution on cache
restoration. Hashing all large media files indiscriminately would add unnecessary startup
cost. Preserve overrides and fallback selection, and invalidate old cache images.

Acceptance must cover loose and ZIP assets, same-path and same-size header changes,
changed shared counts, malformed replacements, and cached/fresh semantic equivalence.

Reference inspected: C++ `src/Mod/Mod.cpp` loads PCK/TAB sets and derives maximum shared
frames from loaded frame counts; `ExtraSprites.cpp` and `ExtraSounds.cpp` contain the
shared-index offset decision. The pinned reference remains the source for new fixtures.

### P1: Starting scientists and engineers are silently discarded

Location: `src/Oxce.Gameplay/Campaigns/CampaignFactory.cs:108-109`, with the omission
originating in `RuntimeRuleLinker.StartingBase` and `RuntimeStartingBaseTemplate`.

The runtime starting-base projection contains no scientist or engineer counts. Creation
then passes literal zeros to `BaseState`, even though these fields are already owned by
gameplay snapshots, queries, validation, and the save adapter.

Reproduction: add `scientists: 10` and `engineers: 20` under `startingBase` in an isolated
copy of the public runtime-rule fixture, then create a Beginner campaign. Observed:
`Scientists = 0`, `Engineers = 0`. The loss happens before saving and becomes persistent
when the campaign is written.

This is an omission in the implemented creation subset, not the documented deferral of
hiring, research, or manufacturing. C++ `Mod::newSave` invokes `Base::load` on the selected
template; `src/Savegame/Base.cpp:172-173` reads both counts.

Fix: project the counts with reference defaults and validation, consume them in creation,
and preserve them through capture/save/reload. Cover default and difficulty-specific
templates, merge behavior, missing fields, and malformed values. Update the creation
matrix and cache schema/revision as appropriate for the changed projection.

## Bottlenecks and validation gaps

- Startup remains the clearest measured performance opportunity. The existing September
  cache baseline records a 5,366.8 ms warm hit and 943.0 MiB allocation versus a nominal
  5-second warm target. These are historical measurements, not fresh measurements from
  this review or proof of a regression at this HEAD. Cache publication previously took
  11,621.3 ms versus 7,811.5 ms with caching disabled. Re-measure after correctness fixes.
- Cache hits deserialize the cold compatibility graph, reconstruct resources, and rerun
  runtime linking. The loader reports `ContentBuildMeasurements.Empty` for a hit, so
  existing stage telemetry cannot identify which restoration stage dominates. Add
  discovery/key hashing/decompression-deserialization/link/publication measurements
  before choosing a new cache representation or parallel compilation.
- Current retained hot content is documented at approximately 30.2 MiB. Existing mixer,
  frame-conversion, and resource-cache-hit measurements do not justify speculative
  lock-free queues, SIMD, or resource-cache concurrency work.
- Both defects escape all current tests. Add public regression fixtures, not just private
  installation checks or C# save round trips, which can preserve already-wrong state.
- CI repeats formatting on three platforms, runs a separate coverage build/test job, and
  rebuilds pinned native SDL on relevant PRs. Neither workflow has superseded-run
  cancellation. These are cost opportunities; retain platform coverage and required
  checks rather than treating fewer checks as a correctness improvement.

## Sequential branches

Implementation follow-up: branch 1 now includes the shared TAB/CAT metadata in cache
identity, uses the live ZIP entry length for archived resources, and invalidates older
keys with compiler revision 2. The original unit reproduction fails before the repair;
the completed Release suite passes 553 tests, including 18 generated resource dependency
scenarios. See ADR 0022 for the bounded metadata fingerprint and sound-config policy.
Branch 2 now projects scientist/engineer counts into starting templates and campaign
creation. Its public fixture covers all five difficulties, default fallback, ordered
mod overlays, absent/zero counts, malformed values, cached content, and atomic save/reload.
The existing 32-bit conversion and negative-count campaign invariant remain intact.
Compiler revision 3 invalidates older cache keys without changing the save/cache payload
format. The two confirmed correctness findings are addressed.

Branch 3 adds non-overlapping installation startup measurements and exposes them in the
fixture tool, removes full VFS construction for the bounded metadata-key lookups, and
cancels superseded validation only for the same PR/workflow. It preserves every main and
manual run and all platform/coverage/native checks. The key stage allocates 52.5% less
on the staged corpus; warm startup still exceeds the target because deserialization
dominates. Native artifact caching is explicitly deferred pending runner evidence.
See [startup/CI status](startup-ci-efficiency-status.md) for measurements and limitations.

Only one branch/PR is active at a time. Each starts from updated `main` after the previous
PR has passed and been merged using **Rebase and merge**. Do not stack unfinished PRs.

| Order | Proposed branch | Complete acceptance boundary |
|---|---|---|
| 1 | `codex/cache-resource-dependency-correctness` | Fix asset dependency identity; public TAB/CAT and ZIP regressions; cached/fresh equality and malformed-input rejection; update cache ADR and resource compatibility evidence. |
| 2 | `codex/starting-base-personnel-correctness` | Project and initialize scientists/engineers; reference fixtures for default/difficulty templates; creation-to-save/reload scenario; cache migration/invalidation coverage; update gameplay/save matrices. |
| 3 | `codex/startup-ci-measurement-efficiency` | Attribute cache-hit stage costs and capture current representative baselines; implement only measured startup improvements; add safe PR supersession cancellation and consider pinned native SDL artifact caching; retain all platform/coverage acceptance. |

Branch 3 is engineering follow-up, not a prerequisite for claiming the two correctness
fixes. Do not promise the 5-second target until new measurements support it. Separate
commits within each branch may keep fixtures, code, and documentation reviewable without
requiring separate PRs or pushes for each commit.

## CI execution policy

1. Develop and run focused tests locally. Before opening the PR, run the Release build,
   full test suite, and formatting verification once on the final candidate. Run owned
   corpus scenarios when the affected capability requires them.
2. Push/open one completed candidate per branch. Draft PRs also trigger the current
   `pull_request` workflows; keeping an unfinished draft open does not save CI runs.
3. Wait for all required checks, address failures locally, and push again only after the
   revised candidate is ready. Merge, wait for the resulting `main` validation, update
   local `main`, and only then start the next branch.
4. With current configuration, expect one main CI workflow on the final PR candidate
   and another after merge: four jobs each (three OS jobs plus coverage). Relevant SDL
   paths add a three-platform native workflow on the PR. This is a planning floor,
   not a guarantee when checks fail or additional validation is required.
5. Keep the existing SDL path scope. For cancellation, scope PR concurrency by workflow
   and PR identity; preserve useful `main` validation. Any native cache must include
   platform, architecture, pinned source checksum, build script/options, and toolchain
   identity, and rerun native smoke tests even on a cache hit.

## Next feature gate

Because problems were found, this review does not declare the foundation clean or
replace the roadmap with an approved next major feature phase. Phase 6 remains next:
base operations, personnel/inventory/craft/transfers, research/manufacture, then world
missions/interception and monthly processing. Plan its detailed feature branches after
the two repairs pass. Each future slice must own rules, commands, script providers,
save fields, a minimal usable UI, and a headless scenario together.

Before transfers, in particular, expand opaque save ownership so soldier and craft
sidecars follow their persistent identity across bases; current lookup is base-local.
Before time-driven actions, execute trigger behavior in reference order during simulation;
the current post-command trigger summary/replay is a foundation event API, not a substitute
for interleaved gameplay mutation.

## Validation performed

- Release solution build with `--no-restore`: passed, zero warnings and errors.
- Release solution tests with `--no-build`: 534 passed, zero failed or skipped; this
  included the locally available private corpus checks.
- Isolated project-local reproduction: both defects confirmed with the outputs above.
  Source and output are retained under ignored `artifacts/review-2026-09-05/` for repairs.
- Inspected repository architecture, compatibility contract/matrices, testing strategy,
  recent fixes, campaign creation/restoration, save overlays, compiled cache/resource
  resolution, extension dispatch, resource cache, and both CI workflow definitions.
- No new performance benchmark, native SDL run, live GitHub settings audit, or exhaustive
  parser/codec audit was performed. Performance conclusions use recorded baselines;
  CI estimates describe checked-in workflow behavior, not remote branch protection.

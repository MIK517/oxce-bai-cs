# Agent instructions

These instructions apply to the entire repository.

## Mission

Implement a .NET 10 version of OXCE that:

1. Loads existing OXCE mods, including the existing script language.
2. Reads existing supported save files and writes saves accepted by this port; when
   feasible, new saves should also load in the reference C++ engine.
3. Reads the same original UFO: Enemy Unknown and Terror from the Deep assets.
4. Implements the same player-visible game rules and mod-visible semantics.

Exact RNG stream parity, pixel-perfect rendering, and an identical UI are explicitly
out of scope. Do not accidentally turn those non-goals into weaker data, scripting,
or gameplay compatibility.

## Authoritative references

- Reference engine: separate `oxce-bai` checkout, conventionally at `../oxce-bai`. Only for reference, no changes expected.
- Compatibility definition: `docs/compatibility-contract.md`
- Architecture and dependency direction: `docs/architecture.md`
- Current roadmap: `docs/implementation-plan.md`
- Test/oracle rules: `docs/testing-strategy.md`

The C++ tree is a behavioral specification, not a template that must be translated
line by line. Inspect the exact reference implementation before porting a behavior.
Do not modify the C++ tree as part of work in this repository unless the user asks.

## Required workflow

Before implementing a compatibility-sensitive feature:

1. Identify the corresponding C++ files and record them in the PR/change summary.
2. Write or capture a small fixture that demonstrates the existing behavior.
3. Implement the behavior in the owning project; do not bypass project boundaries.
4. Add unit tests for local rules and compatibility tests for external behavior.
5. Update the relevant compatibility matrix or plan item.

Prefer vertical slices that load real data and produce observable output. Avoid large
mechanical translations with no executable acceptance test.

## Compatibility rules

- Preserve ruleset keys, default values, merge order, error conditions, and script
  semantics. C# property names are not the external schema.
- Do not use a reflection serializer as the compatibility model for YAML. A library
  may parse YAML, but OXCE node semantics must live in our own adapter.
- Preserve integer widths, signedness, overflow, rounding, coordinate systems, and
  ordering wherever they affect gameplay, saves, resources, or scripts.
- Do not depend on dictionary enumeration unless ordering is explicitly defined.
- Keep randomness behind `IRandomSource`. Reproducible tests are required even though
  the sequence need not match C++.
- Treat save and mod parsing as untrusted input: validate sizes, offsets, recursion,
  and resource limits.
- Never commit copyrighted original game assets. Private fixtures belong under the
  ignored `fixtures/private/` directory.

## Engineering rules

- Target `net10.0`; use nullable reference types and keep warnings as errors.
- Keep the core and compatibility libraries platform-independent.
- Keep UI decoupled from gameplay logic so multiplayer, modding and other expansions stay
  possible: gameplay owns every rule (availability, validation, outcomes) behind commands,
  events and queries, and screens only lay out what queries return and send commands. No UI
  types in gameplay projects and no gameplay decisions in UI code.
- Native calls belong only in `Oxce.Platform.Sdl` or another explicitly approved
  platform project. Prefer source-generated `LibraryImport` and safe lifetime wrappers.
- The canonical software surface is 8-bit indexed data. Do not make RGBA textures the
  domain model merely because the display backend uses them.
- Avoid per-frame and inner-loop allocations. Measure before using unsafe code; keep
  unsafe blocks small, documented, and covered by tests.
- Add external packages only when their license, maintenance status, Native AOT impact,
  and compatibility implications have been reviewed.
- Record consequential architectural choices in `docs/decisions/`.
- `docs/code-map.md` indexes projects, public types, test helpers and fixture ownership.
  Regenerate it with `python3 tools/generate-code-map.py` after adding or moving types,
  helpers or fixtures; CI runs the script with `--check`.

## Host-authoritative design (future multiplayer)

The game runs as a host (state, rules, randomness, clock, saves) and thin clients (presentation and
per-player view state), single-player included: one host plus one local client in the same process.
Multiplayer later adds only a wire protocol and remote clients. Keep new code compatible with that
([ADR 0037](docs/decisions/0037-host-authoritative-session.md)):

- Commands are immutable data: primitives, strings, enums and stable IDs; no object references,
  delegates, runtime rule handles or live entities.
- A command that cannot run returns a classified refusal before any mutation; do not throw on invalid
  player input after state has changed.
- Every observable change produces an event or a query revision; clients refresh from queries and never
  assume their own command was the only change.
- Queries return immutable views. Anything with hidden information (all battlescape queries) takes the
  viewer (faction, later player) as a parameter.
- Only the host advances time. Do not add pause or speed decisions to UI clients.
- Per-player interaction state (selections, quotes, drafts) is client state or keyed by actor; never a
  single campaign-wide slot that another player's action or a time advance invalidates.
- Decisions with gameplay effects are pending state with an ID that a command answers.
- No process-wide mutable state in simulation code; test switches use `AsyncLocal` or options.
- Phase 7 battles: an action resolves fully in the simulation (animation replays its events), every unit
  has a controller (AI or human), and visibility is per faction.

## Test conventions

- Shared helpers live in `tests/Shared/TestFixtures.cs` (linked into both test
  projects). Use them instead of local copies: `LoadVerifiedManifest`,
  `ReadVerifiedExpected`/`VerifiedExpectedPath` (the only way to read an oracle),
  `CreatePlan`, `CreateRuntimeRuleLinkingPlan`, `LoadStrategicLogistics`,
  `CreateLogisticsCampaign`, `LoadLogisticsSave`, `CopyDirectory`.
- Tests must be deterministic: fixed campaign IDs, `FixedClock`, explicit
  `SplitMix64RandomSource` seeds; never `Guid.NewGuid()` or the system clock
  for campaign identity.
- Compare fixture output with
  `Assert.Equal(CanonicalJson.Normalize(expected), CanonicalJson.Normalize(actual))`
  so failures print the difference.
- Iterate oracle arrays through `TestFixtures.Rows(...)`, which fails on an
  empty array.
- A unit test class that asserts exactly zero bytes from
  `GC.GetAllocatedBytesForCurrentThread` joins
  `[Collection(AllocationMeasurement.Name)]`, which runs alone after the
  parallel tests; under parallel load, runtime work can add stray bytes.
- `StrategicReadinessTestContent.Load` builds every ruleset fresh, restores it from the compiled
  cache and asserts both are identical (`ContentStateDump`); tests run on the restored content.
  Do not add fresh/cache theory parameters.
- Private-asset tests must `Assert.SkipUnless` when `fixtures/private/` is absent.
- Order file-system enumerations (`StringComparer.Ordinal`) before selecting
  entries.
- Verify with `dotnet test` from the repository root.
- Private corpus tests (`Private*`, `Phase3ContentCorpusTests`, `ModLoadingFixtureTests`) need `fixtures/private/` and `data/` and are slow; the fast check is the unit tests plus the remaining compatibility tests.
- Do not lower test strictness just because they fail, consider wheter it weakens the coverage first
- Run formatting and checksum tests before major branch push

## Documentation ownership

- Record each fact once and link to it elsewhere; do not copy paragraphs between documents.
- Phase 6 world work: subsystem slices, audits and evidence belong in the matching
  `docs/strategic-*-status.md`; cross-cutting checkpoints, current scope and remaining guards
  belong in the Progress section of the branch's delivery plan
  (`docs/phase-6-world-integration-plan.md` for branch 4, `docs/phase-6-interception-plan.md`
  for branch 5, `docs/phase-6-campaign-cycle-plan.md` for branch 6).
  `docs/phase-6-ownership.md` keeps the ownership and fixture ledger, and
  `docs/implementation-plan.md` keeps phase-level status; both link to those records.

## Completion standard

A subsystem is not complete because it compiles. It is complete when its agreed
fixtures pass, malformed inputs fail intentionally, relevant plan/matrix entries are
updated, and no compatibility behavior remains hidden in placeholder code.

A branch is not ready to merge while it still has named refusals (time or a command stopping
with a "requires ..." reason), unless a plan explicitly assigns the underlying work to a later
phase or branch. Follow the reference; where its behavior is a bug (a crash or undefined
behavior), choose a deterministic behavior and record it in an ADR. After the refusals are
resolved, audit the whole branch (`git diff main...HEAD`) before reporting it ready. The
maintainer pushes and merges branches; agents commit locally only.

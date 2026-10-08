# World-owned research consequences

Branch 4c; reference `4df3a5e571a1a4b5e8a46d3161fb2e21a2adba15`.

## Research diary persistence (2026-10-08)

The shared research capability owns ordered diary occurrences and a monotonic
identity sequence in save-neutral snapshots. The adapter reads and writes
`researchDiary` entries with native `name`, unsigned 32-bit `date` components,
byte `sourceType` and `sourceName`. Missing source rules retain their names;
unrecognized byte-valued source kinds also survive. Removed research topics
are skipped before reading their entry payload, matching `SavedGame::load`.
Historical diary entries do not themselves grant research.

Native enum/source/entry structs from `ResearchDiary.h` are inserted verbatim
into `strategic-research-diary`; typed YAML collaborators capture 24 source/date
combinations. The topic-lookup collaborator models the inspected outer load
filter in `SavedGame.cpp`. Fresh/cache tests canonically compare all records
through three rewrites, including duplicate topics and removed source names.
Stable per-occurrence port keys preserve surviving unknown fields without
letting new or deleted occurrences inherit old sidecars. Removed topics with
conflicting sidecar keys cannot block a valid survivor's rewrite.

Missing required fields, negative/out-of-width numeric values, duplicate keys,
identity exhaustion/inconsistent sequences and populations above 100,000 fail
intentionally. Invalid snapshot restoration preserves the supplied RNG state.
All 28 diary/world-persistence tests pass. Captures remain useful; the obsolete
native object was removed. This slice owns persistence; automatic and
mission-source diary/grant execution remain the next slices. World expiry
research and unavailable event payloads retain their existing guards.

Reference methods: `ResearchDiaryEntry::load/save/setDate`,
`SavedGame::load`'s research-diary topic filter and `addResearchDiaryEntry`.

The interim diary audit reproduced a removed-topic filtering-order defect:
an optional port key was read before the native topic filter. Removed records
with malformed optional keys now skip that payload on both load and rewrite,
while original slot indices and surviving occurrence identities remain stable.
The existing fresh/cache native comparison adds this case and passes three
rewrites; all 28 focused diary/world-persistence cases pass. The prior root
checkpoint passed 3,499 tests with no failures/skips. Final acceptance after
the correction also passes all 3,499 root tests, including native checksums,
private corpus and populated allocation gates. Full formatting verification,
code-map and diff checks pass. The reference checkout remains clean and the
empty successful format log was removed. This closes the persistence slice
and its interim audit; the two execution slices and full cycle audit resume
after refreshed quota. World-owned research execution remains guarded.

## Daily and automatic diary execution (2026-10-08)

The shared `addFinishedResearch` transition now appends FREE_AFTER entries for
new automatic discoveries, retaining the initial grant as their source even
through nested queues and repeatable grants. Daily completion records core and
lookup BASE entries before reward selection, then FREE_FROM reward/lookup
entries at their respective native grant positions. Daily source entries honor
debug mode; automatic entries use actual completion state. Arc grants add only
their automatic entries, as in the reference caller.

`strategic-research-diary-execution` inserts the native diary structs, daily
helper and grant blocks, automatic diary block and complete mission research
caller. Explicit queue/reward collaborators isolate 16 ordering, debug,
disabled-core, repeatable-core and automatic-lookup cases; they do not claim a
full native eligibility implementation. Daily fresh/cache comparisons preserve
the captured ordering through three reloads. Additional cases cover nested
sources and repeatable automatic occurrences.

Daily preflight uses the shared pure stock/discovery forecast to count entries
without RNG use. Ambiguous rewards reserve bounded direct/lookup/automatic
chains. Arc slot forecasts reserve automatic diary capacity separately from
score bounds. Population and identity exhaustion stop before the whole tick,
while debug completions requiring no diary entries can proceed with an exhausted
identity sequence. World mission-source grants and their call-site integration
remain the next execution slice.

Reference callers: `GeoscapeState::time1Day`, `SavedGame::addFinishedResearch`,
and `GeoscapeState::processArcScript`.

## Mission-source research operation (2026-10-08)

Registered research/world operations execute
`SavedGame::handleResearchUnlockedByMissions` semantics: first-base eligibility,
core then lookup grants, reward selection after those grants, reward/lookup
FREE_FROM sources, and immediate interruption for the explicit grant vector.
Disabled or previously completed explicit topics still interrupt matching
missions. Automatic discoveries do not join that vector. Mission diary checks
ignore debug mode. The operation leaves laboratory projects, primary item/event
effects and custom counters untouched.

The shared caller sequence drives both execution and a pure proof over cloned
research/status sets. Score prefixes, diary identities/population and bounded
discovery work are checked before mutation. Geography/function list sizes are
charged to automatic eligibility work. Ambiguous random rewards retain a
specific uncertainty guard until bounded outcome forecasts are available.
Missing linked research is a native no-op; the existing campaign contract still
rejects snapshots with no player base.

Fresh/cache tests compare all eight mission rows in the native execution oracle,
including diary order and the interruption vector, through three reloads.
Focused tests cover protected rewards exposed by the core grant, absence of
primary effects, disabled-topic interruption, atomic capacity/uncertainty guards
and overflowing score prefixes that later rewards would cancel. Site expiry
still stops before research while its multi-site/later-handler forecasts are
not yet connected. This kernel does not claim call-site closure.

Reference methods: `SavedGame::handleResearchUnlockedByMissions`,
`selectGetOneFree`, `addFinishedResearch`, and the caller in
`GeoscapeState::processMissionSite`.

The cycle audit confirmed that mission eligibility charged geography/function
work only after evaluating the expensive query. It now refuses a query before
that scan/union when the remaining budget cannot cover it. Cases with 32
regions, 256 areas each and 120 shared provided names allow 50 automatic
candidates and atomically refuse 100.
The daily ambiguous-reward bound also charges empty protected-group containers.
The initial batch acceptance passed all 3,515 root tests and full formatting;
final validation of the audit correction is recorded below.

All 18 focused mission/daily execution cases pass after the correction,
including both geography resource bounds. The resource fixture respects the
native 128-distinct-base-function limit. Changed-file formatting, code-map
and diff checks pass; the reference checkout remains clean. All three research
slices and their batch audit are committed separately. Site caller integration
and broader uncertainty composition remain open 4c work.

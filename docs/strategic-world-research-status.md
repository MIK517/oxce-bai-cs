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

## Bounded mission research alternatives (2026-10-08)

Mission grants now admit random rewards when every outcome has a bounded
proof. Ordered projections retain separate completed/status sets, score
prefixes, diary multiplicities and explicit interruption topics across grants.
They cap alternatives at 256 and shared work at one million; all copying and
summary unions/intersections are charged. Cached guaranteed/possible summaries
support later phase consumers without repeated scans. Forecasts neither draw
nor restore RNG. Real grants still select from the original ordered list,
including duplicate entries and protected groups.

The execution capture additionally inserts the complete native
`SavedGame::selectGetOneFree` method with controlled selection indices. Four
rows cover duplicate choices, disabled/discovered rewards, protected topics
and a repeatable core. The previous 16 caller rows remain identical. Fresh/cache
tests compare possible explicit topics and reload real grants from eight fixed
seeds per row. Unsafe score alternatives and a second 32-way grant exceeding
the outcome cap are refused before owned state changes. All 22 focused mission
and daily execution cases pass. This closes the operation's random-reward guard;
site and later-handler integration remain separate slices.

## Ordered saved-site research expiry (2026-10-08)

Saved mission sites now execute mission-source research before their despawn
counters and instant-event suppression checks, matching
`GeoscapeState::processMissionSite`. One ordered alternative forecast covers
all expiring sites and their combined score/diary capacity. Each site's event
uses its own post-grant guaranteed research; a later site's grant cannot
authorize an earlier unavailable payload. Held sites grant nothing. Actual
grants preserve first-base eligibility, laboratory projects and immediate
explicit-topic interruption. Reloading an expired site cannot repeat effects.

Fresh/cache composed cases compare all eight mission diary/interruption rows
from the native research capture at the real site caller and use the native
site timer evidence. Tests cover three reloads, shared two-site identity
exhaustion, interrupted versus unavailable instant events, and earlier-event
ordering. All 44 focused site-timer/site-research/mission-research cases pass.

This slice admits worlds without live UFOs, airborne craft, alien-base
generation or due mission waves. Those combinations retain named phase
forecast guards. Daily projects/construction and timed item-dependent automatic
eligibility also retain explicit guards. Newly generated research-bearing sites
require ordered birth/expiry forecasts. Scheduled-event countdown composition
is the next slice; unavailable payloads still stop before the entire tick.

## Site research and scheduled-event countdowns (2026-10-08)

Due and already-over scheduled events now use guaranteed research after every
saved-site grant. This matches `GeoscapeState::time30Minutes`'s sites-before-events
order and avoids reusing pre-site completion when a grant disables research.
Repeated interrupted instances are removed independently; generated history is
retained. Future events still count down when their payload remains unavailable,
then stop before the tick that would execute it.

Fresh/cache composed cases cover core and automatic interruption, repeated
instances, already-over timers and three rewrites. Disabled/repeatable cores
cannot authorize a payload. A grant that removes an existing interruption
blocks the entire tick; a 60-minute event remains allowed for one countdown and
blocks at its later horizon after reload. Native `strategic-event` timer evidence
and the research execution capture back these cases. All 33 focused site/event
cases pass. This does not execute event effects or remove other named research
phase guards.

The three-slice audit confirmed two resource/guard details. Unconditional
instant-event payloads now retain precedence over research/UFO composition
guards, preserving the dormant-UFO atomic boundary. Ordered projections charge
each grant's repeated mission-link validation/interruption passes and its
per-grant snapshot proof. A 1,001-mission case permits 100 no-op repeat grants
and refuses further work before the shared budget can be exceeded.

The initial root run passed 3,534 of 3,536 tests; both failures concerned exact
guard reasons. The dormant-UFO payload precedence was fixed. The generated-site
test now expects the intentional precise research-birth guard for its research
case and retains the payload guard for its event case; whole-tick state/RNG
assertions remain unchanged. All 102 audit regression cases pass. Full
formatting passed before the correction. Final root acceptance passes all
3,537 tests, with full formatting and code-map verification passing. One
intermediate root run exceeded the patrol allocation assertion; the identical
assertion passed in isolation, in all 23 craft regressions and in the complete
root recheck without changes to the test or allocation code.

## Site research and airborne craft (2026-10-08)

Saved-site research now composes with existing craft movement and fuel proofs.
`GeoscapeState::processMissionSite` returns forced-expiry followers before the
mission research caller; `time10Minutes` consumes fuel afterward. Neither
`Craft::setDestination/consumeFuel/think` nor their registered port transitions
consult research eligibility. The existing follower return, fuel, escort and
landing forecasts therefore remain the owners of those transitions.

Native site-timer and research execution captures back fresh/cache follower
cases, three rewrites, independent atomic landing guards and 72 hours of patrol
with daily reloads. Ordinary followers hold the site without granting research;
forced expiry grants once, repairs the destination and uses return speed for
fuel consumption. All 37 focused site/craft/timer tests pass. Timed item
eligibility, UFOs, actual base generation and newborn sites remain separate
composition boundaries.

## Site research and quiet alien bases (2026-10-08)

Site research now admits alien bases without possible daily or hunting mission
generation on the affected tick. The proof bounds deployment/timeline scans;
zero daily frequency, exhausted generation limits, disabled detection and
hunting cooldowns can establish safety. Positive nonempty eligible generation
choices retain the named composition boundary. Existing base proofs still own
malformed timers, weights, activity, discovery and deletion.

`GeoscapeState::time1Day` performs base self-destruction/activity/supply before
`processMissionSite` grants research. Fresh/cache cases preserve that ordering:
a new site grant cannot destroy today's base; the next daily pass deletes it.
Native activity, cleanup, hunting-timer and mission research captures back the
composed cases, including three rewrites and atomic generation refusal. All
101 focused site/base and existing activity/cleanup/hunting tests pass.

## Site research after daily facility completion (2026-10-08)

The mission research projection receives the highest tick trigger explicitly.
At a daily boundary it includes first-base facilities with one day remaining;
other callers use completed facilities only. It shares the existing ordered
country/region and facility function check, including disabled facilities, and
retains its geography/list work reservation before eligibility scans. Actual
grants validate the state after construction, using the default current-state
context.

Reference: `GeoscapeState::time1Day` construction before research/world handlers,
`Base::getProvidedBaseFunc` and `SavedGame::addFinishedResearch` eligibility.
Native research production/execution captures back fresh/cache cases for zero,
one and two-day construction, non-daily exclusion, unchanged forecast state,
score/diary results and three rewrites. A due event is interrupted only when the
automatic research is eligible; otherwise the whole tick remains untouched.
All 34 focused facility/mission-research/event cases pass, including the existing
bounded geography and alternative-outcome cases. Active daily research projects
and timed item eligibility retain their separate composition guards.

The craft/base/facility batch audit found no confirmed gameplay defect in these
three slices. Additional regressions prove that facility-enabled automatic
research score and diary overflow stop before construction, even when a
current-state grant alone would fit. The 159 focused regressions and all 3,567
root tests pass. Full formatting passes after normalizing the new base test's
line endings; the initial focused invocation used an invalid wildcard and was
rerun with valid filters. Code-map verification passes and the reference tree
remains unchanged. Generated native captures remain useful; empty successful
format logs were removed.

## Site research before ordinary UFO flight (2026-10-08)

Ordinary UFO flight now uses guaranteed explicit mission-source interruption
after the site pass. Earlier mission think and detection retain their native
pre-site state. Disabled explicit topics still interrupt their matching
missions; automatic discoveries do not enter that immediate topic vector.
Uncertain explicit interruption retains a named whole-tick boundary, and
hunter/escort and dormant reactivation composition remain separate guards.
UFO/mission lookup work is bounded before visiting links.

Reference: `SavedGame::handleResearchUnlockedByMissions`'s explicit vector,
`AlienMission::ufoReachedWaypoint`'s interruption-first branch and
`GeoscapeState::time30Minutes/time5Seconds` caller order. Native lifecycle and
mission-research captures back fresh/cache arrival cases, including disabled
cores, automatic research exclusion, three rewrites, delayed UFO cleanup and
uncertain reward refusal before RNG/state changes. All 16 focused site/UFO and
native lifecycle trace regressions pass.

## Safe uncertain transit and detection caller order (2026-10-08)

Random mission research may execute while an ordinary UFO cannot reach its
waypoint in the affected flight pass. `AlienMission::ufoReachedWaypoint` reads
interruption at arrival; transit and `ufoLifting` do not. The bounded proof now
checks post-site timed UFO state before retaining the uncertain-arrival guard.
After committing a safe transit's selected outcome, later ticks project its
actual saved mission state normally.

Actual detection VM cases use `SavedGame::isResearchedScript` through
`detectUfoFromBase`: the same-tick half-hour pass sees pre-site research, and the
next detection pass sees the committed grant. Eight explicit saved RNG seeds
exercise both reward/interruption outcomes in fresh/cache content with reloads.
Native detection, mission research and lifecycle captures back these composed
cases. All 17 focused transit/arrival/research-detection tests pass; uncertain
arrivals still stop before RNG or campaign mutation.

## Research-bearing dormant site reactivation (2026-10-08)

Research-bearing sites now use the existing ordered dormant UFO lift forecasts.
`processMissionSite` grants research, updates counters/events/activity and then
calls `AlienMission::ufoLifting`; interruption affects the later waypoint arrival
rather than preventing the lift. Dormant contacts still miss the earlier radar
pass. Existing lift-speed, follower repair, shield and combined activity checks
remain authoritative before the tick mutates.

Native site-lift/lifecycle and mission-research captures back fresh/cache cases
for lift-plus-transit and lift-plus-interrupted departure, three rewrites,
retained ground timer/landing identity, shield initialization and later cleanup.
A combined nine-point site/lift overflow and an unconditional instant event both
stop before the research grant or reactivation. All 38 focused site/reactivation
and arrival regressions pass. Hunter/escort, generated actors and daily project/
item eligibility still retain separate composition boundaries.

The UFO/transit/reactivation batch audit found two corrections. Repeated
post-site UFO forecasts rescanned each site's followers, allowing cubic work
behind a limit that reserved only one site/follower pass. Expiry eligibility is
now cached by site identity and trigger for one preflight pass, then cleared on
every retry/tick. No handler consumes the cache. Populated 8/32/128 cases verify
successful grants/reactivation and bounded allocation growth; the allocation
assertion also passed before the cache, so it is not evidence of an allocation
regression. The correction removes the repeated follower work.

A fresh/cache regression reproduced a false uncertainty guard after guaranteed
daily interruption. A later site can disable/reenable the prerequisite and
randomly grant it again; the earlier mission interruption remains permanent.
The arrival check now uses the ordered mission forecast and reserves its base
link work. Reset regressions also exercise dispatch adding a follower after a
blocked tick and patrol removing one before the same later trigger. All 100
focused audit cases pass; full formatting passes. Root acceptance follows.
Final UFO/research cycle acceptance: all 3,587 root tests, full formatting and code-map verification pass. The reference checkout is unchanged; no unexpected access denials occurred.

## Site research before idle hunter selection (2026-10-08)

Idle hunter-killers can now select craft after site grants. The uncertain flight
check runs after the existing hunting/target projection, so a hunter redirected
away from its original waypoint does not falsely require an ordinary arrival
outcome. `GeoscapeState::time10Minutes` retargeting follows sites and fuel;
`AlienMission::ufoReachedWaypoint` applies only to the ordinary arrival path.
Native hunting and mission research captures back deterministic and uncertain
fresh/cache cases, three rewrites, continued hunting after interruption and
retargeted speed/destination. All 20 focused hunting/site/arrival cases pass.
Active hunts, escorts and forced site-return fuel projection remain the next
parts of this cycle; combat boundaries stay in branch 5.

## Site research during active hunting (2026-10-08)

Active hunts now compose with saved-site research. Craft forecasts return forced
site followers, and followers of bases removed by earlier daily research, before
ten-minute fuel consumption and hunter selection. The projection preserves
saved craft order and charges the target scans against the hunting work budget.
Reference: GeoscapeState::processMissionSite, time10Minutes and
ufoHuntingAndEscorting; Craft::returnToBase/consumeFuel. Native site-timer,
hunting and mission-research captures back fresh/cache low-fuel target loss,
continued pursuit, three rewrites and an atomic branch-5 combat refusal. All
485 focused hunting/site cases pass. Escort composition is the next slice.

## Site research with UFO escorts (2026-10-08)

Saved escorts no longer use the generic UFO/research composition guard.
GeoscapeState::ufoHuntingAndEscorting selects leaders without consulting mission
interruption; time5Seconds moves escorts but applies interruption to ordinary
arrivals. Existing ordered flight projections therefore remain authoritative.
Fresh/cache cases compare chain, ring, self-target and terminal-leader positions
against every captured strategic-world escort trace after the site grants CORE.
They also verify idle selection, stale unique identity repair, next-tick terminal
cleanup and repeated rewrites. All six composed escort cases pass. Due/newborn
waves, base generation and daily/item research composition remain explicit work.

The hunting/escort batch audit reviewed native phase order, existing combat
refusal, selected-speed retargeting, saved craft order, target identity repair,
terminal fallthrough and bounded follower/escort scans. Six added fresh/cache
regressions verify that a ten-minute tick cannot
apply future half-hour expiry, and that earlier daily base deletion returns
followers before fuel and hunter eligibility. All ten focused return cases pass.
The initial root run passes all 3,607 tests. A subsequent native-source check and
four failing fresh/cache regressions confirmed that grounded followers were
incorrectly launched after base deletion, or rejected before forced site expiry.
Craft::returnToBase/setDestination preserves status while setting speed and the
takeoff timer. Both return handlers now preserve that status; hunting forecasts
apply the return before testing airborne status and consume fuel only in STR_OUT.
All 44 immediate audit-fix cases pass. Final affected-subsystem checks are
recorded below; a full root rerun after this late fix remains the next checkpoint.
Final audit fix acceptance: all 209 affected compatibility tests pass; full formatting and regenerated code-map verification pass. The filtered root invocation selected zero unit tests (exit 8), so the clean affected-subsystem run uses the compatibility project explicitly. A full root rerun after the fix is pending at the 07:30 cutoff.

## Deterministic daily research before site grants (2026-10-08)

The shared daily research proof now publishes immutable post-daily statuses,
remaining project identities, score change and work alongside discoveries,
stock changes and diary count. Later site grants start from that complete
prefix, preserving daily disables/reenables and active-project exclusion and
reserving combined score and diary capacity before the tick mutates.
Ambiguous daily rewards keep a precise deterministic-prefix boundary.
Reference: GeoscapeState::time1Day construction/research/project cleanup before
processMissionSite; SavedGame::addFinishedResearch and
handleResearchUnlockedByMissions. Existing native daily, mission and hunting
captures back fresh/cache unfinished/completed projects, disable/reenable,
combined overflow, prefix-versus-execution and three-rewrite cases. All 20
focused cases and the preceding affected site/diary suite pass. Timed item
eligibility, generated actors and later event payloads retain their boundaries.
The pending prior-cycle full acceptance also passes all 3,611 root tests.

## Timed item eligibility at site research (2026-10-08)

Automatic mission-source research now reads first-base item stocks after earlier
hourly maintenance, transfers, production and half-hour refuelling. The existing
bounded craft/production stock proof has a site-caller endpoint before follower
return, fuel and hunter retargeting; normal hunting callers retain their endpoint.
Daily refunds and removed projects flow through the same prefix. Item-presence
proofs accept positive stock in every ammunition-saving outcome and refuse a
zero-versus-positive ambiguity without choosing RNG. Unavailable production or
maintenance effects retain their existing precise boundaries.
Reference: GeoscapeState::time1Hour/time30Minutes/processMissionSite,
Craft::rearm/refuel, CraftWeapon::rearm, Base::removeResearch and
SavedGame::handleResearchUnlockedByMissions. Native hunting/mission captures
back 20 fresh/cache stock and ammunition-saving cases with three rewrites.
All 40 daily/item cases and all 599 shared hunting/site regressions pass.

## Counter-only waves before site research (2026-10-08)

Bounded no-object score waves now advance before saved-site research instead of
using the blanket due-wave composition boundary. Every immediate step is checked
through the existing counter-only capability and aggregate chain limit. Actor
births and other objective transitions retain the precise wave boundary.
Reference: AlienMission::think/spawnUfo and GeoscapeState::time30Minutes mission
think/cleanup before processMissionSite. The strategic-wave-chain capture backs
fresh/cache first-call counter and RNG comparisons for all five no-object chain
shapes, later explicit interruption, completed mission removal, repeated reloads
and unchanged intentional excessive-chain rejection. All 141 wave/site cases
pass. The following batch audit covers this and the daily/item slices together.

The daily/item/counter-wave batch audit found no confirmed defect. It reviewed
daily project cleanup and disable ordering, post-maintenance item presence,
private forecast state, wave overrides, RNG ownership, event interruption and
combined diary/score/work limits. Root `dotnet test` passes all 3,657 tests with
zero failures/skips; full formatting verification passes. Existing save and
native-checksum tests are included. Actor births, other objectives, generation
and uncertain daily rewards remain explicit composition work; 4d is untouched.

## Daily delayed generation before site research (2026-10-08)

Delayed missions generated by surviving alien bases now precede saved-site
research without the blanket generation boundary. Their entire initial timer
range must remain beyond the creation-tick mission think threshold. Every
reachable generated interruption rule is resolved before mutation, and each
site grant reserves validation/interruption work for the maximum enlarged
mission list. Other generation phases retain their precise boundary.
Reference: GeoscapeState::GenerateSupplyMission/time1Day/time30Minutes and
SavedGame::handleResearchUnlockedByMissions. The existing native generation
and diary captures back fresh/cache creation-before-interruption, exact draw
counts, repeated rewrites, next-pass cleanup, invalid links and exhausted-work
regressions. The older generation refusal case now explicitly creates an
immediate actor; it continues to assert whole-tick atomic refusal.

Daily counter-only score/supply creation now uses the existing bounded immediate
chain proof before site research. Every reachable wave must advance without
an actor or objective transition; finite zero-timer chains retain native order
and draws, and oversized chains stop before any daily mutation. The native
generation and wave-chain captures back fresh/cache timer alternatives, five
immediate steps, cleanup, interruption and reload cases. All 21 generation/base
research tests pass. Hunting generation and actor/objective births remain open.

Late hunting generation now shares the delayed/counter-only creation proof.
It remains after site grants and therefore neither inherits their earlier
explicit interruption nor enlarges their earlier research scans. Native
GeoscapeState::baseHunting/start/think and the hunt-generation capture back
fresh/cache delayed, short and immediate timers, completed-mission cleanup,
exact draws and repeated reloads. A combined midnight case verifies daily and
hunting births from one source on opposite sides of the same site grant.
All 71 affected generation/base/hunting tests pass. Immediate actor/objective
generation and newborn-site grant ordering remain explicit work.

The generation batch audit confirmed one consumed-link over-restriction:
immediate counter-only missions that are guaranteed to finish and be removed
before site grants were still resolving unused interruption research. Two
fresh/cache regressions failed before the fix. The shared proof now retains
the final counter result and the maximum initial timer, resolving links and
reserving new-mission scans only when a daily birth can survive to the site
caller. Late hunting births remain after that caller. Four additional cases
verify that incomplete counters or timer alternatives still resolve malformed
links atomically. These rules use independent identities because native reverse
file ordering overwrites a seeded wave override with the later fixture wave.
Reference: GeoscapeState::time30Minutes finished-mission deletion before
processMissionSite; SavedGame::handleResearchUnlockedByMissions active-list scan.

Final generation-cycle acceptance passes all 3,684 root tests with no failures
or skips, including native checksums, private corpus and allocation checks.
Full formatting verification, regenerated code-map and diff checks pass.
The audit reviewed caller order, counter/timer alternatives, survivor link
validation, enlarged research scans, mission ID reservations and later event
guards. No further confirmed defect was found; generated captures remain
useful and no new native objects were produced.

## Counter-only objective prefixes before site research (2026-10-08)

Saved mission waves now compose across every objective when their due chain
changes only counters or no-target retaliation termination. Final base and
infiltration transitions and actor births retain the wave boundary; instant
retaliation with a player target retains the branch 5 defense boundary.
Retaliation candidate/cleanup work is reserved before classification.
Reference: AlienMission::think/spawnUfo objective completion and no-target
termination; GeoscapeState::time30Minutes mission cleanup before site grants.
Existing native wave-chain/diary fixtures back fresh/cache checks for all seven
objectives, timer draws, subsequent explicit interruption, no pact/base birth,
three reloads and next-pass removal. Finite infiltration is selected explicitly
so final-transition tests do not instead exercise the endless-zero-chain limit.
All 41 affected objective/wave/generation research tests pass.

Daily generated base, infiltration and site objectives now share the same
counter-only prefix proof. It explicitly rejects final base/infiltration
transitions and actor/direct-site births. Site objectives still select their
initial area at native creation, even when the opening wave has no objective
flag; that identity and its draw persist across reloads. Reference:
GeoscapeState::GenerateSupplyMission and AlienMission::start/think. Native
generation/wave captures back fresh/cache counter/timer, selected area, source
link, later interruption, no pact/base birth and three-rewrite checks. All 41
affected objective/generation research tests pass; late non-score/supply
prefixes and generated retaliation retain their explicit boundaries.

Late hunting-generated base/infiltration/site prefixes now use that same proof,
with their own caller acceptance. They remain uninterrupted by the preceding
site grant and retain selected areas and source links after reload. Final
objective effects still stop atomically. Six 72-hour fresh/cache scenarios
compare day-sized advancement with hourly advancement and daily rewrites;
repeated hunting births, later daily interruption and cleanup remain identical,
with bounded mission populations and no duplicate diary, pact, base or UFO
effects. Craft destinations use a real persisted waypoint so unrelated patrol
discovery does not add RNG to the caller comparison. All 49 affected
objective/generation research tests pass. Reference: GeoscapeState::baseHunting,
AlienMission::start/think and daily interruption before generation.

The objective-prefix batch audit confirmed the saved-mission counterpart of
the consumed-link issue. Six failing fresh/cache score/site/supply cases showed
that research validation was still inspecting missions removed by the preceding
half-hour cleanup. The world now supplies proven saved removals to the research
capability; default immediate callers still validate their actual mission list.
Proof work includes removal membership and original list traversal, while new
daily survivors retain their enlarged-scan reservation. Surviving malformed
prefixes still stop atomically. All 57 affected research/generation cases pass.
Reference: GeoscapeState::time30Minutes cleanup and SavedGame's active-mission
scan in handleResearchUnlockedByMissions. No event/combat/month boundary changes.

The objective-prefix batch acceptance passes all 3,714 root tests, including
private corpus and checksum checks, with no failures or skips. Full formatting
verification and regenerated code-map validation pass. The audit reviewed
saved removal proofs, daily and hunting caller order, final objective guards,
mission scan reservations and 72-hour reload equivalence. No additional
confirmed defect was found.

## Saved ordinary UFO births before site research (2026-10-08)

Saved airborne ordinary waves now compose before expiring-site grants when the
existing spawn geometry proves the newborn cannot reach its destination in the
current tick. Hunter/escort creation and final base/infiltration effects retain
their precise composition boundaries. Source, Cartesian geometry and land
selection work are reserved before the birth proof; no point or RNG outcome is
selected by preflight. Completed wave counters no longer imply removal when a
new UFO keeps its mission alive for the later interruption scan.
Reference: AlienMission::think/spawnUfo, GeoscapeState::time30Minutes mission
cleanup/detection before processMissionSite, and SavedGame's explicit research
interruption. Existing verified spawnPathRows and diary captures back fresh/cache
birth position, timer/draw order, completed-wave survival, malformed surviving
links, interruption and three save rewrites. All 30 affected birth/objective/spawn
path tests pass; the new six cases first failed at the previous composition guard.

Grounded ordinary births whose ground timer extends beyond this tick now use
that same proof. Site grants can interrupt their surviving mission without
consuming the newborn's timer or allocating a marker for an undetected ship.
The next five-second tick lifts the ship through the existing owned operation.
Reference: AlienMission::spawnUfo/ufoLifting and GeoscapeState's half-hour
detection before site expiry and subsequent five-second UFO handling. Verified
spawnPathRows, diary and timer captures cover both destination altitudes, final
and prefix wave counters, native activity plus despawn penalty, timer/marker
rewrites and atomic exhausted-marker refusal. All 29 affected tests pass.

One-tick ordinary ground starts now compose too: detection runs before the site
grant, then the owned lifting handler runs after it. AlienMission::ufoLifting
does not inspect interruption, so an explicitly interrupted newborn may still
lift safely in this tick. Native normalLiftRows/spawnPathRows and
UfoDetectedState's contact/landing ID allocation back immediate lift activity,
timer and marker persistence. A real detectUfoFromBase VM program sees CORE
absent at birth detection, then present at the next half-hour after save/reload.
All 28 affected birth/detection/spawn-path tests pass; the six new cases first
failed at the prior composition guard. Hunters, escorts and objective effects
remain separately guarded, without changing event or combat boundaries.

The birth batch audit adds fresh/cache geometry-limit and remaining-hunter
boundary checks. It also replaces the old blanket actor-chain refusal with
acceptance for the now-owned finite three-UFO chain, including interruption,
surviving live counts and reloads; excessive chains still fail before mutation.
All 352 affected research/spawn/escort tests pass. Review covered removal masks,
native detection/site/lift ordering, strict first-destination distance, source
and geometry work reservations, marker/activity limits and late event guards.
No implementation defect was confirmed by this audit.

Full batch acceptance passes all 3,736 root tests with no failures or skips,
including checksums, private corpus and allocation coverage. Formatting,
regenerated code-map validation and diff checks pass. No new native artifacts
were produced; existing captures and ignored validation logs remain useful.

## Generated ordinary births and site research (2026-10-08)

Daily generated airborne ordinary waves now compose with site grants through
the existing prepared region/race/area alternatives. The research proof checks
every reachable candidate with bounded scans and birth geometry, preserves
final base/infiltration guards and reserves surviving generated missions in the
later interruption scan. A final-wave UFO keeps an invalid interruption link
reachable, so the whole tick still stops before creation. The old actor refusal
fixture now names the remaining hunter boundary explicitly.
Reference: GeoscapeState::GenerateSupplyMission/time30Minutes and
AlienMission::start/think/spawnUfo, including the Earth-operation source override.
Verified generation call traces, spawnPathRows and diary captures back fresh/cache
initial/next timer draws, source linkage, birth movement, interruption and three
rewrites. All 47 affected generation/objective/base research tests pass; the six
new birth cases first failed at the previous generation composition boundary.

Late hunting-generated ordinary airborne births now compose with saved-site
grants. Their shape excludes hunters/escorts and final objective effects; the
owning later generated-wave preflight still validates every reachable region,
race, area and first-destination geometry before mutation. Since this caller
follows site grants, these new missions neither receive the earlier explicit
interruption nor enlarge its link scan. Reference: GeoscapeState::baseHunting
and AlienMission::start/think/spawnUfo. Verified hunting generation calls and
spawn/diary captures back fresh/cache birth movement, native RNG order, final
and prefix counters, source links and three rewrites. All 30 affected tests
pass; the four new late-birth cases first failed at the previous boundary.

Daily and late generated ordinary ground starts now share the owned birth/lift
proof. Native ground timers extending past this tick persist before later
takeoff; one-tick starts lift after site processing without consulting mission
interruption. Half-hour birth activity/detection applies only to daily births;
late births still receive any five-second lift award. Reference:
AlienMission::spawnUfo/ufoLifting and the two GeoscapeState generation callers.
Verified generation, spawnPathRows and normalLiftRows cover both timers and
callers, source coordinates, native draw order, interruption, combined activity,
next-tick lift and three rewrites. All 46 affected birth/base research tests
pass; four new grounded cases first failed at the previous composition guard.

The generated-birth batch audit passes all 370 affected research/spawn/escort
tests. Same-source daily and hunting births retain separate mission/UFO
identities and opposite interruption states through repeated rewrites. Two
72-hour fresh/cache scenarios compare daily batches with hourly advancement
and daily reloads, including later interruption, cleanup, bounded populations
and intact UFO-to-mission links. Review covered all prepared alternatives,
consumed-only link validation, source matching and work limits, native caller
order and later event/combat/objective guards. No runtime defect was confirmed.

Full generated-birth acceptance passes all 3,754 root tests, with no failures
or skips, plus full formatting and regenerated code-map verification. Existing
captures and ignored validation logs remain useful; no native objects were added.

## Newborn hunters and site research (2026-10-08)

Saved non-escort hunter births now compose when the existing bounded craft
eligibility proof establishes that no craft can become a target this tick.
The ordinary spawn predicate still proves strict destination separation;
targetable births retain the research composition boundary. Reference:
Ufo::Ufo constructor selection, AlienMission::spawnUfo and GeoscapeState's
ten-minute hunter retargeting. Verified constructorRows/spawnPathRows back
fresh/cache fixed and random hunt modes, native draw/timer order, waypoint
movement, later explicit mission interruption and three rewrites. All 106
affected birth/hunting tests pass; six new cases first failed at the prior
wave composition boundary. Generated hunters remain the next caller slice.

Saved grounded non-escort hunters now compose even with selectable craft.
GeoscapeState::time10Minutes skips landed UFOs, and lifting follows sites and
retargeting through the owned five-second handler. Both extended and one-tick
ground timers preserve constructor state and waypoint identity; no hunt starts
at birth. Verified hunting/lifecycle captures and a no-site control confirm
unchanged UFO/RNG outcomes except for the later mission interruption and grant.
Fresh/cache reload checks and retained airborne targetable refusals pass all 36
affected tests; four new cases first failed at the old composition boundary.

Both generation callers now accept those same safe non-escort hunter shapes:
daily targetless airborne births and daily/late grounded starts. Daily births
still receive the later explicit site interruption; late births do not.
The owning generated-wave and hunting preflights retain race/stat, geometry,
target selection and combat checks before mutation. Reference:
GeoscapeState::GenerateSupplyMission/baseHunting/time10Minutes and
Ufo::Ufo/AlienMission::spawnUfo. Verified constructor/generation/lifecycle
captures and no-site controls cover caller-specific UFO/RNG outcomes, timers,
source links and reloads. All 42 affected tests pass; four new generated-hunter
cases first failed at the prior boundary. The old remaining-generation fixture
now explicitly covers escorts; targetable airborne hunters remain guarded.

The safe-hunter batch audit passes all 462 affected research/birth/hunting/
escort tests. A later retarget regression confirms that a permitted grounded
birth still stops atomically before combat after lifting. Review covered native
constructor order, no-target proof reuse, landed-versus-flying retarget phases,
source/race alternatives, first-destination separation, persisted mission links
and deferred event/objective effects. No implementation defect was confirmed.

Final safe-hunter acceptance passes all 3,768 root tests with no failures or
skips, including checksum, private-corpus and allocation checks. Full formatting,
regenerated code-map and diff verification pass. Existing captures remain useful;
no generated native objects were added. This closes the sixth daytime cycle,
not the remaining 4c research-composition and headless acceptance work.

## Targetable newborn hunting across site research (2026-10-08)

Saved non-escort airborne hunter births now reserve their geometry and counters
before the site-grant forecast, then defer target-selection proof to the existing
mission preflight after that forecast. Every phase still validates before tick
mutation. GeoscapeState::time30Minutes spawns before processMissionSite;
time10Minutes/ufoHuntingAndEscorting retarget after sites without inspecting
mission interruption. AlienMission::spawnUfo and Ufo::Ufo retain their existing
constructor and waypoint semantics. Combat remains a precise branch 5 boundary.

Verified strategic-hunting constructor/retarget rows, world-lifecycle spawning
and research-diary execution back six fresh/cache fixed/random-mode scenarios.
No-site controls compare identical UFO/RNG outcomes; grants interrupt surviving
missions, pursuit continues after reload, and nearby targets refuse the whole
tick before grants or birth. All 116 affected spawning/newborn-hunting tests pass.
Generated targetable hunters and escorts remain the next caller slices.

Daily and late base-hunting generation now use the same deferred retarget proof
for airborne hunters. Daily alternatives validate geometry before site grants;
late alternatives retain their later creation preflight. Native
GenerateSupplyMission/baseHunting/time10Minutes ordering preserves interruption
of daily missions only. Eight fresh/cache final/nonfinal scenarios compare
constructor-backed hunting, source and craft identities, no-site UFO/RNG control
outcomes, grant history and transition reloads. Pursuit runs only to a horizon
before its expected interception. All 56 affected tests pass. Escorts remain
guarded pending their separate leader-origin composition slice.

Saved and generated escorts now pass the same early shape/geometry reservation;
their existing later preflight validates leader origins, initialized escort speed,
craft alternatives, identities and bounded selection/movement work. Native
AlienMission::spawnUfo relocates to the first ordinary same-mission UFO before
the later retarget caller; site interruption does not cancel that caller.
Twelve fresh/cache fixed/random-mode saved scenarios and four generated cases
compare native selection rows, original-waypoint backup, no-site UFO/RNG controls,
grant interruption and pursuit reloads. The obsolete generation refusal fixture
now supplies noncolliding mission IDs and an eligible hunting craft, so it tests
actual acceptance rather than failing an unrelated precondition. The other 482
affected tests pass, and all 10 base/research fixture tests pass after correction.
The three slices are ready for their joint audit; no combat boundary is removed.

The targetable-hunter/escort batch audit confirms native birth/grant/retarget
ordering, deferred proof consumption, saved and newborn leader alternatives,
marker reservations and retained objective/event/combat guards. Four added
fresh/cache audit cases cover all four constructor combinations over 32 fixed
seeds, same-wave leader reuse, exact no-site RNG/UFO comparisons and reloads,
plus atomic leader-origin combat and marker exhaustion refusals. All 488 affected
tests pass. No additional runtime defect was confirmed.

The committed implementation passes all 3,798 root tests with zero failures or
skips, including checksum, private-corpus and populated allocation gates. Full
formatting verification passes; added audit tests pass after focused formatting.
Code-map and diff checks pass. Existing captures remain useful, no native objects
were added and the pinned reference remains read-only. Objective/newborn-site
composition, remaining uncertainty and final headless closure keep 4c open.

## Final objectives across site research (2026-10-08)

Saved final base/infiltration waves now pass the early research composition
scan; existing mission preflight reserves all base/pact effects before mutation.
Native AlienMission::think completes the objective after advancing counters,
and GeoscapeState::time30Minutes removes finished missions before processing
sites. Removed missions therefore do not receive the later explicit grant or
consume interruption-rule links. New bases and pending pacts persist normally.

Verified objective-base-spawn, infiltration-region and wave-chain evidence backs
fresh/cache before/after reloads, no-site base/pact/RNG controls, unused missing
interruption links and atomic marker exhaustion. Site activity penalties are
checked separately from pact flags. All 75 affected tests pass, followed by all
24 expanded objective/research boundary tests. Generated completions remain
the next two caller slices; event payloads and combat/month guards are unchanged.

Daily generated final base/infiltration completions now compose before the grants
with their owned candidate/geometry reservations. Native GenerateSupplyMission
and the following time30Minutes think/cleanup callers preserve source counters,
new base/pact state and unused interruption-link filtering. Fresh/cache controls,
three rewrites and marker-limit refusals cover both objectives.

Precommit review reproduced an endless-infiltration survivor error: completion
restarts the mission, so excluding it from the later grant scan allowed an invalid
link to throw during execution. The removal proof now excludes endless restart.
Verified infiltration-repeat regressions require atomic missing-link refusal and
valid restart/interruption/reload/next-half-hour cleanup. All 45 affected generated
objective, generation and infiltration-repeat tests pass. Late completions remain
the third caller slice; no event payload or later-branch transition is enabled.

Late base-hunting final objectives now compose after the saved-site grants.
GeoscapeState::baseHunting appends and thinks the mission after processMissionSite;
it does not run the earlier half-hour cleanup again. New finished missions retain
their un-interrupted state until the next cleanup, and endless infiltration
restarts without receiving the earlier explicit grant. The later objective owner
still proves marker/population/birth-sequence capacity before tick mutation.
Fresh/cache native generation/objective/infiltration-repeat evidence backs
no-site base/RNG controls, three rewrites, next-boundary deletion, unused earlier
grant links and atomic marker/birth-sequence exhaustion. All 62 affected tests
pass. The three objective slices are ready for their joint audit.

The final-objective batch audit confirms completion/restart counters, earlier
cleanup versus late persistence, consumed interruption links, newborn base/pact
reservations and later hunting capacity. Existing restore normalization clears
stale craft destinations before newborn identities can be created. No additional
runtime defect was confirmed. The precommit endless-survivor correction is covered
by both missing-link atomicity and valid restart/cleanup regressions.

All 3,826 root tests pass with zero failures/skips, including native checksums,
private corpus and populated allocation gates. Full formatting, code-map and diff
verification pass. The reference remains clean at the pin; useful captures remain
and no native objects were generated. Newborn-site and generated-retaliation
research composition, remaining uncertainty and final headless closure keep 4c
open. Branch 4d and later-branch transitions are unchanged.

## Direct site births across research (2026-10-08)

Saved direct-site wave chains with positive minimum duration now compose before
the saved-site grants. AlienMission::think/spawnMissionSite appends the sites
before GeoscapeState::time30Minutes processes them; their positive timers prevent
a birth-tick grant. The composition proof bounds reachable deployment lookups
without drawing RNG; the owning site preflight retains geometry, marker,
population and duration validation. Final direct-only missions leave the later
interruption scan after cleanup; unfinished chains receive the saved-site grant.

Verified site-spawn, site-timer and diary evidence backs fresh/cache controls,
three rewrites, final/prefix chains, atomic immediate-expiry and marker-capacity
refusals, and later grants. The longer scenario checks native hourly discovery's
staggered expiry postponement and exactly one diary entry per topic. All 340
affected tests pass. Daily and late generated direct births are the next slices;
immediate appended-site research still needs an ordered expiry forecast.

Daily generated direct births now use every prepared region/race/area alternative
to prove the same positive-duration boundary. GenerateSupplyMission runs before
the half-hour think, cleanup and site caller. The proof now distinguishes UFO
survivors from direct sites, so a completed direct-only mission does not consume
an unused interruption link, while a retained prefix or actual UFO birth still
participates in the grant scan. No random selection occurs during this proof.

All 76 affected tests pass. Fresh/cache cases cover deployment overrides and site
objectives, two-site immediate chains, final cleanup with undefined unused links,
retained-prefix interruption, exact no-site site/RNG controls, generation counters,
three rewrites and atomic immediate-expiry/marker-limit refusals. Late hunting
births remain the third caller slice.

Late hunting direct births now pass the research composition scan, including
zero-duration sites. Native GeoscapeState::baseHunting thinks and appends them
after processMissionSite, so they retain the full initial timer and do not grant
research in the birth tick. The later generated-wave/site owner still validates
all geometry, duration and capacity alternatives before mutation.

All 48 affected tests pass. Fresh/cache cases compare exact site/RNG state with
no-site controls, keep completed late missions until next cleanup, verify that
retained prefixes do not receive the earlier grant, persist three rewrites and
check the next half-hour's positive countdown or zero-duration grant/removal.
Marker exhaustion remains atomic. These three direct-site slices are ready for
their joint audit; earlier zero-duration grant composition remains unavailable.

The direct-site batch audit confirms duration units, before/after-site caller
order, daily removal versus UFO survival, deferred validation consumption,
unknown interruption links and birth/expiry identity reservations. No additional
runtime defect was confirmed. All 3,844 root tests pass with zero failures/skips,
including checksums, private corpus and populated allocation gates. Full
formatting, code-map and diff verification pass. The reference remains clean at
the pin; useful captures remain and no native objects were generated. Immediate
appended-site research, generated retaliation and uncertainty reconciliation
still keep the headless gate open.

## Ordered newborn site research (2026-10-08)

Saved deterministic zero-duration births now append their grant forecasts after
all saved-site grants, in mission/wave order. A per-birth proof supplies the exact
post-grant state to direct-site event validation; later flight consumes the final
research forecast. Births can initiate this proof even without a saved site.
This follows AlienMission::spawnMissionSite's append and
GeoscapeState::processMissionSite's grant-before-counters/event/removal order.
No RNG or allocated target identity is used by the proof.

Fresh/cache final/prefix chains cover two births, with/without saved grants,
explicit interruption, three rewrites and no duplicate diary effects. The former
zero-hour research refusal now checks execution, counters and reload; unavailable
instant payloads still stop atomically. Diary/marker exhaustion and invalid active
mission links remain guarded. Precommit review also excludes finished missions
with surviving UFOs from the new birth scan, with a focused regression. All 362
affected tests pass. Daily immediate births and stochastic deployment/expiry
alternatives remain the next slices.

Daily deterministic immediate births now append after saved-mission births and
all saved sites. Prepared region/race/area alternatives must have identical
deployment and wave/counter grant slots; each selected birth is forecast once,
with an exact proof for every candidate. Generation must be certain and its
initial timer always due. Optional generation and divergent grant sequences
retain precise boundaries until their alternative forecasts are owned.

Fresh/cache final/prefix and site-objective cases cover with/without saved grants,
missing unused links after cleanup, retained interruption, generation counters,
three rewrites and next-boundary cleanup without duplicate diary entries.
Own-grant event suppression passes; unavailable events and optional generation
stop atomically. All affected research tests pass. Broader deployment/expiry
alternatives remain the third slice and next compatibility boundary.

Different reachable zero-duration deployments now share one grant slot when
their research topics are equal. Selected deployment identity, RNG, counters,
events and geometry still belong to the site owner, which checks every reachable
alternative against that slot's exact post-grant research. Texture-selected
births can initiate the proof without a saved site; their preliminary selection
scan reserves source/area/weight work and never selects or draws RNG.

The broader run passed 375 of 376 affected tests; the preliminary selection scan
threw on its work limit instead of returning the owned atomic refusal. The batch
audit corrects that failure path. Fresh/cache saved and daily cases use 16 fixed seeds,
exact no-site RNG/identity controls, separate saved-site counter consequences,
selected deployment counters and three rewrites. Every reachable unavailable
event, divergent grant and mixed immediate/positive duration still stops before
mutation. Verified site-spawn selection and daily-generation evidence backs the
composition. Optional generation and divergent grant/expiry sequences remain
precise forecast boundaries. The three immediate-site slices are ready for audit.

The immediate-site batch audit confirms grant/cleanup order, candidate-slot
identity, conditional event state, alias reuse, bounded work and repeated rewrites.
It corrects two confirmed defects: preliminary work-limit failures now return the
owned atomic refusal while malformed texture/weight errors retain their exception
contract; both saved-birth scans honor AlienMission::think's multi-UFO suppression.
The latter was reproduced with a phantom grant incorrectly permitting a due event
to reach unavailable payload execution. Fresh/cache regressions now refuse that
tick before mutation, with/without an earlier saved-site grant.

All 378 affected tests pass, followed by all 3,870 root tests with zero failures
or skips. Full formatting, code-map and diff verification pass, including native
checksums, private corpus and populated allocation gates. The reference remains
clean at the pin; useful captures remain and no native objects were generated.
Random immediate/surviving durations, optional generation, divergent grant
sequences and generated retaliation remain explicit research composition work.

## Optional immediate research (2026-10-08)

Saved and deterministic daily births with random zero/positive duration now use
bounded grant-or-no-grant alternatives. The research kernel retains skipped
outcomes, publishes guaranteed/possible research and explicit interruption sets,
and separately exposes the post-grant state conditional on expiry. Direct-site
events use that conditional state; later countdown events and flight use the
combined outcomes. Native duration selection still occurs only during creation.

All 404 affected tests pass. Verified spawn/timer/generation evidence backs
fresh/cache 32-seed controls, actual expiry/diary/interruption decisions, event
suppression, three rewrites and exact RNG/site comparisons. A possible grant
cannot suppress a due event, and excessive alternative chains stop atomically.
Kernel checks preserve campaign/RNG state and distinguish conditional from
guaranteed research. Malformed duration and selected-choice regressions remain
strict. Optional generation and divergent research choices are the next slices.

Optional daily generation now forecasts its whole ordered grant sequence or no
generation, retaining correlation between births from one source. Conditional
newborn event proofs are created inside the generated branch; final summaries
include the skipped branch for subsequent events/flight. A single selected
mission with an always-due initial timer can use this projection. Multiple mission
choices and uncertain initial timer sequences retain their precise boundaries.

All 410 affected tests pass. Native generation/spawn evidence backs 32-seed
fresh/cache generated/skipped outcomes, two different ordered topics, a second
site event requiring the first grant, exact marker/diary presence and three
rewrites. Another 32-seed scenario combines optional generation with random
duration and verifies actual grants/interruption/reload. Pure kernel checks keep
campaign/RNG unchanged. Due events cannot rely on optional generation. Divergent
deployment topics remain the third slice before the batch audit.

Divergent immediate deployment topics now use bounded selected-grant branches,
including a skipped branch for suppressed or surviving sites. The kernel restores
the same prior state for each choice, including missing-topic no-ops, and publishes
combined summaries afterward. Per-mission/deployment conditional proofs prevent
one choice's research from suppressing another choice's event. Daily alternatives
must retain equal topic sets and ordered grant slots, with source-generation
correlation preserved around the whole sequence.

All 422 affected tests pass. Fresh/cache saved/daily 32-seed scenarios preserve
selected topic/diary presence, marker counts, own-event suppression, exact RNG
controls and three rewrites. Suppressed no-site choices retain no grant, and due
events cannot depend on an unchosen topic. Pure kernel tests verify conditional
topic separation, missing-choice state restoration and unchanged campaign/RNG.
Positive-duration/null deployment alternatives use the same skipped projection.
The three uncertainty slices are ready for their batch audit.

The uncertainty batch audit confirms skipped/selected branch isolation, prior
state restoration, missing-topic no-ops, conditional per-deployment event proofs,
source-generation correlation and outcome/work limits. Root acceptance reproduced
one allocation regression: a captured local callback caused a closure allocation
at the per-tick entry point even before its early return. The callback now lives
in a separate method called only for actual daily birth forecasts. Both original
campaign allocation budgets pass unchanged, along with all 12 foundation tests.

All 3,898 root tests pass with zero failures/skips after the fix, including native
checksums, private corpus and populated allocation gates. Full formatting,
code-map and diff verification pass. The reference remains clean at the pin;
useful captures and the failure log remain, with no new native objects. Initial
timer uncertainty, multiple source mission choices, generated retaliation and
remaining stock/flight reconciliation keep the headless gate open.

## Generated timer and source alternatives (2026-10-08)

Native `AlienMission::start` chooses `(steps / 2 + random[0, steps]) * 30`,
where `steps = firstWave.spawnTimer / 30`. Daily generation can now forecast
the whole immediate birth sequence or a delayed start, preserving its conditional
newborn event proofs and withholding uncertain research from later due events.
The cleanup proof still retains any mission whose initial timer may be delayed.
Source mission-choice alternatives remain a separate boundary for the next slice.

Verified generation/spawn fixtures and fresh/cache 32-seed cases cover timers
59, 60, 90 and 120, actual interruption/diary decisions, retained wave counters,
exact RNG controls and save/reload. A due event is suppressible only with an
always-due grant. The C++ reference remains unchanged at the pinned commit.

Daily source mission choices now forecast exclusive ordered sequences from the
same prior research state. Source/mission aliases share grant slots within each
choice; different choices do not execute together. Null, delayed and non-site
choices retain a no-immediate-grant outcome. The kernel restores all research
and explicit-topic summaries before each sequence and bounds combined outcomes.

All 378 affected tests pass. Native `GenerateSupplyMission`, `AlienMission::start`
and site-spawn evidence backs fresh/cache 32-seed two-sequence, delayed and null
choices, selected markers/diaries/counters, conditional event suppression and
three rewrites. A due event cannot depend on an unchosen grant. Pure sequence
tests preserve campaign/RNG state and reject more than 256 outcomes intentionally.
The timer and source slices await their combined audit after retaliation research.

Generated retaliation now composes with site grants through the daily prepared
region/race candidates and effective scouting/assault waves. No-target instant
missions are recognized as removed before the research caller even with unfinished
wave counters. Surviving daily missions receive later interruption; hunting
missions defer to their later owning preflight and cannot receive an earlier grant.
Targeted instant retaliation still stops atomically at the base-defense boundary.

Native retaliation-spawn, generation and hunting fixtures back fresh/cache
scouting, assault overrides including absent original UFOs, immediate chains,
three rewrites and exact UFO/RNG controls. No-target instant chains skip an unused
missing interruption link after daily cleanup; retained daily missions validate
their link. Player-base movement in the no-target hunting test preserves ample
patrol return fuel so it exercises actual creation, with the reset timer checked.
The timer/source/retaliation slices are ready for the combined audit.

The combined audit finds no additional confirmed defect. It checks native timer
rounding and recursive wave order, sequence summary restoration, null/delayed
choices, source alias isolation, early no-target cleanup, retained interruption
links and later hunting ownership. All 3,920 root tests pass with zero failures
or skips, including private corpus, fixture checksums and original allocation
budgets. Full formatting, code-map and diff checks pass; the reference is clean.
Distinct generated region/area grant sequences, stock eligibility uncertainty,
nonterminal flight uncertainty and final headless reconciliation remain open.

Generated region/race/area candidates now retain exclusive research paths inside
each selected source mission. Shared wave slots remain local to their path; a
topic from another candidate cannot enter its conditional newborn event proof.
The native generation and site-spawn fixtures back fresh/cache 32-seed region
and fixed-area choices with distinct topics, markers, diaries, own-event
suppression and three rewrites. A due event cannot rely on an unchosen path.
Differing slot shapes and no-grant candidate paths retain their next-slice guard.

Each candidate path can now have its own ordered grant slots. Positive-duration
or suppressed paths with no immediate grant add a skipped outcome; paths with
later common grants retain those grants without requiring an earlier slot.
Fresh/cache 32-seed fixed-area scenarios cover surviving/suppressed births,
missing first slots and common later slots, exact marker/diary/site timers and
reload. Common later research suppresses its due event in every outcome; a
missing path's topic cannot suppress its event. Verified wave-chain, site-spawn
and timer evidence backs the ordering. The blanket equal-sequence guard is gone.

Uncertain explicit research now permits an ordinary arrival at the trajectory's
last point: interrupted and uninterrupted native `ufoReachedWaypoint` branches
both destroy that actor. Nonterminal arrivals retain their separate uncertainty
boundary. Verified lifecycle/diary fixtures back fresh/cache 32-seed reward
selection, identical UFO/RNG controls, exact selected interruption, three
rewrites and next-tick removal/live-UFO repair. The path/slot/terminal slices
are ready for the combined audit.

The path/slot/terminal batch audit confirms exclusive source and path ordering,
skipped no-grant alternatives, per-path event proofs, terminal fallthrough and
next-tick live-UFO cleanup. No additional defect was confirmed. All 3,936 root
tests pass with zero failures/skips, including original allocation budgets,
private corpus and checksum gates. Full format/map/diff checks pass and the
reference remains clean. Stochastic maintenance stock eligibility, nonterminal
flight alternatives and final headless reconciliation still keep 4c open.

## Stock presence and nonterminal flight (2026-10-08)

Mission research now retains bounded immutable stock-presence alternatives for
automatic topics whose needed item may be exhausted by earlier maintenance.
The same item outcome survives every ordered grant; duplicate item references
do not create more branches. Native `SavedGame::getAvailableResearchProjects`
checks quantity against zero, and `addFinishedResearch` does not consume stock
for automatic discovery. The real maintenance caller still selects its result.

All 428 affected tests pass. Pinned hunting/diary evidence backs fresh/cache
32-seed bullet-saving cases with automatic research exactly when ammunition
remains, unchanged craft/RNG controls and three rewrites. Kernel fixtures cover
repeated grants, duplicate inputs, 256 outcomes, intentional outcome/work refusal
and unchanged campaign state. Possible-only automatic research cannot suppress
unavailable site or due event effects; guaranteed explicit research still can.

Uncertain explicit interruption now allows a nonterminal airborne waypoint when
the ordinary arrival proof succeeds. Native `AlienMission::ufoReachedWaypoint`
destroys an interrupted actor before selecting its next waypoint; the surrounding
flight pass then skips later UFOs and craft. The uninterrupted branch retains
the existing waypoint, resource and interception proofs. Ground arrivals retain
their separate boundary for the next slice.

Verified lifecycle/diary evidence backs fresh/cache 32-seed reward selection,
both flight outcomes, later UFO and craft fallthrough, exact control RNG and
world state, three rewrites and next-tick live-UFO cleanup. Unsafe waypoint
speed and a possible interception still stop the whole tick without mutation.

Ordinary ground arrivals now use the same continuing-branch proof. Selected
interruption destroys before landing; continuation retains native land/fake-water
eligibility, ground timers, marker allocation, alerts and later-actor movement.
Site conversion and landing-base creation still require their separate research
alternatives to be proved. The unsafe-ground-timer regression now reaches its
owning waypoint guard, while an objective conversion retains its uncertainty
guard before reward selection or movement.

Fresh/cache 32-seed dry-land, accepted/rejected fake-water and ocean scenarios
compare the full selected snapshot with deterministic interruption controls,
including RNG, diaries, landing alerts and later UFO/craft fallthrough. Three
rewrites and subsequent timer/takeoff or destroyed-UFO cleanup are checked.
Reference: `AlienMission::ufoReachedWaypoint`, native lifecycle/site-spawn and
diary captures. The stock/airborne/ground slices are ready for their batch audit.

The initial batch passes all 3,954 root tests without failures or skips. Review
also identified a missing immediate contact-loss alert: native
`GeoscapeState::time5Seconds` reports a detected UFO's loss to its followers
before returning from an interrupted or terminal arrival. Pursuit repair is
correctly deferred to the next tick, but that earlier alert was absent. A
separate audit correction now reports one `UfoContactLost` before the terminal
return, with both craft and UFO followers included as native `MovingTarget`
destinations require. Preflight reserves one bounded scan including incoming/
produced craft and the supported same-tick UFO population. Eight fresh/cache
32-seed regressions reproduced the missing alert before the fix and now pass
with three rewrites, deferred movement and next-tick repair. Hidden and
unfollowed terminal contacts remain silent. The legacy terminal pursuit test now
requires the exact advance-plus-loss event sequence and retains its deferred
movement and cleanup checks.

All 3,968 root tests pass with zero failures/skips, including private corpus,
fixture checksums and unchanged populated allocation budgets. Full formatting
and the subsequently changed pursuit assertion's formatting pass; code-map and
diff checks pass, and the pinned reference remains clean. Conversion/base flight
alternatives, daily research/stock uncertainty, newborn flight/pursuit composition
and final headless reconciliation keep 4c open; 4d is untouched.

## Conversion and landing-base alternatives (2026-10-08)

Uncertain site research now permits an ordinary objective site conversion when
the existing continuing-branch proofs succeed. Native
`AlienMission::ufoReachedWaypoint` destroys an interrupted UFO before conversion;
normal `GeoscapeState::time5Seconds` preserves conversion, follower transfer and
the terminal return. Dormant respawn UFOs continue the flight pass, while a
destroyed conversion actor defers later UFO and craft movement until the next tick.

Verified site-spawn, lifecycle and diary fixtures back fresh/cache 32-seed cases
with respawn enabled/disabled and craft followers with/without units. Selected
site IDs, timers, dormant links, notifications and follower destinations match
independent deterministic interruption controls, including the full snapshot and
RNG. Three rewrites and next-tick live-UFO cleanup retain the selected outcome.
Possible conversion marker exhaustion still blocks before reward selection.
Landing-base creation retains its separate research guard for the next slice.
All 448 affected tests pass; targeted formatting, code-map and diff checks pass.

Landing-base creation now uses the same continuing-branch proof rather than a
generic research-outcome refusal. The obsolete flight branch scan is removed;
the shared mission/base link-work bound remains. Native
`AlienMission::ufoReachedWaypoint` creates at the actual landing position with
the default deployment texture; `GeoscapeState::time30Minutes` only applies
`showAlienBase` to bases born during mission think. Landing births remain hidden.

Pinned objective-base, lifecycle and diary evidence backs fresh/cache 32-seed
interrupted/continuing cases, both show flags, full control snapshots/RNG,
three rewrites and next-tick cleanup. Base markers, generated preservation keys,
birth sequence, race, month and activity are checked. Possible marker, birth
sequence and score overflow remain whole-tick refusals before research selection.
All 504 affected tests pass; targeted formatting, code-map and diff checks pass.

Six fresh/cache endurance cases now compose both selected research outcomes
through 72 hours of conversion (with/without dormant respawn) or landing-base
activity. Single-tick, hourly and daily batches match the entire snapshot at
each day boundary, including RNG, craft maintenance, stocks and research diary.
Transition/day/final rewrites retain only surviving targets; deleted sites,
UFOs and missions do not return. Repeated future events retain distinct
preservation keys, ordered timers and history for removed rules. Due payloads
and integrated month rollover still block atomically. All six scenarios pass;
these three arrival slices are ready for their batch audit.

The conversion/base/endurance batch audit confirms native interruption,
notification, follower and terminal-return order. No additional defect was
confirmed. The remaining link-work check still bounds the ordinary owners'
mission/base scans; unsafe continuations retain their own capacity and combat
proofs. All 3,988 root tests pass with zero failures/skips, including private
corpus, fixture checksums and unchanged populated allocation budgets. Full
formatting, code-map and diff checks pass; the pinned reference remains clean.
Daily research/stock alternatives, newborn arrival/pursuit composition and final
headless reconciliation remain open; 4d is untouched.

## Daily mission and base research alternatives (2026-10-09)

Daily mission interruption now accepts possible-only research outcomes through
the existing continuing-branch proofs. Native `GeoscapeState::time1Day` finishes
research before setting mission flags, followed by half-hour mission think and
later flight. Guaranteed daily topics still project permanent interruption;
same-day site grants retain their separate ordered daily research requirements.

Pinned generation, lifecycle, site-spawn and objective-base evidence backs
fresh/cache 32-seed transit, airborne, ordinary ground, conversion, dormant
respawn, landing-base and terminal arrivals. Full snapshots/RNG match selected
deterministic controls, with three rewrites and later movement/live-UFO repair.
Possible marker exhaustion and interception remain atomic. Earlier single-free-
reward and direct-disabler fixtures now require their exact native outcomes.

Review before committing reproduced an omitted cleanup alternative: an imported
mission with no saved live UFOs can still have a UFO or invalid retaliation link,
and possible daily interruption can make it expire before flight. The daily
preflight now reserves the existing bounded expiry/link validation for that
alternative. Fresh/cache regressions refuse before any research or RNG mutation.
All 564 affected tests pass; targeted formatting, code-map and diff checks pass.

Daily interruption now has fresh/cache 32-seed scheduling acceptance for delayed
missions, ordinary UFO births, immediate no-object prefixes, final base and
infiltration objectives, and positive-duration direct sites. Selected results
match full deterministic control snapshots/RNG; three rewrites and the next
tick preserve counters, IDs, live counts and pact state. Possible base/site
marker exhaustion still blocks before research selection. Infinite zero-timer
infiltration retains its intentional work refusal; finite fixtures disable the
native endless option explicitly. Mission-think revelation was found and
implemented alongside these cases; its captured evidence is recorded in
[mission-think base revelation](strategic-mission-script-status.md#mission-think-base-revelation-2026-10-09).

Possible daily research now permits quiet, unlinked base self-destruction through
the surviving actor's activity/timer/discovery proofs. Empty generation choices
and pact fields that deletion leaves unchanged add no unavailable effect.
Native `GeoscapeState::time1Day` and `SavedGame::clearLinksForAlienBase` place
deletion before daily activity and later generation. Linked missions/craft,
incoming follower craft, active generation/hunting and changed pact eligibility
retain precise alternative-forecast checks. Ordinary loaded UFO coordinates
remain anonymous waypoints, as `Ufo::finishLoading` requires.

Pinned cleanup/activity captures back fresh/cache 32-seed deletion/survival,
visible/hidden targets, empty generation and unchanged pact cases. Full selected
control snapshots/RNG, activity, stable birth identity and counters are checked.
Three loaded rewrites conserve unknown fields on a surviving base and remove
its deleted sidecar permanently. Ambiguous markers and each remaining effect
stop before the tick mutates. All 607 affected tests pass; targeted formatting,
code-map and diff checks pass. The three daily slices are ready for batch audit.

The daily mission/wave/base batch audit confirms interruption and deletion before
activity, generation and flight, mission-think-only revelation, and anonymous
loaded UFO destinations. No additional defect was confirmed. Optional linked
cleanup, generation and pact changes retain their precise guards. All 4,049 root
tests pass with zero failures/skips, including private corpus, fixture checksums
and unchanged populated allocation budgets. Full formatting verification,
code-map and diff checks pass; the pinned reference remains clean. Ordered daily
research/site alternatives, linked deletion, newborn arrival/pursuit composition
and final headless reconciliation remain open; 4d is untouched.

## Exact daily world interruption forecasts (2026-10-09)

World preflight now reuses the existing bounded daily stock/discovery projection
for guaranteed research. Exact single, sequential and protected rewards, reward
lookups, automatic dependencies and re-enabled automatic topics can interrupt
due events and delete linked bases before activity. The result and support flag
are cached only for the current preflight pass and shared with ordered site
research. Uncertain or failed projections retain the conservative summary;
no reward selection, script execution, RNG or campaign mutation occurs in proof.

Reference: pinned `GeoscapeState::time1Day` and
`SavedGame::selectGetOneFree`/`addFinishedResearch`, followed by daily mission/base
checks and later event cleanup. Existing diary, event and base-cleanup captures
back 28 fresh/cache regressions. Six exact reward paths reproduce the old false
payload refusal, and linked cleanup reproduces the old uncertainty refusal.
Selected execution matches full control snapshots/RNG and three rewrites.
Repeatable, disabled, item-ineligible, unfinished and uncertain topics still
refuse unavailable payloads; a later site disabler also refuses the entire tick.
All 4,077 root tests pass with zero failures/skips, including fixture checksums,
private corpus and unchanged populated allocation budgets. Full formatting,
code-map and diff verification pass; the pinned reference remains clean. This
is the first slice of the next three-slice batch; its remaining slices and batch
audit are pending.

Interim review before the next slice confirmed an optional activity defect in
quiet base deletion: the score forecast counted a possibly deleted base as
certain. An opposite-sign later UFO score could then exceed the signed limit
when the base disappeared. Four fresh/cache regressions reproduced an unsafe
tick being accepted before the fix. Possible base deletion is now recorded in
pass-local proof state, and activity includes both zero and the signed score.
Eight regressions cover both score limits across 32 seeds, atomic unsafe refusal,
safe selected controls/RNG and three rewrites. Native `GeoscapeState::time1Day`
deletes bases before scoring survivors and later half-hour UFO activity.
All 4,085 root tests and full formatting verification pass, including fixture
checksums, private corpus and unchanged allocation gates. Code-map/diff checks
pass. The confirmed correction is committed separately from the execution slices.

The same review confirmed optional wave awards also need zero in their activity
envelope. Daily interruption can omit an ordinary UFO or direct site before later
site scoring. Eight fresh/cache regressions reproduced an unsafe tick being
accepted at both signed limits. Possible mission interruption is now recorded
once per pass; newborn activity, direct sites and later newborn takeoff awards
retain the omitted outcome. Existing saved UFO activity remains mandatory because
half-hour scoring precedes waypoint interruption. Sixteen new regressions include
32-seed safe selected controls/RNG and three rewrites. All 677 affected research,
interruption, objective, base and event tests pass. Full repository validation
will be repeated with the execution batch; this correction is committed separately.

## Optional operation-base deletion (2026-10-09)

Possible source deletion now composes with ordinary Earth-based missions. Native
`SavedGame::clearLinksForAlienBase` clears their source and interrupts every
non-space operation before half-hour think and five-second arrival. The surviving
branch retains normal spawning, creation, capacity and combat proofs. The deleted
branch reserves earlier zero-live expiry and retaliation repair, and omitted
wave awards use the optional activity envelope. Space-source unlinking, special
UFO links, craft followers and changed pact fields retain precise remaining checks.

Pinned cleanup, lifecycle, wave-chain, objective and site captures back seven
ordinary flight paths and six due-wave kinds across all six Earth operation
modes. Fresh/cache 32-seed selected controls match full snapshots/RNG, three
rewrites and next-tick cleanup. Zero-live retaliation links repair at the actual
caller; orphan UFOs, invalid retaliation links, possible marker exhaustion and
interception stop before research or RNG mutation. Eight further signed-limit
regressions verify source-driven interruption cannot credit an omitted UFO or
direct-site award. All 713 affected tests and 60 final focused cases pass;
targeted formatting, code-map and diff checks pass. This is cycle 11's second
execution slice; generation and the batch audit remain.

## Optional surviving-base generation (2026-10-09)

Possible source deletion now composes with both daily generation and later
hunting generation. The surviving source retains existing bounded mission-choice,
geometry, counter, identity, activity and creation-wave proofs; deletion omits
those actors. Immediate daily research births still require the ordered daily
research projection. A zero-duration site created by hunting follows the current
site caller and grants research only at the following site-processing pass.

Reference: pinned `GeoscapeState::time1Day`, `GenerateSupplyMission::operator()`
and `GeoscapeState::baseHunting`; the cleanup, daily/hunting generation, wave-chain,
objective, infiltration and site captures back the composed fixtures. Native
daily deletion precedes scoring and generation; hunting increments the surviving
timer, selects an eligible craft, allocates a source-linked mission and calls
think immediately. Daily generation increments its own counter; hunting resets
its timer only on successful generation. Selected deletion consumes neither
generation caller's RNG or identity sequence.

Fresh/cache fixtures cover delayed, ordinary, finite no-object prefixes, final
base/infiltration and positive-duration site waves from each caller and both
together. Every kind runs 32 seeds with both outcomes, full selected controls/RNG,
three rewrites and next-tick comparison. Further cases retain mission/base/site
capacity refusal, daily immediate-grant refusal, late zero-duration expiry and
no eligible hunting craft. All 737 other affected cases passed the broad run;
after correcting two fixture diary assertions for automatic zero-cost UNLOCK,
all 22 generation cases pass. Targeted formatting, code-map and diff checks pass.
This completes cycle 11's execution slices; their batch audit and full repository
validation follow.

Cycle 11 batch audit closes the exact daily discovery, Earth-source deletion and
optional generation slices, including the two separately committed activity
corrections. No further confirmed issue was found. Source/mission cleanup,
generation omission, ordered research/event boundaries, forecast reset and work
reservations match the inspected callers. All 4,163 root tests pass with zero
failures/skips, including checksums, private-corpus classifications and populated
allocation gates. Full formatting, code-map and diff checks pass; the pinned
reference is unchanged. Remaining ordered stochastic daily research, Space/special
source links, craft followers, pact alternatives, newborn first arrival, stochastic
maintenance and final acceptance reconciliation keep 4c open.

## Optional Space-source unlinking (2026-10-09)

Space missions other than supply now keep executing when uncertain daily research
deletes their source. Native `SavedGame::clearLinksForAlienBase` clears the pointer
without interruption; `AlienMission::spawnUfo` and `getWaypoint` use regional
geometry for these Space operations. Supply's objective birth reads the source
even in Space mode and retains a separate optional-supply forecast boundary.
Earth special-UFO links and direct/incoming craft followers retain their checks.

Cleanup, lifecycle, mission-origin, wave, objective and site captures back seven
ordinary flight paths, six due-wave kinds and saved hunter/escort behavior. Each
runs 32 seeds in fresh/cache mode with both outcomes, full selected controls/RNG,
three rewrites and following-tick comparison. A prior cleanup refusal fixture now
executes its newly supported Space/Earth links and explicitly checks zero-cost
automatic UNLOCK, retained Space missions and removed zero-live Earth missions;
missing research still refuses atomically. All 770 affected tests and 25 final
focused cases pass; targeted formatting, code-map and diff checks pass. This is
cycle 12's first execution slice; supply, pact alternatives and batch audit remain.

## Optional Space supply-source loss (2026-10-09)

Space supply missions now suppress objective UFO births when uncertain daily
research removes their source, without marking the mission interrupted. Other
regional UFOs and direct deployment sites continue. Possible objective awards
include zero; counter completion reserves earlier zero-live mission expiry and
retaliation-link validation. Existing special actors or same-tick special births
with a conditional ordinary leader retain a precise guard; delayed source unlinking
does not invent that dependency when no objective birth can occur this tick.

Reference: pinned `AlienMission::spawnUfo` supply branch, `think`'s next-wave timer,
`SavedGame::clearLinksForAlienBase` and the half-hour mission cleanup. Verified
`strategic-mission-origin` supply rows pin source suppression and constructor
defaults; cleanup, wave-chain and site captures back the composition. Fresh/cache
32-seed cases cover objective/non-objective, finite immediate mixed waves,
deployment sites, delayed waves and ignored objective hunter/escort metadata.
Selected controls compare all state/RNG through three rewrites and next ticks.
Signed-limit cases cover omitted awards and safe bounds; identity exhaustion,
early invalid retaliation cleanup and conditional leader paths refuse atomically.

All 1,040 other successful cases in the affected run and all 26 final supply cases
pass. Four fixture failures came from a nonzero next-wave timer in an intended
immediate chain; the native next-wave timer was restored to zero and all final
cases pass. Targeted formatting, code-map and diff checks pass. An overlapping
test/build attempt hit an owned-process executable lock; after both runs exited,
sequential validation passed without permission or host changes. This is cycle
12's second slice; pact alternatives and the full batch audit remain.

## Optional daily pact cancellation (2026-10-09)

Country-linked source deletion now composes with later infiltration. Preflight
reserves the union of original eligibility and the possible
`Country::setCancelPact` result (`newPact=false`, `cancelPact=pact`). Existing
country/spawn/capacity bounds cover that union without changing country state or
random ordering. Disabled cancellation keeps original eligibility and does not
reserve a birth for an ineligible country. Monthly pact settlement and unavailable
event consequences remain guarded at their existing branch 6 boundary.

Reference: pinned `SavedGame::clearLinksForAlienBase`, `Country::setCancelPact`/
`canBeInfiltrated`, daily deletion ordering and later saved/daily/hunting mission
completion. Cleanup and infiltration truth-table captures back all eight flag
combinations, no-mission cancellation and all three later mission callers.
Fresh/cache 32-seed selected controls compare whole snapshots/RNG, both outcomes,
three rewrites and next ticks. Newly possible eligibility reserves marker and
birth-sequence capacity atomically. Disabled-option cases retain unused exhausted
marker capacity. Eight selected seeds also compose 72 hours of hunting cleanup,
day/hour batches and transition reloads with stable pact/base populations.
All 166 affected cases pass; targeted formatting, code-map and diff checks pass.
This completes cycle 12's execution slices; their batch audit and root validation
follow.

Cycle 12 batch audit closes Space unlinking, optional supply-source births and
daily pact cancellation. No additional confirmed defect was found. Reviewed all
native source reads, supply constructor/escort defaults, immediate-wave timer
ordering, omitted signed awards, early expiry, country eligibility unions and
per-tick forecast reset. Conditional supply leaders and Earth special/follower
alternatives retain their precise checks. All 4,211 root tests pass without
failures/skips, including checksums, private classifications and populated
allocation gates. Full formatting, code-map and diff checks pass; the reference
checkout remains unchanged. Ordered daily research/site alternatives, remaining
linked deletion, newborn first arrival, stochastic maintenance and final
acceptance reconciliation keep 4c open; 4d has not started.


## Optional Earth source loss with special UFOs (2026-10-09)

Saved hunters and escorts now use the shared flight/retarget proofs when uncertain
daily research deletes their Earth operation base. Native source unlinking marks
the mission interrupted without resetting UFO hunt/escort flags; an ordinary
arrival can end the pass earlier, while the surviving branch retains every owned
arrival and pursuit check. Conditional newborn leaders keep their existing proof
boundary. Fourteen fresh/cache regressions reproduce the blanket refusal across
seven flight/lifecycle paths, then cover hunter/escort variants over 32 seeds,
both research outcomes, whole selected-control snapshots/RNG, three rewrites and
following ticks. All 104 affected cases pass. Reference: pinned SavedGame::
clearLinksForAlienBase, GeoscapeState::time1Day/ufoHuntingAndEscorting/time5Seconds
and AlienMission::ufoReachedWaypoint, with verified cleanup/lifecycle/hunting
captures. This is cycle 21 slice one; Space supply hunter composition and audit remain.

Saved Space supply hunters without escort state now compose with optional objective
ship suppression. They do not consume the conditional ordinary leader; shared
hunting/waypoint operations retain their status and movement. Existing escort
state still requires its own conditional leader proof. Six fresh/cache regressions
reproduce the restriction across idle, arriving and actively hunting sources,
objective-only/mixed waves and 32 seeds. Both deletion outcomes compare whole
snapshots/RNG with selected controls through three rewrites and continuation.
All 54 affected cases pass. Reference: AlienMission::spawnUfo's supply branch,
SavedGame::clearLinksForAlienBase and GeoscapeState::ufoHuntingAndEscorting, with
verified mission-origin supplyRows, cleanup and hunting captures. This is cycle 21
slice two; independent newborn supply hunters and the batch audit remain.

Non-escort hunter births now compose with optional Space supply objective-ship
suppression. Native non-objective Space origins/destinations ignore the source;
only escort initialization consumes the possible ordinary leader. Ten fresh/cache
regressions reproduce the old restriction, then cover 50/100-percent constructors,
source deletion/survival, no-target and distant actual hunting over 64 seeds.
Selected controls compare complete snapshots/RNG through three rewrites and
following ticks; close hunting and contact-ID exhaustion remain atomic. Integration
exposed a preflight proof-key mismatch after unlinking; the source-independent
non-objective/non-escort Space supply key now omits that unused link. Four tests
reproduced the execution exception before the correction. All 172 affected cases
pass. Reference: AlienMission::spawnUfo's supply constructor/coordinates/escort
branch and GeoscapeState::ufoHuntingAndEscorting, with verified supply, cleanup,
hunting and immediate-wave captures. This completes cycle 21's three slices;
the batch audit checks every optional source consumer and proof reuse boundary.

Cycle 21 batch audit found no additional confirmed defect. The audit checked
Earth interruption with retained special flags, Space objective suppression,
every newborn hunting proof caller and source-key normalization, early expiry,
optional signed awards, work/population/identity bounds and per-pass reset.
The source-independent proof-key exception was fixed within slice three.
All 4,525 root tests pass with zero failures/skips, including checksums, private
classifications and populated allocation gates. Full formatting, code-map and
diff checks pass.
No new native artifacts were generated; useful test logs and evidence remain,
and the pinned reference checkout is clean. Conditional supply escorts, remaining
ordered research/arrival alternatives and final acceptance keep 4c open.

## Optional Space supply escort leaders (2026-10-09)

Saved escorts now compose with optional objective supply leaders. Source deletion
keeps Space missions running but can omit that ordinary constructor, so the
leader forecast retains its presence and absence rather than promising attachment.
The shared bounded flight proof covers a distant old waypoint or one validated
airborne arrival; dependent UFOs/craft consume its movement envelope. Reachable
ground/terminal transitions and newborn escort initialization retain their checks.
Four fresh/cache regressions first reproduced the blanket restriction, then cover
both research outcomes over 32 seeds, exact source movement, whole selected-control
snapshots/RNG, three rewrites and following ticks. All 102 affected cases pass.
Reference: pinned AlienMission::spawnUfo's supply constructor/origin/destination
and escort selection, SavedGame::clearLinksForAlienBase, and ordered Geoscape
retarget/movement, backed by verified supply, cleanup, hunting and lifecycle
captures. This is cycle 22 slice one; ordered followers/pursuit and newborn
escort alternatives follow before the batch audit.

Optional supply leaders now have composed follower/pursuit acceptance: 28 new
fresh/cache cases cover both source outcomes, forward/reverse list order, flying
chains, grounded dependency cuts, ordered takeoff vectors and far craft pursuit.
An independent guaranteed regional birth can replace the optional point leader;
the first ordinary UFO remains the native selection. Selected controls compare
whole snapshots/RNG through three rewrites and following ticks. Exact follower
positions and takeoff vectors independently enforce native saved-list ordering.
Missing radar coverage retains contact-loss notification and patrol response;
close transitive pursuit and malformed ground timers stop before research.
Reference: GeoscapeState::time5Seconds/ufoHuntingAndEscorting, Ufo::think/
setEscortedUfo, MovingTarget/Target coordinates and Craft target-loss handling,
backed by the verified world idle-escort follower and lifecycle captures. The
shared owning proofs added in slice one cover these cases without another guard
removal. All 150 affected cases pass, with targeted formatting, code-map and diff checks.
This closes cycle 22 slice two; newborn escort alternatives remain.

Newborn non-objective Space supply escorts now compose with an optional earlier
ordinary objective ship. The origin proof keeps both possible leader origins
and nominal idle flight until an independent ordinary birth guarantees attachment.
Their native source link is unused by coordinates or escort selection, so the
shared hunting key omits it while the leader-domain owner proves source-dependent
birth suppression separately. Eighteen fresh/cache cases cover 50/100-percent
constructors, no-target and distant actual hunting, objective-prefix and
escort-only waves, both deletion outcomes over 64 seeds, complete selected-control
snapshots/RNG, three rewrites and continuation. The escort-only targetable fixture
confirmed four execution exceptions before the source-key fix. Combat and marker
exhaustion remain atomic. All 184 affected cases pass. Reference: pinned
AlienMission::spawnUfo supply/escort branches and GeoscapeState ordered retargeting,
with verified supplyRows, cleanup, hunting and wave-chain captures. This completes
cycle 22's three slices; the batch audit follows.

Cycle 22 batch audit confirmed that the distant conditional flight proxy hid a
negative saved speed in the possible idle branch. Four fresh/cache/current-point
regressions advanced before the correction; the owning conditional proof now
refuses that consumed input before mutation. Two guaranteed independent-leader
cases retain executable replacement of the unused old speed, and two distant
terminal-waypoint cases retain normal flight without consuming a future arrival.
All 192 affected cases pass. The audit also checked leader order/origin unions,
source-key reuse across generated and escort-only paths, radar-loss patrol,
transitive movement/takeoff, work bounds and per-pass reset. All 4,583 root tests pass with zero failures/skips, including checksums, private
classifications and populated allocation gates. Full formatting, code-map and
diff checks pass; correction 84630b4b is separate. No new native artifacts were
generated, useful captures/logs remain, and the reference checkout is clean.
Remaining linked deletion, ordered research/arrival alternatives and final
acceptance keep 4c open; 4d remains untouched.

## Optional alien-base craft followers (2026-10-09)

Grounded non-patrolling owned craft now compose with optional destination-base
deletion. Native Target destruction calls returnToBase before servicing; it
preserves status, replaces destination/speed and assigns takeoff60 while grounded.
Later repair, rearm and refuel callers do not consume those flight fields. The
owner validates effective return speed even while grounded. Ten fresh/cache
regressions reproduced the old linked-target boundary, then cover all four service
states over 32 seeds, both research outcomes, exact return fields, complete
selected-control snapshots/RNG, three rewrites, following ticks and malformed
effective speed. All 85 affected cases pass. Reference: Target::~Target,
Craft::returnToBase/setDestination and GeoscapeState daily deletion/servicing order,
backed by verified cleanup followerRows and logistics captures. This is cycle 23
slice one; auto-patrol relaunch and compatible airborne returns follow. Transfers
and unsupported flight alternatives retain their precise boundary.

Grounded auto-patrol followers now compose too. Servicing either leaves them
grounded, without reading changed flight fields, or relaunches them with an
independent player waypoint/full speed/takeoff60 in both source alternatives.
Ten new fresh/cache cases first reproduce the guard, then exercise all four
service states and actual saved hunter selection/detection after rearm/refuel
relaunch. Both research outcomes over 32 seeds compare complete selected-control
snapshots/RNG, three rewrites and following ticks. Relaunch waypoint capacity,
hunter contact identity and close hunting combat remain atomic. All 95 affected
cases pass. Reference: Craft::setDestination/repair/rearm/refuel and GeoscapeState
time1Hour/time30Minutes/ufoHuntingAndEscorting, backed by verified hunting
maintenanceRows/hourlyMaintenanceRows, cleanup and logistics captures. This closes
cycle 23 slice two; compatible airborne followers and the batch audit remain.

Airborne owned followers now compose when their saved speed already equals the
effective return speed and home remains outside a current-tick arrival. Fuel,
hunter eligibility and craft-escort consumption are unchanged before movement;
flight retains both destinations. Eighteen fresh/cache cases cover takeoff,
ordered craft escorts and actual hunting over 32 seeds, both outcomes, exact
movement/fuel, complete selected-control snapshots/RNG, three rewrites and
continuation. Review reproduced two already-at-home takeoff failures: native
Craft::think checks arrival even while decrementing takeoff. The owning proof now
checks existing home arrival regardless of takeoff. Speed-changing returns,
near-home arrival, invalid takeoff and transfer followers retain atomic boundaries.
All 623 adjacent hunting/cleanup cases passed before that correction, and all 113
focused optional-base/cleanup cases pass afterward. Reference: Craft::think,
getFuelConsumption/getFuelLimit/consumeFuel, Target destruction and Geoscape
daily/fuel/hunting/movement order, with verified cleanup/world/hunting captures.
This completes cycle 23's third slice; its combined audit follows.

Cycle 23 batch audit found two obsolete guard assertions for grounded owned
followers in the earlier daily-research fixture. Those cases now assert both
research outcomes, native return identity/speed/takeoff, complete selected-control
snapshots/RNG, three rewrites and continuation over 32 seeds. The transferred
follower boundary retains its original atomic rejection. All 24 focused daily
research cases pass; full formatting/map/diff pass. Runtime review found no
additional defect beyond the takeoff-at-home correction within slice three.
The first root run passed 4,619 of 4,621 cases with only those two stale assertions;
the corrected root rerun follows.

Corrected cycle 23 acceptance passes all 4,621 root tests with zero failures/skips,
including checksum, private-corpus and populated allocation gates. Full formatting,
code-map and diff checks pass; the reference checkout remains clean. Confirmed
audit test correction fd87a7cf is separate from the three slices. No native
artifacts were generated; useful captures and logs remain. Variable-speed/fuel,
near-home and transfer alternatives plus branch-wide closure keep 4c open.

Optional airborne returns now admit different saved/effective speeds when native
fuel consumption is identical: item fuel burns one, and ordinary speeds can
share a floor bucket. Equal fuel preserves low-fuel recall and hunter eligibility;
flight keeps both speeds/destinations. Ten fresh/cache cases reproduce eight old
guard failures, then cover takeoff, actual hunting, both outcomes over 32 seeds,
exact fuel/movement, complete control snapshots/RNG, three rewrites and following
ticks. Negative speed and different burn remain atomic. Craft-escort references
are conservatively scanned and charged against the existing work budget; they
retain a precise boundary pending their consumer proofs. All 130 affected tests
pass, with targeted format/map/diff checks. Reference: Craft::getFuelConsumption/
getFuelLimit/consumeFuel, Geoscape time10Minutes and MovingTarget::calculateMeetPoint
(disabled interception extrapolation), backed by verified world craft/hunting
captures. This is cycle 24 slice one.

Item-fuelled craft escorts now compose with those speed-changing leaders. Their
native one-unit consumption is independent of saved leader speed, including
saved-order low-fuel propagation. Eight fresh/cache regressions reproduced the
escort guard; ten cases now cover both craft orders, takeoff, actual hunting,
both source outcomes over 32 seeds, exact fuel/follower movement, complete
selected-control snapshots/RNG, three rewrites and continuation. Ordinary escorts
retain an atomic consumer boundary. All 140 affected tests pass, with targeted
format/map/diff checks. Reference: Geoscape time10Minutes dynamic craft destination
and Craft::getFuelConsumption item branch, backed by verified escortFuelRows.
This is cycle 24 slice two; ordinary capped consumers and the batch audit follow.

Ordinary craft escorts now compose when native capped consumption is equal at
both positive leader-speed endpoints. This also covers earlier leader recall
changing speed before later fuel consumption. The proof validates follower
effective maximum, charges installed-weapon work and conservatively scans every
owned/transfer reference. Zero-speed fallback and differing caps retain their
precise boundary. Twelve new fresh/cache cases cover both saved orders, takeoff,
actual hunting, both outcomes, exact ordinary burn/movement, complete controls/
RNG, three rewrites and continuation; invalid follower maximum, differing cap and
1,100-craft aggregate work remain atomic. Eight execution cases reproduced the
old guard before implementation. All 152 affected tests pass, with targeted
format/map/diff checks. Reference: Craft::getFuelConsumption positive escort caps
and Geoscape saved-order fuel loop, with verified escortFuelRows. This completes
cycle 24's third slice; the batch audit follows.

Cycle 24 batch audit found no additional confirmed defect. Reviewed source and
escort burn branches, strict range/positive-speed caps, saved-order low-fuel
propagation, grounded relaunch and transfer inputs, immutable detection stats,
pre-movement hunter coordinates, work accounting and malformed boundaries. All
4,653 root tests pass with zero failures/skips, including checksums, private corpus
and populated allocation gates. Full formatting/map/diff pass and reference is
clean. No new native artifacts were generated; useful logs/captures remain.
Zero-speed consumer proofs, near-home/transfer alternatives and final closure
keep 4c open; 4d remains untouched.

Zero saved leader speed now composes for already-owned airborne ordinary escorts
when their native own-speed fallback equals the positive return-speed cap. Their
status and saved speed remain stable until fuel. Grounded relaunch and transfer
checkup inputs retain a precise boundary; zero return maximum remains excluded.
Ten fresh/cache cases first reproduce eight guard failures, then exercise both
saved orders/takeoff, stationary continuing leaders, exact follower burn/movement,
actual hunting, both outcomes over 32 seeds, complete control snapshots/RNG, three
rewrites and continuation. Different own-speed burn, grounded and arriving transfer
inputs remain atomic. All 162 affected tests pass. Reference: Craft::getFuelConsumption
zero escort-speed branch and Geoscape fuel-before-movement ordering, backed by
verified world craft/escortFuelRows. This is cycle 25 slice one; far/grounded
consumer proofs and the final batch audit remain pending.

Cycle 25 slice-one interim audit found no additional confirmed issue. Reviewed
owned OUT speed stability until fuel, independent source-removal returns,
zero/positive native branches, transfer/grounded exclusions, hunter eligibility
and ordered movement. All 4,663 root tests pass with zero failures/skips, including
checksums, private corpus and populated allocation gates. Full formatting/map/diff
pass and reference remains clean. Obsolete task-local fixture drafts were removed;
useful evidence/logs remain. The timed run stops after closing this validation.
Two execution slices and cycle 25's combined audit remain; 4c remains open and
4d is untouched.

Owned airborne escorts outside the native strict range now ignore the optional
leader's changing speed. Both coordinates remain unchanged until the fuel caller;
transfer/checkup positions retain their boundary. Fourteen fresh/cache cases
reproduce twelve guard failures, then cover both saved orders/takeoff, 32 seeds
and both source outcomes, exact own-speed burn/ordered pursuit, actual hunting,
complete control snapshots/RNG, three rewrites and continuation. Zero escort
range with coincident craft pins the strict comparison. Nearby differing caps
and transferred position alternatives remain atomic. All 176 affected tests pass.
Reference: GeoscapeState::time10Minutes strict getDistance comparison and
Craft::getFuelConsumption, backed by verified world and escortFuelRows captures.
Targeted formatting/map/diff checks pass. This completes cycle 25 slice two.

Grounded owned craft escorts now compose with optional leader speed changes.
Hourly/half-hour servicing either keeps them outside fuel use or relaunches to
an independent waypoint first. Twenty execution cases reproduce the guard, then
cover four service states, auto-patrol, zero/moving leaders, both saved orders
and both source outcomes, exact status/fuel/flags/takeoff, actual hunting, complete
control snapshots/RNG, three rewrites and continuation. Two waypoint-exhaustion
cases retain atomic identity checks. The earlier grounded zero-speed assertion
is replaced by this execution coverage; different airborne burn and transfer
checkup retain their boundaries. All 198 affected tests pass. Reference: Craft
repair/rearm/refuel/setDestination and Geoscape time1Hour/time30Minutes/time10Minutes
ordering, backed by verified maintenanceRows/hourlyMaintenanceRows/escortFuelRows.
Targeted format/map/diff pass. This completes cycle 25's third slice; combined
audit includes the earlier committed zero-speed slice a869570b.

## Optional craft fuel eligibility (2026-10-09)

Optional alien-base returns now allow unequal ordinary burns when both capped
fuel outcomes preserve the native low-fuel eligibility. Current fuel quantity
is not exposed by Craft::ScriptRegister or consumed by hunter attraction;
lowFuel/missionComplete remain the selection inputs. Primitive capped fuel
bounds avoid allocating speculative craft records. Changed leader speed still
requires the existing escort-consumer proofs, and near-home arrivals remain
separately guarded. Item fuel retains its identical one-unit burn.

Reference: pinned Craft.cpp getFuelConsumption/setFuel/isIgnoredByHK/ScriptRegister
and GeoscapeState time10Minutes. The hunting capture adds 96 native ordered
return/consumption traces with controlled limits 0/139; 139 is linked to the
native strategic-world getFuelLimit row at speedMax 2,100, craft (1, 0.6) and home
(0.4, 0.2). Every prior hunting row is unchanged. Twenty fresh/cache execution
cases reproduce the old guard, then cover low/inclusive/high/capped fuel,
already-low flags, takeoff, both research outcomes over 32 seeds, canonical
native output, actual hunting, complete control snapshots/RNG, three rewrites
and continuation. Two ambiguity cases keep the 150/160-unit boundary atomic;
older unequal-burn guard examples now use actual eligibility differences.
All 198 affected tests pass. This is cycle 26 slice one; escort fuel alternatives,
converged recall and their batch audit remain pending.


## Optional escort fuel eligibility (2026-10-09)

Owned airborne ordinary escorts now compose with an optional leader return when
both capped fuel outcomes preserve low-fuel eligibility. Saved craft order still
controls the speed consumed before the leader's own fuel recall. Transfer/checkup
alternatives, malformed negative fallback speed and differing low-fuel outcomes
retain atomic guards; grounded and distant escorts use their existing proofs.

Reference: pinned GeoscapeState::time10Minutes, Craft::getFuelConsumption/setFuel,
returnToBase and isIgnoredByHK. Forty-eight new native optionalEscortFuelRows retain
all earlier hunting rows. Eight fresh/cache execution cases cover both saved
orders and takeoff, low/high/capped/already-low fuel, both research outcomes over
32 seeds, canonical native output, ordered movement, actual hunting, complete
control snapshots/RNG, three rewrites and continuation. Two boundary cases cover
150/160 fuel and negative fallback speed, reproducing the missing guard before
its correction. Prior guard examples now demonstrate actual eligibility differences.
All 208 affected tests pass. This is cycle 26 slice two; converged recall and the
batch audit remain pending.


## Optional fuel recall before arrival (2026-10-09)

Optional alien-base returns now allow near-home arrivals when both fuel burns
empty the tank and force the same home destination/full speed before movement.
The existing escort-consumer proofs still run first. Already-low craft skip the
native automatic recall and retain the arrival guard; differing low-fuel outcomes
and malformed speed/coordinates remain atomic. No research draw is forecast.

Reference: pinned GeoscapeState::time10Minutes and Craft::consumeFuel/setFuel,
returnToBase, think and checkup. Twelve new native optionalRecallArrivalRows use
controlled at-home reachability/inert movement and the exact native think/checkup
bodies, retaining all earlier hunting rows; world equality is captured separately.
Four fresh/cache execution cases reproduce the arrival guard, then cover fuel
0/1/10, takeoff 0/5, both research outcomes over 32 seeds, canonical native arrival
and servicing, no hunter selection, full control snapshots/RNG, three rewrites
and continuation. Two boundary cases retain uncertain recall and already-low
arrival refusal. All 214 affected tests and full formatting pass. This completes
cycle 26's third implementation slice; the batch
audit and root validation remain pending.


Cycle 26 batch audit closes the own-fuel/escort-fuel/converged-arrival slices
608f20ba, f8456ba5 and 19d733ae. No additional confirmed runtime defect was found.
Reviewed native saved order, strict escort distance, inclusive low-fuel thresholds,
installed fuel capacities, positive/zero/negative fallback domains, early arithmetic
validation, takeoff arrival/checkup, current-fuel script visibility, hunting eligibility,
resource accounting and repeated saves. All 4,758 root tests pass with zero
failures/skips, including native checksums, private corpus and population/allocation
gates with unchanged bounds. Full formatting, regenerated code-map and diff checks
pass. The pinned reference is clean; obsolete probe objects were removed. Optional
arrival/transfer and wider composition guards still keep 4c open; 4d is untouched.

## Stock-equivalent free research choices (2026-10-10)

Daily stock consumers can now compose with distinct random free rewards when
every bounded outcome has identical consequences: per-base stock deltas,
ordered arrivals, transfer identity, ordered custom-counter changes, score and
diary-entry count. The existing pure daily discovery engine projects each choice
path; no script or RNG is executed, rewound or provisionally committed. At most
4,096 path nodes and one million charged work units are supported, with prefix
storage charged before allocation. Exact discovery callers still refuse uncertain
research identities; the stock-only result exports no completed/status/project
identity. Execution chooses and persists its actual reward normally.

Pinned SavedGame::selectGetOneFree/addFinishedResearch, the daily primary-topic
caller and item/counter effects add eight researchEquivalentRewardRows. They
cover both selected identities, duplicate labels and zero/minimum versus positive
quantities. Eight fresh/cache cases cover 32 explicit restored seeds, common
projection purity, same-day item-fuel refuelling/auto-patrol/hunting, exact native
output, three save rewrites and continuation. Two further cases preserve atomic
refusal of unequal quantities, counter traces and score, exact-identity requests,
exhausted work and oversized branching. Previous native rows remain unchanged.
This is cycle 28 slice one; broader ordered execution and batch acceptance follow.

Validation: the ten reward/limit cases and all 470 existing hunting cases pass. Full formatting verification, regenerated code-map and diff checks pass. Exact discovery callers retain their separate uncertainty boundary.

### Convergent free-reward discovery projections (2026-10-10)

Exact discovery consumers now share bounded reward branching when every terminal
branch agrees on completed topics, status entries and active project sets, as well
as the existing ordered item/arrival/counter and scalar consequences. Actual
execution still selects its reward and records its own diary order. A common
completed set alone is insufficient: disable/reenable ordering can leave different
statuses and remains refused before mutation.

Reference: SavedGame::selectGetOneFree/addFinishedResearch, its zero-cost dependency
closure and GeoscapeState::time1Day primary side effects. strategic-hunting adds
four convergent-reward and two convergent-status rows; previous rows are unchanged.
Twenty reward tests pass, including fresh/cache, both actual choices across 32 saved
seeds, same-tick site expiry, hunter relaunch, pure forecasts and three save rewrites.
No event, combat, deployment or campaign-month boundary is enabled.

Validation: all 306 existing site-research cases pass; full formatting verification, code-map and diff checks pass. Useful captures remain; the temporary probe object was removed.

### Weighted duplicate reward proof paths (2026-10-10)

Pure daily projections now retain only the first candidate index for each distinct
reward identity at a given branch. The actual selector still consumes the full
ordered candidate list, retaining duplicate weights and protected-list semantics.
Every label is charged to the work budget, and stored distinct paths remain capped
at 4,096 nodes. Repeated labels therefore do not unnecessarily exhaust the path
limit; 4,097 distinct choices still refuse before unbounded expansion.

Reference: SavedGame::selectGetOneFree and its ordered candidate construction.
strategic-hunting adds four controlled endpoint draws with 3/4,096 repeated labels;
all previous rows are unchanged. All 24 reward cases pass, including explicit
saved seeds for both weighted outcomes, common stock purity, hunter relaunch,
three save rewrites, unequal consequences and distinct-identity/work limits.
No runtime selection, cache schema or later-branch boundary changes.

### Reward projection batch audit (2026-10-10)

The three-slice audit found no confirmed runtime defect. Root verification ran
4,806 cases: 4,804 passed and two old work-limit assertions expected the later
custom-counter diagnostic. The new bounded reward search runs earlier in diary
preflight and refuses this 1,001-base repeated-reward workload there instead.
The exact diagnostic assertion was updated; repeated-attempt snapshot equality
and the original work limit remain unchanged. Score prefix safety remains with
the existing signed-capacity owner; equal net forecasts do not waive it.

Reviewed candidate order/weights, nested prefix indexing, common consequence
comparisons, pure state copies, exact research consumers, event and resource
guards, and native pointer/automatic-discovery evidence. Multi-base repeated
choices remain a useful additional integration case for the following cycle.
Full root rerun follows the diagnostic correction.

Cycle acceptance: root rerun passes all 4,806 tests with zero failures or skips
in 6m17s725ms. Full formatting verification, code-map, diff, fixture checksums,
private corpus and unchanged allocation/endurance gates pass. The reference tree
remains clean; no unexpected access denial occurred. Branch 4c stays open for
wider consumed-consequence, stock/research and transfer composition and final
guard reconciliation. Branch 4d is untouched.

### Consumed stock projection metadata (2026-10-10)

Daily stock projections now explicitly omit scalar metadata unless the caller
requests exact research discovery state. Nullable score/diary fields prevent a
stock-only projection from presenting one uncertain reward as the scalar result.
Stock-only consumers require equal ordered stock/arrival/transfer/counter effects;
exact consumers still require all scalar and research consequences to agree.
Actual execution retains the selected score, lookup discoveries and diary order.

Reference: SavedGame::selectGetOneFree/addFinishedResearch and time1Day bonus
lookup/primary callers. Four strategic-hunting scoped reward rows and four native
strategic-research-diary-execution stockScopeRows cover unequal points and unequal
lookup diaries with equal stock. All previous native rows are unchanged. Twenty-
eight reward tests pass, including both selected outcomes across 32 saved seeds,
actual native score/diary comparison, hunter relaunch and three save rewrites.
Score overflow and diary identity exhaustion still stop before mutation. This
changes only internal forecasts; no save/cache schema or later-branch boundary.

### Empty custom-counter names in reward projections (2026-10-10)

Projected primary counter traces now omit empty names, matching native
increaseCustomCounter/decreaseCustomCounter short-circuits. Input entries still
consume work, and every nonempty increase/decrease retains declaration order.
Equivalent free rewards can therefore share stock proofs when their only counter
difference is an unconsumed empty name. Execution remains with the shared native
counter operation; no empty saved identity is fabricated.

Reference: SavedGame.cpp custom-counter methods and time1Day primary side-effect
loops. Six strategic-hunting rows cover empty increases, decreases and both, with
absence of empty keys and unrelated counter conservation. Fresh/cache cases
observe both actual reward choices across 32 saved seeds, ordered forecast effects,
hunter relaunch and three save rewrites. Saved empty identity names remain subject
to the existing malformed-input validation. Previous native rows are unchanged.

### Multi-base reward and arrival composition (2026-10-10)

Twenty-four strategic-hunting native rows cover two nested selections from three
reward identities across two/three finishing bases, with existing incoming item
transfers. They run the native reward selector, discovery, primary spawn/counter
callers and Transfer::advance in reference order. Sixteen fresh/cache execution
cases observe all six ordered paths across 32 saved seeds, both saved base orders,
scarce item-fuel relaunch/hunting, transfer identity totals, actual stock/counter/
score outputs, three rewrites and stepped/batched pursuit continuation.

Three bases exhaust the pool and agree on exact completed/status/project state;
two bases retain differing identities and remain refused for exact consumers.
Two further cases compose repeated future events with the common closure,
preserve their identities/countdowns over rewrites, and reserve the due branch 6
payload boundary before mutation. All 18 new composition cases pass. Reference
GeoscapeState daily/hourly/half-hour fallthrough and existing strategic-event
fixtures establish the unchanged event boundary. Previous native rows are unchanged.

Slice verification: all 34 reward cases and all 18 composition cases pass. Full formatting verification, regenerated code-map and diff checks pass; the pinned reference remains clean and obsolete probe objects were removed.

### Consumed-consequence cycle batch acceptance (2026-10-10)

The batch audit found no confirmed defect. Root dotnet test passes all 4,834
tests with zero failures/skips in 6m28s867ms. Reviewed nullable metadata consumers,
exact versus stock-only contracts, nonempty counter order, signed score/diary
capacity checks, nested saved-order choices, hourly transfer delivery, identity
counts, reload equality and deferred event boundaries. Full formatting, regenerated
code-map, diff, fixture checksums, private corpus and unchanged allocation/endurance
gates pass. Useful captures/executables remain and obsolete objects were removed;
the pinned reference is clean. No unexpected access denial occurred.

This closes cycle 29. Wider stock/research/site and production/transfer composition
and final guard reconciliation still keep branch 4c open; branch 4d is untouched.

### Empty-only fast stock proof (2026-10-10)

The existing no-stock-change fast proof now checks effective nonempty counter
names after charging every increase/decrease input label. Empty-only lists no
longer force full reward replay; nonempty operations still require the ordered
projection. This permits a common stock proof within the caller's remaining
budget without selecting research or omitting input work. Exact discovery callers
retain their full projection and do not use this shortcut.

Reference: SavedGame::increaseCustomCounter/decreaseCustomCounter and the daily
primary callers. Four strategic-hunting researchEmptyOnlyRewardRows cover zero
and 10,000 empty labels, both reward choices, unchanged item/transfer/counter
state and unequal scores. All previous native rows are unchanged. Thirty-eight
reward cases pass, including a 985,000-step starting budget, exhausted-work
refusal, pure omitted metadata, actual item-fuel relaunch/hunting, both saved
reward outcomes across 32 seeds and three save rewrites. This is cycle 30 slice
one; its remaining slices and batch audit are still pending.

Slice verification/interim review: all 470 hunting regressions also pass, including
refund/corpse, nonempty counter and work-limit cases. Full formatting, code-map
and diff checks pass; no confirmed issue was found. Useful native captures remain
and the obsolete object was removed. The next two slices and cycle batch audit
are pending; the latest full root gate is the prior 4,834-test cycle acceptance.

### Ordered stock-only research arrivals (2026-10-10)

Stock-only projections now canonicalize non-BT_NONE item arrivals while preserving
all duplicate transfer entries and the ordered BT_NONE subsequence. Exact discovery
projections retain native order. Reference Craft::reuseItem can mark only the first
of two different ammunition types before its status becomes rearming, so reusable
arrivals cannot be treated as a multiset. ItemsArrivingState excludes every other
battle type from reuse; their positive stock additions and identity counts commute.
The sort, classification and copying consume the shared work budget before allocation.

Reference: GeoscapeState::time1Day/time1Hour, primary research spawned-item callers,
Transfer::advance, ItemsArrivingState and Craft::reuseItem at the pinned revision.
All stock-only consumers were reviewed: counter and production identity callers
consume counters; hunting consumes positive arrivals, transfer counts and the
reusable subsequence after hourly service. Exact world/diary/price consumers remain
ordered. Four native researchOrderedArrivalRows cover reversed CLIP/SUPPLY lists
and duplicate clips, with exact transfer order, quantities and score. Fresh/cache
execution cases observe both actual choices, exact ordered projections, identity
multiplicity, fuel relaunch/hunting, exhausted-budget purity and three save rewrites.
This is cycle 30 slice two; broader composition and batch acceptance remain pending.

Slice verification: four new arrival-order cases, full formatting, regenerated
code-map and diff checks pass. Previous native rows are unchanged; reference
checkout remains clean. No confirmed issue in interim consumer review.

### Reusable-order and bounded population acceptance (2026-10-10)

The stock-only arrival canonicalizer now charges classification first and avoids
sort work and scratch allocations when every arrival is reusable. Otherwise it
charges the sort for the actual non-reusable count and bounds allocation/copying
before creating lists. This preserves existing order while permitting equivalent
1,000/10,000-item reward lists within a 940,000-step starting budget. Every transfer
remains distinct; quantities and the next transfer identity match execution.

Two native researchReusableArrivalRows prove that reversed BT_NONE ammunition
lists set different weapon rearming flags, despite equal stock totals. Fresh/cache
cases retain their ordered projections, refuse the ambiguous hunter tick without
mutation, and execute/rewrite the actual native outcome when no hunter consumes
that forecast. Two researchLargeReusableRows capture positive stock totals, raw
transfer counts and scores. Four large-input cases observe both reward identities
within 32 fixed saved seeds, exhaustion purity, relaunch/hunting and three rewrites.
The initial two chosen seeds produced the same reward; the test now searches the
bounded fixed range and still requires both identities. No assertion was weakened.

All ten arrival-order cases pass. The native fixture collaborator required an
explicit zero selection for its single-candidate large-list trace (its controlled
RNG stub does not honor bounds); the pinned selector itself is unchanged. All prior
native rows remain identical. Cycle 30 batch audit/root acceptance follows.

Slice verification: full formatting, regenerated code-map and diff checks pass.
All ten arrival cases passed after the seed coverage correction; the final edit
only fixes initializer layout. No unexpected access denial; reference clean.

### Arrival-order cycle batch acceptance (2026-10-10)

The cycle 30 batch audit found no additional confirmed runtime issue. Root dotnet
test passes all 4,848 tests with zero failures/skips in 6m26s809ms. Full formatting,
regenerated code-map, diff, fixture checksums, private corpus and existing populated
allocation/endurance gates pass with their limits unchanged. Reviewed every
stock-only consumer, BT_NONE reuse ordering, duplicate transfer identities, positive
stock/range checks, pure work/RNG/state and exact-versus-common projections.
Useful native captures remain; the obsolete object was removed and the reference
checkout is clean. No unexpected access denial occurred.

This closes cycle 30. Remaining production/transfer and wider consumed research/site
compositions plus final guard reconciliation keep 4c open. Branch 4d is untouched.

## Bounded exact daily states for world consumers (2026-10-10)

Cycle 33 slice one adds a research-owned pure world projection while preserving
the existing strict stock/exact projection contract. When native daily reward
paths share stocks, ordered arrivals, counters and transfer IDs, the new operation
retains each distinct completed/status/active/score/diary state. Duplicate equivalent
states collapse; distinct world states are bounded at 256, alongside the existing
4,096 prefix and million-step limits. The summary contains only guaranteed completed
identities and omits uncertain scalar/status/active metadata. It never selects a
reward or consumes campaign/RNG state. World caller integration follows separately.

Reference: GeoscapeState::time1Day core diary, selected bonus/lookup then core/lookup
discovery, disabled-project cleanup and primary side effects; SavedGame::selectGetOneFree
and addFinishedResearch. Existing pinned strategic-hunting researchScopedRewardRows
and strategic-research-diary-execution stockScopeRows establish unequal scores and
bonus lookup diary states before implementation. Four fresh/cache cases preserve
both exact outcomes across twelve saved seeds, canonical native consequences/diary
order and three save rewrites; the strict projection still refuses these differences.
Two stock/counter cases retain refusal. The 256/257 distinct-state boundary passes
and refuses intentionally, and every forecast/work-exhaustion check is pure. All
eight cases, full formatting, regenerated code-map and diff checks pass. Interim
review preserves strict callers, collapses equivalent terminal states before the
world population bound and charges the frozen summary copy. Site integration and
composed acceptance remain pending.

## Daily exact alternatives through site grants (2026-10-10)

Cycle 33 slice two connects the cached world daily projection to site research.
MissionResearchForecast initializes every retained exact state, publishes their
intersection/union, and keeps each path's active projects through item alternatives
and grant cloning. Common stock effects remain shared. Each copy/summary is charged
before allocation, and the existing 256-outcome/million-step guards remain intact.
The strict price/diary projection still requires one common state.

Four new pinned strategic-hunting researchDailySiteAlternativeRows compose native
time1Day bonus/lookup/core and primary side effects with a later site-source
addFinishedResearch grant and a conditional zero-cost dependency. Four fresh/cache
execution cases first reproduced the deterministic-daily-phase guard, then compare
actual item/counter/score/research states and daily/mission diary order across twelve
saved seeds. Both paths persist through three rewrites and continued pursuit. All
prior native rows remain unchanged. All twelve daily-world projection/integration
cases, full formatting, code-map and diff checks pass. Interim review confirms
only the world operation exposes alternatives, guarantees are intersections and
active project context survives each clone. Event/active/capacity composition and
root batch acceptance remain pending.

## Daily/site all-path event, active and capacity acceptance (2026-10-10)

Cycle 33 slice three verifies the new world alternatives through their consumed
limits and event boundary. Two new native researchDailySiteActiveRows capture
getAvailableResearchProjects active-project exclusion and the time1Day disabled
project cleanup before a site grant re-enables the topic. Removal is controlled
and nonholding in this fixture; the original bodies determine exclusion/cleanup
order. All older native rows remain unchanged.

Two fresh/cache cases keep an active project per daily path: one selected reward
disables/removes it, allowing later re-enable and automatic discovery, while the
other keeps it active. Actual project/status/research/score/counter and diary state
match the trace across twelve saved seeds and three rewrites. Eight event cases
require interruption on every path: the unconditional site grant suppresses safely,
while conditional automatic discovery cannot suppress an unavailable payload.
Two score cases refuse when only one path exceeds the range and allow exact
headroom. One direct/transaction case preserves diary and shared work limits with
no campaign/RNG mutation. The world-owned diary check precedes the general daily
check, so the test retains its exact earlier diagnostic. All thirteen cases, full formatting, code-map and diff checks pass. The combined
batch audit and root acceptance follow.

## Daily/site alternatives batch audit (2026-10-10)

The cycle 33 audit confirmed six obsolete deterministic-phase guard assertions
(two saved-site and four optional-source generation cases). The first root run
passed 4,952 of 4,958 tests, with only those six failures. The saved-site tests now
retain an actual unavailable payload whose interruption is not guaranteed by the
daily reward. The optional-source tests now execute both deleted/surviving paths
and compare full snapshots against deterministic controls across twelve saved
seeds, daily/late-hunting site horizons, three rewrites and subsequent progression.
The later hunting site remains until its next native site caller. All four optional
cases and all twenty daily/site cases pass. The corrected root run passes all
4,958 tests with zero failures or skips. Full formatting, regenerated code-map
and diff checks pass before the separate audit correction commit.
No additional runtime defect has been confirmed.

## Daily research arrival quantity bounds (2026-10-10)

The world-only daily projection retains exact discovery states when item arrival
quantities vary but their identities, order, transfer population, refunds and
counter operations agree. Its immutable summary carries upper quantities and
per-base/item maximum-minus-minimum deltas. Exact branch arrivals participate in
outcome deduplication. Strict stock and exact metadata consumers retain their
original contracts; 256 world states, 4,096 choice prefixes and the shared million
step limit remain. Arrival comparisons are charged only after discovery states
match, preserving the existing 256-state acceptance.

Craft forecasts add daily uncertainty after hourly craft/facility servicing and
before production, item refuelling and site research. Every affected craft must
have the same complete projected state at both stock endpoints. Site needed-item
proofs retain zero/present alternatives. A failed cheap stock proof can use the
cached world projection, charging its work to the shared caller budget. Native
SavedGame::handlePrimaryResearchSideEffects, Transfer::advance, Craft::refuel and
addFinishedResearch/getAvailableResearchProjects pin four quantity/site traces in
strategic-hunting. They retain two transfers, primary counter order and actual
selected rewards. Six new fresh/cache cases pass with twelve saved seeds, three
rewrites, subsequent progression, strict-consumer refusal and malformed arrival
population coverage. All 38 strict-stock and 12 bounded-world regressions and full
formatting pass; hunting regression validation precedes the slice commit.

The hunting regression run passed 692 of 694 cases; its only failures were two
obsolete stochastic quantity guard assertions. Those now pass with exact selected
branch, native fuel/hunting, other-base project retention and three-rewrite checks.
Final full formatting, code-map and diff verification pass. Root acceptance remains
at the three-slice batch boundary.

## Correlated daily item context for site research (2026-10-10)

Each retained daily discovery state now carries its own first-base item context
through site grants. Starting from the common post-servicing upper stock, the
forecast subtracts that branch's missing arrival quantities. It separates the
daily deltas from remaining craft/production uncertainty, retaining zero/present
masks only where that branch can actually cross zero. Immutable stock contexts and
active-project contexts survive grant cloning and optional sequences. All copies,
arrival scans and masks stay within the shared work and 256-outcome bounds; invalid
or negative contexts fail before campaign mutation.

Four native researchDailyQuantityContextRows couple selected A/B rewards, one/two
item arrivals, refuelling and a later item-required automatic site dependency.
The new regressions reproduced the old false score-overflow refusal: the item is
absent in the only branch satisfying the research dependency. Native fixture setup
now enables the explicit needItem flag alongside its needed-item link; all prior
trace values remain unchanged. The direct site proof removes that impossible
outcome, while the separate general daily score reserve remains conservative.
Six fresh/cache cases pass, including actual selected score/stock/diary identity,
twelve seeds, three rewrites, subsequent ticks, unrelated uncertainty, malformed
stock contexts and exhausted work. Site regressions and full formatting precede
the commit; combined root acceptance follows the third slice.

All 350 site-research regressions, six new context cases, full formatting,
regenerated code-map and diff verification pass before the slice commit.

## Common daily counters with variable item quantities (2026-10-10)

Daily counter validation and produced craft/soldier population checks now consume
the common ordered counter sequence from the world projection when strict stock
projection cannot agree on quantities. The cheap strict path remains first;
failed proof work and the fallback's branch work share the same million-step
budget. Different counter operations, refunds, arrival identities/order or transfer
population still do not authorize an exact sequence. Price and diary scalar
consumers retain their strict contracts.

Twelve researchQuantityCounterBirthRows use native primary counter order, variable
item transfers, produced craft/person delivery and later refuelling. The controlled
allocation collaborator is seeded from the actual native counter result. Initial
one, twenty and int-max-minus-one counters cover the floor and final safe birth.
Four fresh/cache execution cases reproduce the old false counter-overflow refusal
and now match native identities, stocks and pending personnel across twelve seeds,
three rewrites and subsequent ticks. Six capacity cases preserve atomic counter
intermediate overflow, insufficient craft/soldier identity headroom and upper
arrival-stock overflow; the earlier world craft-birth guard keeps its precise
priority. All ten cases and 27 counter regressions pass. Full formatting and the
three-slice batch audit follow.

Final formatting verification, regenerated code-map and diff checks pass before
the third slice commit. The batch audit/root gate remains pending.

## Daily quantity composition batch audit (2026-10-10)

The three-slice root run passes 4,980 tests with zero failures or skips. Batch
review confirmed a resource-limit defect in the new hunting fallback: a failed
cached world projection discarded its actual work count. A fresh/cache ordinary
fuel scenario with 257 varying item rewards and 800 inert rules exhausted the
projection budget but still advanced the tick. The separate audit fix stores
work for successful and failed cache results, resets it at BeginWorld and always
charges the hunting caller. Two regressions now refuse atomically, repeat the
refusal and verify safe progression matches a restored control after cancellation.
No RNG or campaign state is consumed by the failed proof.

Reviewed native caller order, arrival identity/order/population, quantity and
intermediate counter ranges, full-state paired craft proofs, reached material
consumption, branch-context cloning, active-project exclusions and event guards.
All older native trace values remain unchanged; the captured quantities and
counter allocation collaborators are explicit. The corrected root run passes
all 4,982 tests with zero failures or skips. Full formatting, regenerated
code-map, diff and pinned reference cleanliness checks pass before the separate
audit fix commit. Wider composition and final guard reconciliation keep 4c open.

## Ordered daily score prefixes (2026-10-10)

Pinned GeoscapeState::time1Day selects and scores a free reward before the
primary topic; SavedGame::addFinishedResearch applies each automatic discovery
and addResearchScore mutates the signed integer score. Four native
researchDailyScorePrefixRows capture positive and negative reward-first prefixes,
final deltas, quantities and an unreachable automatic dependency. Exact pure
branches retain their score prefix extrema; world summaries bound every branch
and deduplication retains distinct prefixes. Strict final-scalar consumers keep
their existing contracts.

The cheap conservative daily reservation remains first. Only its range failure
can invoke the bounded world proof, with shared work accounting and no RNG or
state mutation. Every intermediate prefix must fit before the final interval is
reserved for later production. Ten fresh/cache cases reproduce six old false
refusals, preserve four final/intermediate overflow refusals and verify both
reward choices across twelve seeds, repeated rewrites and subsequent ticks. The
site context case excludes an unavailable automatic discovery from the daily
reservation. All ten cases and six score-capacity regressions pass; the
three-slice batch audit/root gate follows. No cache or save schema change.

## Bounded daily diary capacity (2026-10-10)

Reuses the pinned strategic-research-diary-execution stockScopeRows: native
time1Day records primary diary entries before free rewards and their lookups,
while SavedGame::addFinishedResearch records automatic discovery sources. The
score-prefix fixture independently demonstrates an inactive automatic dependency.
The strict exact or cheap conservative diary proof remains first; only a
capacity failure attempts bounded world alternatives. Monotonic entry growth
allows their maximum exact count, with the same shared work budget. Unequal
lookup counts, unreached automatic topics and later site grants retain their
actual ordered discovery context without selecting RNG.

Twelve fresh/cache cases cover tight sequence headroom, twelve seeds and both
reward choices, three rewrites and subsequent ticks, unsafe alternative counts,
site discovery capacity and the full gameplay population ceiling. The huge
population snapshot restores exactly; its serialized YAML intentionally exceeds
the independent one-million-node reader limit, which the test preserves rather
than raising. Ordinary transition rewrites remain covered by the identity/site
cases. All 30 diary execution/persistence regressions pass. Batch audit/root
validation follows; no content cache or save schema change.

## Guaranteed daily score before production (2026-10-10)

Sixteen native researchDailyProductionScoreRows compose reward-first daily
research with Production::step score-per-completion and money continuation
checks. Four/five requested units and funding stops expose the distinction
between actual and merely possible completions; every prior native trace stays
unchanged. GeoscapeState time1Day falls through to time1Hour before world sites.

Only a later production score refusal retries with a guaranteed daily final
interval, after all ordered daily prefixes fit. Both attempts share one work
budget. ResearchScoreCapacity moves its baseline only through this checked
guaranteed transition; ordinary Reserve still never credits opposite-sign
optional completions. Eight fresh/cache execution cases match the native final
delta and refuel stocks across twelve seeds, three rewrites and subsequent
ticks. Four refusal cases preserve unsafe production counts and optional
offsets that could stop before creating the claimed headroom. All twelve cases
and eight score-capacity unit cases pass. The batch audit/root gate follows,
including review of subsequent site-score composition. No cache/schema change.

## Daily score and diary batch audit (2026-10-10)

Audit confirmed a score composition defect: site research forecasts omitted
preceding hourly production scores. Six fresh/cache daily/hourly regressions
exposed missing atomic refusal, including runtime exceptions after production
had already mutated state. The separate audit correction adds a gameplay-owned
production score-bound operation, retaining actual projected staffing and
including zero through all requested completions. Every ordered site research
score mutation checks both production endpoints. Optional opposite-sign
completions remain uncredited; direct grants use their actual current state.
Shared work is charged before staffing, rule validation and output scans, and
wider accumulation fails intentionally before narrowing.

Sixteen native researchDailyProductionSiteScoreRows extend the prior production
traces through actual addFinishedResearch site scoring; all older rows are
unchanged. Twelve new cases preserve daily/hourly atomicity, safe combined
endpoints across twelve seeds and repeated rewrites, optional funding stops and
pure work exhaustion. All twelve new cases and 350 site-research regressions pass. The corrected
root gate passes all 5,030 tests with zero failures or skips; full formatting,
regenerated code-map, checksums, diff and pinned reference cleanliness checks
pass. Reviewed signed prefix/final bounds, guaranteed versus optional effects,
diary limits, transfer shape, work sharing, strict scalar consumers and subsequent
site mutations. Wider counter/stock composition and final guard reconciliation
keep 4c open; no 4d or later-branch effects are enabled.

## Ordered daily counter alternatives (2026-10-10)

Pinned GeoscapeState time1Day applies primary counter effects in catalog order;
SavedGame::increaseCustomCounter/decreaseCustomCounter preserve absent-counter
initialization and the floor at one. Twelve native researchDailyCounterAlternativeRows
capture unrelated names and convergent ordered traces at initial one, twenty
and int-max-minus-one, alongside variable quantities and refuelling.

World-only daily projections now retain differing counter traces with an
explicit CounterChangesAreCommon flag. Distinct traces participate in bounded
deduplication; an empty noncommon summary never means inert counters. Strict
stock/scalar consumers retain their existing contracts. Common-counter callers
fall back to their conservative capacity proof when traces vary, and hunting
passes a null exact trace so production uses its existing counter envelopes.
Shared refunds, arrival identities/order/population and 256-state/4096-prefix/
one-million-work bounds remain mandatory.

Six fresh/cache cases reproduce the old fuel/site projection refusal and match
native effects across three counter headrooms, twelve seeds and both choices,
three rewrites and atomic overflow refusal. Obsolete counter-equality guard
assertions now verify executable counters and reloads while unequal arrival
shape stays blocked. All six cases, 38 strict stock and twelve world-alternative regressions pass.
Full formatting, regenerated code-map and diff checks pass; the cycle batch
gate follows. No content
cache or save schema change; 4c remains open.

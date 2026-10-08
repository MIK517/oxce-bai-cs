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

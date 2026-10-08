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

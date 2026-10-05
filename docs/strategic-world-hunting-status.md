# UFO hunting and escort integration

Branch 4c. Pinned reference: 4df3a5e571a1a4b5e8a46d3161fb2e21a2adba15.

Current maintenance composition supports repairs, deterministic craft/facility ammunition,
ordered transfers, item reuse, shared refuelling and escort fuel. Research-owned
forecasts cover stock-free completions, held-item refunds, global obsolete
and disabled-project cancellation, fixed spawned item arrivals, lookup and automatic
discoveries, disablers/re-enablers and deterministic free/protected-free rewards. Non-fallback
production without a completing unit, scored fixed/sold item output, deterministic random
output, scientist/engineer transfers, recurring paid/item-input units and fallback staffing
participate in hunter forecasts. Research forecasts also include native corpse recovery.
Remaining projections cover scripted sale prices, genuinely different random output,
craft/soldier production, stochastic shared ammunition and random research reward choices.
The progress notes below record earlier guards and their later replacements.

The strategic-hunting capture inserts Craft::getHunterKillerAttraction,
Ufo::insideRadarRange and GeoscapeState::ufoHuntingAndEscorting's selection
fragment verbatim. Controlled collaborators supply effective capacities, craft
eligibility and distances. Ninety-six attraction rows and fourteen selection
rows pin both hunt modes, strict saved-order comparisons, original-target ties,
ignored/undetectable exclusions, zero radar and inclusive range.

The gameplay-owned selection operation preserves those rules. Arithmetic outside
signed 32-bit range and zero divisors fail intentionally. Captured outputs and
malformed inputs have focused compatibility tests. The retarget and integration slices below connect the operation to loaded actors.
Combat remains branch 5; branch 4d is untouched and the headless gate remains open.

The retarget slice adds reference destination backup, hunt speed, escort speed and
waypoint restoration operations. Captured transitions retain the original waypoint
across hunt-to-escort changes and leave speed unchanged on restoration. Explicit
selected speed controls the five-second/one-minute fast path; ten-minute retargeting
is always due. Mod::hunterKillerFastRetarget defaults true and survives fresh/cache
projection. The redundant-HK-alert option defaults true, honors fixed user options
and persists through the save adapter. Compiler revision 33 invalidates stale caches.
The option and transition fixtures cover fresh/cache projection and reload;
the live integration is described below.

Live imported hunters and escorts now use registered ten-minute retargeting after
base hunting, plus the explicit slow-speed fast path before five-second movement.
Retargeting honors projected fuel returns, preserves original waypoints, allocates
contact markers and publishes gameplay-owned hunt notifications. Alert suppression
does not prevent revelation or targeting. Movement follows craft/UFO positions in
reference list order; snapshot capture resolves final target coordinates without
changing movement. Deleted escort targets restore their original waypoint.

Combat arrivals stop atomically before mutation. Retargeting involving same-tick
grounded auto-patrol relaunches or airborne craft transfers retains specific
forecast guards. Escort chains/cycles and malformed originals fail intentionally.
Deletion scans are bounded before nested work. Grounded non-escort waves and
airborne non-escort waves with no possible craft target execute through the
constructor/spawn slices below. Creation-time escorts and candidate-bearing
airborne waves retain their atomic forecast boundary.
The headless gate remains open and branch 4d is untouched.

The three-slice batch audit reproduced a same-tick boundary bypass: a hunter's
new revelation was absent from an imported pursuer's craft-arrival forecast.
Craft interception checks now use the retarget projection before any mutation.
Regression tests also reject signed 32-bit intermediate attraction overflow even
when later terms would produce an in-range result; each C++ operation is bounded.
The root allocation gate caught eager closure allocation in the escort forecast
on ordinary flight. An explicit identity scan removes that allocation, preserving
the existing 8/32/128-UFO budgets. Handler-order assertions include both registered
retarget callers without weakening their exact ordering.

Final cycle validation passes all 1,692 root tests with no failures or skips,
including fixture checksums, all 19 staged-save classifications, 72-hour composed
scenarios and populated allocation budgets. Formatting verification, code-map
validation and diff checks pass. The pinned C++ checkout remains clean; obsolete
probe object output was removed. Branch 4c remains incomplete and 4d is untouched.

Hunter constructor initialization is now an executable gameplay operation.
The pinned Ufo constructor capture has 192 rows covering nonpositive percentages,
failed/successful rolls, percentages above 100, literal modes/behaviors and the
ordered optional random selections. Failed rolls consume no mode/behavior draws;
nonpositive percentages consume no RNG. Supply objective callers retain the
reference constructor defaults. Positive hunter spawning remains guarded until
the following slices connect safe wave contexts.

Grounded non-escort hunter waves now use the captured constructor operation
before geometry selection. Wave values inherit rule defaults only at -1.
Initial ground timers above one keep retargeting outside the creation tick;
fresh/cache tests cover constructor metadata, ground countdown, lift and reload.
Airborne hunter creation and spawn escorts remain precisely guarded.

Airborne non-escort hunter waves now execute when existing craft and undelivered
craft transfers cannot supply a current or auto-patrol target. That structural
proof keeps the creation-tick path ordinary; later dispatch uses the loaded
hunter retarget capability. Rule-derived hunter percentage/mode/behavior and
fresh/cache creation/targeting reloads are covered. Candidate-bearing airborne
spawns and creation-time escorts retain the existing atomic wave boundary.

The constructor/spawn batch audit confirmed an empty hunter trajectory could
index its first waypoint before the existing minimum-length guard. Length and
altitude validation now precede hunter context checks. Empty/one-point regressions
assert an unchanged campaign and RNG, and a separate auto-patrol regression
checks that a possible same-tick relaunch cannot enter the no-candidate path.

The previous supply constructor-choice fixture now executes its formerly guarded
no-candidate hunter cases and asserts captured hunter flags, counts, live links
and reload. Objective supply ships still assert the default non-hunter
constructor. Mode/behavior capture draws now alternate so swapped draw order
cannot pass the shared canonical oracle comparison.

Final constructor/spawn cycle validation passes all 1,701 root tests with no
failures or skips, including checksums, private classifications, endurance and
populated allocation checks. Formatting verification, code-map validation and
diff checks pass. The reference checkout is clean and obsolete probe object
output was removed. The remaining 4c headless gate is open; 4d is untouched.

Airborne hunter waves now accept existing airborne craft ignored by the pinned Craft::isIgnoredByHK rule (low fuel or mission complete). Four captured truth-table rows and fresh/cache spawn reloads cover this extension; active targets remain guarded.

New airborne hunters also accept undetectable existing craft, which the pinned saved-order selection trace excludes before attraction evaluation. Fresh/cache tests assert the effective rule, non-hunting spawn and reload. Original targets retain their separate reference semantics; transferred craft remain conservative.

New flying hunter waves reserve their aggregate craft-scan work together with loaded retarget actors before the creation tick. A reproduced 1001-by-1001 workload previously threw after creation; it now blocks without state or RNG changes. The no-target structural scan is cached only for the current preflight/tick, keeping immediate chains bounded.

The fourth batch audit checks returning/undetectable exclusions and per-tick cache lifetime. An hourly-production reservation test covers the finite queue upper bound. Suspected multi-craft output undercount was rejected: runtime linking already enforces exactly one craft per production unit. No additional gameplay defect was confirmed in these three slices.

Final fourth-cycle validation passes all 1,710 root tests with zero failures or
skips. Fixture checksums, private-corpus classifications, 72-hour scenarios,
allocation budgets, formatting verification, code-map and diff checks pass.
The pinned C++ checkout remains clean, and obsolete capture object output was
removed. Branch 4c remains incomplete; branch 4d has not started.

Creation-time escort initialization now has pinned executable evidence from
AlienMission::spawnUfo and Ufo::setEscortedUfo/backupOriginalDestination.
Eighty captured rows cover successful/failed hunter status, the escort flag,
missing leaders, mission filtering, hunter exclusions, first saved-order leader,
all leader statuses, origin relocation, original destination and full speed.
The owning gameplay operation reproduces those rows; live wave callers are the
next two slices. Combat and deployment remain branch 5.

### Creation-time escorts: live single waves

Airborne escort waves now run AlienMission::spawnUfo's constructor and initial
leader attachment through gameplay-owned mission spawning. The saved-order leader
may be flying, landed, ignored, crashed or destroyed. Timer-driven deletion repairs
the new follower's original waypoint and mission live count; no-leader waves retain
the escort request without changing their trajectory speed. Fresh/cache cases and
save/reload on both sides of the transition are covered.

Preflight reserves creation/retarget scans and cleanup over the final UFO population,
validates the effective full escort speed, and stops before craft retargeting or
grounded escort takeoff needs further forecasts. Multiple same-tick births remain
guarded for the immediate-wave slice. Reference: AlienMission::spawnUfo,
Ufo::setEscortedUfo/resetOriginalDestination and GeoscapeState::time5Seconds.

### Creation-time escorts: immediate wave chains

Finite immediate waves now preserve the live saved-list order when leaders and
escorts are created in the same half-hour tick. The reference capture adds four
sequence rows from AlienMission::spawnUfo and the idle escort fragment of
GeoscapeState::ufoHuntingAndEscorting. Earlier leaders relocate the initial escort
origin; later leaders attach during retargeting without relocating it. Multiple
escorts skip other hunters, follow the first ordinary leader and keep their own
original waypoints.

Fresh/cache integration advances both ordering variants, saves before and after
subsequent movement, and preserves references to a leader whose contact ID is
still zero using its positive STR_UFO_UNIQUE identity. Scheduling reserves newborn
and existing escort scans over the final population. Excessive immediate escort
work stops before any birth or RNG draw. Loaded idle escorts attaching to newborn
leaders still stop at a specific pursuit-forecast boundary; grounded escorts and
creation-time craft hunting remain explicit future 4c work.

### Escort batch audit

The three-slice audit reproduced a relocation scoring defect: newborn escort
activity was forecast at its ordinary origin even though AlienMission::spawnUfo
moved it to a leader before GeoscapeState::time30Minutes scored it. Overflow or
underflow in the leader's region/country therefore threw after mutation.

Activity preflight now tracks saved leader positions and all possible earlier
ordinary births in list order, including probabilistic hunter constructor outcomes.
A certain hunter escort with a certain leader scores only at the candidate leader
origins; otherwise the original birth point remains a possible outcome. Geography
work and candidate storage stay bounded and no forecast draws RNG.

Sixteen region/country boundary regressions cover saved and newly born leaders,
positive/negative scores, atomic refusal and exact-fit success with reload.
A terminal-leader regression also saves after the reference's early return,
then verifies next-tick follower restoration and mission live-count release.

Final escort-cycle validation passes all 1,745 root tests with no failures or
skips, including private classifications, endurance, checksums and allocation
checks. Formatting verification, code-map and diff validation pass. The pinned reference checkout remains
clean; obsolete probe object output was removed. Branch 4c's headless gate remains
open and branch 4d has not started.

### Grounded escort births

Grounded escort waves with a timer above one now initialize beside a saved
ordinary leader, or keep their request and trajectory speed without a leader.
The extended pinned spawn capture has 160 rows and confirms that escort
initialization retains the source status. Active craft do not block a grounded
birth because GeoscapeState skips landed UFOs in the retarget pass.

Fresh/cache scenarios assert ordered detection notifications, stationary ground
timers, untouched shields, preserved original waypoints, takeoff at trajectory
speed and save/reload before and after flight. Reference: AlienMission::spawnUfo,
GeoscapeState::ufoHuntingAndEscorting/time5Seconds and AlienMission::ufoLifting
(the existing lift oracle). Creation-tick takeoff is the next slice.

### Creation-tick grounded escort takeoff

Ground timer one now permits grounded hunter and escort births with active
craft. The reference retarget pass skips these landed actors, then
AlienMission::ufoLifting takes off at the current trajectory speed without
movement, shield recharge or a new craft hunt. Existing lift fixtures and the
pinned grounded spawn rows establish the two operations.

Activity preflight reserves newborn takeoff points after site processing and
earlier UFO movement. A terminal earlier arrival skips both the newborn takeoff
and its award. Fresh/cache takeoff, next-tick interception refusal, site
underflow, exact-fit terminal scoring and transition reloads pass in the
138-case focused suite. Combat remains guarded before the next affected tick.

### Saved idle escort and newborn leader composition

Saved idle escorts can now see ordinary UFOs appended by same-tick waves,
using the pinned GeoscapeState::ufoHuntingAndEscorting attachment fragment and
existing sequence oracle. They back up the old destination, use full effective
speed and follow the later leader's current coordinates without creation-time
relocation.

The structural preflight proof permits this composition only when no current
or potential airborne craft can depend on pursuit, the old waypoint would not
be reached in this tick, and no existing UFO escort follows the saved source.
Hidden/ignored craft still require pursuit forecasts. Scans and final-population
movement work are bounded. Fresh/cache early/late leader ordering, unmarked
unique references, transition reloads, three precise atomic boundaries and a
resource-limit regression pass in 146 focused cases. Broader pursuit forecasts
remain 4c work; this does not close the headless gate.

- Grounded/saved-escort batch audit: reviewed creation, landed/takeoff scoring,
  retarget ordering, terminal fallthrough, follower repair and aggregate work
  limits against the pinned C++ methods. No confirmed defect was found. All
  1,761 root tests pass with zero failures/skips, including private corpus,
  endurance, checksums and allocation checks. Formatting verification, code-map
  and diff checks pass. The reference checkout is clean and obsolete probe
  object output was removed. Branch 4c remains open; 4d has not started.

### Maintenance before hunter selection

Half-hour forecasts apply ordinary and item refuelling and auto-patrol relaunch
in saved craft order before fuel use and hunter selection. Private fuel-stock
copies include grounded consumers and native fuel refunds. Missing-fuel messages
prevent that craft's relaunch but preserve the current tick's fallthrough. Hourly
repair and empty-rearming transitions preserve the native extra service hour.
Preflight allocates no identity, draws no RNG and mutates no campaign state.

The pinned capture now includes Craft::setFuel/refuel/setDestination/repair/rearm
and the GeoscapeState::time30Minutes auto-patrol fragment: 48 single-craft,
18 shared-stock and 24 hourly rows retain all earlier traces. Fresh/cache cases
compare canonical native rows and reload before/after the transitions.

The batch audit confirmed missing incoming-craft and escort-speed compositions.
Due grounded auto-patrol arrivals and escorts of potential relaunching leaders
now stop atomically with precise reasons until their forecasts are implemented.
Hourly item stocks and production ammunition reuse also remain explicit guards.
Craft-reference fuel scans are reserved against the shared work bound. Negative
fuel capacity and overflowing service rates refuse the tick intentionally.
Combat, deployment, event payloads and campaign month progression remain deferred.

Branch 4c composed escort fuel: hunter preflight now builds one pure ordered craft projection, applies all maintenance/relaunches, then fuel use in saved base/craft order. The shared AdvanceCraftFuel transition accepts projected leader states, so a relaunch or an earlier low-fuel return supplies the speed consumed by later escorts. This removes the audit-added relaunch-leader guard. Four new native strategic-hunting rows insert getFuelConsumption/consumeFuel/returnToBase and the exact time10Minutes escort/fuel-return caller; all older traces remain unchanged. Eighty-eight focused hunting/craft-operation tests pass, including fresh/cache canonical order comparisons, notification fallthrough and reload. Incoming craft and hourly stock/ammunition effects remain precise subsequent work; 4d and combat/event/month boundaries are unchanged.

Branch 4c incoming hunter targets: due craft transfers now append in native base/transfer order to the pure projection, apply setBase/checkup and half-hour maintenance without receiving the earlier hourly service pass. Projected coordinates and live craft health participate in UFO arrival/combat preflight. The prior arriving-craft guard is removed for these supported transitions. Forty-eight native transfer/checkup rows retain all older hunting traces; fresh/cache canonical tests cover due/pending transfers, imported status, damage, full tanks, relaunch delays and reload. A close incoming target refuses combat before delivery. Hourly item-stock and ammunition-reuse composition remain explicit subsequent work; later-branch boundaries and 4d remain unchanged.

Branch 4c supplied hunting stocks: hourly projections now separate servicing, all transfer deliveries/item reuse, half-hour refuelling and ordered fuel use. Item arrivals feed shared fuel stocks and existing/new craft reuse without selecting RNG. The reference ItemsArrivingState batch ordering is now explicit in the transfer handler, with bounded all-arrival reuse work even when no UFO exists. Seventy-two supplied fuel/ammunition rows and twelve combined item/craft order rows retain all earlier native traces. Focused tests cover fresh/cache canonical outcomes, pending transfers, scarce hourly stocks, transition reloads and malformed work limits. Facility/craft ammunition consumption, production stock/craft consumption and finishing daily research stocks retain precise pre-tick composition checks. Reference: Transfer::advance, ItemsArrivingState constructor, Craft::reuseItem/refuel/rearm and GeoscapeState hourly fallthrough. Later-branch boundaries and 4d remain unchanged.

The maintenance/arrival audit also pins the malformed zero/negative-hour transfer boundary. Native Transfer::advance delivers these payloads after decrementing below zero, while ItemsArrivingState only removes/reuses transfers at exactly zero hours. Both gameplay transfer preflight and the hunter projection therefore refuse undelivered non-positive-hour arrivals before mutation. Reload preserves the imported state for inspection. This intentionally avoids native repeated-delivery behavior; normal positive-hour arrivals retain their reference timing.

Branch 4c facility ammunition stocks: execution and hunter forecasts now share the pure BaseFacility::rearm transition, preserving alert suppression, full-ammo reset, build-time eligibility and disabled-facility behavior. Facility consumption runs after craft servicing and before transfers/refuelling, removing the prior facility-stock composition guard. Thirty-six native facility/fuel rows retain all earlier traces and compare fresh/cache outcomes with transition reloads. All 117 focused hunting/readiness tests pass; malformed consumed fields and int-max rates have focused unit coverage. Reference: BaseFacility.cpp rearm and GeoscapeState time1Hour facility loop. No host/native dependency, new save field or later-branch transition is introduced; 4d remains untouched.

Branch 4c deterministic craft ammunition stocks: hunter preflight now reuses CraftServicing.Rearm with a refusing random provider, applying deterministic stock consumption in native craft order. It never selects a bullet-saving draw or touches campaign RNG. A draw sharing the fuel item retains a precise atomic stochastic-stock guard; unrelated ammunition cannot change the craft's service status during that hour. Forty-eight native CraftWeapon::rearm/Craft::rearm shared ammunition/fuel rows retain all older traces, covering missing stock, clip rounding, completion delay and stochastic boundaries. Fresh/cache cases reload before and after supported transitions; blocked cases preserve the entire snapshot. All 100 focused hunting/readiness and 17 servicing tests pass. Production and daily research stock composition remain subsequent 4c work; later branch boundaries and 4d remain unchanged.

Branch 4c incomplete production composition: execution and hunter preflight share the pure ProductionProgress completion count. Existing non-fallback units paid at start cannot consume or deliver stocks/craft until a completion, so their unrelated pending work no longer blocks hunting. Fallback staff assignment and completing production retain precise composition guards. The pinned capture adds 96 Production::getAmountProduced/step rows; fresh/cache cases cover pending fuel, craft-consumption and ammunition projects, fallback refusal, progress overflow and transition reload. Focused native hunting/economy and pure progress tests pass. Reference: Production.cpp completion-count prefix and getAmountProduced; no event payload, combat, month guard or 4d behavior changes.

The facility/ammunition/production batch audit closes with two confirmed fixes: unchanged facility-service results retain their existing records (the idle-campaign allocation test caught the unconditional hourly copy), and daily research composition checks cover ammunition reuse without item fuel plus cross-base held-item refunds. The latter has fresh/cache reload regressions and a non-hunter control showing cancellation/refund followed by same-tick relaunch. Research scanning is bounded and shared once per hunting forecast. Reference: Base::removeResearch, GeoscapeState::time1Day and SavedGame primary research side effects before hourly servicing/transfers. Final root dotnet test passes 2,477 tests with zero failures/skips; native checksums, private corpus, populated measurements and code-map checks pass. One initial query allocation sample exceeded its original 448 B/UFO limit; focused endurance and both subsequent root runs passed without changing any threshold. Useful captures/executables remain; the obsolete hunting object was removed and the pinned reference remains clean. Completing production, stochastic shared ammunition, research stock forecasts and branch-wide closure remain open 4c work; later branches and 4d remain unchanged.

Branch 4c stock-neutral daily research: the research capability now supplies a pure bounded proof that daily item stocks and incoming item deliveries remain unchanged. It includes direct completions and every possible free/protected-free topic, preserves global cancellation uncertainty when any project holds an item, and distinguishes lookup/automatic addFinishedResearch from primary item side effects. Stock-free completions no longer trigger hunter fuel/ammunition guards. Fresh/cache loaded fixtures complete research plus a free topic, launch fuel/ammunition craft and reload both sides; guarded stock-changing fixtures remain intact. Reference: GeoscapeState::time1Day, SavedGame::handlePrimaryResearchSideEffects and Base::removeResearch. Event scheduling remains gameplay-owned; due payloads and later branch boundaries remain guarded. Branch 4c closure is still open and 4d is untouched.

Branch 4c deterministic research refunds: the research capability now projects simple daily completions and global obsolete-project cancellation in saved base order, returning immutable per-base stock changes before hourly servicing. Choices involving rewards, lookups, protected unlocks, disablers and matching corpse units remain explicit uncertainties. The pinned Base::removeResearch fragment contributes sixteen native held/finished/return/stock rows; cancellation now correctly avoids refunding the needed item for an already-finished destroying project. Fresh/cache fixtures compare every native row and reload cancellations, direct refunds and cross-base refund/relaunch transitions. Stock overflow stops atomically. No campaign RNG, identity or state is changed by this forecast. Spawned item deliveries are the next slice; branch 4c and later transition boundaries remain open/guarded, and 4d is untouched.

Branch 4c fixed research item deliveries: deterministic daily research projections now append the direct spawned item and every list entry as one-hour arrivals, preserving native count minimums, list order, duplicate entries and missing-item behavior. Hunter forecasts apply refunds before hourly service, then existing plus research arrivals as one batch before refuelling/reuse. Seventy-two pinned primary research/transfer/refuel rows compare canonical fresh/cache output and reload both sides. Fixed ammunition delivery now prevents a no-item-fuel craft's same-tick relaunch correctly. Transfer identity and stock overflow refuse before research, RNG, clock or inventory changes. Reward/disabler/lookup/protected-unlock and corpse uncertainties remain precise research composition boundaries. Reference: SavedGame::handlePrimaryResearchSideEffects spawned-item fragment, Transfer::advance and ItemsArrivingState batch reuse. Branch 4c closure is still open; due event payloads, combat/month progression and 4d remain deferred.

The research-stock batch audit closes with a confirmed end-of-day guard fix: already-disabled held projects can cancel/refund without any fresh research completion. A bounded research capability query now identifies this case, and the projection applies disabled cleanup after all daily base loops. The regression first reproduced a close hunter consuming an unavailable combat tick, then passes atomically with the fix; fresh/cache cases also verify the safe refund/relaunch and reload. Reviewed deterministic source ordering, global cancellation, finished-item semantics, duplicate/missing spawned items, all-arrival reuse, ID/stock limits and pure RNG/identity handling. Root dotnet test passes 2,497 tests with zero failures/skips, including native checksums, 19 staged save classifications and populated performance gates with unchanged limits. The corpus retains ten executable 360-tick horizons, one 125-tick guard stop and eight tactical preservation cases. Code-map/diff checks pass; useful captures/executables remain, the obsolete hunting object is removed and the pinned reference is clean. Compound research stock consequences, completing/fallback production, stochastic shared ammunition, remaining hunter/escort births, detection reconciliation and branch-wide headless closure remain 4c work. Branch 4d is untouched; due event payloads, combat/deployment and unrestricted month progression remain later-branch boundaries.

Branch 4c lookup stock composition: daily stock forecasts accept status-neutral lookup discovery while preserving its addFinishedResearch-only semantics. Lookup item payloads and matching projects remain untouched by primary side effects; lookup disablers/re-enablers and automatic disablers retain precise uncertainty guards. Four captured native primary-topic rows pin direct/free membership with lookups excluded. Fresh/cache scenarios exercise held lookup projects, direct fuel arrivals, unchanged lookup stocks and reloads before/after hunter selection. Reference: GeoscapeState::time1Day primary topic collection, SavedGame::handlePrimaryResearchSideEffects and Base::removeResearch. No RNG, identity or campaign state is changed during the forecast; later branch boundaries and 4d are unchanged.

Branch 4c research discovery stock composition: execution and preflight now share the addFinishedResearch discovery transition over live or copied completion/status sets. Direct and lookup disablers, re-enablers, automatic zero-cost chains and protected-unlock cancellation eligibility now participate in ordered daily stock refunds. Forecasts account for completing facility functions, active projects and refunded needed items before automatic discovery; work stays bounded and no RNG/state is consumed. Sixteen pinned native discovery/disabled-refund rows compare fresh/cache daily results and transition reloads, including already-complete sources and already-finished held projects. Reward choices and corpse item projections remain for subsequent slices. Reference: SavedGame::addFinishedResearch/hasUndiscoveredProtectedUnlock and GeoscapeState::time1Day construction/research ordering. Branch 4c closure and later-branch guards remain open; 4d is unchanged.

Branch 4c deterministic daily research rewards: stock forecasts now select sequential free topics and unambiguous eligible outcomes without consuming RNG, including duplicate candidates and prerequisite-gated protected rewards. Shared discovery runs the bonus and its lookup before the direct topic, then catalogue-ordered primary item arrivals and global obsolete-project cancellation. Seventy-two pinned selectGetOneFree/remaining-reward rows cover completed/disabled candidates, gated groups and duplicates; fresh/cache fixtures verify fuel, retained/cancelled cross-base held projects, reloads and atomic refusal of distinct random outcomes. Focused hunting/research compatibility passes 148 tests. Reference: SavedGame::selectGetOneFree/hasUndiscoveredGetOneFree and GeoscapeState::time1Day bonus-before-core ordering. Completing production, stochastic stock choices, corpse projections and final headless closure remain open 4c work; later branch boundaries and 4d are unchanged.

The research-composition batch audit confirms and fixes automatic discovery ordering: SavedGame::getAvailableResearchProjects iterates the research std::map in UTF-8 name order, independently of ruleset insertion and listOrder. Runtime catalogs now derive that immutable order on both fresh and cache construction; execution and forecast share it. Native competing-disabler and BMP/supplementary-name traces pin queued topics that become disabled after queueing, and fresh/cache reload scenarios verify safe hunter selection plus atomic combat stops. Protected-unlock membership scans are now charged at their actual nested cost; an 1,100-topic malformed-work scenario refuses before any tick mutation. Reviewed live/projected discovery semantics, global cancellations, reward eligibility, duplicate/missing arrivals and RNG/identity purity. Branch 4c remains open; 4d and later-branch boundaries remain unchanged.

Cycle validation: root dotnet test passes 2,511 tests with no failures/skips, including checksum verification, 19 staged-save classifications and populated 8/32/128-UFO measurements under unchanged limits. The corpus retains ten executable 360-tick horizons, one 125-tick stop and eight tactical preservation-only cases. Code-map/diff checks and reference-checkout cleanliness pass. Useful probe captures/executables remain; the obsolete compiled hunting object is removed at this milestone.

Branch 4c zero-quantity production delivery: the pinned Production::step item fragment retains zero-valued outputs as observable delivery/reuse operations. Immediate zero outputs now interrupt eligible craft ammunition servicing exactly like nonzero outputs; delayed zero item transfers retain their identity, hours and payload through save rewrites and arrive/clean up once. Snapshot validation permits zero only for item transfers and still rejects negative quantities and empty staff transfers. Seventy-two native delivery/refuel/reuse rows include zero quantities; fresh/cache tests compare zero rows and reload both sides, plus incoming-zero cleanup regressions. Reference: Production.cpp fixed item delivery, Transfer::advance/save, Craft::reuseItem and Geoscape hourly ordering. This fixes delivery semantics without enabling speculative production forecasts; branch 4c and later transition gates remain open, and 4d is unchanged.

Branch 4c final prepaid production forecasts: registered production-owned operations now project final fixed item outputs before hunter retargeting, after servicing/transfers and before every base refuels. Final units never charge inputs again, including paid materials/craft with insufficient current funds. Immediate outputs preserve native zero-quantity reuse; delayed outputs stay future arrivals and consume bounded transfer identity headroom, coordinated with earlier daily research arrivals and same-hour cleanup. Seventy-two native fixed-delivery rows compare fresh/cache hunting outcomes and both transition reloads; stock/identity overflow stops atomically. Ongoing/fallback, sold, random, craft and person production remain precise stock composition boundaries. Reference: Production::step final-unit break and item delivery, GeoscapeState::time1Hour ordering. Focused hunting/economy tests pass 169 cases; later branches and 4d are unchanged.

Branch 4c recurring free production forecasts: non-fallback zero-cost/no-input projects now project every fixed item completion in native order, honoring finite caps and infinite production. Native completion-count and delivery fragments contribute 192 composed rows for zero/multiple units, zero/two outputs, delayed transfers, shared fuel and ammunition reuse. Fresh/cache scenarios reload before/after and preserve negative funds for free unit starts. Work, cumulative future item quantities, identity slots and captive populations are bounded before any campaign mutation. Sold, funded/material-consuming, fallback, random/craft/person outputs and nonzero research-score effects retain explicit composition guards pending their projections. Focused hunting/economy tests pass 181 cases. Reference: Production::step, getAmountProduced, haveEnoughMoneyForOneMoreUnit and fixed delivery/reuse. Branch 4c closure remains open; later boundaries and 4d are unchanged.

The production batch audit fixes a confirmed mixed-producer identity hole: an unsupported random producer could allocate transfers omitted from the fixed-producer budget, overflowing only after tick mutation began. A precise capability guard now blocks that mixed future stream before clock/RNG/inventory changes; fresh/cache regressions reproduced the overflow before the fix and pass after it. Cross-handler cases also verify daily research consuming the final identity slot and same-hour cleanup releasing an imported highest ID. A production-funded relaunch remains atomically blocked at same-tick hunter interception. Reviewed prepaid/recurring unit counts, all-base ordering, zero quantity reuse and persistence, aggregate incoming/captive quantities, resource limits and remaining score/accounting/fallback uncertainty. Branch 4c remains open; 4d and later-branch boundaries are unchanged.

Cycle validation: all 2,544 root tests pass with zero failures/skips, including native checksums, private save horizons and original populated performance gates. Code-map/diff checks and pinned-reference cleanliness pass. Useful captures/executables remain; the regenerated hunting object is removed at this milestone.

Branch 4c funded production composition: non-fallback fixed item projects without consumed inputs now forecast paid continuation in saved base/project order. The current unit is prepaid; later starts use the same checked accounting operation as execution, stop on insufficient money, and avoid charging after the finite last unit. Unknown earlier sale/cost effects retain a precise shared-accounting guard. Sixty-four native Production::step continuation/startItem/money rows compare fresh/cache unit counts, funds, stop reasons, stock and hunter targets with both transition reloads. Focused hunting/research compatibility passes 193 tests. Material consumption and fallback staffing remain subsequent slices; later-branch boundaries and 4d are unchanged.

Branch 4c consumed production materials: fixed item completions now apply immediate output/reuse before each continuation input check. Delayed outputs stay unavailable to the next unit, and failed input checks leave funds unchanged. The production-owned forecast consumes only projected stocks and preserves saved project order; unknown earlier material effects retain a precise shared-material guard. Seventy-two native money/material/startItem rows compare unit counts, stop reasons, stocks, future arrivals and hunter outcomes with fresh/cache reloads. Craft input consumption and fallback staffing remain subsequent work; branch 4d and later-branch transitions are unchanged.

Branch 4c fallback production composition: execution and forecasts share the queued-space/engineer assignment transition. Forecasts include due engineer arrivals and workshop construction completed at the preceding daily hook, then preserve project order and ongoing occupancy. The exact hourly reference caller confirmed that project removal and staff release occur after every production step in a base; execution now retains completed/stopped projects until that pass, so later fallback work cannot borrow their engineers early. Seventy-two native staffing rows and four deferred-release rows compare fresh/cache counts, stocks, funds-independent stops and hunter results with transition reloads. Focused hunting/research compatibility passes 203 cases. Capture inserts the native progress enum instead of synthesized values. Sold, craft-consuming, random/craft/person outputs and score effects remain explicit subsequent 4c work; later-branch guards and 4d are unchanged.

The paid/material/fallback production batch audit fixes two confirmed issues. A fallback project could acquire staff after ordinary preflight and overflow its spent time after the tick began when no hunter existed; fresh/cache regressions reproduced the exception. A shared bounded staffing forecast now validates every hourly production pass and supplies the hunter projection, preserving due transfers, daily construction and deferred project removal. Idle bases also bypass empty removal-list allocation; the unchanged 16,384-byte summary test caught that regression and now passes. Reviewed prepaid units, money-before-material checks, output-before-input reuse, saved project/base order, future quantity/identity bounds and transition reloads. Focused compatibility/endurance passes 215 tests and foundation/progress passes 22. One initial populated query sample exceeded its existing limit; the focused rerun passes without changing it. Branch 4c remains open; 4d and later-branch boundaries are unchanged.

Cycle validation: all 2,563 root tests pass with zero failures/skips, including reference checksums, 19 private save classifications and unchanged populated performance limits. Code-map/diff checks pass. Useful captures/executables remain; the obsolete compiled hunting object is removed at this milestone. The pinned reference checkout is unchanged.

Branch 4c native craft inputs: Production::haveEnoughMaterialsForOneMoreUnit uses the required quantity only to count grounded craft, while startItem removes one first matching craft per listed type, including zero-valued entries and airborne first matches. Twenty-four native eligibility/selection traces reproduced the zero-input mismatch before the fix and now compare fresh/cache results with empty unload payloads and reloads. Execution preserves that split; counter lower bounds treat zero craft inputs as possible consumption. The hunter forecast retains a precise consumption guard for these now-correct transitions until the next slice. Existing hunting/research compatibility passes 207 tests. Reference: Production::startItem, Base::getCraftCountForProduction/removeCraft and Craft::unload. Branch 4d and later-branch boundaries are unchanged.

Branch 4c craft consumption forecasts: execution and preflight share material eligibility and first-craft selection. Each projected start removes item inputs before craft unloading, adds inventory/weapons/vehicle ammunition through the existing unload operation, and removes consumed actors before refuelling/retargeting. Twenty-eight pinned item-unload/refuel rows cover missing input, payload-driven relaunch and a valid int-max inventory that requires item-first ordering. Fresh/cache cases reload both sides; unsafe unload stock/quantity arithmetic refuses atomically. The prior ordinary craft-consumption stock guard is removed for fixed zero-score item producers. Focused hunting/research/counter tests pass 223 cases. Reference: Production::startItem, Craft::unload and Base::getCraftCountForProduction. Deleted-target pursuit composition is the next slice; later branches and 4d remain unchanged.

Branch 4c consumed target repair: a registered world-owned operation now applies Target::~Target follower consequences when production removes a craft. Craft return destinations/speeds change before later hourly, refuel and fuel hooks; hunted UFOs restore the original waypoint without changing speed. Forecast sources share the craft return operation, and absent projected identities remain absent even for imported hunting UFOs with the hunter-killer flag clear. Four native destructor/fuel rows compare initial production commands and same-hour removal with fresh/cache reloads. Work bounds include repeated removals, arrivals and possible production births before any tick mutation; a 1,001-by-1,001 follower workload refuses atomically. Focused hunting/research/counter tests pass 229 cases. Reference: Target::~Target, Craft::returnToBase/setDestination and Ufo::resetOriginalDestination. No notification performs mutation; command/tick operations retain the single writer. Branch 4c remains open; later-branch transitions and 4d are unchanged.

The craft-input/unload/pursuit batch audit fixes a confirmed late-validation boundary. An invalid grounded craft follower at an unplaced base could cause a production stop only after its prepaid output and clock were mutated; fresh/cache regressions reproduced this for current and same-hour arriving followers. Production preflight now supplies bounded possible removal types, and world preflight validates existing plus due follower payloads before the tick. The exact registry-order assertion includes the new preflight without reducing strictness. Reviewed zero/multiple craft quantities, first-craft selection, item-before-unload overflow, identity lower bounds, synchronous return/restoration, future arrivals/birth work budgets and deleted-target reloads. Focused existing/arriving follower, native removal and populated performance checks pass with unchanged limits. The first full query allocation sample exceeded its original bound; both focused populated reruns pass without relaxing it. Branch 4c remains open; sold/random/craft/person production, score effects, stochastic/corpse projections and final headless reconciliation remain subsequent work. Later-branch boundaries and 4d are unchanged.

The final ordering check also reproduced an unstable material partition: sorting equal craft entries reordered an 18-craft native input list and its follower notifications. Material planning now appends craft inputs after items while retaining the reference order within each group, without allocating a craft list for item-only projects. The pinned startItem trace and fresh/cache transition-reload regression cover the complete deletion and feedback sequence.

Two default-runtime root runs exceeded the unchanged query boundary allocation gate (501.8 and 450.1 B per additional UFO); isolated default and process-local non-tiered runs passed. Inspection found interface enumeration in WorldGeometry.InsideRegion. Indexed traversal preserves the existing ordered geographic predicate and removes that enumeration allocation regardless of JIT optimization. The original populated measurements fall from approximately 369 to 208 B per additional UFO for eight-base query boundaries; warm-boundary costs fall by 160 B/UFO. Existing native geography fixtures and allocation thresholds remain unchanged. This addresses the concrete allocation source without claiming a proven explanation for runtime-dependent sample variation.

Cycle validation: all 2,583 root tests pass with zero failures/skips after the fixes, including native checksums, 72-hour scenarios, all 19 private save classifications and the original populated allocation gates. No test or threshold was relaxed. Code-map/diff checks pass; the pinned reference is clean. Useful captures/executables remain, and the compiled hunting object was removed. Branch 4c headless closure remains open and branch 4d is untouched.

Branch 4c fixed production sales: hunter projections now apply unscripted, fixed item sales before each continuation money/material check in saved base/project order. Sold outputs create neither stock/reuse nor delayed transfers, and sale coefficients truncate before quantity multiplication. The pinned Production::step, RuleItem::getSellCostAdjusted and SavedGame::setFunds fragments add 128 continuation rows plus two mixed-sign ledger rows, with fresh/cache same-base and cross-base reload coverage. The audit reproduced and fixed netting mixed sale signs in live execution, which previously lost separate income/expenditure entries, and a no-hunter accounting exception after tick mutation. Production-owned preflight now reserves signed capacity for every potential completion/start, bounding scripted prices by their int range without executing a future-context VM. These conservative capacity bounds intentionally refuse extreme imported ledgers without relying on later opposite-sign offsets or uncertain production stops. Local capacity tests cover signed headroom, independent ledgers, large counts and malformed bounds. Direct/global time-reading sale scripts retain a precise hunter forecast guard while ordinary execution still runs the real VM at the advanced clock. Random/craft/person production, score effects and price-script projections remain subsequent 4c work; later-branch boundaries and 4d are unchanged. Reference checkout remains read-only.

Slice validation: all 2,617 root tests pass with zero failures/skips, including native fixture checksums, private save classifications, 72-hour scenarios and populated allocation gates. Full formatting verification and code-map/diff checks pass. No test or threshold was relaxed. The pinned reference checkout is clean; useful captures remain and the compiled probe object was removed. Branch 4c remains open and branch 4d is untouched.

Branch 4c scored production: fixed item and sale output with positive/negative manufacture points now participates in hunter stock/funding forecasts. Twenty-four pinned Production::step / SavedGame::addResearchScore rows cover per-unit points before continuation, finite/infinite caps, material stops and subsequent refuel/hunting. A shared signed-capacity preflight bounds daily primary/reward/lookup/automatic discoveries plus hourly output before tick mutation, without selecting random rewards. Extreme score histories are conservatively refused rather than relying on cancellation or uncertain stops. Fresh/cache fixtures and transition reloads pass; later-branch boundaries and 4d remain unchanged.

Branch 4c deterministic random manufacture: positive-weight choices with identical ordered item outputs now compose with fixed output, sales, continuation materials, reuse/refuel and hunting without speculative RNG. Sixty-four pinned random-delivery traces cover one/two identical choices, zero quantities, auto-sale, delayed output and finite/infinite continuation. Random output is delivered even when fixed output is sold, and every queued zero-quantity entry reserves its transfer identity. Per-project history preflight bounds all reachable positive-weight item increments plus unsold fixed output, preventing overflow before mutation even without hunters. Fresh/cache and history-limit reload tests pass. Genuinely different outcomes keep precise composition guards; the mixed uncertain-transfer fixture now contains distinct choices. Later-branch boundaries and 4d are unchanged.

Branch 4c scientist/engineer production: delayed staff output now composes with fixed/deterministic-random item output, funding, reuse and hunter selection. Forty-eight pinned Production::step / Base::getUsedQuarters rows cover default/minimum personnel delay, finite/infinite completion, intermediate living-space fallthrough and final continuation checks. Inspection found and corrected the missing one-quarter reservation for each person-producing project, including queued projects; deferred removal/cancellation releases that reservation. Production-owned preflight bounds possible transfer creation and personnel headroom before mutation, including ordinary worlds with no hunters. Fresh/cache native rows, queued cancellation/reloads, transfer exhaustion and population limits pass. Soldier generation and genuinely uncertain outputs remain guarded in hunter composition; later-branch boundaries and 4d are unchanged.

The score/random/personnel batch audit bounds empty protected-reward groups, accounts for the existing saved transfer population before production creates more, and rejects imported personnel totals that overflow after adding project reservations. Regression fixtures cover 1,001-by-1,001 empty groups, 100,000 existing transfers and signed personnel limits. Twenty-four additional native rows pin omitted personnel transfer delay at 24 hours while preserving the original 48 rows. No speculative VM/RNG, fixture strictness or allocation threshold was weakened.

Cycle validation: all 2,647 root tests pass with zero failures/skips after the batch audit fixes, including fixture checksums, the 19 private save classifications, composed 72-hour scenarios and unchanged population/allocation checks. Full formatting and regenerated code-map/diff verification pass. The reference checkout remains clean; useful native captures remain and the compiled object was removed. Branch 4c remains open; branch 4d is untouched.

Branch 4c corpse evidence and projections: 512 pinned removal/daily-interrogation traces distinguish held-item return from corpse recovery across finished/cancelled research, retainCorpses, unit lookup and both recovery flags. Runtime units link armor, armor links its geoscape corpse, and items retain recover/recoverCorpse defaults; fresh/cache tests pass with compiler revision 53. Campaign retainCorpses defaults false per Options.cpp, honors fixedUserOptions and survives the save adapter/reference option snapshot. The separate native callers can award two corpses on completion; gameplay application and forecasting follow in the next slices. Reference: Base::removeResearch, GeoscapeState::time1Day, RuleResearch::isHoldingNeededItem, Armor::getCorpseGeoscape and RuleItem recovery getters. Later-branch boundaries and 4d remain unchanged.

Branch 4c live research recovery: completion applies the Base::removeResearch held/corpse effect and the separate daily interrogation corpse effect before discoveries. Cancellation applies only the removal effect, looking up the unit by research name even when neededItem differs. All 512 native rows pass fresh/cache and before/after reloads; the shared local lifecycle rule uses the same verified oracle. Command/daily preflight bounds recovered stocks, including possible earlier cross-base cancellations, so corpse overflow refuses before staff/project/time mutation. Completed projects remain deleted after rewriting saves. Hunter corpse stock composition follows in the next slice; later-branch boundaries and 4d are unchanged.

Branch 4c corpse composition: daily stock projection now uses the same removal/daily recovery operation as execution before automatic discoveries and hourly inputs, including earlier-base cancellations and final disabled-project cleanup. The old unit/corpse forecast guard is removed. Another 128 pinned recovery/refuel/hunting rows pass fresh/cache plus transition reloads; cross-base fixtures prove a cancelled finished project receives only the removal corpse and cannot run its later daily caller. Recovery changes retain exact item identities and do not invoke item reuse for direct corpse addition. Random reward choices and other uncertain compositions retain their specific guards. Later-branch boundaries and 4d remain unchanged.

The corpse batch audit removes empty-research preflight allocations and avoids duplicate unit-armor runtime diagnostics: typed validation still rejects absent/missing armor, with two focused regressions. All 2,669 root tests pass with zero failures/skips, including the unchanged allocation gates and private corpus classifications. Native checksums and code-map/diff verification pass; no fixture expectations or limits were relaxed. Branch 4c remains open and 4d is untouched.

Branch 4c native craft manufacture: runtime STR_CRAFT projections now retain only the first producedItems entry, matching RuleManufacture::afterLoad; later undefined names are ignored. Production creates fixed weapons, moves to its base and runs checkup before immediate insertion or delayed transfer, sharing existing craft operations. Thirty-two pinned Production::step / Craft::setBase/checkup rows cover weapon rearming, zero/nonzero fuel, negative/zero/positive craft delays and coordinates; fresh/cache transition reloads plus local first-output tests pass. Compiler revision 54 invalidates old projections. Hunter birth composition follows in subsequent slices; later-branch boundaries and 4d are unchanged.

# Phase 6 branch 5 delivery plan: interception and strategic deployment

Approved 2026-10-12. Branch `claude/strategic-interception-deployment`, from `main` at
`1991902b` (merged branch 4d, PR #74). Reference: `4df3a5e571a1a4b5e8a46d3161fb2e21a2adba15`.
Scope and acceptance come from [Phase 6 branch 5](phase-6-plan.md#branch-5--interception-and-strategic-deployment-handoff);
this plan replaces that section's commit purposes (mapping below). Slice evidence and audits are
recorded in [strategic interception status](strategic-interception-status.md); checkpoints,
current scope and remaining guards are recorded in [Progress](#progress).

## Starting audit (2026-10-12)

Branch 4d closed with S0–S11 and [ADR 0032](decisions/0032-geoscape-ui-intentional-differences.md).
These world commands and checks refuse before mutation at a branch 5 boundary:

- `WorldCommandReasons.InterceptionBoundary` and `LandingBoundary` (`CampaignWorldPursuit` and the
  target actions in `CampaignWorld`); `StartingConditionBoundary` (dispatch and wing commands refuse
  targets whose deployment has a starting condition).
- `HuntCombatReason` (declared in `CampaignWorldHunting`, returned by `CampaignWorldUfos` and
  `CampaignWorldNewbornHunting`): a hunter-killer reaching a craft.
- Retaliation assault arrival at an X-COM base (`CampaignWorldRetaliationSpawning`) and crashed UFO
  arrival with followers (`CampaignWorldUfos`).
- Corpus: `modded/rosigma early/Early.sav` stops after 6,289 ticks on the landing boundary
  (`PrivateStrategicWorldTests`); it becomes the positive case for the deployment request.

Kickoff measurement (`--time-profile Flight32 Flight128 --seconds 4`, Release, one-day speed):

| Scenario | Machine | us/tick | s per in-game day | Largest phases (us/tick) |
|---|---|---:|---:|---|
| Flight32 | 2-core Linux VM | 13.1 | 0.23 | world preflight 9.2 (UFO movement 4.0, site conversion 2.5, half-hour 0.8); UFO movement apply 5.0 |
| Flight32 | Windows workstation | 17.3 | 0.30 | world preflight 13.6 (UFO movement 5.5, site conversion 4.2, half-hour 1.3); UFO movement apply 6.3 |
| Flight128 | 2-core Linux VM | 50.2 | 0.87 | world preflight 31.1 (UFO movement 15.3, site conversion 9.6, half-hour 2.9, hunting 1.6); UFO movement apply 18.7 |
| Flight128 | Windows workstation | 50.2 | 0.87 | world preflight 32.0 (UFO movement 14.4, site conversion 10.7, half-hour 3.4, hunting 1.9); UFO movement apply 18.1 |

The three UFO preflight steps each re-project every UFO's flight
(`ProjectFlightTarget(UfoForFlight(...))` through `UfoMovementReason`, `ArrivesAtSite` twice,
`EndsUfoFlightPass` and `ArrivesOnGround`), about five times per UFO per tick. They also scan
missions linearly (`FindMission`), look up rules and forecast missions, so the share of repeated
projection is not yet measured. The 0.6 s/day target is about 34.7 us/tick; with the movement
apply phase unchanged, the preflight must roughly halve.

## Reference behaviour that shapes the design

- A tick does not resolve combat or battles. `GeoscapeState::time5Seconds` queues
  `DogfightState`s (at most four; one hunter-killer attack at a time, which clears the other
  interceptions; escorts within `escortRange` join a hunter-killer fight with
  `escortsJoinFightAgainstHK`; depth and land waits with `DogfightErrorState`), queues
  `ConfirmLandingState` for a landed/crashed UFO, mission site or discovered alien base, and starts
  base defense when a UFO reaches an X-COM base. `_pause` then stops the remaining ticks.
- The base attack does change the world inside the tick. Before either path it draws the wave
  countdown, clears the UFO destination and calls `Base::setupDefenses`. Without defenses (or with
  `ignoreBaseDefenses`) `handleBaseDefense` runs at once: the UFO is destroyed, an instant
  retaliation mission is interrupted, missiles damage facilities (`stopHuntingXcomCrafts`,
  `damageFacilities`, `cleanupDefenses`) or destroy the base, `missileStopChance` is rolled, and a
  base-defense battle or `BaseDestroyedState` follows. `time5Seconds` then returns, so this is the
  tick's last mutation.
- A hunter-killer attack sets `ufoIsAttacking`, which skips craft movement, shield recharge and
  arrival for the rest of the tick.
- `popup()` pauses but does not end the tick, so several crafts can queue confirmations in one
  tick. The queue belongs to `GeoscapeState`, which survives a battle (`DebriefingState` pops back
  to it), so confirmations queued behind a confirmed landing are shown afterwards;
  `ConfirmLandingState::init` closes one whose craft is now headed for its own base. Popups are not
  saved; after a load, a craft still at its destination raises its confirmation on the next tick.
- Every dogfight start, landing confirmation and base attack calls `timerReset()` (5-second speed),
  which also changes `hunterKillerFastRetarget` behaviour on the next ticks.
- Combat runs on its own timer (`Options::dogfightSpeed`; keys 1/2/3 select 50/35/20 ms while a
  window is open) in `DogfightState::update`. Time advances only with no popups and every window
  minimized; then `GeoscapeState::think` runs the geoscape timer before the dogfight timer.
  Minimized combat only checks break-off and end conditions.
- Combat state is not saved. The save keeps craft `damage`, `shield`, `interceptionOrder` and
  weapon ammunition, and UFO `damage`, `shield`, `fireCountdown` and `escapeCountdown`, all of which
  the port already persists. Loading drops active dogfights.
- Combat outcomes are discrete mutations in the dogfight step: crash, destruction or splashdown,
  crash timers, activity scores, `ufoShotDown`, retaliation mission spawn, craft destruction (crew
  evacuation, transported soldiers, stop hunting, `craftLostDogfight`), ammunition, hunter-killer
  break-off and pilot experience (`handleDogfightExperience`). `DogfightState`,
  `ConfirmLandingState` and `BaseDefenseState` have no script hooks.
- The reference has no pending deployment: `ConfirmLandingState::btnYesClick` checks the starting
  condition (destroying required items when it passes) and runs `BattlescapeGenerator` at once; base
  defense with defenders and the Cydonia confirmation do the same. No bases left sets `END_LOSE` in
  the next `time5Seconds`.

## Decisions

- **D1 — Atomic preflight stays; flight projections are shared per tick**
  ([ADR 0033](decisions/0033-shared-per-tick-flight-projection.md)).
- **D2 — Interception is a registered capability with transient state.** `CampaignInterceptions`
  ([ADR 0027](decisions/0027-registered-campaign-capabilities.md)) owns active interceptions: craft,
  UFO, `ufoIsAttacking`, interception number, minimized and wait flags, distance and target
  distance, mode, weapon enable and countdowns, projectiles and end flags. Headless commands advance
  one dogfight timer step for all windows (with the per-step `interceptionProcessed` reset), set
  mode or standoff, disengage, minimize or restore, and toggle weapons. Saving writes only reference
  fields; loading starts without interceptions. Randomness goes through `IRandomSource`; gameplay
  has no UI types.
- **D3 — Time and combat interleave explicitly.** The geoscape controller keeps the 4d chunked
  advance and adds a dogfight timer that follows `GeoscapeState::think`. A tick that starts a
  dogfight, raises a landing confirmation or starts a base attack reports the speed reset; the
  controller switches to 5-second speed and headless scenarios pass the new speed to later ticks.
  A test compares stepped and chunked runs with the same choices.
- **D4 — The deployment request is a port-only, namespaced save node** beside unchanged reference
  state. Confirmation applies the reference pre-generation mutations (starting-condition refusal and
  return, required-item destruction), then records the mission type, deployment and custom
  deployment IDs (including `_UNDERWATER`), alien race, world texture and shade, target identity,
  craft and assigned soldiers, vehicles and items, or the base for base defense. The craft keeps its
  pre-battle world state (not `inBattlescape`), so the C++ engine loading such a save sees the craft
  at its destination again; the limitation is documented. While a request is pending, time and
  world commands refuse with a classified reason; the headless consumer reads and validates it
  exactly once. A request cannot be withdrawn: the campaign waits at the launch point until Phase 7
  consumes it.
  At most one request exists. Confirmations queued in the same tick stay queued, unsaved, behind
  the pending request, as the reference keeps them behind the battle, and are shown after it is
  consumed, each first applying the reference `init` check. A reload drops the queue as in the
  reference. Headless, a confirmation while a request is pending is refused. Intentional
  difference: a queued confirmation whose craft has no destination left is closed like the
  home-base case; the reference shows it and throws "No mission available!" on Yes. S8 records this
  in ADR 0032.
- **D5 — Base loss includes `END_LOSE` when the last base is lost.** Base destruction and "no bases
  left" are separate steps; the ending is decided by one internal campaign-ending rule (the
  reference rule) behind a narrow seam that a later mod or extension override could replace. No
  override mechanism is added, and the seam must not cost an architectural compromise; otherwise the
  reference rule is implemented directly and the override is recorded as a later item. The defeat
  cutscene stays Phase 8; monthly failure and final victory stay branch 6.
- **D6 — Managed extension state stays refused (`EXT1011`).** Integration happens when the first
  state-owning extension exists ([ADR 0021](decisions/0021-versioned-managed-extensions.md)).
  Branch 5 adds extension events only if a slice needs them.

## Slices

Each slice is followed by an independent audit before the next.

- **S0** — Record this plan and ADR 0033; correct the stale status lines; replace the branch 5
  commit purposes; documentation ownership in `AGENTS.md`; baseline validation.
- **S1** — Shared per-tick UFO flight projection (ADR 0033): measure first, then cache
  `ProjectFlightTarget(UfoForFlight(ufo))` per UFO after the forecast tables are final, with the
  bypasses and the recompute-and-compare test mode the ADR defines; an indexed mission lookup if
  the measurement shows it matters. Update [performance targets](performance-targets.md).
- **S2** — Evidence and ownership: pinned C++ probes for dogfight step arithmetic (approach and
  standoff distances, weapon reload and fire, hit chance, damage and shields, tractor beams, UFO fire
  and escape countdowns, break-off), outcomes (crash, destruction, splashdown, retaliation odds and
  region). Project the missing craft-weapon, UFO and global rules (`retaliationTriggerOdds`,
  `retaliationBaseRegionOdds`, `escortsJoinFightAgainstHK`, `chanceToStopRetaliation`,
  `lessAliensDuringBaseDefense`, missile power and stop chance, ...) with a compiler/cache revision;
  ownership ledger and option inventory (`dogfightAI`). Start rules, landing starting conditions
  and base-defense rounds are captured in S4, S6 and S7 with their consumers (moved 2026-10-12).
- **S3** — Interception state and combat step (D2), headless: fixtures for each mode, weapon type and
  outcome path except world consequences; save/reload drops combat as the reference does.
- **S4** — Interception start in `time5Seconds`: craft reaching a flying UFO, hunter-killer attacks
  (UFO and craft loops), escorts joining, depth and land waits, end-of-tick wait checks,
  destroyed-UFO cleanup of interceptions and the speed reset; target loss (UFO destroyed, landed, out
  of reach or undetected); replace `InterceptionBoundary` and `HuntCombatReason`; the preflight
  models the `ufoIsAttacking` skip. Captures the start-rule oracle.
- **S5** — Interception outcomes in the world: crash and crash-site timers, scores, `ufoShotDown`,
  retaliation spawn, craft destruction, ammunition, experience, break-off and hunter reset, fuel
  interruption; crashed UFO arrival with followers; transition reloads.
- **S6** — Landing and deployment request (D4): `ConfirmLandingState` decisions for UFO, site and
  alien base (texture and shade, `_UNDERWATER`, custom deployments, starting conditions), Patrol and
  No, `CraftErrorState`; several confirmations in one tick, the held queue and its `init`
  dismissal, reload with an unanswered confirmation; replace `LandingBoundary` and
  `StartingConditionBoundary`; headless consumer; Rosigma `Early.sav` reaches a request. Captures the
  starting-condition oracle.
- **S7** — Base defense, base loss and Cydonia: arrival at an X-COM base (wave countdown, defenses,
  `ignoreBaseDefenses`, insta-hyper), defense rounds, retaliation stop chance, missile damage and
  nuclear destruction, base-defense request, base destruction effects and `END_LOSE` (D5); the
  final-destination request. The in-tick base attack runs as the tick's last step, after the
  preflight has validated its inputs. Captures the base-defense oracle.
- **S8** — UI: dogfight windows (up to four, minimize icons, modes, disengage, weapon toggles,
  status texts, distance), landing confirmation, base defense, base destroyed, Cydonia,
  starting-condition errors and a "tactical execution unavailable" launch screen; D3 in the
  controller.
- **S9** — Scenarios: dispatch → intercept → crash → land → request → reload; hunter-killer and
  escort fights; target loss; base defense variants; site and alien-base requests; TFTD depth cases;
  reclassify the 19 staged saves; SDL smoke on Windows.
- **S10** — Closure: populated interception performance and time profile, ADR, matrix and ownership
  updates, code map, acceptance mapping.

### Mapping of the original commit purposes

| Purpose in `phase-6-plan.md` | Slices |
|---|---|
| 1. Interception, timing, ammunition/damage and landing/deployment fixtures | S2 (probes and rules); fixtures in S3–S7 |
| 2. Interception state and resolution, reference script hooks | S3, S4; the reference has no script hooks on these paths |
| 3. Strategic outcomes and base-defense pre-battle effects | S5, S7 |
| 4. Deployment request boundary and port-only envelope | S6 (D4); S7 for base defense and Cydonia |
| 5. Interception, landing and loadout confirmation UI; consumer; extension persistence | S8, S6. The reference has no loadout step at landing: craft loadout comes from the branch 2 craft screens and the request records it. Extension persistence: D6 |
| 6. Handoff scenarios, rollback/cancellation, target loss, extension state, matrices | S9, S10. Before confirmation the reference allows No, Patrol and the starting-condition refusal (S6); a recorded request cannot be withdrawn (D4). Target loss: S4, S9. Extension state: none while `EXT1011` stands (D6) |

## Acceptance

- Controlled choices reproduce legal combat outcomes and ammunition and fuel use for each mode, the
  multi-craft and hunter-killer cases, with transition reloads.
- Launch cannot duplicate or double-consume participants after reload; one request per
  confirmation and at most one pending request; refused starting conditions return the craft as the
  reference does.
- Landed and crashed UFO, terror and custom site, alien base, final destination and base-defense
  request variants are covered where strategically reachable.
- Saving during combat behaves as the reference (combat dropped on load); no fake battle graph or
  simulated victory.
- Validation: Release build, unit and public compatibility tests, private corpus, formatting,
  checksums, code map and SDL smoke on Windows.

Acceptance mapping (S10, 2026-10-13):

| Criterion | Evidence |
|---|---|
| Combat outcomes, ammunition and fuel per mode; multi-craft and hunter-killer cases; transition reloads | `strategic-dogfight` oracle replay (`StrategicDogfightOracleTests`); `StrategicInterceptionTests` (modes, shoot-downs, tractor beam, destroyed craft, disengage, reloads); hunter-killer and escort cases in `StrategicWorldHuntingTests` |
| No duplicate launch or double consumption; one request per confirmation, at most one pending; refused starting conditions return the craft | `StrategicLandingTests` (request taken once per load, every other command refused while pending, refusals send the craft home) |
| Landed and crashed UFO, terror and custom site, alien base, final destination and base-defense requests | `StrategicLandingTests` (landed UFO, site, alien base), `StrategicInterceptionTests` (crash recovery through the client), `StrategicFinalMissionTests`, `StrategicBaseAttackTests`; Rosigma `Early.sav` terror site (`PrivateStrategicWorldTests`) |
| Saving during combat drops combat on load; no fake battle or simulated victory | `StrategicInterceptionTests.RememberedWeaponTogglesPersistAndAReloadDropsTheInterception`; requests wait for Phase 7 (`TakeDeploymentRequest`) |
| Validation | Root `dotnet test` (all green at the branch audit, including the private corpus), formatting, code map ([status](strategic-interception-status.md#branch-audit-2026-10-17)) and the Windows SDL smoke ([status](strategic-interception-status.md#s9--scenarios-2026-10-13)) |

## Progress

### S0 — Plan and baseline (2026-10-12)

This plan, [ADR 0033](decisions/0033-shared-per-tick-flight-projection.md) and the status
corrections are recorded; baseline validation is in
[strategic interception status](strategic-interception-status.md#s0--plan-and-baseline-2026-10-12).
All branch 5 boundaries listed in the starting audit remain.

### S1 — Shared per-tick flight projection (2026-10-12)

Each saved UFO's flight is projected once per tick across the movement, site-conversion and
half-hour steps, with the recomputation check on in every test
([status](strategic-interception-status.md#s1--shared-per-tick-flight-projection-2026-10-12)).
Flight128 improves by about 28% but stays above the soft one-day target, with the reason recorded
in [performance targets](performance-targets.md). No gameplay behaviour changes; the branch 5
boundaries remain.

### S2 — Evidence and ownership (2026-10-12)

Interception rules, globals and options are projected with reference defaults (compiler/cache
revision 61) and a verbatim dogfight step oracle pins 15 scenarios for S3
([status](strategic-interception-status.md#s2--evidence-and-ownership-2026-10-12),
[ownership](phase-6-ownership.md#branch-5-interception-ownership-s2-2026-10-12)). Start-rule,
starting-condition and base-defense oracles move to S4, S6 and S7. No gameplay behaviour changes
yet; the branch 5 boundaries remain.

### S3 — Interception state and combat step (2026-10-12)

The `Dogfight` engine replays all 18 reference scenarios step by step, and open interceptions run in
the campaign through headless commands with a forecast that refuses outcomes needing later world
consequences before any change
([status](strategic-interception-status.md#s3--interception-state-and-combat-step-2026-10-12)). The
craft's `pilots` list is now owned. Time stops while an interception is open until S4; crash,
destruction, forced landing, retaliation, experience and break-off consequences remain for S5.

### S4 — Interception start (2026-10-12)

Interceptions start inside time advance: hunter-killer attacks with escorts, craft arrivals at flying
UFOs with the four-window limit and water-only waits, the destroyed-UFO cleanup and end-of-tick wait
checks; time runs while every window is minimized
([status](strategic-interception-status.md#s4--interception-start-2026-10-12)). `InterceptionBoundary`
and the flight-pass `HuntCombatReason` are gone; a hunter-killer reaching a craft on its birth tick
kept its refusal until the pre-merge pass (S10). No native start-rule probe was captured; the start rules are pinned by focused
tests from the reference statements.

### S5 — Interception outcomes in the world (2026-10-13)

Interception steps apply their world consequences: crash and forced-landing bookkeeping,
`ufoShotDown`, wave interrupts, retaliation missions, X-COM activity, pilot experience with the diary
and daily cache, break-off and hunter-killer endings, crash followers, and the loss of destroyed
crafts with crew evacuation and `craftLostDogfight`
([status](strategic-interception-status.md#s5--interception-outcomes-in-the-world-2026-10-13)).
`InterceptionOutcomeBoundary` is gone. Named restrictions remain for crew who could die with their
craft (soldier deaths and the memorial are not owned), retaliation missions run from alien bases and
a retaliation against a base outside every region. The crashed-UFO ground arrival with followers
kept its refusal until S6, where crafts reaching a crash site are handled. All were resolved before
merge (S10).

### S6 — Landing and deployment request (2026-10-13)

Done: the landing-site shade in gameplay with a verbatim globe shading oracle; landing confirmations
queued by the craft pass with texture and shade (`globeSeasons` is now a campaign option), held time,
`init` closing, No and Patrol; starting conditions on Yes (runtime family, texture fallback, race
custom base missions) with the refusal sending the craft home; the deployment request with
required-item destruction, the port-only save node and the once-per-load Phase 7 seam
([status](strategic-interception-status.md#s6--landing-and-deployment-request-2026-10-13)).
`boundary.starting-condition` is gone. Remaining: refusing other world commands while a request is
pending, the follower landing at a site respawned by a UFO conversion and the crashed-UFO ground
arrival with followers (both still `boundary.landing`), the Rosigma `Early.sav` request, and the S6
audit. Update (2026-10-13): all of these are done; only escorts of a crashed leader keep a refusal.

### S7 — Base defense, base loss and Cydonia (2026-10-13)

Done: the defense rounds (verbatim `strategic-base-defense` oracle), the facility defense projections
and `setupDefenses` list, the base assault arrival with the defense window, a shot-down retaliation
(with `chanceToStopRetaliation`), the base-defense deployment request, base loss and `END_LOSE` (D5)
([status](strategic-interception-status.md#s7--base-defense-base-loss-and-cydonia-2026-10-13)).
Since done: the wounded-defender option, the S7 code-review pass and the Cydonia request (with a
refusal when several research topics unlock the final mission). Since done: nuclear missiles, an
instant retaliation born over the base, and stored vehicles among the defenders. The last three
refusals (missiles that damage facilities, stored vehicles competing for too little ammunition, and
several final-mission research topics) were resolved before merge (S10).

### S8 — UI (2026-10-13)

Done: the geoscape windows for landing confirmation (Yes/No, Ctrl turning No into Patrol), a refused
starting condition, base defense (start or skip firing, and a shot-down UFO), a destroyed base, the
campaign's end and the "tactical combat is not available yet" screen for a battle request, and the
Cydonia button; a loaded save reopens a pending battle request or base defense
([status](strategic-interception-status.md#s8--ui-2026-10-13)). Since done: the interception windows
(as text panels) with D3's dogfight timer, and the globe seasons option. Remaining: the windows'
sprites and the base-defense round animation.

### S9 — Scenarios (2026-10-13)

Done: dispatch-to-request through the geoscape client (interception, crash, landing, request, reload),
the base-defense variants (fire, skip, base lost and campaign lost), a hunter-killer attack and escorts
joining it, site and alien-base requests, and the TFTD depth wait; target loss is covered by the pursuit
tests ([status](strategic-interception-status.md#s9--scenarios-2026-10-13)). Since done: the 19 staged saves'
reclassification with headless branch 5 choices, and the Windows SDL smoke run. S9 is complete.

### S10 — Closure (2026-10-17)

Done: [ADR 0034](decisions/0034-strategic-interception-and-deployment-boundary.md) and the branch 5
additions to ADR 0032, and the populated interception step profile
([status](strategic-interception-status.md#s10--closure-2026-10-17)), the ownership rows and the
acceptance mapping above. The named refusals were then resolved, and the whole-branch audit
(2026-10-17) fixed its findings
([status](strategic-interception-status.md#branch-audit-2026-10-17)). No named refusal remains;
branch 5 is complete apart from Phase 8 window polish (sprites, range bar, defense round animation).


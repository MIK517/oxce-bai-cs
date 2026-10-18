# Project audit, 2026-10-18

Baseline: `main` at `5efafc78` (Phase 6 closed, PR #76 merged). Scope: all production code under `src/`
(413 files, about 75k lines) and the test projects. Focus, in order: code duplication, decoupling for
extensibility, and whether the architecture can support multiplayer. Nothing was changed except this
document. The measuring scripts are in `.agent-tmp/audit-2026-10-18/` (`clones.py`: a normalized-window
clone detector; `methods.py`: a method-size classifier).

## Outcome

- **Little copy-paste, a lot of structural duplication.** Only about 2.4% of normalized production lines
  are literal clones (12.4% in tests). Most of the duplication is structural, in four places:
  - Code that proves a tick is safe before running it, which mirrors the execution logic (D1). It is about
    a fifth of `Oxce.Gameplay`.
  - Several parallel representations of the same concept (D2).
  - One copy of the loading boilerplate per rule-family group (D4).
  - Hand-maintained lists that name the same fields several times (D3).
- **Project boundaries hold. Seams inside `Oxce.Gameplay`, `Oxce.Mods`, `Oxce.Savegames` and the UI don't.**
  Inside those projects, several monoliths sit on Phase 7's path (E1–E6). They are cheap to fix before the
  battlescape adds 20+ rule families and the largest state graph in the game, and expensive afterwards.
- **Multiplayer doesn't need a full rework of the strategic layer.** A host-authoritative model (dedicated
  or listen server, thin clients) fits it with moderate changes:
  - a new session layer;
  - moving time and pause policy out of the UI client;
  - a few small gameplay changes: per-actor logistics quotes, command roles, an explicit atomicity contract.

  Peer-to-peer lockstep is not recommended. The battlescape doesn't exist yet. If Phase 7 adopts the
  checklist in M4, it needs no rework for co-op squads or a human alien team. If it is designed
  single-player first, retrofitting those later would be the expensive part.

## Part 1 — Duplication

### D1. The forecast and preflight layer (largest)

Every tick is proved safe before it changes anything (ADR 0025, 0033, 0035). Each executed rule therefore
has a speculative twin that predicts what the rule, and every earlier handler in the same tick, will do.

Evidence:

- **Dedicated files.** 5,871 of the 32,822 lines in `Oxce.Gameplay/Campaigns` are in files that exist only to
  forecast or prove: `*Forecast*`, `*Preflight*`, `*Alternatives*`, `*Capacity*`, the
  `CampaignWorldHunting{Crafts,Inputs}`, `NewLeader*`, `Newborn*`, `ConditionalLeaderFlight` and
  `GeneratedWaves` files, and the `WorldDetection*` safety, range and forecast family. More forecast
  methods sit inline in execution files, for example `CampaignResearchProduction.cs`,
  `CampaignWorldAlienBaseCleanup.cs` and `CampaignWorldMissionInterruption.cs`.
- **Rules split into edge-case variants.** One reference rule (UFO hunting and escort flight) is spread over
  `CampaignWorldNewLeaderFlight`, `CampaignWorldConditionalLeaderFlight`, `CampaignWorldNewLeaderPursuit`,
  `CampaignWorldNewbornHunting`, `CampaignWorldNewbornEscortOrigins`, `CampaignWorldHuntingCrafts` and
  `CampaignWorldHuntingInputs`. Each file covers one combination of "born this tick", "leader" and
  "conditional" that the forecast cannot otherwise prove.
- **Static analysis of compiled scripts.** `WorldDetectionScriptSafety` walks compiled script instructions to
  prove that detection scripts cannot fail mid-tick: in effect, an abstract interpreter over VM bytecode.
- **Work budgets** are hand-threaded:
  - 111 `ref long work` parameters and 156 inline `1_000_000` checks;
  - 72 distinct "exceeds the supported work limit" sentences;
  - `MaximumAlienBaseChecks` is the de facto world budget, used 195 times in 31 files, most of them
    unrelated to alien bases;
  - the production stock forecast reports "UFO retargeting exceeds the supported work limit" (23 times).
    That is correct only because its one caller is the hunting forecast, so the budget's owner is implicit.
- **Refusals that exist only because a forecast can't prove a case**, for example "Craft production requires
  daily research identity forecasts.", together with the tests that pin them.
- **Tests mirror the variants.** There are 198 `StrategicWorld*` test files. The test clones are concentrated
  in them (`StrategicWorldHuntingTests` 28% duplicated, many `Optional*` and `Newborn*` files 30–58%).

Why it matters: every new rule is written twice (execute and forecast), and the two must agree forever.
ADR 0035 already throws when a staged recheck disagrees with its shadow, which shows the risk is real. The
battlescape has far more script hooks, per-unit randomness and interleaved reactions than the geoscape, so
this pattern does not scale into Phase 7.

Options, cheapest first:

1. **Consolidate the infrastructure now.** A `WorkBudget` value carries its own limit and reason (one per
   pass, named by owner) and replaces the `ref long work` plus literal pairs. Stable reason codes replace
   the free-text copies.
2. **Retire forecasts as features become executable.** Many forecasts guard port-incomplete behaviour (the
   136 named restrictions) or arithmetic bounds. Once a handler is complete and its inputs are validated at
   restore time, it cannot fail mid-tick, so its forecast and its variant tests can be deleted. Make
   "delete the forecast" an explicit closure step of each Phase 7 restriction-retirement slice.
3. **Decide before Phase 7 how a tick (or battle action) that cannot complete is undone.** ADR 0035 already
   proves exact shadow execution works. Two generalizations remove the need to forecast:
   - *Promote the shadow*: run the tick on the shadow and adopt its state on success, instead of running the
     tick twice.
   - *Journaled mutation*: an undo log on the real state.

   Both conflict with the "no rollback" rule in ADR 0035 and need a cost measurement. Branch 4c measured
   capture at 0.03 ms and restore at 0.27 ms. ADR 0035 also notes that staging every half-hour tick would
   allocate about 1 MB per tick, which the budgets reject. Either path needs cheaper copies: copy-on-write
   collections or a journal. Record the choice in an ADR in the Phase 7 planning slice. Whatever is chosen,
   don't carry prove-before-run into the battlescape.

### D2. Parallel representations of the same state

For one strategic entity the code typically has:

- an internal state class (`CampaignState.BaseState`, `CraftState`, …);
- a save-neutral snapshot record (23 `*Snapshot` types);
- one or more query view records (`CampaignBaseOverview`, `CampaignWorldCraft`, …);
- the YAML mapping in `Oxce.Savegames`;
- an extension DTO mirror (`ExtensionCampaignBaseOverview`, …).

Some of this is deliberate (ADR 0008 keeps persistence out of gameplay). Two parts are accidental:

- **Two live-state styles.** The campaign core keeps mutable classes with rule handles. `CampaignWorld`
  keeps lists of immutable snapshot records with string rule IDs as its live state (`List<UfoSnapshot>`,
  `List<AlienMissionSnapshot>`). It resolves those IDs on every use: the idiom
  `rules.X[rules.X.GetRequired(id)].Value` appears 146 times, and `GetRequired` 336 times. `BaseState` mixes
  both styles: it keeps `List<TransferSnapshot>` as live state.
- **The extension mirror grows by hand.** `CampaignExtensionSession` maps two of about 40 commands with a
  `switch`, and every new command needs another DTO and another case.

Recommendations:

- Add a one-call lookup (`rules.AlienMissions.Required(id)`) now. This is mechanical.
- Pick one live-state convention for new aggregates (rule handles in live state, snapshots only at the
  persistence and shadow boundary) and record it in the Phase 7 battle ADR. The world doesn't need a rewrite.
- Replace the extension mirror with the shared public contract layer described under M2. Extensions, the
  network protocol and replays need the same versioned commands and events.

### D3. Hand-maintained field lists

`CampaignOptions` names each of its 20 options five times:

- the record (`CampaignContracts.cs`);
- `WithFixedUserOptions`;
- `ReadOptions`, `ReadReferenceOptions` and the `oxcePortOptions` writer in `OxceSaveAdapter.cs`.

Defaults are written in three of those places. A descriptor table (key, default, kind) would cut that to one
place. For multiplayer, the same table should mark which options are campaign rules (shared) and which are
player preferences (`UfoLandingAlert`, `Autosave`, `SuppressRedundantHunterAlert`, …).

### D4. Per-group rule-loading scaffolding in `Oxce.Mods`

Eight rule-family group catalogs repeat the same load scaffolding (`Load(plan…)` → compose → typed loaders),
with their own pieces each:

- `CampaignStart`, `EquipmentProduction`, `Items`, `MissionEvents`, `PersonnelTactical`, `Presentation`,
  `TerrainDeployment` and `Phase3`.
- Seven `XValidation` / `XValidationIssue` record pairs with the same shape.
- Their own `*Yaml.cs` helpers.

Nine files across `Oxce.Savegames`, `Oxce.Mods` and `Oxce.App` define their own keyed wrappers
(`Integer(map, key, default)`, `RequiredString`, …) over `YamlValueReader`. They are thin, but they are
repeated.

`RuntimeRuleLinker.Link` is a single method of about 900 lines that builds every runtime family. Phase 7
adds terrains, map scripts, unit, armour and item tactical fields, and the battle script hooks.

Recommendations:

- One generic validation issue type.
- Keyed accessors (`Optional`, `Required`) on `YamlValueReader`.
- Per-family link registration, the same pattern as `ICampaignCapability`.
- Rename the phase-era names (`Phase3ContentCatalog`, the `Phase3` folder) while touching them.

### D5. Small items

- `CampaignSdlCommand.Run`, `RunSmoke` and `Program.cs` repeat the content and asset loading sequence.
- `GeoscapeClient` and `CampaignLogisticsClient` each have their own text drawing, palette and key handling.
- Image codecs repeat header parsing (`IndexedBmp/Gif/LbmCodec`).
- `IndexedInterfaceFont` is 41% duplicated: glyph tables.
- Tests repeat scenario setup blocks (the `StrategicWorldDailyResearch*` family shares a 35-line block).
  A few scenario builders in `tests/Shared` would remove most of these.

## Part 2 — Decoupling and extensibility

### E1. Capabilities share one mutable graph

ADR 0027's registry is a good extension point: commands by exact type, ordered timed handlers, preflights,
queries, operations, save gates and transient copies. Under it, though, 8 of the 9 capabilities are partial
classes of `CampaignState` (49 files, 9.4k lines). `CampaignWorld` is one partial class of 61 files (14.2k
lines) that reads `campaign.BaseStates` 127 times and `campaign.Content` 411 times. Cross-capability calls
go through 17 internal operation interfaces.

This is acceptable under a single writer. The recommendation is not to split it further but to give the
battle its own aggregate root with its own registry, and to generalize `CampaignCapabilityRegistry` so
both can use it.

### E2. Gameplay reads script VM internals

`WorldDetectionScriptSafety` inspects `CoreScriptOperation`, operands and jump labels of compiled programs.
Any change to code generation in `Oxce.Scripting` can silently change gameplay refusals. If this analysis
stays, it belongs in `Oxce.Scripting` as a published property of a program, for example "bounded steps" or
"bindings used". It would disappear with D1 option 3.

### E3. The save adapter is one module per format

`OxceSaveAdapter.cs` (79 KB) knows every implemented piece of state. The world and research diary are
already split out (`OxceWorldSave`, `OxceResearchDiarySave`). `SavedBattleGame` is about 3.8k lines in the
reference, so before it arrives:

- let capabilities register save parts;
- add `OxceBattleSave` as its own module, with the opaque-overlay rule it has today.

### E4. No screen framework

`architecture.md` lists a "state stack" in `Oxce.Engine`, but there is none. `GeoscapeClient` tracks its modals
as nullable fields (`_selecting`, `_confirm`, `_intercept`, `_bases`, `_naming`, `_popups`).
`GeoscapeCovered` decides from that UI state whether game time and dogfights run (ADR 0034 D3).
`CampaignLogisticsClient` is a second, separate keyboard UI. The battlescape and Phase 8 screens need:

- a screen stack;
- shared widgets and input routing;
- the pause policy expressed outside the screens, which multiplayer needs too (M3).

### E5. The command contract signals invalid input with exceptions

`GeoscapeClient.Execute` catches five exception types from `Execute`. Atomicity on an exception is implicit:
nothing guarantees that a handler which throws hasn't already mutated state. For a local UI this is
tolerable. For untrusted remote input, the contract has to be explicit: either "validation never throws
after the first mutation" (and is tested), or "a throwing command rolls back".

### E6. Documentation drift

- `architecture.md` describes the state stack (E4).
- [`implementation-plan.md`](implementation-plan.md#immediate-next-tasks) still says branch 6 is "delivered on
  its branch".
- The [`phase-6-plan.md`](phase-6-plan.md) status line still says "not a Phase 6 completion claim".
- Commit hashes quoted in plans and status records (for example `e10d96f`, `1d532c1`, `752e976`) are no
  longer in `main`'s history after the history rewrite. Cite PR numbers instead.

## Part 3 — Multiplayer assessment

Target features:

1. co-op control of one organization;
2. pre-divided roles (strategic command, squad per player);
3. shared time controls (slowest selected speed, or last activated);
4. playable aliens as a separate team.

### M1. What already fits

- **Headless authoritative simulation.** `Oxce.Gameplay` has no UI, platform or wall-clock dependencies; time
  only advances through `AdvanceCampaignTime`.
- **Commands are plain records** with stable value identities: base IDs, `(craft type, id)`, UFO unique IDs,
  `AnswerLanding(Confirmation, …)`, `SubmitLogisticsOrder(QuoteId, …)`. They serialize naturally and are
  executed under a single writer lock.
- **Deterministic randomness** sits behind `IStatefulRandomSource`, with `State`/`Restore`. There is no
  `DateTime.Now`, `Guid.NewGuid` or dictionary-order dependence in gameplay (AGENTS rules, staging tests).
- **Cheap, complete snapshots** (`Capture`/`Restore`, ADR 0035 numbers) and a canonical snapshot compare
  (`StagingVerification`). These are what late join, resync and desync detection need.
- **Separate query and command ports.** The UI consumes only query view records and sends commands
  (`CampaignUiSession`), and those views are already presentation-shaped and visibility-filtered for X-COM.
- **A decorator seam that observes committed events:** `CampaignExtensionSession` wraps
  `ICampaignCommandTarget`. A network session is the same shape.
- **Modal decisions are gameplay state with IDs**, not UI state: landings, base defense, monthly report,
  event windows, deployment request (D2 and D4 holds). A server can route them to the right player.
- **A content identity hash already exists** (`CompiledContentCache.ComputeKey`). Peers can compare it at
  handshake to prove identical mods.

### M2. Topology: host-authoritative, not lockstep

Peer-to-peer lockstep requires bit-identical simulation on every machine. Gameplay uses `double` in 231
places and `Math.Sin/Cos/Atan2/Acos/Sqrt` in globe geometry (64 calls). .NET's libm differs between OS and
CPU targets, so lockstep would be safe only between identical builds on identical platforms. It would also
give an alien player the full state, which breaks fog of war.

A host-authoritative model avoids all of that:

- One process (a dedicated server, or a "host" player's machine) runs `CampaignState`.
- Clients send command envelopes and receive committed events plus query views.
- The current UI already works against `ICampaignQuery` and the typed query interfaces, so a client-side
  implementation fed by server pushes (revision-based, like `PresentationRevisionGate`) drops in.

Where bandwidth matters (battle maps), views can be replicated as deltas, and a snapshot hash at each
month or turn boundary detects divergence.

Structural changes required (new code, not rework):

1. **A session layer**, for example a new `Oxce.Session` project between `Engine` and `Gameplay`:
   - players and roles;
   - `Execute(PlayerId, ICampaignCommand)` with authorization;
   - broadcast of committed events (today only the caller receives them);
   - the server clock (M3);
   - save, load and join.
2. **One versioned public contract layer** for commands, events and query views, shared by the network
   protocol, managed extensions (replacing the hand mirror, D2) and replays.
3. **Command metadata.** Register a role (strategy, logistics, squad, …) with each command handler in
   `CampaignCapabilityRegistry`, and give unit-addressed commands an interface exposing the unit ID, so
   ownership is checked in one place.
4. **An explicit atomicity contract** for commands (E5).

### M3. Feature by feature

| Feature | Fit | What changes |
|---|---|---|
| Co-op, one organization | Good | Session layer (M2). One gameplay blocker: the logistics quote is a single campaign-wide slot (`CampaignState._logisticsQuote`), cleared by every time advance and by several other commands. With shared time running, a buy, sell or transfer screen could never submit. Make quotes per actor, keyed by `QuoteId`, and revalidate them on submit instead of clearing them. In the reference, base screens pause time because the geoscape isn't on top; under shared time that assumption has to become an explicit policy. |
| Pre-divided roles | Good (strategy) / design-time (battle) | Strategic roles: role metadata on commands (M2.3), plus routing of modal decisions (who answers the monthly report, a landing, a base defense). Squad per player: units need an owner, which belongs in the Phase 7 design (M4). In the reference the geoscape is frozen during a battle, so the commander plays a squad or spectates. Running the geoscape concurrently with a battle would be a game-design change and would break invariants such as the craft's inventory. |
| Shared time controls | Good, but the policy moves | `AdvanceCampaignTime` already carries `SelectedSpeed`, and gameplay needs no change. Three things live in the UI client today: the clock (`GeoscapeTimeController`), the dogfight timer and "game time runs only when every window is minimized" (ADR 0034 D3), and pause on notification (`Interrupt` on any event). They move to a server-side session clock that combines per-player selections (slowest, or last activated) with per-player "covered" flags. The reference's `timerReset` popups reset the shared speed; other popups no longer stop everyone. |
| Playable aliens, battlescape | Feasible if designed in Phase 7 | Needs a faction controller abstraction, per-faction visibility and viewer-scoped queries (M4). ADR 0021 already plans the tactical AI as a single-owner provider with immutable battle queries, validated actions and host randomness. A remote human is another implementation of that provider. |
| Playable aliens, geoscape | Not without new game design | Alien strategy is script- and rule-driven (mission scheduling, arcs, waves). It has no player decision points to hand over. Scope this out, or treat it as a separate design project. |

### M4. Phase 7 checklist (decide in the planning slice)

Each item is cheap to adopt now and expensive to retrofit:

1. **Battle as its own aggregate**, with its own registry, entered through `TakeDeploymentRequest` and left
   through debriefing. No prove-before-run (D1).
2. **Actions resolve atomically in the simulation; animation replays the events.** The reference interleaves
   logic with animation states (`UnitWalkBState`, `ProjectileFlyBState`, reaction fire mid-walk). The port
   should resolve a whole action, interruptions included, as one deterministic step. Its events (path,
   shots, reactions) are what clients animate. This is required for remote clients and for several players
   acting in one side's turn.
3. **Every unit has a controller**: faction plus optional owner. AI is one controller implementation (ADR 0021),
   a local human another, and a remote human a third. End of turn waits for every controller of the side.
4. **Visibility is per faction.** The reference tracks tile discovery and spotted units for the player side,
   while the AI reads the map directly. A human alien team needs hostile-side discovery state: port-only
   data, kept out of compatible saves or stored as an overlay.
5. **Battle queries take a viewer** (faction and player) from the start, so a server can send each client only
   what that client may see.
6. **Commands that address units carry unit IDs**, and ownership is checked at the session boundary, not in
   rules.
7. **A separate battle save module** (E3).

### M5. Verdict

- **Strategic layer:** reworkable with minimal changes. The changes in `Oxce.Gameplay` are small: per-actor
  quotes, command role metadata and the atomicity contract. The real work is new code: a session layer and
  protocol, and moving the time and pause policy out of the UI.
- **Battlescape:** no rework is needed if Phase 7 adopts M4. Without it, the battle would likely need a
  substantial redesign later.
- **Geoscape alien player:** out of reach without new game design.

## Part 4 — Recommended order

1. **Cleanup branch** (mechanical; behaviour unchanged):
   - fix the documentation drift (E6);
   - the `WorkBudget` value and reason codes (D1.1);
   - the one-call rule lookup (D2);
   - the options descriptor (D3);
   - keyed YAML accessors and one validation issue type (D4);
   - shared test scenario builders (D5).
2. **ADR before Phase 7 code:** how a tick or battle action that cannot complete is undone (D1.3), with cost
   measurements of shadow promotion against a journal.
3. **Phase 7 planning slice:** add the M4 checklist and the live-state convention (D2) to the battle ADR, plus
   per-family link registration (D4) and battle save parts (E3) as early prerequisites.
4. **Before battlescape UI:** a screen stack (E4), and the session layer that single-player also runs through
   ([ADR 0037](decisions/0037-host-authoritative-session.md)):
   - player identity on commands;
   - broadcasting committed events;
   - the host-side clock and pause policy, moved out of the client (M3);
   - per-actor quotes.
5. **Multiplayer proper**, after Phase 7's first playable battle:
   - the public contract layer and the protocol (M2);
   - remote thin clients;
   - speed arbitration and role routing.

## Progress

### Cleanup branch (`claude/audit-cleanup`, step 1)

Behaviour-preserving; no refusal text, limit, save output or oracle changes.

- **E6, documentation drift.** `architecture.md` describes the engine as it is. The implementation plan and the
  Phase 6 plan record branch 6 as merged. The 28 commit hashes in the documents that the history rewrite
  removed from `main` now point at the `main` commits with the same subject. One measurement baseline
  (`c094c28`, performance baselines) is in neither repository and stays as recorded. The examples in E6
  above keep the old hashes on purpose.
- **D1.1, work limits.** `ForecastLimits` names the campaign forecasts' work budget (`Work`, which replaces 151
  literal `1_000_000` budget uses) and the 69 refusal sentences that more than one forecast repeated (300
  lines). `MaximumAlienBaseChecks` is renamed `MaximumWorldWork`, the world forecasts' shared budget. The
  production stock forecast names the reason it borrows from UFO retargeting (`StockWorkReason`).
  - *Deviation:* not a single `WorkBudget` value. A `ref long work` counter passes between the world and the
    campaign forecasts, and each side compares it with its own limit (100,000,000 or 1,000,000). A single
    budget object would change which limit applies, so the limits stay where they are and are named instead.
- **D2, rule lookup.** `RuntimeRuleFamily.GetRequiredValue(id)` replaces 208 `x[x.GetRequired(id)].Value`
  lookups.
- **D3, options.** `CampaignOptions.Fields` lists every option once: key, kind, and whether it is a reference
  user option. Mod-fixed options, the port options node (read and written in the same order) and a reference
  save's options snapshot all use it.
- **D4, YAML and validation.** Keyed `ReadString/ReadInt32/ReadInt64/ReadUInt64/ReadDouble/ReadBoolean`
  extensions on `YamlMappingNode` replace the forwarding wrappers in the save adapter, five rule composers or
  loaders, mod metadata and the UI assets. Wrappers with their own semantics (`RequiredString`, the linker's
  helpers that accept any node) stay. `RuleValidationIssue` and `RuleValidation` replace five per-group pairs
  and the personnel issue type; `PersonnelTacticalValidation` keeps its derived caches.
- **Not done here:** the shared test scenario builders (D5). They need test-by-test review and belong in a
  separate change.

# 0034: Strategic interception and the deployment boundary

Status: accepted for Phase 6 branch 5.

## Problem

Branch 5 brings interception, landing, base defense and the final mission to the campaign, and
ends each at the point where the reference starts a tactical battle. Combat runs on transient
state, the battle itself belongs to Phase 7, and several reference paths depend on undefined
behaviour or on state the port does not model yet. Each needs one recorded choice. The plan is
[phase-6-interception-plan](../phase-6-interception-plan.md) (decisions D1-D6); slice evidence is in
[strategic interception status](../strategic-interception-status.md).

## Decision

**Interceptions are a registered capability with transient state (D2).** Open interceptions live in
the campaign world; `AdvanceInterceptions` is one dogfight timer step for every window, and mode,
minimize, restore and weapon toggles are commands. Saves keep only the reference craft and UFO
fields, so a reload starts without interceptions, as in the reference. Every step is forecast on
copies first and refused before any change when an outcome is not supported.

**Time and combat interleave in the controller (D3).** The geoscape client runs the dogfight timer
every `dogfightSpeed` (30 ms) while interceptions exist and nothing covers the geoscape; game time
runs only when every window is minimized. Pending windows open in the same tick rather than after the
`_dogfightStartTimer` zoom delay.

**The battle request is a port-only save node (D4).** A confirmed landing, a base defense with
defenders and the final mission record `oxcePortDeploymentRequest` (mission type, deployment and
custom deployment ids, race, textures, shade, target, craft or base, soldiers, vehicles, items and
the base-defense UFO damage). Every command but time, `EnterCampaignWorld` and the Phase 7
`TakeDeploymentRequest` refuses while it is pending; the request is taken once per load and cannot be
withdrawn.

**Base loss ends the campaign by the reference rule (D5).** An undefended or nuclear-struck base is
destroyed with its crafts, crew and stores; the next five-second tick with no base sets `END_LOSE`.
A started campaign without bases is a valid save.

**Managed extension state stays refused (D6).**

**Intentional differences and named restrictions.**
- A newborn hunter-killer that selects and reaches a craft in its birth tick attacks it as a saved hunter
  does; restoring a water-only craft's waiting window re-checks depth and land as
  `DogfightState::btnMinimizedIconClick` does.
- `handleBaseDefense` reads a nuclear UFO's `missileStopChance` after its mission and UFO were
  deleted; the port keeps the roll and interrupts the mission only if it still exists.
- Stored vehicles join base defenders in rule order. The reference walks its stores in `RuleItem`
  address order, which follows the order rules were first loaded, so vehicle types sharing too little
  ammunition are served first-loaded first. A vehicle that loads zero clips, which the reference
  divides by, cannot be loaded and stays in the stores.
- Missiles that damage facilities follow `Base::damageFacilities` and `destroyFacility`. Which crafts a
  destroyed hangar holds comes, in the reference, from the last base view drawing (`BaseView::draw`), so it
  depends on whether the player opened the base screen; the port places the crafts as that drawing would
  at the moment of the strike.
- Of several research topics marked `unlockFinalMission`, the compiler keeps the reference's pick
  (`Mod::loadAll`: by name, a topic whose last updating mod loads no earlier replaces the previous one).
- Waves without a UFO rule for a retaliation assault are refused where the reference dereferences
  a missing rule.
- An alien-base battle request records mission texture 0 (`setWorldTexture(0, globeTexture)`).
- A UFO shot down by a craft from a base outside every region starts no retaliation against that base;
  the reference dereferences the missing region (`DogfightState::update`, `locateRegion`).
- Crew who fail the evacuation roll of a destroyed craft die (`SavedGame::killSoldier`) and join the
  memorial (`deadSoldiers`, with the death time). The reference kills them only for a craft with a unit
  limit and otherwise leaves them assigned to the deleted craft; the port kills them in either case.
- A retaliation run from an alien-base operation picks or spawns its operation base as `AlienMission::start`
  does; the interception step's forecast makes the same draws and reserves a spawned base's marker ID and
  activity as mission scripts do.

## Consequences

Each choice has a test or a named refusal. Phase 7 consumes the request through
`TakeDeploymentRequest`; removing a refusal requires the reference behaviour and its fixture or a
recorded decision.

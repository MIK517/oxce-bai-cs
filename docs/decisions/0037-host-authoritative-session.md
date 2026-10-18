# ADR 0037: Host-authoritative session boundary

Status: accepted, 2026-10-18.

## Context

The [2026-10-18 audit](../project-audit-2026-10-18.md#part-3--multiplayer-assessment) found that the strategic
layer can support multiplayer (co-op control of one organization, divided roles, shared time controls, a human
alien team in battles) without a rework, if one process is authoritative and clients are thin. It also found the
places where current code assumes one local player:

- the clock, the dogfight timer and the pause-on-popup policy live in the UI client
  (`GeoscapeTimeController`, `GeoscapeClient.GeoscapeCovered`, ADR 0034 D3);
- committed events reach only the caller of `Execute`;
- the logistics quote is one campaign-wide slot that any time advance clears;
- invalid command input is signalled partly by exceptions, with atomicity left implicit.

Multiplayer itself is not scheduled. These rules keep it from getting more expensive while the battlescape and the
remaining strategic work are written.

## Decision

The game runs as a **host** and one or more **clients**, in single-player too.

- **The host** owns the campaign and battle state, the random source, the clock, saving and loading, managed
  extensions, and every rule decision. In a later multiplayer build it runs in a dedicated server or in the hosting
  player's process.
- **A client** owns only presentation and per-player view state: open screens, selections, camera and globe view,
  key bindings, and preferences such as sound. It sends commands, receives committed events, and reads query views.
- **Single-player** is the host plus one local client in the same process, joined by direct calls. A multiplayer
  build adds only a wire protocol and a remote implementation of the same client-facing ports.

The client-facing ports are the ones that exist today (`ICampaignCommandTarget`, `ICampaignQuery` and the typed query
interfaces in `CampaignUiSession`). A session layer will wrap them before multiplayer work starts; until then, new
code follows the rules below so that wrapping them stays mechanical.

### Rules for new and changed code

1. **Commands are data.** A command is an immutable record of primitives, strings, enums, stable IDs (base, craft
   type and ID, unit, UFO unique ID, confirmation ID) and records or lists of those. It carries no object references,
   delegates, runtime rule handles or live entities, so it can be serialized as is.
2. **Validation is total and atomic.** A command that cannot run returns a classified refusal
   (`CampaignActionBlocked` with a `CampaignReason`) before it changes anything. Malformed remote input must not throw
   after the first mutation. Exceptions remain for programming errors and corrupted state. Existing handlers that
   throw on invalid arguments are migrated when they are touched.
3. **Every observable change has an event or a query revision.** A client must be able to tell that something changed
   without having caused it. Events are serializable data, like commands. A client refreshes from queries when it
   receives events or a new revision, and never assumes that its own command result is the only change.
4. **Queries return immutable views.** No live collections, mutable entities or runtime handles leave the host.
   Features with hidden information (every battlescape query, anything a future alien player must not see) take
   the viewer, a faction and later a player, as a parameter from the start.
5. **The host decides time.** Clients request a speed; the host's clock applies it. New code does not add pause,
   speed or "screen covers the geoscape" decisions to UI clients. The existing ones move to a host-side session clock
   when the session layer is built (audit M3).
6. **Per-player state stays out of the campaign.** State that belongs to one player's interaction (a pending quote,
   a selection, a draft) is either client state or host state keyed by actor and ID. It is never a single campaign-wide
   slot that another player's action or a time advance invalidates.
7. **Decisions are gameplay state with IDs.** A window whose answer has gameplay effects (landing, base defense,
   monthly report, event) is pending state with an ID that a command answers, as today (ADR 0034, ADR 0036). The host
   can then route it to the right player.
8. **No process-wide mutable state in the simulation.** One host process may run several sessions. Test-only switches
   use `AsyncLocal` or per-instance options, as the existing ones do.
9. **Determinism stays as it is.** All randomness goes through `IStatefulRandomSource`; there is no wall clock and no
   unordered enumeration in rules. The host relies on this for staged ticks, replays and desync checks. Lockstep across
   machines is not a goal (globe geometry uses platform floating point).
10. **Battles are designed for several controllers.** Phase 7 follows the audit's checklist (M4): the battle is its own
    aggregate; an action resolves completely in the simulation and animation replays its events; every unit has a
    controller (AI, local or remote human); visibility is per faction; battle queries take a viewer; commands address
    units by ID and ownership is checked at the session boundary.

## Consequences

- The UI keeps talking to command and query ports, which a remote client can implement. The single-player build
  exercises the same boundary that multiplayer will use, so most multiplayer defects would show up as single-player
  defects first.
- Some single-player conveniences need an explicit host decision instead of a UI shortcut. For example, pausing when
  a popup opens becomes a session policy, and is still the reference behaviour when there is one client.
- The extension API and a future wire protocol need the same versioned contracts for commands, events and views
  (audit M2). They should be built once.
- Not decided here: the transport, the protocol format, lobby and join, and how player roles map to commands. These
  belong to the multiplayer branch.

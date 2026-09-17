# ADR 0027: Registered campaign capabilities

Status: accepted, 2026-09-17.

## Context

`CampaignState` grew to about 4.5k lines of partial classes behind one transaction gate,
one `Execute` switch and a hard-coded `TimeEffects.Apply` sequence. Every strategic
feature edited those central members, so the order of timed handlers was implicit in
source layout rather than stated against `GeoscapeState`. The
[2026-09-16 audit](../project-audit-2026-09-16.md) (item 4) scheduled a split at the start
of Phase 6 branch 4, before world simulation adds its state.

## Decision

- A campaign is composed of `ICampaignCapability` instances listed in one composition root
  (`CampaignCapabilityCatalog.cs`). Each capability registers, through
  `CampaignCapabilityRegistry`:
  - commands, dispatched by exact concrete type; a second handler for a type is an error;
  - time handlers per trigger with an explicit order from `CampaignTimeOrder`, and time
    preflight checks with an order from `CampaignPreflightOrder`; equal orders or names
    within one trigger are rejected, so the reference order is always stated;
  - snapshot capture and restore pieces for the state it owns, validators that run on the
    complete restored graph, and initializers for new campaigns;
  - query interfaces, published through `CampaignState.GetQuery<TQuery>()`.
- `CampaignState` keeps the single writer, the transaction gate, the calendar, base graph
  validation and the dispatcher. Capabilities never take their own locks; queries use
  `CampaignState.Read`.
- The order constants are sparse and mirror statement order inside the reference
  `time5Seconds` ... `time1Month` handlers. Where the reference interleaves several
  features inside one loop (for example the `time1Day` base loop), the loop stays one
  registered handler so that state and random-number order do not change.
- Existing features register through partial-class methods; new features (world
  simulation onward) are separate classes that own their state and register the same way.

## Consequences

The refactoring is behavior-preserving: existing unit, compatibility and save fixtures pass
unchanged, and a unit test locks the installed handler order. New capabilities add a
registration and, when needed, an order constant instead of editing `Execute`, the time
effects or `CaptureCore`. Registrations are built once per campaign; dispatch uses a frozen
dictionary and pre-sorted arrays, so the empty time workload stays allocation-free.

# ADR 0021: Versioned managed-extension boundary

- Status: accepted
- Date: 2026-09-03

The initial `0.1` combined campaign-access shape below was superseded by the versioned
capability bundle in [ADR 0023](0023-hot-runtime-content-and-versioned-capabilities.md).
The trust, loading, failure-containment, state, and deferral policies remain in force.

## Context

The first persisted campaign slice has exercised gameplay-owned identities, read-only
queries, validated commands, ordered events, and external save adapters. These are now
mature enough to support the previously deferred trusted managed-extension boundary.
The expected production use is a future BrutalAI-equivalent tactical AI provider, but
the boundary must remain extensible without exposing mutable engine state or turning a
general service locator into the public API.

Some future extensions may own state whose meaning must survive a save/load cycle. For
example, an extension may place a player craft into a special geoscape mission whose
flight pattern and completion event do not exist in OXCE. Such a save is not expected
to remain semantically compatible with the reference engine while the extension is
enabled, but extension-free saves remain within the normal compatibility contract.

## Decision

Add a small, explicitly versioned `Oxce.Extensions.Abstractions` assembly. Extension
assemblies reference this contract rather than gameplay, persistence, ruleset, YAML,
rendering, SDL, or engine implementation assemblies.

Managed extensions have these initial policies:

- Extensions are trusted in-process code. The API narrows coupling and mutation but is
  not a security sandbox.
- Extensions are manually installed in one directory per stable extension ID. A small
  JSON manifest declares identity, extension version, entry assembly and type, and the
  supported host API range.
- The initial contract version is experimental `0.1`. Host/API compatibility is
  negotiated independently of the engine product version. Unsupported versions,
  duplicate IDs, malformed manifests, and invalid entry points fail with structured
  diagnostics.
- Extensions are loaded once for the process lifetime. Hot reload, unloading, and
  package management are deferred.
- Contracts are capability-oriented. Extensions receive only explicitly granted
  read-only queries, validated command submission, committed event observation,
  lifecycle context, diagnostics, and cancellation. They do not receive mutable
  gameplay entities, persistence DTOs, YAML nodes, service-provider access, runtime
  handles, resource caches, or platform objects.
- An extension failure is contained at its callback boundary, reported, and disables
  that extension. It must not escape into the authoritative simulation loop.
- Command execution remains single-writer and non-reentrant. Events are observed only
  after the owning command has committed.

Reserve a bounded, save-neutral extension-state envelope now. Payloads are namespaced
by stable extension ID and carry extension version, state-schema version, a
`requiredForContinuation` flag, and an owned structured value. Extensions never read or
write YAML directly. `Oxce.Savegames` owns external encoding and preservation, while
the application coordinates a consistent campaign and extension-state capture.
Extensions own migrations between their state-schema versions and use stable external
entity/rule IDs rather than object references or generation-scoped handles.

Loading policy is explicit:

- a missing extension whose state is required prevents normal continuation;
- optional missing-extension state is preserved without interpretation;
- incompatible state requires an extension-provided migration or prevents loading;
- extension-free campaigns emit no managed-extension section and retain ordinary OXCE
  compatibility.

## Initial executable slice

The current campaign foundation can prove manifest discovery, version negotiation,
assembly/type validation, lifecycle, failure containment, campaign overview queries,
post-commit campaign events, validated campaign commands, and bounded state-envelope
round trips. A test extension must compile against abstractions only.

The persistence envelope and codec are implemented as an independent semantic
contract. Integrating it into the OXCE save root and coordinating live extension state
capture is deferred until an extension owns meaningful state; doing that earlier would
invent transaction and migration behavior without a consumer.

## Deferred capabilities

- Tactical AI is added with the tactical vertical slice. It will use a dedicated,
  single-owner AI-provider capability, purpose-built immutable battle queries, validated
  tactical actions, host-supplied deterministic randomness, and explicit work/allocation
  expectations. It will not reuse a generic event bus as an AI ABI.
- Geoscape craft-mission hooks and their persistent identities wait for the owning
  craft/mission gameplay slice.
- UI contribution, telemetry, content-generation hooks, extension dependency graphs,
  package management, hot reload, unloading, and out-of-process isolation require
  separate decisions if demanded by real extensions.

## Consequences

- The public extension ABI can evolve independently of gameplay implementation
  assemblies and saves.
- New capabilities require explicit adapters, which is deliberate friction against
  accidental broad coupling.
- Extension-enabled saves may intentionally diverge from OXCE, while disabled or absent
  extensions cannot silently alter extension-free compatibility behavior.
- Trusted code can still crash, block, or inspect the process; failure containment is
  resilience, not isolation.

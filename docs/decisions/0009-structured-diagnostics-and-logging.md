# ADR 0009: Owned structured diagnostics with a standard logging adapter

- Status: accepted
- Date: 2026-08-19

## Context

Mod discovery, ruleset composition, scripting, saves, and asset loading need stable,
testable diagnostics carrying provenance such as source span, layer, mod, rule type, and
rule ID. Formatted log text is not a compatibility contract and cannot be the only
representation. Untrusted inputs can also produce excessive diagnostic volume.

The application still needs integration with maintained console, file, and other log
providers without implementing a logging framework locally.

## Decision

Define `DiagnosticEvent`, `DiagnosticContext`, severity, `IDiagnosticSink`, a null sink,
and a bounded thread-safe collector in `Oxce.Core`. Compatibility tests assert these
owned values rather than formatted messages.

Use `Microsoft.Extensions.Logging.Abstractions` 10.0.10 through an adapter in
`Oxce.Engine`. The adapter preserves diagnostic fields as structured logging properties.
Lower compatibility projects do not depend on Microsoft logging types, and the
application remains responsible for selecting providers and output policy.

The package is maintained by Microsoft, MIT-licensed, supports .NET 10 through its
modern target frameworks, and introduces no native dependency. The abstractions package
is compatible with the current non-AOT deployment and does not require reflection-based
serialization in this use.

## Consequences

- Diagnostic codes and structured fields are stable test inputs; rendered log messages
  may evolve.
- Producers accept or receive an `IDiagnosticSink`; they do not write directly to the
  console or select log destinations.
- Collection is explicitly bounded and exposes the number of dropped diagnostics.
- `Oxce.App` will configure providers when user-facing startup and mod loading arrive.
- New context fields should remain compatibility-domain concepts rather than logging-
  provider-specific state.

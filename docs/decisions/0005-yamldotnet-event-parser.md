# ADR 0005: Use YamlDotNet as a low-level YAML parser

## Status

Accepted for the Phase 1 compatibility spike.

## Context

OXCE uses rapidyaml behind an owned `YamlNodeReader` layer. Compatibility depends on
OXCE behavior for missing and null values, ordered and duplicate mapping entries,
multiple documents, aliases, typed conversions, diagnostics, and resource limits. A
reflection serializer would make the external C# object model the schema and would not
provide the required control.

## Decision

Use YamlDotNet 18.1.0, under its MIT license, only through its low-level parsing events.
Build an immutable DOM in `Oxce.Formats` and implement all OXCE lookup, conversion,
diagnostic, alias, and resource-limit semantics in owned code.

YamlDotNet is a managed dependency compatible with .NET 10. It introduces no native
library or dynamic-code requirement in the parsing path used by this project.

## Consequences

- Parser upgrades require fixture and malformed-input regression runs.
- Production code must not use YamlDotNet object serialization as a ruleset or save
  compatibility model.
- The owned DOM may deliberately differ from YamlDotNet's representation when required
  by reference fixtures.
- Native AOT impact is limited because the selected event API does not require runtime
  reflection-based serialization.

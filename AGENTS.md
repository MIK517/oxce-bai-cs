# Agent instructions

These instructions apply to the entire repository.

## Mission

Implement a .NET 10 version of OXCE that:

1. Loads existing OXCE mods, including the existing script language.
2. Reads existing supported save files and writes saves accepted by this port; when
   feasible, new saves should also load in the reference C++ engine.
3. Reads the same original UFO: Enemy Unknown and Terror from the Deep assets.
4. Implements the same player-visible game rules and mod-visible semantics.

Exact RNG stream parity, pixel-perfect rendering, and an identical UI are explicitly
out of scope. Do not accidentally turn those non-goals into weaker data, scripting,
or gameplay compatibility.

## Authoritative references

- Reference engine: separate `oxce-bai` checkout, conventionally at `../oxce-bai`. Only for reference, no changes expected.
- Compatibility definition: `docs/compatibility-contract.md`
- Architecture and dependency direction: `docs/architecture.md`
- Current roadmap: `docs/implementation-plan.md`
- Test/oracle rules: `docs/testing-strategy.md`

The C++ tree is a behavioral specification, not a template that must be translated
line by line. Inspect the exact reference implementation before porting a behavior.
Do not modify the C++ tree as part of work in this repository unless the user asks.

## Required workflow

Before implementing a compatibility-sensitive feature:

1. Identify the corresponding C++ files and record them in the PR/change summary.
2. Write or capture a small fixture that demonstrates the existing behavior.
3. Implement the behavior in the owning project; do not bypass project boundaries.
4. Add unit tests for local rules and compatibility tests for external behavior.
5. Update the relevant compatibility matrix or plan item.

Prefer vertical slices that load real data and produce observable output. Avoid large
mechanical translations with no executable acceptance test.

## Compatibility rules

- Preserve ruleset keys, default values, merge order, error conditions, and script
  semantics. C# property names are not the external schema.
- Do not use a reflection serializer as the compatibility model for YAML. A library
  may parse YAML, but OXCE node semantics must live in our own adapter.
- Preserve integer widths, signedness, overflow, rounding, coordinate systems, and
  ordering wherever they affect gameplay, saves, resources, or scripts.
- Do not depend on dictionary enumeration unless ordering is explicitly defined.
- Keep randomness behind `IRandomSource`. Reproducible tests are required even though
  the sequence need not match C++.
- Treat save and mod parsing as untrusted input: validate sizes, offsets, recursion,
  and resource limits.
- Never commit copyrighted original game assets. Private fixtures belong under the
  ignored `fixtures/private/` directory.

## Engineering rules

- Target `net10.0`; use nullable reference types and keep warnings as errors.
- Keep the core and compatibility libraries platform-independent.
- Native calls belong only in `Oxce.Platform.Sdl` or another explicitly approved
  platform project. Prefer source-generated `LibraryImport` and safe lifetime wrappers.
- The canonical software surface is 8-bit indexed data. Do not make RGBA textures the
  domain model merely because the display backend uses them.
- Avoid per-frame and inner-loop allocations. Measure before using unsafe code; keep
  unsafe blocks small, documented, and covered by tests.
- Add external packages only when their license, maintenance status, Native AOT impact,
  and compatibility implications have been reviewed.
- Record consequential architectural choices in `docs/decisions/`.

## Completion standard

A subsystem is not complete because it compiles. It is complete when its agreed
fixtures pass, malformed inputs fail intentionally, relevant plan/matrix entries are
updated, and no compatibility behavior remains hidden in placeholder code.

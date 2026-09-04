# ADR 0024: Portable virtual-path canonicalization

- Status: accepted
- Date: 2026-09-04

## Context

OXCE indexes resource paths through `FileMap::canonicalize`, which calls
`Unicode::lowerCase`. The pinned Windows build uses `CharLowerW`; non-Windows builds
use the first discovered UTF-8 locale. Consequently, unusual Unicode equivalence can
vary with the operating system and locale. OXCE does not normalize Unicode composition
forms, and ZIP entry sanitation changes separators but not normalization.

The port previously used `string.ToLowerInvariant`. That was deterministic but did not
state its compatibility policy, discarded an entry's original relative spelling, and
folded some compatibility characters that the pinned Windows reference leaves alone.
Durable compiled-content keys must not vary with the user's current culture.

The normative sources inspected for this decision are `src/Engine/FileMap.cpp` and
`.h` (`sanitizeZipEntryName`, `VFSLayer::insert`, `mapZip`, `mapPlainDir`, and
`canonicalize`) plus `src/Engine/Unicode.cpp` and `.h` (`getUtf8Locale` and
`lowerCase`) at reference commit `4df3a5e571a1a4b5e8a46d3161fb2e21a2adba15`.

## Decision

Virtual paths use one portable, culture-independent definition on every supported
platform:

1. Convert backslashes to forward slashes and reject rooted, ambiguous, empty-segment,
   traversal, NUL-containing, or invalid-Unicode paths.
2. Preserve Unicode normalization forms exactly; canonically equivalent composed and
   decomposed spellings remain different virtual files, as in OXCE.
3. Lowercase each Unicode scalar only when its invariant lowercase mapping round-trips
   through the same invariant uppercase scalar. This matches the pinned Windows
   reference fixture for ASCII, Latin, Greek, dotted-I, capital sharp-S, Kelvin-sign,
   combining-mark, and supplementary-plane cases without native platform calls.
4. Store the separator-normalized original relative spelling on `VirtualFileEntry`
   alongside the canonical lookup key. ZIP prefix removal operates on original path
   segments rather than slicing a case-mapped string.

`VirtualPath.CanonicalizationVersion` identifies this contract and participates in the
compiled-content cache key. Any later change to case or normalization semantics must
increment it and replace the reference fixture.

## Consequences

- Lookup, same-layer collision, layer precedence, and loose-over-archive behavior are
  deterministic across host cultures and filesystems.
- The portable policy matches the captured pinned Windows behavior. It does not attempt
  to emulate every locale-dependent result from every C++ host; mods intended to be
  portable must not depend on such differences.
- Diagnostics and tools can display the declared spelling while caches and lookups use
  a stable key.
- The ASCII normalization workload remains 102.4 ns and 376 B per operation on the
  Ryzen 9 7940HS comparison host, versus the prior 90.9 ns and 376 B result. Unicode
  normalization is 343.6 ns and 216 B. Both remain small relative to file discovery
  and content compilation, and the common ASCII path avoids rune processing.

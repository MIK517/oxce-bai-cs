# ADR 0026: Strict and compatibility input validation modes

Status: accepted, 2026-09-16.

## Context

The port rejects some game and mod input that the reference engine silently tolerates.
The 2026-09-16 audit found two such places:

- Virtual-file lookups. `FileMap::canonicalize` only lowercases a requested path, so a
  `\` separator, a rooted path or an empty, `.` or `..` segment simply never matches.
  The port normalized `\` to `/` and threw for the other spellings.
- CAT containers. `CatFile` maps no entries when the first offset is not inside the
  file, ignores the 8-byte alignment of that offset, and skips entries whose offset is
  outside the file, which shifts the index of every later entry. The port rejected these
  tables. Its shared-sound count read only the first offset, so a table whose first
  offset equals the file length counted entries the reference never maps.

Rejecting malformed input finds mod defects early and is the preferred default. Some
existing mods that load in OXCE may still depend on the tolerant behavior.

## Decision

`Oxce.Core.Compatibility.InputValidationMode` selects the behavior per launch:

- `Strict` (default) rejects malformed input with an error. Virtual-file lookups still
  accept `\` separators.
- `Compatibility` follows the reference. A malformed lookup path is a miss (including any
  path containing `\`). A CAT table whose first offset is not inside the file has no
  entries. Out-of-range CAT entries are skipped, and later indexes shift like
  `CatFile::getRWops`.

The application selects the mode with a leading `--input-mode=strict|compatibility`
argument. `InstallationLoadRequest.ValidationMode` carries it into planning. The mode is
stored in `ModLoadPlan.ValidationMode`, applied by the plan's `VirtualFileCatalog`, used
for shared CAT counts, and included in the compiled-content cache key (compiler
revision 15). `CatArchive.Parse` and `CatArchive.ReadEntryOffsets` take the mode
directly.

Some checks protect the host and apply in both modes: path escapes from external
resource directories, ZIP entry sanitization, and entry, size and depth limits.
Decreasing CAT offsets are also rejected in both modes, because the reference would
derive a wrapped size from them.

## Consequences

- Strict remains the reference for fixtures and CI. Compatibility-mode behavior has
  explicit unit and loader tests.
- A new tolerance goes behind this mode, with a test for each mode. It is not added
  silently to the strict path.
- Tools that build content without `InstallationLoadRequest` get strict behavior unless
  they pass the mode to `ModLoadPlanner.Create`.

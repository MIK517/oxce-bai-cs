# ADR 0010: BCL ZIP archives as lazy virtual-file sources

- Status: accepted
- Date: 2026-08-20

## Context

OXCE mods may be directories, single-mod ZIPs, or multi-mod ZIPs. Resource lookup must
retain layer provenance and open archive content on demand without extracting whole
mods into temporary directories. ZIP input is untrusted and needs explicit limits and
path validation.

An external archive package would add licensing, maintenance, deployment, and Native
AOT review work. The required compatibility slice only needs ordinary ZIP central
directory enumeration and entry decompression; it does not require archive creation,
repair, encryption, or uncommon compression formats.

## Decision

Use `System.IO.Compression.ZipArchive` from the .NET base class library. File- and
ZIP-backed virtual sources create specialized entries behind the same `OpenRead` API;
ordinary loose-file entries carry no archive-only state. Opening a ZIP-backed entry
creates an archive reader and returns a stream that disposes the entry, archive, and
underlying file together.

Index archives once during discovery, but do not retain open archive handles or expanded
entry data. Bound compressed archive size, entry count, path length/depth, individual
expanded size, and total declared expanded size. Ignore unsafe entry paths and reject
archives that exceed configured limits.

Keep external resource search roots explicit and ordered at composition time. This
allows the application to reproduce user-before-data search policy without placing
platform folder discovery in `Oxce.Mods`.

## Consequences

- No production ZIP package or native archive dependency is added.
- Archive entries remain lazy and work through the same `OpenRead` API as loose files.
- Reopening an entry also reopens and validates the archive, avoiding shared mutable
  archive state and making concurrent reads independent.
- Encrypted entries and compression methods unsupported by the BCL fail explicitly.
- If future representative mods require unsupported ZIP features, package adoption must
  be reconsidered with license, maintenance, Native AOT, and compatibility evidence.

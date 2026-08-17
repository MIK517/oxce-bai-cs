# ADR 0007: Streamed music decoding and owned software synthesis

- Status: accepted for later implementation
- Date: 2026-08-17

## Context

The reference engine resolves each `RuleMusic` through a preferred format followed by
FLAC, OGG, MP3, MOD, WAV, AdLib, GM CAT, and MIDI. `name` defaults to the rule type,
`catPos` defaults to no catalog entry, and AdLib normalization defaults to 0.76. These
are mod-visible lookup semantics even though exact waveform output is not a compatibility
goal.

The supplied corpus establishes three non-optional use cases:

- UFO rules use catalog positions. Its 22-entry `GM.CAT` is covered by the compatible
  GM-to-MIDI converter, while `ADLIB.CAT` and `AINTRO.CAT` require AdLib synthesis.
- TFTD supplies 20 direct MIDI files. Its 23 rules include one filename alias and two
  tracks without a corresponding MIDI file.
- The 40k rules declare 27 tracks: 26 resolve to OGG and `BRIEFGENERIC` resolves to MP3.
  The Rosigma overlay declares 71 tracks; 66 resolve to OGG in the combined 40k/Rosigma
  layers and five currently have no matching file. FLAC and additional MP3 files occur
  elsewhere in the supplied sound corpus, but no supplied music rule selects them.

## Decision

Keep SDL3 as the final PCM device boundary and the owned managed mixer from ADR 0006.
Do not introduce SDL_mixer or rely on operating-system media or MIDI services.

Add music later as seekable, pull-based PCM sources rather than decoding long tracks
into immutable `PcmAudioClip` instances. A music source will produce interleaved signed
16-bit samples for the mixer, support bounded probing and decoding, and implement loops
by seeking or reopening a decoder. Decoder and synthesizer state must not escape the
formats/engine boundary or enter save data.

Preserve the reference lookup order exactly, including the user's preferred format as
the first attempt and normal fallback when that attempt fails. Lookup will use the
layered virtual catalog and the rule's effective `name`; missing optional tracks will
produce diagnostics rather than placeholder audio.

Implementation priority is corpus driven:

1. OGG and MP3 streaming are required for the supplied large mod combination. WAV music
   can reuse the existing bounded WAVE parsing rules with a streaming adapter.
2. Direct MIDI and GM-CAT-derived MIDI feed one owned software synthesizer. It must be
   deterministic for tests, work without platform MIDI devices, accept an explicitly
   selected SF2-compatible sound font, and ship a default only when its redistribution
   license and provenance are documented.
3. AdLib catalog entries feed a managed port of the reference OPL/adlplayer behavior,
   producing PCM through the same music-source contract. Reference captures will cover
   event progression, looping, duration, and bounded output; sample-exact synthesis is
   not required.
4. FLAC remains required for complete reference format support. Tracker MOD playback is
   explicitly deferred because it is absent from the supplied music rules. Both stay
   visible as partial until implemented and corpus tested.

No decoder or synthesizer package is selected by this ADR. Package selection happens
with the implementing Phase 8 slice because maintenance and Native AOT status are
time-sensitive. Each candidate must be reviewed for license compatibility, active
maintenance, bounded or controllable allocation, streaming/seek behavior, malformed
input handling, and desktop plus prospective Android support. A small auditable managed
implementation is preferred when it is lower risk than a native dependency.

## Verification plan

- Capture format-selection, filename alias, catalog fallback, missing-track, and
  normalization fixtures from `Mod::loadMusic` and `RuleMusic`.
- Test decoders with truncation, invalid metadata, excessive duration/rate/channel
  limits, seek/loop boundaries, and non-seekable streams where supported.
- Test MIDI/AdLib with bounded synthetic inputs and short reference event/PCM summaries.
- Run private corpus probes over every declared music rule without committing the
  copyrighted tracks or sound fonts.

## Consequences

- Music uses the same mixer, buses, volume policy, headless behavior, and SDL3 backend as
  sound effects.
- Phase 2 can close with a stable boundary and explicit later work; music playback does
  not masquerade as compatible merely because the device stream exists.
- OGG, MP3, MIDI, and AdLib are known Phase 8 requirements. FLAC and MOD cannot be
  silently ignored when the compatibility matrix is closed for release.

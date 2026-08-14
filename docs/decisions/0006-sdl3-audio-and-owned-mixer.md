# ADR 0006: SDL3 audio output with an owned mixer

- Status: accepted for incremental implementation
- Date: 2026-08-14

## Decision

Use SDL3 core audio only for desktop device discovery and PCM output. Implement the game
audio mixer and playback policy in managed code behind `IAudioOutput`; do not add an
SDL_mixer dependency. Decoders will produce immutable, interleaved signed 16-bit PCM
clips before playback. Headless hosts use `NullAudioOutput`.

The initial mixer will expose separate effects, UI, ambient, unit-response, and music
buses. It will preserve the reference nonlinear 0-128 volume curve, infinite and finite
loop counts, UI/ambient/unit-response channel policy, pause/resume, and positional sound
semantics where they are player-visible. Internal channel numbers are not part of the
new API.

## Rationale

SDL3 provides a maintained cross-platform device boundary without deciding how OXCE
assets, buses, priority, or AdLib playback should behave. An owned mixer keeps those
compatibility decisions testable and avoids exposing native handles or callback timing
to gameplay code. It also keeps a future Android host on the same backend boundary.

## Format strategy

- Implement CAT/WAV sound extraction and decoded PCM first, with bounded parsers and
  corpus tests.
- Decode common mod music formats only after confirming the supplied corpus and reviewing
  a maintained, license-compatible managed decoder or a small owned implementation.
- Port the existing AdLib synthesis behavior to PCM rather than depending on SDL_mixer.
- Decide MIDI synthesis and sound-font packaging from the actual corpus before adding a
  dependency. Unsupported formats must remain explicit in the asset matrix.
- FLC audio remains part of the future bounded FLC decoder rather than a special native
  mixer callback.

## Consequences

- The engine now owns stable audio buses, PCM clips, playback options, the reference
  volume curve, and a no-device implementation.
- The managed voice mixer and SDL3 default-playback stream are implemented as separate,
  composable layers. Device failure can fall back without exposing native handles to
  the engine; this decision does not by itself mark every sound or music format compatible.
- Adding a decoder package still requires license, maintenance, Native AOT, and corpus
  review under the repository engineering rules.

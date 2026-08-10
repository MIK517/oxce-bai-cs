# Compatibility contract

This document defines what “fully compatible” means for the port. It is the product
contract; implementation convenience does not override it.

## Required compatibility

### Mods and rulesets

Existing OXCE mods must be usable without modification, subject only to defects or
undefined behavior already present in the reference engine. Compatibility includes:

- Mod discovery, metadata, dependencies, conflicts, activation, and load order.
- Ruleset YAML syntax, aliases/references, scalar conversions, defaults, inheritance,
  list/map merge behavior, deletion and replacement semantics, and error handling.
- Resource lookup precedence and case/path behavior on supported platforms.
- Extra sprites, sounds, palettes, strings, interfaces, map scripts, event scripts,
  mission scripts, arc scripts, and other extension points supported by the reference.
- The full OXCE scripting language: grammar, types, constants, bindings, events,
  execution order, mutation, persistence, limits, and error behavior.

The initial compatibility corpus must include bundled standard mods plus at least one
large, script-heavy community mod. Passing only vanilla rules is insufficient.

### Saves

- Read campaign and active-battlescape saves produced by the targeted C++ OXCE branch.
- Preserve unknown or forward-compatible information when the format allows it.
- Write stable saves that this port can reload without loss.
- Aim for C++-readable output, but document fields where the reference engine cannot
  consume a semantically equivalent extension.
- Produce explicit diagnostics for missing mods, missing rules, unsupported versions,
  corrupt input, and incompatible script state.

Byte-for-byte YAML output is not required. Semantic round-trip preservation is.

### Original assets

Read the same supported UFO and TFTD resources and containers, including palettes,
indexed images and sprite sets, terrain/map data, routes, sounds/music, fonts, videos,
and original save formats currently supported by the reference engine.

Resource decoding is compatible when decoded dimensions, indices, records, topology,
and playback-visible content are equivalent. File hashes and internal representations
need not match.

### Gameplay

The same inputs and rules should lead to the same class of legal outcomes and the same
player-visible rule decisions. This includes strategic simulation, base management,
research/manufacturing, interception, battlescape actions, pathfinding rules, line of
sight, damage, AI permissions, mission generation, scoring, and campaign transitions.

When behavior appears accidental but mods may depend on it, treat it as compatibility
behavior until a deliberate decision record says otherwise.

## Accepted differences

- Random results may diverge between engines after any random draw.
- Rendering need not be pixel-identical if palette meaning, visibility, selection,
  animation state, and gameplay information remain correct.
- UI composition, navigation conveniences, scaling, accessibility, and input mapping
  may improve, provided every required action and piece of information remains present.
- Timing, logging text, save formatting, collection implementation, and memory layout
  may differ unless observable by a mod, save, asset, or game rule.

## Unsupported shortcuts

The following do not satisfy the contract:

- Converting mods to a new schema as a prerequisite.
- Replacing OXCE scripts with C# plugins, Lua, or another language.
- Supporting only new saves.
- Pre-converting original assets into a new mandatory format.
- Reinterpreting mechanics for balance or cleanup without an explicit compatibility
  decision and migration path.

## Tracking

Create machine-readable or tabular matrices under `docs/compatibility/` during Phase 0:

- YAML and ruleset features.
- Script grammar, operations, types, and bindings.
- Save nodes and versions.
- Original asset formats.
- Gameplay subsystems and scenarios.
- Supported operating systems and architectures.

Each entry should be `not started`, `partial`, `compatible`, or `intentionally differs`,
with fixture names and reference source locations.


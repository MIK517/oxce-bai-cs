# Campaign foundation slice status

Review date: 2026-09-03.

## Outcome

The first persisted strategic foundation is implemented. `Oxce.Gameplay` now owns
campaign identity, the OXCE calendar, countries, regions, the starting base, stable
entity/next-ID state, campaign and country script values, validated commands, and
domain events. Campaign creation uses runtime-linked rules with injected clock and
stateful randomness. Capture and restore operate on save-neutral snapshots, and
restore validates the complete implemented graph before publishing it.

`Oxce.Savegames` reads and writes the implemented subset of OXCE's two-document save
format. It validates active mods, external rule IDs, history bounds, coordinates,
facility placement, entity identities, and script tags. Unknown header/body fields and
unimplemented nested state remain in an opaque YAML sidecar and survive overlay writes.
Writes use a flushed temporary file, atomic replacement, and a recoverable `.bak` when
replacing an existing save.

No command in this foundation invokes an executable gameplay script event yet, so the
slice adds no speculative provider ABI. It does restore the `GeoscapeGame` and
`Country` values used by this state through the compiled tag catalog. Providers and
event sources will be added with the first gameplay action that calls each reference
binding.

The headless `campaign-scenario` command creates a deterministic campaign, places its
base, advances one minute, writes atomically, reloads, and reports semantic counts and
timings. A minimal SDL presentation is intentionally the immediately following slice,
as allowed by the agreed headless-first sequencing.

## Compatibility evidence

The public campaign fixture pins country funding adjustment, area-filtered world
creation, starting-base content, external IDs, calendar trigger precedence, commands,
and events against OXCE commit `4df3a5e571a1a4b5e8a46d3161fb2e21a2adba15`.
Malformed state tests cover missing mods and rules, duplicate identities, overlapping
facilities, invalid histories, invalid tags, and interrupted writes.

The private test loads and round-trips every supplied vanilla UFO and 40k/Rosigma save,
including active-battle saves. It compares the implemented snapshot after reload and
therefore also exercises preservation of the unimplemented battle graph. TFTD private
save acceptance is deferred because its current content build does not yet reach
`runtime-linked` due to an earlier unprojected unit property; the save adapter itself
does not special-case either game.

The self-contained staged installation produced these headless acceptance results on
the baseline host:

| Scenario | Content build | Atomic save | Reload | Save size |
|---|---:|---:|---:|---:|
| Vanilla UFO | 542.6 ms | 40.6 ms | 29.2 ms | 5,361 B |
| 40k/Rosigma | 8,205.7 ms | 29.9 ms | 22.9 ms | 4,718 B |

These are single cold-process integration observations, not microbenchmark means.

## Reference implementation inspected

- `src/Mod/Mod.cpp`: `newSave`, starting-base selection, country/region construction,
  funding adjustment, and starting entities;
- `src/Savegame/SavedGame.cpp`: two-document load/save, global histories, IDs, bases,
  countries, regions, and tags;
- `src/Savegame/GameTime.cpp`, `Country.cpp`, `Region.cpp`, `Base.cpp`,
  `BaseFacility.cpp`, `Craft.cpp`, `Soldier.cpp`, and `Target.cpp`: field defaults,
  identity, coordinates, grid placement, and calendar behavior.

## Deliberate next boundaries

- TFTD runtime projection closure and private-save acceptance;
- full personnel/craft fields, inventory operations, transfers, facilities, and
  finance rather than placeholder mutation of opaque save nodes;
- gameplay script providers/events when their owning commands arrive;
- the minimal SDL campaign inspector/starting-base interaction.

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
Base sidecars follow persistent IDs and facility sidecars follow `(base ID, type, x, y)`;
reordering, adding, or removing either entity cannot transfer unknown fields by list
position. Reference saves without base IDs receive deterministic IDs on import.
Writes use a flushed temporary file, atomic replacement, and a recoverable `.bak` when
replacing an existing save.

No command in this foundation invokes an executable gameplay script event yet, so the
slice adds no speculative provider ABI. It does restore the `GeoscapeGame` and
`Country` values used by this state through the compiled tag catalog. Providers and
event sources will be added with the first gameplay action that calls each reference
binding.

The headless `campaign-scenario` command creates a deterministic campaign, places its
base, advances one minute, writes atomically, reloads, and reports semantic counts and
timings. The immediately following performance-hardening slice added a minimal indexed
campaign overview. `Oxce.Engine` renders a gameplay-owned view and submits existing
commands through separate query/command ports; it does not capture persistence
snapshots during redraw. Facility extents come from linked runtime rules, and long time
advances retain a fixed-size trigger summary with allocation-free ordered replay. SDL
remains a platform host. The app command accepts the same self-contained
installation inputs, lets the user click to place the starting base, advances one
minute per Space press, suppresses unchanged uploads, and optionally saves on exit:

```powershell
dotnet run --project src/Oxce.App --configuration Release -- --campaign-sdl artifacts/private-install xcom1 - artifacts/campaign-foundation/ui.sav
```

## Compatibility evidence

The public campaign fixture pins country funding adjustment, area-filtered world
creation, starting-base content, external IDs, calendar trigger precedence, commands,
and events against OXCE commit `4df3a5e571a1a4b5e8a46d3161fb2e21a2adba15`.
Malformed state tests cover missing mods and rules, duplicate identities, overlapping
facilities, invalid histories, invalid tags, and interrupted writes.

The private test loads and round-trips every supplied vanilla UFO, vanilla TFTD, and
40k/Rosigma save, including active-battle saves. It compares the implemented snapshot
after reload and therefore also exercises preservation of the unimplemented battle
graph. TFTD's legacy unit-level `zombieUnit` property is accepted as the reference
`Unit::load` no-op rather than being projected as a relationship with invented
semantics.

The self-contained staged installation produced these headless acceptance results on
the baseline host:

| Scenario | Content build | Atomic save | Reload | Save size |
|---|---:|---:|---:|---:|
| Vanilla UFO | 542.6 ms | 40.6 ms | 29.2 ms | 5,361 B |
| Vanilla TFTD | 651.3 ms | 60.0 ms | 33.6 ms | 5,460 B |
| 40k/Rosigma | 8,205.7 ms | 29.9 ms | 22.9 ms | 4,718 B |

These are single cold-process integration observations, not microbenchmark means.

## Reference implementation inspected

- `src/Mod/Mod.cpp`: `newSave`, starting-base selection, country/region construction,
  funding adjustment, and starting entities;
- `src/Mod/Unit.cpp`: the loaded unit properties and the observed omission of the
  legacy TFTD unit-level `zombieUnit` property;
- `src/Savegame/SavedGame.cpp`: two-document load/save, global histories, IDs, bases,
  countries, regions, and tags;
- `src/Savegame/GameTime.cpp`, `Country.cpp`, `Region.cpp`, `Base.cpp`,
  `BaseFacility.cpp`, `Craft.cpp`, `Soldier.cpp`, and `Target.cpp`: field defaults,
  identity, coordinates, grid placement, and calendar behavior.

The maximum one-million-tick command has a unit performance gate of 16 KiB allocated
on the calling thread after warm-up. This guards retained trigger processing against
returning to per-tick event arrays; it is not a wall-clock target.

## Deliberate next boundaries

- full personnel/craft fields, inventory operations, transfers, facilities, and
  finance rather than placeholder mutation of opaque save nodes;
- gameplay script providers/events when their owning commands arrive;
- richer localized text and controls as personnel, inventory, craft, and finance
  actions become available.

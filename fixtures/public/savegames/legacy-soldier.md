# Legacy soldier regression fixture

Synthetic, redistributable save using the public `runtime-rule-linking` mod fixture.
The omitted soldier `type` follows `src/Savegame/Base.cpp` (`Base::load`), which
defaults to the first soldier rule. Soldier fields follow `src/Savegame/Soldier.cpp`.
Reference inspected: OXCE commit `4df3a5e571a1a4b5e8a46d3161fb2e21a2adba15`.
This is a hand-authored regression input, not captured C++ oracle output.

`CampaignSaveRegressionFixtureTests` verifies preservation of the soldier name,
stats, and equipment across two save rewrites.

The time is immediately before month rollover. The calendar regression checks
the day increment mandated by `src/Geoscape/GeoscapeState.cpp`: `TIME_1MONTH`
falls through to `time1Day()`, which calls `increaseDaysPassed()`.

# UFO hunting and escort integration

Branch 4c. Pinned reference: 4df3a5e571a1a4b5e8a46d3161fb2e21a2adba15.

The strategic-hunting capture inserts Craft::getHunterKillerAttraction,
Ufo::insideRadarRange and GeoscapeState::ufoHuntingAndEscorting's selection
fragment verbatim. Controlled collaborators supply effective capacities, craft
eligibility and distances. Ninety-six attraction rows and fourteen selection
rows pin both hunt modes, strict saved-order comparisons, original-target ties,
ignored/undetectable exclusions, zero radar and inclusive range.

The gameplay-owned selection operation preserves those rules. Arithmetic outside
signed 32-bit range and zero divisors fail intentionally. Five focused tests
verify captured outputs and malformed inputs. Live retargeting and movement
remain guarded until the next two slices connect this operation. Combat remains
branch 5; branch 4d is untouched and the headless gate remains open.

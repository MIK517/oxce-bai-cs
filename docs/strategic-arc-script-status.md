# Monthly arc-script subsystem

Branch 4c incremental slice. Pinned reference:
`4df3a5e571a1a4b5e8a46d3161fb2e21a2adba15`.

The relevant C++ path is `GeoscapeState.cpp::determineAlienMissions` (arc-script
eligibility, odds collection and ordered grants), `SavedGame.cpp::isResearched` and
`SavedGame.cpp::addFinishedResearch`, `RuleArcScript.cpp` (defaults and arc lists),
and `WeightedOptions.cpp::choose/set`. The arc phase precedes monthly mission and
event scripts. A script counts researched entries in both lists, including duplicates;
it filters weighted candidates before granting the first unresearched sequential
entry. It can then grant one weighted entry from that prefiltered list. Each grant
opens the lookup article when its research rule has one, otherwise its own article.

The registered `ICampaignWorldArcScripts` operation and internal
`ScheduleMonthlyWorldArcs` fixture command execute this phase through the campaign
transaction. The research capability owns the direct `addFinishedResearch` style
grants, including lookup and zero-cost chains. Arc grants do not run the separate
daily project side effects. `CampaignArcUnlocked` carries article notifications;
completed research and score use the existing save fields and round-trip through
OXCE saves. The operation uses live trigger facts and supplied monthly score/funds.

Before odds or mutation, the phase bounds script/reference work, checks reachable
required research and conservative score capacity, and accounts for earlier scripts
that could expose later sequential entries through direct, lookup or automatic
research. A guaranteed sequential grant that fills `maxArcs` makes the weighted
choice unreachable, so its links and score are not required. A merely possible
earlier disable does not make that grant guaranteed. Score bounds use the
largest possible contribution per reachable grant slot, including an optional
lookup and zero-cost chains, rather than summing mutually exclusive choices.
Unavailable transitions return a blocked result without changing the campaign or
RNG. An execution-odds check consumes a roll even at zero odds.

`strategic-arc-scripts.rul` demonstrates duplicate sequential/random choices,
lookup and zero-cost research, pre-collected trigger eligibility, month-to-month
maxArcs counting, reachable and unreachable missing tails, safe large grants,
weighted alternatives, score overflow, and save/reload. The protected zero-cost
topic uses the reference `requires` key. `StrategicArcScriptTests` run fresh and
cache content paths. Their assertions follow the named reference methods above;
the fixture is public and
contains no original game assets.

Validation: root `dotnet test` passes 1,212 tests with zero failures or skips,
including checksum and private-corpus tests. Formatting verification passes.

This is an isolated monthly subsystem. The actual month caller, score/funding
calculation, initial hook and full branch 4c acceptance remain outstanding.
Campaign month rollover stays guarded; this slice does not implement branch 4d.

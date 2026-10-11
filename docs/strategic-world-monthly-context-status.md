# World-script score and funding context

Branch 4c incremental slice. Pinned reference:
`4df3a5e571a1a4b5e8a46d3161fb2e21a2adba15`.

`GeoscapeState::determineAlienMissions` computes the score and eligible funds
before collecting scripts. `SavedGame::getCurrentScore` adds the current research
score, each region's XCOM activity minus alien activity, and 400 after month one.
`Mod::getPerformanceBonus` truncates score times `performanceBonusFactor` to an
integer; negative bonuses are clamped to zero. For a new month only, the
scheduler peeks at current funds plus country funding and bonus minus base
maintenance. Adhoc scheduling uses current funds. `SavedGame::getBaseMaintenance`
calls `Base::getMonthlyMaintenace`; its personnel maintenance includes inventory
upkeep through `getTotalOtherStaffAndInventoryCost`.

The runtime campaign settings now project the mod's `performanceBonusFactor`
(default zero); content compiler revision 28 invalidates older caches. The
isolated `CampaignState.WorldSchedulingInputs` reads live histories and
maintenance, then applies bounded `WorldMonthlySchedulingMath`. It is ready for
the future ordered monthly caller but does not advance the month or call any
script phase. Unsafe score, bonus and funds arithmetic fails intentionally.

The `strategic-world-monthly-context` manifest pins a C++ probe that inserts
the three SavedGame methods, the Mod bonus method, and the GeoscapeState context
block verbatim. It covers month-zero/one/two score, negative and fractional
bonuses, fractional int boundaries, and the adhoc current-funds path. `StrategicWorldMonthlyContextTests`
compare those rows and check live campaign projection and save/reload in
fresh/cached content. A malformed nonfinite factor fails at rule load.

Integrated month advancement remains guarded for branch 6. This slice does not
claim the initial-campaign/monthly scheduling hooks or branch 4c closure.

Validation: root `dotnet test` passes 1,227 tests with zero failures or skips,
including checksum and private-corpus tests. Formatting, regenerated code-map and
diff checks pass.

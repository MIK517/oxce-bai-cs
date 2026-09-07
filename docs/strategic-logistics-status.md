# Strategic logistics branch status

Validation date: 2026-09-07. Branch: `codex/strategic-base-logistics`.
First commit: `586df1b` (reviewed implementation plan, before implementation).

## Delivered boundary

Phase 6 branch 1 is implemented for eligible campaign states. The indexed UI and
headless commands share stores inspection, prepared purchase/sale/transfer quotes,
quantity/cost/capacity checks, hiring, recruitment, dismissal, craft transactions,
incoming supplies and exactly-once arrival. Soldier generation happens before transit;
scalar and randomized templates, names, stats, armor and identities survive reload.
Starting campaigns assign crew and award the original-eight commendation where defined.
Craft initialization, cargo refunds, immediate arrival checkup and refuelling are owned.

Ordered time dispatch and mobile save overlays lead the implementation. Transfers retain
identity as entities move between bases and transit; deletion/recreation cannot inherit
another entity's source data. Unknown equipment, diary and script fields remain preserved
where no enabled action owns them. Options, completed research and monthly purchase logs
persist. Monthly reset is covered by an isolated handler fixture; complete campaign
monthly processing remains a later branch.

See the [ownership and reference ledger](phase-6-ownership.md) for exact C++ entry points,
fixture scope and [ADR 0025](decisions/0025-strategic-time-and-mobile-save-ownership.md)
for dispatch/persistence boundaries. Runtime compiler/cache revision is 9.

## Validation

- Release solution build: zero warnings and errors.
- Full solution tests: **627 passed, zero failures, zero skips** on this checkout.
- Public fixtures cover prices, distance/travel costs, time fallthrough, templates,
  initial crew, critical sales, transfer capacity/arrival, malformed inputs and overflow.
- UI tests cover stable quotes, explicit confirmation, cancellation, transit save/load,
  and read-only inventory inspection. The staged 40k indexed font and English mod labels
  were rendered and visually inspected using the public logistics scenario.
- Fresh and compiled-cache content produce equivalent creation, purchase, sale,
  recruitment where available, save/reload and blocked-time results for UFO, TFTD and
  40k/Rosigma. All 40 Rosigma template-bearing soldier rules have supported template
  payloads; none is offered for recruitment by that new Beginner state's legal quote.
  Public template-recruit fixtures exercise generation and transit persistence directly.

| Content family | New soldiers / assigned | Legal item purchases | Imported saves | Purchase + reload | Explicitly blocked |
|---|---:|---:|---:|---:|---:|
| UFO | 8 / 8 | 32 | 7 | 1 | 6 |
| TFTD | 8 / 8 | 32 | 5 | 1 | 4 |
| 40k/Rosigma Beginner | 0 / 0 | 36 | 7 | 1 | 6 |

Of 19 imported saves, eight are blocked by active battles and eight by active
research/production accounting. All remain inspectable and round-trip through the
implemented strategic save subset. These are eligibility classifications, not blanket
claims that imported campaigns can progress. Public two-base fixtures exercise delivery
and entity movement; new vanilla deliveries beyond midnight remain queued at the guard.

The existing empty-workload allocation gate still passes. Fresh and cached content
loads must produce equivalent logistics outcomes for the owned corpus; startup
deserialization performance remains a separate backlog item.

## Deliberate guards and next work

- Complete campaign time stops before midnight, active world/tactical/project behavior,
  and subsequent craft servicing that requires later providers. Arrival notifications
  pause subsequent ticks after completing the current tick's fallthrough.
- Unsupported script host bindings and complex recruit payloads block their actions;
  they do not return fabricated results. Existing opaque imported fields alone do not
  block unrelated supported actions.
- Critical sales that would remove mounted weapons with nonzero bonus stats stop before
  mutation. The reference retains cached craft stats after that removal; branch 2 must
  own those mutable stats instead of silently recalculating them.
- Base construction, additional-base UI, loadout editing, training and service progression
  belong to branch 2. The current placement shortcut uses coordinates (0, 0); geographical
  placement constraints and selection remain in that branch's scope.
- Main navigation/help text is English. Item labels use layered English language files
  and extra strings. Broader locale selection and diary presentation remain future UI work.
- Name-pool directory ordering uses the reference's portable case-insensitive lexical
  fallback. Windows `StrCmpLogicalW` natural ordering is not imported into core/mod
  libraries; generated RNG stream/name parity across reference platforms is not claimed.

## Running the UI

```powershell
dotnet run --project src/Oxce.App --configuration Release -- --campaign-sdl artifacts/private-install xcom1 - artifacts/campaign-foundation/campaign.sav
```

`I` opens stores; `B` purchases/hires; `S` sells/dismisses; `T` chooses a transfer
destination. Use arrows, +/- (Shift changes ten), Enter to review, and Y to confirm.
Tab changes bases; Escape cancels, closes a screen, then exits. Space advances one
minute and Shift+Space one hour, subject to guards. F5 saves; F9 requests confirmation
before loading the save path. Quotes pause time. A `-` save destination disables saving
and loading. Closing the application saves when a destination is supplied.

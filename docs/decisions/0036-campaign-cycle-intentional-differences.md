# 0036: Campaign cycle intentional differences

Status: accepted for Phase 6 branch 6 (2026-10-17); later slices add entries.

## Problem

The month rollover, the monthly report and event payloads reach a few reference paths whose behaviour is a
crash, undefined behaviour or an implementation accident. The port must be deterministic there and record what it
does ([AGENTS.md](../../AGENTS.md)). The plan is [phase-6-campaign-cycle-plan](../phase-6-campaign-cycle-plan.md);
evidence is in [strategic campaign cycle status](../strategic-campaign-cycle-status.md).

## Decision

**Country draws over a negative range (S3).** `Country::newMonth` draws `RNG::generate(0, good)` or
`RNG::generate(0, bad)`, and a country with negative activity makes the bound negative. `RandomState::generate`
then takes the raw 64-bit value modulo the range converted to an unsigned number, which almost always leaves the
raw value, adds the minimum and truncates to `int`: in effect any `int` (the `strategic-monthly-report` oracle's
negative-activity scenario). A bound of exactly -1 makes the modulus zero, a division by zero. The port draws
uniformly over the whole `int` range for every negative bound, -1 included; the comparison that follows
(`> bad`, `> good`) is then decided as the reference decides it for that drawn value.

**No country to average (S3).** `MonthlyReportState::calculateChanges` divides the country funding by the number
of countries for the average a cancelled pact restores; with no country that divides by zero. The port uses an
average of 0, which nothing reads because no country makes a decision.

**An undefined lose cutscene (S3).** On the second OK of a failed month, `Mod::getVideo(cutscene, true)` throws when
the mod defines no `loseRating` or `loseMoney` video. The port plays nothing and play goes on, as for a defined
video without `loseGame`.

**The monthly report is a modal hold (S3, plan D2).** The report's decisions are made inside the month tick, as the
reference's report constructor makes them; the popup then holds the clock from the next tick (the time preflight
refuses) and refuses every other command except its acknowledgement, which the reference's modal window enforces
through its input. `time1Month` queues the report before any window the same tick's later levels open (a base
defense, landings, dogfights), and the popup is pushed over them, so the report is answered first: its preflight
precedes theirs and their gates let its acknowledgement through. A reload drops an open report, as the reference
keeps popups unsaved. Saving with a report open is refused from S4.

**An out-of-range performance bonus (S3).** `Mod::getPerformanceBonus` converts `score * factor` to `int`, which is
undefined outside the `int` range (and for NaN). The port takes x86's truncating conversion, `int.MinValue`, so the
report pays no bonus there, as the reference pays only a positive one.

**A mission without waves started without a delay (S3).** `AlienMission::start` reads the first wave's timer when
the mission script gives no start delay; a mission with no waves then reads past its wave list. With a delay (as
Rosigma's base-starting missions use) nothing is read and the port follows the reference exactly. Without one, the
port takes a zero timer: the countdown draw is made over a zero range, and the mission ends on its next pass like any
other mission without waves.

**A site script naming a mission without waves (S3).** `GeoscapeState::processCommand`'s site branch reads the
mission's first wave (`getWave(0)`) to pick the site's deployment, which reads past an empty wave list. The port
keeps refusing such a script with its mission-script boundary reason rather than inventing a deployment.

**A commendation without criteria (S4).** `SoldierDiary::manageCommendations` tries a commendation again after
each award, so one with no criteria (such as `STR_MEDAL_ORIGINAL8_NAME`, which the reference awards only in code at
the start of a campaign) is awarded without end and the game hangs. The port never awards such a commendation there.

**A noun level past its last threshold (S4).** For a noun criterion, a commendation whose saved level reaches past
the last threshold makes `critDef.at` throw. The port awards nothing for that noun.

**A kill without a span (S4).** The per-mission and per-turn kill criteria start without counters and read them for a
kill whose mission or turn is -1. The port starts a fresh span there. A kill-list field that a save omits is left
unset by `BattleUnitKills`; the port takes the default constructor's value.

**An ironman campaign after an undefined lose video (S4).** The reference throws before it queues the ironman save;
the port plays nothing (above) and still requests the ironman save, as for every other failed month.

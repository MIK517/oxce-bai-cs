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

**The monthly report and event windows are modal holds (S3, S5, plan D2).** The report's decisions are made inside the month tick, as the
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

**An event payload that names an unknown rule (S5).** `eventLogic` looks its regions, soldiers, items and craft up with
an error (`Mod::getRule`), so an unknown name throws after the score and funds have already changed and the game stops.
The port runs the payload on the staged shadow first (ADR 0035), so the tick is refused with the name (for example
"Strategic event item 'STR_BOGUS' is not defined.") and nothing changes. Empty names and `STR_NULL` (and `STR_NONE`
for items) name no rule, as `Mod::isEmptyRuleName` says; an unknown `spawnedPersonType` spawns nobody, as the reference
looks it up without an error. A payload without a base (the reference reads the first base of an empty list) or one
that would spawn more than 10,000 soldiers is refused the same way. An empty or `STR_NULL` region names no region,
so the score goes to research; a city-specific event without a region, whose cities the reference then reads from no
region, is refused.

**Events that a payload schedules during the countdown (S5).** `time30Minutes` walks the event list with a range-for
loop while a payload's research side effects can append to it, which invalidates the loop. The port counts down only
the events the tick began with; events scheduled meanwhile join the list after them and first count down on the next
pass.

**An undefined event cutscene (S5).** `GeoscapeEventState::btnOkClick` reads the video with an error; the port plays
nothing and keeps the campaign's ending unchanged, as for the lose cutscenes above.

**Names and order of spawned soldiers (S5).** A spawned person's `spawnedPersonName` is kept as its string ID, as the
port keeps every text, where the reference stores the translated name (manufacture does the same). The merged
soldier lists are kept in rule load order, where the reference orders them by the rules' addresses, which follow load
order for rules allocated as they load. The geoscape debug log (`oxceGeoscapeDebugLogMaxEntries`) is not modelled.

**A save the report requests under this tick's event windows (S5).** The report's OK requests an autosave or ironman
save while the same tick's event windows are still queued; the reference saves then, with their payloads applied and
without the windows. The port refuses saving while a window is open (D2), so the host makes the requested save once
the windows are answered (S7).

**An undefined research cutscene (S6).** `time1Day` reads a discovery's video with an error, and `CutsceneState::init`
reads it again; the port plays nothing and leaves the ending unchanged, as for the other undefined videos above.

**Windows left open by an ending (S6).** A tick can open windows (this month's report, an event, a base defense) and
then end the campaign at a later level. The reference's game-over video replaces the game with the statistics screen,
which drops the queued popups for a loss and for those queued after the first video for a win. The port refuses every
command of an ended campaign, so those windows stay unanswered, and it no longer lets them forbid saving the ended
campaign. In particular, a research win on the month tick leaves the month's report unanswered. The reference shows
it before the win's video and applies its OK: monthly service, commendations and an autosave written with the win
already set.

**A preflight that does not foresee a loss (S6).** The tick's preflight runs before its day level, so it checks the
five-second UFO pass that a loss in the day level will skip; a work-limit refusal there could stop a tick the reference
would run. The case needs a research loss on a tick whose UFO pass alone exceeds a bound.

# 0030: Bounded site-script region selection

Status: accepted for Phase 6 branch 4c.

## Context

At reference commit 4df3a5e571a1a4b5e8a46d3161fb2e21a2adba15,
GeoscapeState::processCommand first builds eligible site areas, then repeatedly
draws a weighted or uniform region until it finds a region in that area list.
The loop has no finite work bound. Occupied missions, base targeting and point
repeat history can remove regions, including during a batch of commands.

## Decision

Condition the region distribution on the regions that still have eligible
areas. Preserve uniform region-list order and ordinal weighted choices, their
relative weights, and uniform area selection inside the selected region.
Mission selection and circular fallback still precede region selection.

If a region originally has weight w, rejection sampling eventually accepts it
with probability w divided by the total weight of all eligible regions. The
conditioned table has exactly that distribution. Uniform selection is the same
calculation with equal weights, independently of each region's area count.

The compatibility contract allows random results to diverge after any draw.
Skipping rejected region draws changes the RNG stream in these cases; it does
not change eligible outcomes or their probabilities. When all regions remain
eligible, draw ranges and order stay unchanged. No script VM callback runs
inside these reference region retries.

## Safety and evidence

The pinned site-command probe includes controlled rejected-region traces.
Preflight validates the selected and listed timeline region sets, all possible
missions/races/operations, IDs, histories and work before mutation. Empty area
lists still return a false command result. A mismatched selected/listed timeline
that could leave only unselectable areas is refused explicitly before RNG.
There is no speculative execution, RNG rewind or rollback.

This replaces incremental blanket multi-region and changed-batch boundaries.
Campaign-event payloads and integrated monthly progression retain their later
branch guards.

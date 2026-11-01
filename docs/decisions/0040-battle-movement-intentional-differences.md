# 0040: Battle movement intentional differences

Status: accepted for Phase 7 branch 4 (2026-11-01); later slices add entries.

## Problem

Battle movement (the [Phase 7 plan](../phase-7-plan.md#branch-4--battle-movement)) reaches reference paths whose
behaviour is undefined or an implementation accident. The port must be deterministic there and record what it does
([AGENTS.md](../../AGENTS.md)). Evidence is in [battle movement status](../battle-movement-status.md). Each entry names its
slice.

## Decision

**The order of the reachable tiles (S2).** `Pathfinding::findReachablePathFindingNodes` sorts its nodes with
`MinNodeCosts`, which says a node comes first only when it is cheaper in both time units and energy. Two nodes that are
cheaper in one and dearer in the other are unordered both ways, which is not a strict weak ordering: `std::sort` is
undefined for it and its result depends on the standard library. The port orders by time units, then energy, and keeps
the order in which the search visited equal costs. The set of tiles is the reference's. The port's start is always first; the reference's need not be, because `MinNodeCosts` treats a cost of zero time and energy as equal to any node with zero energy (a grav lift step, a fall), which `std::sort` may put before it.

The open set of the A* search is not such a case: `PathfindingOpenSet` is a `std::priority_queue`, and the port's heap
follows the standard library's sift order (the same in libstdc++ and the MSVC library), so equal costs leave in the
reference's order and the chosen path is the reference's.

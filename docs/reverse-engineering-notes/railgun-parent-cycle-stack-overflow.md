# Railgun parent-cycle stack overflow

`Railgun.Update` searches the active rail list for intersecting rails. The
original filters reject the current rail, its direct parent, its direct child,
and a rail that directly names the current rail as a parent. They do not reject
an indirect ancestor.

For a child chain `A -> B -> C`, the corresponding parent links are
`B -> A` and `C -> B`. If `C` later selects `A` as its child, the attachment
adds `C` to `A.mParents` and closes the parent cycle `A -> C -> B -> A`.
`LockAll` recursively follows `mParents` without an already-locked guard, so
that cycle ends in `System.StackOverflowException`.

Magicka 1.10.4.2 and the Community Patch before this repair have the same
candidate selection, graph attachment, and `LockAll` recursion. The existing
Community Patch edits in `Railgun` only add GC-retention lifecycle hooks. The
cycle is therefore an original game defect, not a patch regression.

## Repair

After the original geometry and range checks accept an intersection, but
before `Update` changes rail state, the patch iteratively walks the current
rail's transitive `mParents` graph. If the proposed child is already an
ancestor, the candidate is skipped. A fixed 256-entry work set bounds memory
and execution. Reaching the bound or encountering an unexpected error rejects
the attachment because its safety could not be established.

`LockAll` also uses a separate traversal-only flag to stop recursion into a rail
that is already active in the current call chain. The flag is cleared when that
rail's traversal returns. It does not reuse `mLocked`, because `mLocked` is game
state and a later call must still propagate to every current parent. This
defensive guard handles a graph that was already corrupt when the call began.
The ancestor check is the root fix because it prevents the cyclic mutation
itself.

Acyclic attachments, rail geometry, depth limits, damage behavior, and network
data remain unchanged.

## Recovery telemetry

The repair emits `magicka_patch_runtime_recovery` through the existing bounded
runtime sender. Its stable reasons are:

| Reason | Meaning |
| --- | --- |
| `railgun_parent_cycle_prevented` | The proposed child was an ancestor of the current rail. |
| `railgun_parent_cycle_check_limit_reached` | The bounded traversal could not establish safety. |
| `railgun_parent_cycle_check_failed` | An unexpected traversal failure caused a fail-safe rejection. |

`collection` is `Railgun.mParents` and `object_type` is
`Magicka.GameLogic.Spells.Railgun`. `details` contains only `visited_count`,
`pending_count`, and `candidate_parent_count`. The shared sender emits the first
event immediately and applies exponential backoff to repeats.

## Assembly validation

The focused assembly comparison permits one traversal-only `Railgun` field, two
added private helpers, and changes only `Railgun.Update` and `Railgun.LockAll`.
The update check appears after the last geometric branch and before the first
accepted-candidate mutation. The executable remains a CLR 2 assembly with no
CLR 4 reference, and the changed methods are JIT-tested with Microsoft CLR 2.0
x86.

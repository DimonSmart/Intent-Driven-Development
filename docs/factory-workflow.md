# Factory linear execution model

IDD Factory is a deterministic, resumable orchestrator. Semantic workers decide
the content of work; the .NET runtime owns state, ordering, persistence,
verification, recovery, and finalization.

For a new end-to-end product request, `idd-factory-run` completes Intent
Preflight before this execution model begins:

```text
original request
-> classify against relevant current intent
-> optional existing intent workflow
-> coverage validation
-> create Factory run with the same original request
```

Intent preparation is outside the runtime. It never appears in `Completed`,
`Current`, or `Remaining`, and implementation workers remain unable to change
`.idd/intent/`.

```text
                   +---------------+
                   |   Completed   |
                   +---------------+
                           ^
                           | successful + verified
                           |
+-------------+     +---------------+
|  Remaining  | --> |    Current    |
+-------------+     +---------------+
       ^
       |
       +-- prepend additional work
       |
       +-- replace by semantic replan
```

`Completed` is immutable history. `Current` contains at most one task.
`Remaining` is an ordered list, and its first item always executes next.

## Planning

Initial planning and replanning use the same flat result:

```json
{
  "outcome": "ready",
  "tasks": [
    { "capability": "implementation", "task": "Do A" },
    { "capability": "research", "task": "Investigate B" }
  ]
}
```

Runtime assigns monotonic IDs and writes immutable contracts. The planner does
not return IDs, statuses, sequence numbers, cross-task references, or runtime
bookkeeping. Planning may be partial: unknowable work is planned later after
completed research or implementation provides the missing facts.

## Scheduling and completion

When no continuation or blocker is active, runtime continues `Current`, or
moves the first `Remaining` item to `Current`. A successful semantic result is
verified according to runtime policy. Only then is it appended to `Completed`
and cleared from `Current`.

There is no dependency resolution or readiness search. Prerequisite order is
the physical order of `Remaining`.

## Dynamic work and replanning

If current task `B` discovers prerequisite `X`, runtime atomically changes:

```text
Current: B        -> Current: none
Remaining: C D    -> Remaining: X B C D
```

After `X` completes, `B` runs again with `X` in completed-work context.

A global strategy change is different: at a safe point the planner returns the
complete future suffix, and runtime atomically replaces all `Remaining` work.
Completed history is never part of that replacement.

## Persistence and recovery

`.idd/factory/current/state.json` is authoritative. Selecting current work,
committing verified work, prepending dynamic work, and replacing the future plan
are atomic state transitions. Immutable contracts and optional
`plan-revisions/Pnnnnnn.json` diagnostics may be written before state; orphan
artifacts are harmless and are never replayed as authority.

Each semantic attempt separates four artifacts:

```text
invocation.json        runtime-owned identity and assigned schema
raw-result.json        untrusted semantic worker output
result.json            validated runtime-owned persisted attempt result
process-telemetry.json backend-observed process evidence
```

The worker returns only semantic meaning. Runtime binds the raw body to the
invocation-specific backend channel, rejects runtime-owned identity or
bookkeeping fields, validates the outcome and payload against the assigned
capability and role, and atomically writes `result.json`. A malformed raw body
never changes authoritative state.

Recovery resumes the exact persisted semantic or verification operation. It
verifies that `invocation.json`, the attempt directory, authoritative current
operation, and persisted result provenance agree. A valid persisted result is
consumed without redispatch, and completed work cannot be committed twice. Raw
output alone is not authoritative.

## Verification, review, and finalization

Runtime selects verification from actual changed paths and project policy.
Final verification is always strict. Intermediate semantic review can be an
ordinary ordered task.

When `Current` is empty and no `Remaining` work exists, runtime performs strict
final verification, then mandatory integrated semantic review. Final review is
a terminal orchestration phase, not a planned work item. Corrections become new
future work and invalidate earlier final evidence.

Successful finalization requires empty current/future work, no active
continuation, current strict verification, and current approved final review.

Active state from the previous incompatible execution model is rejected with an
explicit legacy-state outcome and must be finished with the old runtime or
cancelled and restarted.

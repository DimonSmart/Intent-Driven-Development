# Factory batch execution model

IDD Factory is a deterministic, resumable orchestrator built around one loop:

```text
plan batch -> execute batch -> plan again
```

Intent preparation happens before runtime creation. Factory receives the
unchanged original request and treats current `.idd/intent/` as read-only.

## Planning

The planner is the only semantic component that decides what work remains. It
reassesses the original request against the current repository, durable intent,
completed task results, actual changed paths, and authoritative verification
evidence.

Planner output is human-readable Markdown:

```markdown
# Task

Implement the first safely contractable change.

# Task

Integrate it into the second already-understood area.
```

Each `# Task` section becomes one immutable work-item contract. The planner
returns every task that can be safely contracted now and stops at the first
material uncertainty that depends on new evidence. An empty response means no
semantic work remains. Planner output contains no capability, persistent ID,
status, dependency, revision, outcome, or transition instruction.

## Execution

Runtime assigns work IDs and executes every task in the current batch in order.
An executor receives one contract plus relevant completed results and
verification evidence. It changes the product as needed and returns a free-form
Markdown report of what happened.

The executor does not create tasks or decide whether to interrupt, correct, or
replan. An unexpected discovery is simply part of its report. Runtime completes
the current batch, then the planner evaluates the integrated state.

Runtime records actual changed paths, attempts, timestamps, exit codes, and
verification evidence independently of the worker report.

## Verification and completion

Required task verification is deterministic. Failure retries the same immutable
task with its prior report and authoritative failure evidence. Planning is not
invoked for an ordinary task-check failure.

After the batch is exhausted, planning always runs again. If the planner emits
no tasks, strict final verification runs. A final failure becomes evidence for
a new planning cycle; a final success permits finalization without a semantic
final-review phase.

## Persistence and recovery

`.idd/factory/current/state.json` stores machine state. Task contracts,
`planning-output.md`, and `semantic-result.md` are separate human-readable
artifacts. `result.json` stores only runtime-owned provenance pointing to the
semantic artifact.

Recovery resumes the exact persisted planning, execution, or verification
operation. Completed history is immutable. Runtime budgets bound planning
cycles, total work items, and attempts per task.

The new schema is intentionally incompatible with active runs from the previous
semantic-outcome protocol; such runs must be cancelled and restarted.

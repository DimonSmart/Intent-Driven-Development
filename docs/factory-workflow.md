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
completed task results, actual changed paths, authoritative verification
evidence, and prior user answers recorded by planning pauses.

Normal planner output is human-readable Markdown and has exactly one logical
form. For contractable work it returns one or more tasks:

```markdown
# Task

Implement the first safely contractable change.

# Task

Integrate it into the second already-understood area.
```

Each `# Task` section becomes one immutable work-item contract. The planner
returns every task that can be safely contracted now and stops at the first
material uncertainty that depends on new evidence.

If no task can be safely contracted because a decision must come from the user,
the planner may instead return exactly one question:

```markdown
# Question

Should deleted records be restored automatically or only after confirmation?
```

If semantic reassessment finds no remaining work and no missing user decision,
the planner returns exactly:

```markdown
# Done
```

`# Done` has no body and cannot be mixed with tasks or a question. Blank or
whitespace-only planner output is malformed and is not a completion signal.
Planner output contains no capability, persistent ID, status, dependency,
revision, outcome, or transition instruction.

A question cannot be mixed with tasks or `# Done`. Runtime turns it mechanically
into a resumable `USER_DECISION_REQUIRED` pause; it does not interpret the
decision.

## User decision

The host presents the planner question to the user. If the answer changes
durable product truth, the normal IDD intent workflow records and validates that
change before Factory resumes. If it is only an implementation choice, intent
remains unchanged.

The exact answer is passed to `factory_continue`, stored separately from the
immutable `request.md`, and supplied to the next planner. If the user chooses
not to continue, the run is cancelled explicitly.

Executors cannot request this pause and their free-form reports are never parsed
for user-question, intent, correction, or replanning signals.

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

After the batch is exhausted, planning always runs again. A validated exact
`# Done` is mechanically mapped to the existing empty-batch representation and
starts strict final verification. `# Done` itself never completes Factory. A
final failure becomes evidence for a new planning cycle; a final success permits
finalization without a semantic final-review phase.

## Persistence and recovery

`.idd/factory/current/state.json` stores machine state. Task contracts,
`planning-output.md`, `semantic-result.md`, and planning question/answer
artifacts are separate human-readable artifacts. `result.json` stores only
runtime-owned provenance pointing to semantic artifacts.

Recovery resumes the exact persisted planning, execution, verification, or
user-question continuation. Completed history is immutable. Persisted planner
output is validated under the current protocol, so an exact `# Done` may resume
normally while a persisted blank result is malformed. Runtime budgets bound
planning cycles, total work items, and attempts per task.

The schema remains intentionally incompatible with active runs from the
previous semantic-outcome protocol; such runs must be cancelled and restarted.

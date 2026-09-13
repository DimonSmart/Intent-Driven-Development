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

For every planning cycle the planner reads `.idd/intent/README.md`, then
`.idd/intent/INDEX.md` as a compact discovery catalog, and opens only plausible
current numbered intent documents needed to determine task semantics or
relevance. The index helps discovery but is not normative product content;
numbered current intent documents remain the source of durable product truth.

Normal planner output is human-readable Markdown and has exactly one logical
form. For contractable work it returns one or more tasks. Each task may end with
`# TaskRelatedIntent` containing stable `IDD-NNNN` IDs selected semantically for
that task:

```markdown
# Task

Implement the first safely contractable change.

# TaskRelatedIntent
IDD-0012
IDD-0027

# Task

Integrate it into the second already-understood area.
```

The flow is:

```text
intent index
    ↓
planner selects relevant intent per task
    ↓
Task + TaskRelatedIntent IDs
    ↓
Factory validates and persists IDs
    ↓
Factory deterministically resolves IDs
    ↓
Task + full current intent contents
    ↓
executor
```

Each `# Task` section becomes one immutable work-item contract. The related
intent IDs are separate machine-owned work-item metadata and never become part
of `contract.md`. The planner decides semantic relevance independently for each
task. Factory does not infer relevance by filename, keywords, embeddings,
ranking, or another semantic agent.

`# TaskRelatedIntent` is optional, may occur at most once after its task,
terminates that contract, contains one canonical ID per non-empty line, and must
be followed only by another `# Task` or end of output. Duplicate, malformed,
unknown, or ambiguous references reject the whole planner batch before any new
work-item identity or contract is adopted. A task without the section has an
empty related-intent set.

The planner returns every task that can be safely contracted now and stops at
the first material uncertainty that depends on new evidence. A task contract
remains self-contained about the concrete result; related intent supplies
additional normative durable constraints rather than replacing the contract or
creating another source of truth.

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
Planner output contains no capability, Factory/work-item identity, status,
dependency, revision, outcome, or transition instruction. Referencing existing
stable durable-intent IDs in `TaskRelatedIntent` is explicitly permitted and is
not runtime identity creation.

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
Before every executor invocation it deterministically resolves the current
work item's persisted `TaskRelatedIntentIds` against direct current numbered
files under `.idd/intent/`, requires exactly one `IDD-NNNN.*.md` match per ID,
and reads each selected document in planner-selected order.

An executor receives one contract, then an explicit task-related durable-intent
section containing the complete current contents of exactly those selected
documents (or `none`), followed by relevant completed results, prior-attempt
context, and verification evidence. README, INDEX, GLOSSARY, unrelated numbered
intent, and intent referenced only by completed tasks are not automatically
injected.

The selected ordered ID sequence is immutable for the work item. Document
contents are not snapshotted into state or `contract.md`; retries resolve the
same IDs again and therefore receive current durable truth. If a persisted ID no
longer resolves uniquely, Factory fails deterministically before executor
dispatch instead of omitting or semantically repairing the reference.

The executor does not create tasks or decide whether to interrupt, correct, or
replan. It may still inspect repository state and additional intent when a
genuine implementation discovery requires it, but explicitly selected intent
does not depend on executor rediscovery. An unexpected discovery is simply part
of its report. Runtime completes the current batch, then the planner evaluates
the integrated state.

Runtime records actual changed paths, attempts, timestamps, exit codes, and
verification evidence independently of the worker report.

## Verification and completion

Required task verification is deterministic. Failure retries the same immutable
task with its prior report and authoritative failure evidence. The same ordered
`TaskRelatedIntentIds` are preserved for ordinary retries, verification retries,
command-timeout/incomplete-command retries, and retry-budget extension. Planning
is not invoked for an ordinary task-check failure.

After the batch is exhausted, planning always runs again. A validated exact
`# Done` is mechanically mapped to the existing empty-batch representation and
starts strict final verification. `# Done` itself never completes Factory. A
final failure becomes evidence for a new planning cycle; a final success permits
finalization without a semantic final-review phase.

## Persistence and recovery

`.idd/factory/current/state.json` stores machine state. `TaskRelatedIntentIds`
are immutable ordered work-item metadata retained for Remaining, Current, and
Completed work. Task contracts, `planning-output.md`, `semantic-result.md`, and
planning question/answer artifacts remain separate human-readable artifacts.
`result.json` stores only runtime-owned provenance pointing to semantic
artifacts.

Recovery resumes the exact persisted planning, execution, verification, or
user-question continuation. It never asks an LLM to reconstruct related-intent
references, and retry never reruns semantic relevance selection for an existing
work item. Later planning cycles may select different intent for newly created
tasks; existing task definitions and completed history remain immutable.

Factory state schema 13 introduces persisted related-intent metadata and is
intentionally incompatible with active schema-12 runs. Missing metadata on an
older active work item is not interpreted as an authoritative empty set. The
existing legacy cancellation/restart policy applies; no implicit migration is
introduced.

Persisted planner output is validated under the current protocol, so an exact
`# Done` may resume normally while a persisted blank result is malformed.
Runtime budgets bound planning cycles, total work items, and attempts per task.

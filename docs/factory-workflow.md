# Factory Workflow

Factory is lightweight native-agent orchestration for implementation work that
benefits from decomposition and fresh contexts. Its entry workflow may coordinate
required durable preparation before orchestration starts; its planner and
workers remain implementation-only.

## Entry preflight and initial planning

For a new implementation-capable Factory run:

```text
Complete materialized logical request
-> Factory Preflight
   -> non-mutating Product Intent analysis and blocker checks
   -> idd-engineering-change when required
   -> normal Product Intent mutation when required
   -> durable coverage validation
-> preparation/request.md -> fresh initial Planner [INACTIVE]
     Question -> persist exact question, ask user and STOP
               -> exact answer, owner workflow(s), renewed coverage
               -> NEW fresh Planner [still INACTIVE]
     Done     -> final verification -> complete without activation
     Tasks    -> validate all contracts and durable references
               -> current/plan.md + exact answers
               -> current/request.md LAST [ACTIVE]
-> sequential native Workers -> fresh Planner -> Question | Done
```

Preflight uses the complete materialized request, not summaries, plans or host
attachment paths. Product Intent changes are owned by normal Intent workflows;
Engineering decisions are owned by `idd-engineering-change`, which also owns
ENG mapping, semantic equivalence and allocator edits. When both are needed,
Engineering changes precede Product changes. Known request-level blockers stop
before durable mutations; failed owners do not trigger rollback of earlier
completed owner writes.

`route-only` has no Planner or writes, `intent-only` stops after authorized
durable preparation, and `implementation-only` forbids changes to both
`.idd/intent/*` and `.idd/engineering/*`, even after a Planner question.
No response silently expands requested scope. A missing Engineering layer is
normal and is not bootstrapped merely by entering Factory.


## Replacement and active-run safety

Continuation of the current run does not repeat preflight.

An explicit replacement that needs no Engineering mutation keeps the existing
replacement semantics: resolve the complete replacement request, complete
replacement Product preflight and coverage, and only then replace temporary
state.

If an active replacement requires an Engineering Rule change, it is blocked
before durable writes. The Engineering active-run guard is not bypassed. Complete
or cancel the current run, then start the complete replacement request as a new
run. Factory does not auto-cancel/restart, temporarily archive state to bypass
the guard, or introduce a suspended lifecycle.

## Planning

The planner creates only implementation/research tasks whose contracts are
reliable now. It never creates tasks to update Product Intent, create/modify
Engineering Rules, or run durable-management workflows.

Planner output is Markdown:

```text
# Task
...

# ExecutionProfile
standard

# TaskRelatedIntent
IDD-0012

# TaskRelatedEngineering
ENG-0004

# Question
...

# Done
```

Exactly one form is used per planner invocation. Blank output is invalid.

Every newly generated task has exactly one `# ExecutionProfile`: `economy`,
`standard`, or `strong`. Validate new output before saving it. Existing pending
tasks in `plan.md` without the section use `standard`, preserving continuation
without migration or restart. Explicit empty, repeated, or unknown profiles
remain invalid. The planner classifies complexity only and does not read model
mappings.

Immediately before a worker starts, the root agent mechanically resolves that
profile through optional project-owned `.idd/execution.yaml`. If the file is
absent, workers inherit host model/reasoning settings. Partial mappings are
valid; missing profiles or active-platform mappings also inherit. Every present
mapping must be structurally valid. Explicit overrides are applied exactly
through native child-agent controls; malformed or host-rejected mappings never
trigger silent model substitution.

When Engineering exists, `TaskRelatedEngineering` contains only
planner-selected Conditional Rules. The current Always set is mechanically
enumerated separately before every worker. Workers read supplied Intent and
Engineering documents but never edit either durable layer.

## Temporary state

```text
.idd/factory/preparation/
    request.md               # unchanged full base request
    answers.md               # exact chronological Q/A, optional
    question.md              # one pending question, optional
    verification-failure.md  # bounded final check, optional

.idd/factory/current/
    plan.md                  # validated Task batch / remaining tasks
    answers.md               # transferred exact Q/A, optional
    request.md               # ONLY active marker, written LAST
    completed.md             # later worker summaries
    question.md              # optional active-run question
    verification-failure.md  # optional active final-check result
```

Only `current/request.md` means Factory is active. An orphaned directory,
plan or legacy `state.json` without the marker is NOT an active run.
Preliminary request and exact answers survive across sessions without relying
on old agent transcripts. The repository remains the implementation truth.
A valid first Task batch is not planned a second time merely on activation.


## Interruption

Factory uses at-least-once execution.

If a worker may have partially changed the repository and then stops, its task
remains in `plan.md`. A later fresh worker inspects the current repository and
finishes from that reality.

Do not start a replacement writer while the previous child may still be active.
Use native stop/close when possible and end the invocation.

## Questions, preparation resume, and activation

Before activation the initial Planner may ask one concrete question. Factory
saves `preparation/question.md` before asking and starts no Worker. On a reply,
it stores the original question and verbatim answer in `preparation/answers.md`
in order, then reclassifies the entire base request plus all clarifications
under the original requested scope. Implementation-local answers produce no
Engineering Rule. Authorized durable Engineering answers go to
`idd-engineering-change` with the full base request and relevant exact Q/A;
Product answers go through existing Intent workflows. When both apply,
Engineering comes first. After renewed coverage validation the next Planner
must be FRESH, without inherited transcript.

When preparation exists but `current/request.md` does not, a later invocation
may resume using `preparation/request.md` and the exact saved question/answers.
A new independent request cannot silently merge with it: request an explicit
decision to continue or discard preparation. Cancellation removes only
temporary preparation and does not undo durable changes. A malformed Planner
output, missing/ambiguous references, blocked owner or scope conflict retains
preparation, never activates or runs Workers.

On initial Tasks, require nonempty self-contained contracts, exactly one
explicit valid `# ExecutionProfile` per new Task, unique resolved Intent IDs
and Conditional-only Engineering IDs. Recheck current repository and durable
knowledge for freshness. Prepare `current/plan.md` and exact answers BEFORE
`current/request.md`, which is written last, preferably via an atomic
same-directory rename. Only both valid active files permit Workers. A partial
write without marker is inactive; verify provenance before reusing its files.
There is no multi-file transaction or exactly-once execution promise.

After activation, later Engineering decisions remain blocked by the unchanged
active-run guard even if no Worker has finished. Explicit active replacement
with Engineering changes also remains blocked.

## Done and verification

`# Done` even before the first Worker uses the existing final project
verification configuration and the existing absent-policy fallback. Successful
pre-activation Done ends without creating an active run or empty plan. On
failure retain bounded check/command, exit result and diagnostic in
`preparation/verification-failure.md` and invoke a fresh Planner. Confirmation
needed and `Not verified` are not success. Identical repeated Done/failure
without intervening change stops with diagnosis, not an infinite loop. After
activation use the same ordinary final verification protocol and active
diagnostic file; root orchestration never repairs checks directly.


## Native host requirement

A host must provide child spawn, fresh context without parent-history
inheritance, shared repository access, terminal waiting without model polling,
final result retrieval, and stop/close lifecycle control.

If those properties are unavailable, Factory may be unsupported. Do not recreate
a packaged C# runtime, MCP transport, process supervisor, or polling protocol.

## Post-run diagnostics

For deterministic post-run diagnostics and statistics over the host-owned trace,
see [`idd-factory-report`](factory-report.md). The reporter is a development
utility and is not part of the Factory workflow or plugin runtime.

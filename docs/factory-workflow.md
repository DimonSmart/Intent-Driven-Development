# Factory Workflow

Factory is lightweight native-agent orchestration for implementation work that
benefits from decomposition and fresh contexts. Its entry workflow may coordinate
required durable preparation before orchestration starts; its planner and
workers remain implementation-only.

## Entry preflight

For a new run:

```text
Complete logical request
-> Factory Preflight
   -> Product Intent analysis
   -> explicit Engineering concern detection
   -> idd-engineering-change when required
   -> existing Product Intent workflows when required
   -> durable coverage validation
-> create .idd/factory/current/request.md
-> fresh planner
-> current batch
-> fresh sequential workers
-> fresh planner
-> Question | Done
```

The request is materialized once and stays authoritative. A referenced local
Markdown specification is read into the logical request so continuation does not
depend on a temporary attachment/path.

Product Intent mutations remain owned by normal Intent workflows. Engineering
mutations remain owned by `idd-engineering-change`; Factory does not decide ENG
ownership, semantic equivalence, allocator changes, or add/modify/remove/no-op
actions itself.

When Engineering management is required, it runs before Product Intent mutation
and before active Factory state exists. `success` and `no-op` continue;
`ambiguous` and `blocked` stop. Durable coverage is validated against the
complete request before `request.md` is created.

`implementation-only` permits no mutation of either `.idd/intent/*` or
`.idd/engineering/*`. Projects without an Engineering layer remain valid and do
not receive one merely because Factory starts.

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

`# ExecutionProfile` is optional task metadata with exactly `economy`,
`standard`, or `strong`; absence means `standard`. The planner selects the
profile from task complexity only and does not read model mappings.

Immediately before a worker starts, the root agent mechanically resolves that
profile through optional project-owned `.idd/execution.yaml`. Missing mappings
inherit the host model. Explicit mappings are applied exactly through native
child-agent controls; malformed/unavailable mappings never trigger silent model
substitution.

When Engineering exists, `TaskRelatedEngineering` contains only
planner-selected Conditional Rules. The current Always set is mechanically
enumerated separately before every worker. Workers read supplied Intent and
Engineering documents but never edit either durable layer.

## Temporary state

```text
.idd/factory/current/
    request.md
    plan.md
    completed.md
    answers.md
    question.md                # optional
    verification-failure.md    # optional
```

`request.md` is the complete self-contained logical request that passed
preflight. The repository is authoritative implementation reality. `plan.md`
contains only remaining implementation/research work in the current batch.
`completed.md` contains short semantic summaries useful to the next planner.

There is no authoritative `state.json`, work-item lifecycle graph, attempt
history, process ownership, retry budget, transaction log, or exact continuation
protocol.

## Interruption

Factory uses at-least-once execution.

If a worker may have partially changed the repository and then stops, its task
remains in `plan.md`. A later fresh worker inspects the current repository and
finishes from that reality.

Do not start a replacement writer while the previous child may still be active.
Use native stop/close when possible and end the invocation.

## Questions

When the planner returns one `# Question`, Factory persists the question and
stops.

A Product-Intent answer uses the normal outer Intent workflow when allowed. An
ordinary implementation decision changes no durable knowledge. If the answer
introduces a new durable Engineering decision, Rules cannot be mutated while the
active `request.md` exists: keep Factory paused and require the current run to
be completed/cancelled before starting a new complete request that includes the
decision.

The exact answer is stored before a fresh planner starts. The old planner thread
is never resumed.

## Done and verification

When a fresh planner returns `# Done`, run configured project verification.

- success: complete Factory and remove/archive temporary state;
- failure: persist a bounded diagnostic and invoke a fresh planner;
- no configured verification: ordinary worker checks plus `# Done` are enough.

Factory does not own a separate verification engine.

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

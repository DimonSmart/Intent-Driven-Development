# Factory Workflow

Factory is lightweight native-agent orchestration for implementation work that
benefits from decomposition and fresh contexts.

## Main loop

```text
User request
-> Intent Preflight
-> fresh planner
-> current batch
-> fresh worker
-> fresh worker
-> fresh planner
-> Question | Done
```

The planner and every worker use separate semantic contexts. Workers share the
repository, not transcripts.

## Planning

The planner creates only tasks whose contracts are reliable now. It can return
multiple tasks when they are already well-defined; workers execute them
sequentially.

If later work depends on evidence produced by an unfinished task, stop the
current plan at that boundary. A new planner invocation observes repository
reality and contracts the next work.

Planner output is Markdown:

```text
# Task
...

# TaskRelatedIntent
IDD-0012

# Question
...

# Done
```

Exactly one form is used per planner invocation. Blank output is invalid.

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

The repository is authoritative. `plan.md` contains only remaining work in the
current batch. `completed.md` contains short semantic summaries useful to the
next planner.

There is no authoritative `state.json`, work-item lifecycle graph, attempt
history, process ownership, retry budget, or exact continuation protocol.

## Interruption

Factory uses at-least-once execution.

If a worker may have partially changed the repository and then stops, its task
remains in `plan.md`. A later fresh worker inspects the current repository and
finishes from that reality.

Do not start a replacement writer while the previous child may still be active.
Use native stop/close when possible and end the invocation.

## Questions

When the planner returns one `# Question`, Factory persists the question and
stops. After the user answers, normal IDD handling decides whether durable intent
changes. The exact answer is stored and a fresh planner starts. The old planner
thread is not resumed.

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

# Codex Factory child-agent dispatch

Codex Factory roles are delivered to generic child agents through the dispatch
prompt. A role is not a native custom agent type. Use the concrete runtime
operations `spawn_agent` and `wait_agent`; the adapter has already mapped the
canonical capabilities, so do not probe or remap them at runtime.

For every Factory child-agent dispatch:

- provide only `message` to `spawn_agent`;
- do not provide `items` and never provide both parameters;
- use `fork_context = false`;
- pass paths to the role, skill, work item, and required references instead of
  copying their contents;
- use the generated role path `references/roles/<role>.md`; never invent
  aliases such as `<role>-role.md`;
- use the `references/project-verification.md` owned by the dispatched skill;
- read the agent id returned by `spawn_agent`;
- call `wait_agent` with that id and wait for the terminal result before
  changing Factory state or continuing;
- if a wait returns while the child is still active, call `wait_agent` again;
  an active child is not a timeout failure or terminal result;
- do not impose a coordinator-local wait deadline; only an external cancellation
  or the enclosing Factory-attempt deadline may interrupt an active child;
- validate the returned result against the worker skill contract;
- do not treat reading a worker skill in the coordinator context as dispatch;
- do not perform worker scope in the coordinator context.

Use this shape only for `factory-step-coordinator` dispatches:

```text
You are executing one IDD Factory role in a separate child-agent context.

Role:
factory-step-coordinator

Action:
<INITIALIZE | CONTINUE>

Workspace:
<absolute-workspace-path>

Resume request:
<resume-request, CONTINUE only>

Read and follow:
- <coordinate-step-skill-directory>/SKILL.md
- <coordinate-step-skill-directory>/references/roles/factory-step-coordinator.md
- <coordinate-step-skill-directory>/references/project-verification.md

Perform only the scope assigned to this role.
Follow the result contract defined by the skill.
Do not perform work owned by another Factory role.
Return only the compact result required by the skill.
```

Omit `Resume request` for `INITIALIZE`. Every later coordinator dispatch uses
`CONTINUE`. For every `CONTINUE`, use exactly:

```text
Resume request: Continue the current Factory run from persisted state and process exactly one next logical action.
```

Pass a confirmed blocker answer or explicit cancellation request separately when
applicable. Never add the next Subtask, checkpoint, final-review, finalization,
or other phase hint to a `CONTINUE` dispatch.

Use this shape for decomposer, implementation, and review workers:

```text
You are executing one IDD Factory role in a separate child-agent context.

Role:
<task-decomposer | implementer | checkpoint-reviewer | final-reviewer>

Workspace:
<absolute-workspace-path>

Read and follow:
- <skill-directory>/SKILL.md
- <skill-directory>/references/roles/<role>.md
- <skill-directory>/references/project-verification.md
- <active-work-item-path when applicable>

Perform only the scope assigned to this role.
Follow the result contract defined by the skill.
Do not perform work owned by another Factory role.
Do not create child agents unless the role explicitly allows agent.spawn.
Return only the compact result required by the skill.
```

Do not include `Action` for task-decomposer, implementer, checkpoint-reviewer,
or final-reviewer. The decomposer additionally receives the complete original
request and durable-intent path. Coordinator `INITIALIZE` additionally receives
the complete original Factory request, methodology version, confirmed
clarifications when present, and complete validated decomposition result.

If `spawn_agent` or `wait_agent` fails terminally, preserve the work item and
stop as `BLOCKED`. A non-terminal wait response for an active child is not a
failure: continue waiting for that same child. Before reporting a
dispatch-related `BLOCKED`, actually invoke the applicable operation and
preserve its observed runtime failure. Do not infer failure from
capability-name uncertainty or the absence of a native custom agent type. Do
not synthesize a result, mark work complete, or execute worker scope in the
coordinator.

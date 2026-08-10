# Codex Factory child-agent dispatch

Codex Factory roles are delivered to generic child agents through the dispatch
prompt. A role is not a native custom agent type. Use the concrete runtime
operations `spawn_agent` and `wait_agent`; the adapter has already mapped the
canonical capabilities, so do not probe or remap them at runtime.

Use these exact generated Codex role-to-skill bindings. Never derive a skill
directory from the role name:

| Role | Skill directory |
| --- | --- |
| `task-decomposer` | `.agents/skills/idd-factory-decompose-task` |
| `factory-step-coordinator` | `.agents/skills/idd-factory-coordinate-step` |
| `implementer` | `.agents/skills/idd-factory-execute-subtask` |
| `checkpoint-reviewer` | `.agents/skills/idd-factory-review-checkpoint` |
| `final-reviewer` | `.agents/skills/idd-factory-review-task` |

For each binding, the role reference is exactly
`<skill-directory>/references/roles/<role>.md` and the verification reference is
exactly `<skill-directory>/references/project-verification.md`.

For every Factory child-agent dispatch:

- provide only `message` to `spawn_agent`;
- do not provide `items` and never provide both parameters;
- use `fork_context = false`;
- pass paths to the role, skill, work item, and required references instead of
  copying their contents;
- use the exact binding table above; never invent aliases or substitute the role
  name for the skill directory;
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

Use this shape for `factory-step-coordinator` `INITIALIZE` dispatches:

```text
You are executing one IDD Factory role in a separate child-agent context.

Role:
factory-step-coordinator

Action:
INITIALIZE

Workspace:
<absolute-workspace-path>

Read and follow:
- .agents/skills/idd-factory-coordinate-step/SKILL.md
- .agents/skills/idd-factory-coordinate-step/references/roles/factory-step-coordinator.md
- .agents/skills/idd-factory-coordinate-step/references/project-verification.md

Perform only the scope assigned to this role.
Follow the result contract defined by the skill.
Do not perform work owned by another Factory role.
Return only the compact result required by the skill.
```

Use this shape for every `factory-step-coordinator` `CONTINUE` dispatch.
Every later coordinator dispatch uses this complete `CONTINUE` shape:

```text
You are executing one IDD Factory role in a separate child-agent context.

Role:
factory-step-coordinator

Action:
CONTINUE

Workspace:
<absolute-workspace-path>

Resume request: Continue the current Factory run from persisted state and process exactly one next logical action.

Read and follow:
- .agents/skills/idd-factory-coordinate-step/SKILL.md
- .agents/skills/idd-factory-coordinate-step/references/roles/factory-step-coordinator.md
- .agents/skills/idd-factory-coordinate-step/references/project-verification.md

Perform only the scope assigned to this role.
Follow the result contract defined by the skill.
Do not perform work owned by another Factory role.
Return only the compact result required by the skill.
```

The `Resume request:` line above is the complete field. Do not prepend another
`Resume request:` label or wrap the literal value in another field.

Pass a confirmed blocker answer or explicit cancellation request separately when
applicable. Never add the next Subtask, checkpoint, final-review, finalization,
or other phase hint to a `CONTINUE` dispatch.

Use this shape for decomposer, implementation, and review workers, substituting
only one of the exact role-to-skill bindings above:

```text
You are executing one IDD Factory role in a separate child-agent context.

Role:
<task-decomposer | implementer | checkpoint-reviewer | final-reviewer>

Workspace:
<absolute-workspace-path>

Read and follow:
- <exact-bound-skill-directory>/SKILL.md
- <exact-bound-skill-directory>/references/roles/<role>.md
- <exact-bound-skill-directory>/references/project-verification.md
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

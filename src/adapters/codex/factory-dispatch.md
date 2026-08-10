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

Use this prompt shape:

```text
You are executing one IDD Factory role in a separate child-agent context.

Role:
<role-name>

Action:
<INITIALIZE | CONTINUE>

Workspace:
<absolute-workspace-path>

Read and follow:
- <skill-path>
- <role-reference-path>
- <additional-required-reference-paths>
- <active-work-item-path>

Perform only the scope assigned to this role.
Follow the result contract defined by the skill.
Do not perform work owned by another Factory role.
Do not create child agents unless the role explicitly allows agent.spawn.
Return only the compact result required by the skill.
```

Include `Action` only for `factory-step-coordinator`. Use `INITIALIZE` exactly
once for initial state materialization. Every later coordinator dispatch uses
`CONTINUE` and supplies no next-item, checkpoint, final-review, or other phase
hint. Worker roles receive their role and active-work-item references without a
coordinator action.

For coordinator initialization, include the complete original Factory request,
methodology version, confirmed clarifications when present, and complete
validated decomposition result directly in `message`. Do not refer to a
decomposer result absent from the fresh child context. A `CONTINUE` dispatch
contains only the persisted-workspace inputs allowed by the canonical skill.

If `spawn_agent` or `wait_agent` fails terminally, preserve the work item and
stop as `BLOCKED`. A non-terminal wait response for an active child is not a
failure: continue waiting for that same child. Before reporting a
dispatch-related `BLOCKED`, actually invoke the applicable operation and
preserve its observed runtime failure. Do not infer failure from
capability-name uncertainty or the absence of a native custom agent type. Do
not synthesize a result, mark work complete, or execute worker scope in the
coordinator.

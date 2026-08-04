# Codex Factory child-agent dispatch

Codex Factory roles are delivered to generic child agents through the dispatch
message. A role is not a native custom agent type.

For every Factory child-agent dispatch:

- provide only `message` to `spawn_agent`;
- do not provide `items`;
- never provide both `message` and `items`;
- use `fork_context = false`;
- pass paths to the role, skill, work item, and required references instead of
  copying their contents;
- read the agent id returned by `spawn_agent`;
- call `wait_agent` with that agent id and wait for the terminal child result
  before changing Factory state or continuing;
- validate the returned result against the worker skill contract;
- do not treat reading a worker skill in the coordinator context as dispatch;
- do not perform the worker scope in the coordinator context.

Use this message shape:

```text
You are executing one IDD Factory role in a separate child-agent context.

Role:
<role-name>

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

The dispatch sequence is:

1. Call `spawn_agent` using `message` only and `fork_context = false`.
2. Read the returned agent id.
3. Call `wait_agent` for that agent id.
4. Validate the terminal child result before updating Factory state.

If `spawn_agent` or `wait_agent` fails, preserve the work item and stop as
`BLOCKED`. Record the exact technical reason, distinguishing invalid dispatch,
spawn rejection, agent creation failure, wait failure, invalid child result,
and a child-reported blocker. Do not create a synthetic result, mark work
complete, or execute the worker scope in the coordinator.

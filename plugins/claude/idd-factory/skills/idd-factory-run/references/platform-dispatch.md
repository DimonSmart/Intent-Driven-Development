# Claude Factory child-agent dispatch

Claude Factory roles run in fresh forked skill contexts. Use the `Task`
capability to start the role and `TaskOutput` to await its terminal result. The
adapter has already mapped the canonical capabilities, so do not probe or remap
them at runtime.

Use these exact role-to-skill bindings. Never derive a skill name from the role
name:

| Role | Skill |
| --- | --- |
| `task-decomposer` | `idd-factory-decompose-task` |
| `factory-step-coordinator` | `idd-factory-coordinate-step` |
| `implementer` | `idd-factory-execute-subtask` |
| `checkpoint-reviewer` | `idd-factory-review-checkpoint` |
| `final-reviewer` | `idd-factory-review-task` |

Within the selected skill, use exactly `references/roles/<role>.md` and
`references/project-verification.md`.

For every Factory child-agent dispatch:

- start a fresh context for the selected role;
- provide the role, exact bound skill, work item, and required reference paths;
- use the binding table above; never substitute the role name for the skill name;
- await the terminal child result before changing Factory state or continuing;
- validate the result against the worker skill contract;
- do not perform worker scope in the coordinator context.

Include `Action` only for `factory-step-coordinator`. Use `INITIALIZE` exactly
once for initial state materialization. Every later coordinator dispatch uses
`CONTINUE`. For every `CONTINUE`, use exactly this resume request:

```text
Resume request: Continue the current Factory run from persisted state and process exactly one next logical action.
```

Pass a confirmed blocker answer or explicit cancellation request separately when
applicable. Never supply a next-item, checkpoint, final-review, finalization, or
other phase hint. Task-decomposer, implementer, checkpoint-reviewer, and
final-reviewer receive no `Action` field.

For coordinator initialization, provide the complete original Factory request,
methodology version, confirmed clarifications when present, and complete
validated decomposition result to the fresh context. A `CONTINUE` dispatch
contains only the persisted-workspace inputs allowed by the canonical skill.

If child creation or awaiting its result fails, preserve the work item and stop
as `BLOCKED` with the observed runtime reason. Do not synthesize a result, mark
work complete, or execute worker scope in the coordinator.

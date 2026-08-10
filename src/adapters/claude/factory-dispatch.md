# Claude Factory child-agent dispatch

Claude Factory roles run in fresh forked skill contexts. Use the `Task`
capability to start the role and `TaskOutput` to await its terminal result. The
adapter has already mapped the canonical capabilities, so do not probe or remap
them at runtime.

For every Factory child-agent dispatch:

- start a fresh context for the selected role;
- provide the role, skill, work item, and required reference paths;
- await the terminal child result before changing Factory state or continuing;
- validate the result against the worker skill contract;
- do not perform worker scope in the coordinator context.

Include `Action` only for `factory-step-coordinator`. Use `INITIALIZE` exactly
once for initial state materialization. Every later coordinator dispatch uses
`CONTINUE` and supplies no next-item, checkpoint, final-review, or other phase
hint. Worker roles receive their role and active-work-item references without a
coordinator action.

For coordinator initialization, provide the complete original Factory request,
methodology version, confirmed clarifications when present, and complete
validated decomposition result to the fresh context. A `CONTINUE` dispatch
contains only the persisted-workspace inputs allowed by the canonical skill.

If child creation or awaiting its result fails, preserve the work item and stop
as `BLOCKED` with the observed runtime reason. Do not synthesize a result, mark
work complete, or execute worker scope in the coordinator.

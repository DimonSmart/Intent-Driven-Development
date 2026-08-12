# idd-factory-review-task

## Purpose

Independently review the complete integrated Factory result after all work items
and checkpoints complete.
This skill is the complete semantic contract for the `final-reviewer` role.

## Inputs and boundaries

Read the original request, relevant current intent, run context, every completed
contract/result, checkpoint results, full baseline-to-current diff,
preservation boundaries, and authoritative final verification evidence. Do not
modify code, intent, verification policy, Factory state, or delegate.

Detect lost requirements, integration gaps, incorrect coverage, intent-changing
implementation work, and unsupported completion claims. The runtime selects and
runs mandatory verification before invoking this skill; the reviewer judges
its evidence and does not rerun the mandatory gate. Focused read-only
diagnostics and relevant project or domain skills remain available.

## Structured result

Return worker protocol version 1 with role `final-reviewer` and one outcome:
`approved`, `needs-fix`, `needs-replan`, `blocked`, or `intent-required`.

`approved` supplies semantic commit-message material under
`payload.commitMessage` with `subject`, `why[]`, and `result[]`.
`needs-fix` supplies one bounded self-contained implementation-only
`payload.correctiveSubtask`; final review itself is the next gate. Keep
implementation and verification assessments separate. Never describe blocked
or unverified work as approved.
The runtime owns machine validation, completion policy, workflow transitions,
and selection of any next semantic capability.

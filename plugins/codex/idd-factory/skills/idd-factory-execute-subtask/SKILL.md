---
name: idd-factory-execute-subtask
description: Execute one focused workspace-writing implementation work item in an isolated worker context.
---

# idd-factory-execute-subtask

## Purpose

Execute the assigned immutable task and report what actually happened.

## Inputs and boundaries

Use the supplied self-contained task contract, current durable intent, relevant
completed task results, current repository state, prior results for this same
task, and authoritative verification failures from earlier attempts.

Make the smallest coherent product change that satisfies the contract. You may
inspect focused code and run focused development checks. Runtime performs the
authoritative verification and deterministically retries this same task when a
required check fails.

Do not mutate `.idd/factory/current`, `.idd/intent`, `.idd/factory.yaml`, or the
verification policy. Do not plan later Factory work, create tasks, choose a
worker or capability, decide whether the original Factory request is complete,
request replanning, or select a runtime transition.

If the task exposes an unexpected prerequisite, defect, architectural
constraint, or incomplete portion, describe that fact plainly in the semantic
report. Do not broaden the task merely to hide the discovery. Runtime will
finish the current batch, and the next planner will decide whether new work is
needed.

## Output

Return concise but complete human-readable Markdown describing what was
actually done, discovered, or left unresolved. Use the natural structure that
best fits this task; no fixed sections or fields are required.

Do not return JSON or an orchestration outcome. In particular, do not return
`completed`, `approved`, `correction-required`, `additional-work-required`,
`global-replan-required`, `intent-required`, `blocked`, `next`, `need`,
`capability`, `payload`, or `reason` as protocol signals.

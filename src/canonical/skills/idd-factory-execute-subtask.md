# idd-factory-execute-subtask

## Purpose

Execute the assigned immutable task and report what actually happened.

## Inputs and boundaries

Use the supplied self-contained task contract, supplied task-related durable
intent, relevant completed task results, current repository state, prior results
for this same task, and authoritative verification failures from earlier
attempts.

The task contract defines the concrete work to perform. Supplied task-related
durable intent is normative product input. Both constrain implementation.
Factory has already selected the persisted stable intent references for this
work item, resolved them mechanically, and loaded the complete current contents
of those selected documents. Correctness for explicitly supplied documents must
not depend on rediscovering them from `.idd/intent`.

You may still inspect current repository state, additional code, additional
durable intent, or the optional glossary when a genuine implementation
discovery makes that necessary. Do not routinely scan `.idd/intent` as a
substitute for the task-related durable intent Factory supplied.

Make the smallest coherent product change that satisfies the contract and its
normative task-related intent. You may inspect focused code and run focused
development checks. Runtime performs the authoritative verification and
deterministically retries this same immutable task when a required check fails.
Retries preserve the task contract and selected intent IDs; Factory reloads the
current contents of those same documents for every invocation.

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

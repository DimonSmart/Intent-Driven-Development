---
name: idd-factory-review-task
description: Independently review the complete result of the Factory Task.
context: fork
agent: Explore
allowed-tools: [Read, Glob, Grep, Bash]
---

# idd-factory-review-task

## Purpose

Independently review the complete integrated Factory result after all work items
and checkpoints complete.
This skill is the complete semantic contract for the `final-reviewer` role.

## Inputs and boundaries

Review the final integrated product against the original request and current
durable intent. Review the final state, not the Factory execution history.

The runtime supplies the original request, completed-work references, and
authoritative final verification evidence references. Factory artifact
references are relative to `.idd/factory/current` unless already rooted. The
final reviewer is invoked only after the authoritative final verification gate
has passed.

Start with the original request and a focused inspection of the current product
changes. Read only the intent, product files, work-item contracts/results,
checkpoint results, or verification evidence needed to resolve a concrete
semantic review question. Runtime-supplied references are navigation aids, not
a requirement to read every referenced artifact.

Do not begin final review with a broad or recursive workspace inventory. Do not
enumerate the whole workspace, `.idd/factory`, `bin`, or `obj`. Use the original
request and known paths first. Discover additional files only with focused
searches needed to answer a concrete semantic question.

Do not recursively inspect Factory state or attempt directories during normal
review. Worker stdout/stderr, invocation data, process telemetry, and worker
conversations are diagnostics, not normal final-review inputs; inspect them only
when a concrete protocol or execution inconsistency prevents semantic review.
Do not rerun mandatory Factory verification or re-audit successful command
output merely to reconfirm that it passed. Do not modify code, intent,
verification policy, Factory state, or delegate.

Detect lost requirements, integration gaps, incorrect coverage, intent-changing
implementation work, and unsupported completion claims. Focused read-only
inspection and relevant project or domain skills remain available when they
help resolve a concrete semantic concern.

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

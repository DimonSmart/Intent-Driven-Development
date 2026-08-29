---
name: idd-factory-review-checkpoint
description: Independently review one active Review checkpoint across its covered completed Subtasks.
context: fork
agent: Explore
argument-hint: "[active review-checkpoint path]"
allowed-tools: [Read, Glob, Grep, Bash]
---

# idd-factory-review-checkpoint

## Purpose

Independently review one selective checkpoint over its explicitly covered
completed Subtasks in a fresh read-only context.
This skill is the complete semantic contract for the `checkpoint-reviewer`
role.

## Inputs and boundaries

Read the checkpoint contract, covered contracts/results, relevant intent,
checkpoint-local diff, and authoritative runtime verification evidence. Do not
read the full request, unrelated or later work, or worker conversations. Do not
modify code, intent, verification policy, Factory state, or delegate.
The runtime runs the checkpoint gate before invoking this skill. Review the
supplied authoritative evidence; do not rerun mandatory Factory verification.

Do not begin checkpoint review with a broad or recursive workspace inventory.
Do not enumerate the whole workspace, `.idd/factory`, `bin`, or `obj`. Use the
checkpoint contract and runtime-supplied references first. Discover additional
files only with focused searches needed to resolve a concrete semantic review
question.

Focused read-only diagnostics and relevant project or domain skills remain
available when they help semantic review.

## Structured result

Return worker protocol version 1 with role `checkpoint-reviewer` and one
outcome: `approved`, `needs-fix`, `needs-replan`, `blocked`, or
`intent-required`.

`needs-fix` supplies `payload.correctiveSubtask`: a complete implementation-only
contract with ID, contract Markdown, dependencies, and verification check IDs.
Do not reopen or rewrite completed work. Use `needs-replan` for invalid coverage,
ordering, or remaining contracts. Separate implementation assessment from
verification assessment and report only material current findings.
The runtime owns machine validation, workflow transitions, corrections, and the
next role or skill.

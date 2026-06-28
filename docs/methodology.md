# Methodology

Intent-Driven Development is a practical way to use CodingAgents without turning the project into disconnected prompts, generated plans, and tool-specific instruction files.

The specification is not a magic executable artifact. It does not replace architecture, code review, testing, or human responsibility. It is a stable description of what the product should become.

## Why This Exists

CodingAgents are useful, but they have a weak memory model.

A chat can contain the right decision today and lose it tomorrow. One CodingAgent can know the project rules while another CodingAgent sees only a local prompt. A generated instruction file can drift from the real product intent. After several iterations, nobody is completely sure which file is the source of truth.

Intent-Driven Development separates three things:

```text
product intent               durable product knowledge
CodingAgent instructions     generated CodingAgent formats
implementation               code, tests, scripts, and concrete changes
```

Product intent should survive tool changes, CodingAgent changes, and implementation attempts.

## Intent

In this method, intent means stable product truth that future implementations must preserve.

A task says what to do next. Intent says what must remain true after the task is done.

A specification should answer one question:

> If we delete the implementation, can we rebuild the product from these files?

If the answer is yes, the specification is useful. If the answer is no, it is probably a task list, a note, or a chat summary.

## How It Differs from Spec-Driven Development

Spec-Driven Development has a good core idea: describe what should be built before asking a CodingAgent to build it.

The problem starts when the specification becomes too many things at once:

```text
- product description
- task tracker
- implementation plan
- generated checklist
- temporary chat memory
- tool-specific command input
```

Intent-Driven Development keeps the useful part and removes the rest.

| Spec-Driven Development | Intent-Driven Development |
| --- | --- |
| The spec often drives a feature workflow | The spec describes the target product state |
| Tasks may become part of the spec flow | Tasks are temporary and should not become product memory |
| The workflow is often tied to one CodingAgent or command system | CodingAgent files are generated from one canonical source |
| Generated plans can look authoritative | Engineering judgment stays explicit |
| The spec can become a process artifact | The spec remains product knowledge |

The point is not to worship the spec. The point is to keep the intent stable.

## One-Page Mental Model

Ask one question first:

> Does this change affect what future implementations must preserve?

If yes, update or create a specification or ADR.

If no, keep it in a task, issue, pull request, commit message, or chat.

If unsure, create a spike or ask for clarification before turning it into normative product intent.

| Change | Goes to `.specs/`? | Reason |
| --- | --- | --- |
| Product behavior changes | Yes | Future implementations must preserve it |
| Domain contract changes | Yes | It affects product meaning |
| Accepted architecture changes | Yes | It constrains future implementation |
| Acceptance criteria or verification changes | Yes | It changes how correctness is judged |
| Local task or TODO | No | It describes work, not product truth |
| Temporary implementation status | No | It may be obsolete tomorrow |
| Formatting or small refactoring | No | It does not change product intent |
| Generated CodingAgent output | No | Generated files are not authoritative |
| Existing implementation differs from spec | Not automatically | Implementation evidence is not intent by itself |

## What Goes Into Specifications

Good specification content:

```text
- product behavior
- user scenarios
- domain contracts
- important technical constraints
- architecture decisions that define the product
- framework or library choices when they are part of the product identity
- compatibility requirements
- non-goals
- acceptance criteria
- verification rules
```

Bad specification content:

```text
- temporary tasks
- what we are doing today
- local implementation notes
- outdated plans
- generated CodingAgent output
- chat history
- duplicated instruction files
```

A specification says what should remain true after the work is done.

## Relation to Tasks, ADRs, Issues, and Generated Files

Tasks, issues, pull requests, commit messages, and chat are good places for temporary work. They describe what is happening now.

Specifications and ADRs are for durable product truth. A specification describes behavior, contracts, constraints, non-goals, and verification rules. An ADR records an accepted architecture decision when that decision constrains future implementation.

Generated CodingAgent output is not authoritative. It can help deliver the method into a specific tool, but it should not become product memory.

Implementation evidence is also not intent by itself. When the implementation differs from the specification, decide whether the product intent changed before changing `.specs/`.

## Document Lifecycle

Git stores history.

`.specs/` stores only current product intent, ADRs, and active spikes.

When product intent evolves inside the same product area, update the existing spec directly.

When a product area is replaced by a substantially different product area, delete the old spec and create a new owning spec.

Delete obsolete, duplicated, task-like, process-only, or incorrect documents from the working tree. Do not preserve old spec versions in a separate directory.

ADRs are decision records. If a durable decision changes, keep the old ADR in place, mark it as `Superseded`, and create a new ADR for the replacing decision.

Resolved spikes should be deleted after their outcome is captured in a spec or ADR, unless they remain useful as active research.

## Context Discipline

IDD avoids large universal workflows that read many files, generate many intermediate artifacts, and leave long reasoning traces in the main CodingAgent conversation.

Specification work should be split into small focused skills. A skill should read only the specifications needed for the current decision.

Large specification-maintenance operations should return a compact result: the proposed change, conflicts, affected files, and verification notes.

When a CodingAgent supports isolated or forked execution for heavy skills, IDD adapters may use it to keep the main conversation focused.

## What This Method Optimizes For

IDD is useful when the project has:

```text
- more than one CodingAgent
- long-lived product rules
- repeated implementation sessions
- architectural constraints that should not be rediscovered every time
- generated CodingAgent instructions
- a need to keep project knowledge outside chat history
```

It is less useful for one-off experiments where the code will be thrown away.

## Non-Goals

IDD deliberately does not try to do several things.

```text
- Do not turn specifications into a task tracker.
- Do not store CodingAgent-specific instruction copies as the source of truth.
- Do not build Claude or Gemini instructions on top of Codex AGENTS.md.
- Do not update CopilotInstructions as a canonical source.
- Do not create a pull request back to CopilotInstructions.
- Do not use legacy terminology in canonical methodology or skills.
```

There should be one canonical source. Everything else is generated, adapted, or temporary.

## Relation to Spec-Guided Development and Spec-Driven Development

Spec-Driven Development starts from a useful idea: describe what should be built before asking a CodingAgent to build it.

Intent-Driven Development keeps that idea, but narrows the source of truth to durable product intent. Tasks, temporary plans, implementation notes, generated checklists, and CodingAgent-specific command files are not product intent.

In that sense, IDD can be described as a spec-guided approach: specifications guide development, but they do not become a task tracker or an AI command script.

## Summary

Intent-Driven Development keeps product memory in specifications, keeps CodingAgent files as delivery formats, and keeps temporary work outside the source of truth.

The specification is the product memory. The adapters are translation layers. The generated files are delivery formats for specific CodingAgents. The engineer still owns the result.

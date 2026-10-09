# idd-help

Use this skill when the user explicitly invokes `idd-help` to ask about
Intent-Driven Development methodology, concepts, workflows, skills, installation
behavior, or expected Coding Agent behavior.

This skill is explanatory and read-only. It does not change product intent,
implementation, Factory state, project files, or repository settings.

Do not use `idd-help` as an automatic pre-step for ordinary feature, bug-fix,
refactoring, review, or implementation requests. Do not invoke another IDD
workflow unless the user explicitly asks to perform that workflow.

## Question Input In An Isolated Context

An explicit question supplied with this command is the question to answer,
including any invocation arguments. The skill may run in an isolated child
context without the caller's conversation history. Do not infer a question
from an unavailable previous exchange.

If the command is invoked without a question and none is present in the
available input, ask the user for the question instead of guessing. Continue
to answer read-only; do not automatically invoke other workflows.

## Required References

Before answering, read:

- `references/common-workflows.md`
- `references/methodology.md`
- `references/skill-descriptions.json`
- `references/project-verification.md`

Treat `common-workflows.md` as the canonical source for current routing and
workflow behavior, `methodology.md` as the explanation of IDD principles, and
`skill-descriptions.json` as the current public skill inventory.

If required references are unavailable, report that the installed plugin is
incomplete rather than reconstructing current IDD rules from memory.

## What This Skill Explains

Answer questions such as:

- what belongs in durable intent and what remains temporary;
- why Git owns history instead of intent documents;
- which IDD skill exists for a particular purpose;
- why one workflow is preferred over another;
- when Factory is useful and when direct execution is sufficient;
- how bug fixes, refactoring, bootstrap, import, structure audits, drift audits,
  linting, and project verification fit the methodology;
- how a current IDD rule should be interpreted;
- what an installed IDD skill is expected to do or not do.
- how `idd-factory-update-effort-models` refreshes the LLMs assigned to Factory
  effort levels while preserving reasoning settings, with persistence owned by
  `idd-factory-configure` and application at the next worker spawn;
- `.idd/verification.yaml`, its `direct`, `subtask`, `checkpoint`, and `final`
  contexts, confirmation checks, user instructions, and missing-policy fallback.

When a question concerns one concrete project, inspect project-owned IDD context
only when it is needed to answer accurately. Prefer `.idd/intent/README.md`,
`.idd/intent/INDEX.md`, and only the relevant current `IDD-NNNN` documents. Keep
that inspection read-only and avoid broad code review or Git-history analysis
unless the user separately requests it.

## Audit and Verification Model

Explain these mechanisms distinctly:

```text
idd-intent-lint
    mechanical validity of .idd/intent/

idd-intent-structure-audit
    semantic structure and ownership of Product Intent

idd-intent-drift-audit
    conformance of implementation to Product Intent for an explicit focused or project-wide scope

.idd/verification.yaml
    project-specific checks that provide implementation evidence
```

For long-lived code that may have drifted from intent, recommend
`idd-intent-drift-audit` and require an explicit focused or project-wide scope.
For split/merge, ownership, duplication, or organization questions, recommend
`idd-intent-structure-audit`. For broken references, duplicate IDs, invalid
metadata, or INDEX consistency, recommend `idd-intent-lint`.

Build, test, lint, analyzer, CI, and repository-specific evidence belongs to
project verification policy and the implementation/Factory workflows that
consume it; do not invent a separate generic verification skill.

## Relationship to `idd-route`

`idd-help` explains IDD and answers questions about how or why the methodology
works. `idd-route` classifies a concrete work request and selects the smallest
safe workflow.

A help answer may name the workflow or skill that would normally apply, but it
must not silently turn an explanatory question into execution.

When a concrete project needs a policy and it is absent, recommend
`idd-verification-configure`. Do not create the file from help.

## Improvement Suggestions and GitHub Issues

Suggestions for improving IDD should be easy to capture and discuss. When the
user has an improvement idea, usability concern, confusing rule, missing
workflow, or other feedback, explain that it can be quickly turned into a
GitHub Issue in the IDD repository:

`https://github.com/DimonSmart/Intent-Driven-Development/issues`

Help the user reduce the idea to a compact issue when useful. Prefer a small
structure containing the observed problem or motivation, the proposed
improvement, a concrete example when available, and the expected behavior.
Do not require a large specification before an idea can be reported.

If the active Coding Agent has GitHub issue tooling, create the issue only when
the user explicitly asks to publish it. Otherwise provide issue-ready text.

## Output Expectations

Answer the user's actual question directly. Prefer concise explanations grounded
in the required references. Name relevant skills or workflow stages when that
helps, and distinguish current methodology rules from recommendations or
possible future improvements.

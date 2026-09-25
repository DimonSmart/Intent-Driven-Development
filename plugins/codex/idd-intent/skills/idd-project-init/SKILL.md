---
name: idd-project-init
description: Initialize `.idd/intent` and the IDD plugin declaration, then pause for a blocking bootstrap decision when an existing implementation has no current intent.
---

# idd-project-init

Use this skill as the only official project initialization workflow for Intent-Driven Development.

## Purpose

Initialize durable product intent storage for a repository that already has the `idd-intent` plugin installed in the user's Coding Agent.

The workflow also makes the repository's use of IDD visible to the active Coding Agent by creating or updating exactly one root instruction file:

```text
Codex        AGENTS.md
Claude Code  CLAUDE.md
```

The agent performing this workflow must edit that file directly. Do not implement this behavior through generator code, a CLI helper, an installation hook, or runtime application code.

This skill does not copy plugins or copy skills into the repository.

## Required Reference

Read `references/project-verification.md` before handling project verification
configuration or fallback. It defines the only supported policy file and its
loading behavior.

For an existing implemented project without current numbered intent, initialization
also offers an optional interactive handoff to `idd-intent-bootstrap`. The
handoff begins only after explicit user consent.

## Behavior

### 1. Resolve the Coding Agent instruction file

Use the repository-root instruction file for the active platform:

- Codex: `AGENTS.md`;
- Claude Code: `CLAUDE.md`.

Create or update only the file for the active Coding Agent. Do not create both files merely for symmetry.

Read the complete existing file before editing it.

### 2. Create project-owned IDD state

Read bootstrap assets from this skill package:

```text
assets/bootstrap/.idd/intent/
```

When the runtime exposes a skill directory or resource URI, resolve the path relative to this `SKILL.md`. If the runtime only exposes packaged resources by reference, use the equivalent resource reference for `assets/bootstrap/.idd/intent/`.

Create only the project-owned IDD state:

```text
.idd/
.idd/intent/
.idd/plugins.json
```

Create minimal bootstrap intent documents when they are missing:

```text
.idd/intent/README.md
.idd/intent/INDEX.md
.idd/intent/_templates/spec.md
.idd/intent/_templates/adr.md
.idd/intent/_templates/spike.md
```

Do not create `.idd/engineering/` during project initialization. Its absence is
the normal default. The installed plugin may contain canonical Engineering
bootstrap definitions for future management workflows, but this skill must not
materialize them.

Write `.idd/plugins.json` as a project declaration, not as a copy of plugin
implementation. `idd-intent` is always required.

Treat Factory as enabled for this initialization only when either:

- the existing project declaration already contains `idd-factory`; or
- the current user request explicitly enables Factory workflows.

Preserve an existing `idd-factory` declaration on idempotent re-runs. Add it
when the user explicitly enables Factory. Otherwise keep only the required
`idd-intent` declaration. For example, a project explicitly using Factory has:

```json
{
  "plugins": [
    "idd-intent",
    "idd-factory"
  ]
}
```

This declaration does not install plugins. Do not infer Factory enablement merely
because the Coding Agent happens to have `idd-factory` installed. Do not create
`.idd/factory` unless Factory workflows are explicitly enabled or actually used.

### 3. Maintain one minimal IDD instruction block

The root Coding Agent instruction file must contain exactly one managed IDD block with this content:

```markdown
<!-- idd:project:start -->
## Intent-Driven Development
This project uses Intent-Driven Development (IDD). Treat `.idd/intent/` as the
current product truth. When `.idd/engineering/` exists, treat it as the current
durable engineering guardrails. When `.idd/verification.yaml` exists, follow it
as the project-owned verification policy. Use the installed IDD skills when
changing intent, implementing behavior, or verifying implementation.
<!-- idd:project:end -->
```

Apply these rules:

- If the instruction file does not exist, create it with only the managed IDD block.
- If a managed IDD block already exists, replace it in place with the canonical block above.
- If more than one managed IDD block exists, keep one canonical block at the position of the first block and remove the duplicates.
- If no managed markers exist but the file already contains clearly IDD-specific instructions, consolidate those instructions into the canonical managed block instead of appending a second IDD section.
- Treat text as clearly IDD-specific when it explicitly names Intent-Driven Development, defines an IDD workflow, or directs the agent to `.idd/intent/`. Do not remove unrelated text merely because it contains the letters `IDD` as part of another term.
- Preserve all unrelated instructions, headings, comments, formatting, and ordering.
- When adding the block to an existing file, append it with a normal blank-line separation unless replacing an existing IDD section in place preserves the document better.
- Do not add detailed workflow documentation, skill catalogs, Factory instructions, implementation plans, or duplicated methodology text to the instruction file.
- Re-running `idd-project-init` must leave the instruction file semantically unchanged and must never create a second IDD section.

### 4. Offer verification configuration

After structural initialization, cheaply inspect only repository technology
markers. If `.idd/verification.yaml` is absent and the user did not explicitly request
structural initialization only, ask: `Configure project-specific verification
rules?` Offer `configure` to hand off to
`idd-verification-configure`, or `use-defaults` to continue with repository or
platform fallback. The latter creates no marker file. Do not repeat this offer
on idempotent initialization and never modify an existing policy without an
explicit request. This decision precedes the optional initial-intent bootstrap.

### 5. Offer Factory model configuration only when Factory is enabled

Do not ask about Factory models for an `idd-intent`-only project.

When Factory is enabled for this initialization and `.idd/execution.yaml` is
absent, offer one blocking choice with this semantic meaning:

```text
How should Factory choose models for worker tasks?

- Use the current model for all tasks
- Configure different models by task complexity
```

Use the current host's native structured interaction when available
(`request_user_input` on Codex, `AskUserQuestion` on Claude Code). Do not
reproduce the tool schema. Require explicit input; do not auto-resolve the
choice.

Handle the answer by handing off to `idd-factory-configure`:

- current model for all tasks -> persist the intentional `modelStrategy: inherit`
  policy;
- different models by complexity -> let the configuration skill propose and
  confirm concrete `economy` / `standard` / `strong` mappings.

If a valid `.idd/execution.yaml` already exists, do not repeat the offer on an
idempotent initialization unless the user explicitly asks to reconfigure it.
The user can run `idd-factory-configure` at any later time.

If Factory was explicitly enabled but the `idd-factory-configure` skill is not
available because the optional Factory plugin is not installed, do not invent a
policy file. Explain that the project declaration does not install the plugin
and leave model configuration pending.

### 6. Offer initial intent bootstrap for existing implementations

After structural initialization, determine whether all of these conditions hold:

- the repository contains meaningful existing implementation rather than only an
  empty scaffold or new-product placeholder;
- no current `IDD-NNNN` documents exist directly under `.idd/intent/`;
- the user did not explicitly request initialization only or forbid project
  analysis;
- the current request has not already supplied a different initial-intent
  workflow.

Use a cheap repository check only. Detect implementation from source roots,
workspace or solution files, executable or library projects, package manifests,
entry points, tests, or equivalent project markers. Do not perform broad
codebase analysis inside `idd-project-init`.

Information supplied with the initialization request, such as a product summary,
technology stack, known project structure, or exclusions, is temporary bootstrap
context. It does not by itself authorize repository discovery or creation of
current `IDD-NNNN` documents. Preserve it for a possible handoff, but still obtain
the blocking bootstrap decision unless the user explicitly requested both
initialization and intent reconstruction.

#### Blocking bootstrap decision

When the conditions hold, the bootstrap offer is a blocking user decision. It is
not a recommendation to mention only in the completion report.

Use the structured user-question tool exposed by the current host:

- Codex: `request_user_input`;
- Claude Code: `AskUserQuestion`.

When either structured tool is available:

1. MUST invoke that tool immediately when this bootstrap decision gate is reached.
2. MUST present the decision through the tool, not as an ordinary assistant
   message or Markdown menu.
3. MUST ask one single-choice question.
4. In Codex, omit `autoResolutionMs`; explicit input is required and the decision
   must not resolve automatically.
5. MUST stop and wait after the tool call.
6. MUST NOT emit the final initialization completion response while the decision
   is unanswered.
7. MUST NOT invoke `idd-intent-bootstrap`, inspect the codebase broadly, or create
   current `IDD-NNNN` documents before an affirmative answer.

Describe the question semantically. Do not reproduce or invent the host tool's
JSON schema in this skill; the runtime supplies that schema and the model forms
the actual tool call.

Use this decision definition:

- decision key: `initial_intent_bootstrap`;
- short header: `Bootstrap`;
- question: `Analyze this existing project and propose its initial IDD intent documentation?`;
- single-choice options:
  - `whole_repository` — **Whole repository (Recommended)**: analyze all detected
    current product areas;
  - `select_areas` — **Select areas**: choose product roots and exclusions before
    analysis;
  - `skip` — **Skip for now**: finish initialization without reconstructing
    product intent.

Handle the selected value as follows:

- `whole_repository`: invoke `idd-intent-bootstrap` in the same request with
  whole-repository scope and pass all temporary context from the initialization
  request.
- `select_areas`: obtain include roots, exclude roots, semantic product areas,
  and optional temporary context through another blocking input request when the
  available interaction tool supports free-form input; otherwise ask one concise
  plain-text question and end the turn. Then invoke `idd-intent-bootstrap` with
  the selected scope.
- `skip`: finish initialization without semantic intent changes and mention that
  `idd-intent-bootstrap` can be run manually later.

If neither `request_user_input` nor `AskUserQuestion` is available:

1. State only that structural IDD initialization is complete.
2. Ask one concise blocking plain-text question:
   `Should I now analyze the existing implementation and reconstruct its initial product intent?`
3. End the turn immediately after the question.
4. Do not print a textual multiple-choice menu.
5. Do not reduce the question to a generic "next step" recommendation.
6. Do not claim the complete initialization workflow has finished while the
   bootstrap decision is still pending.
7. After an affirmative answer, ask for whole-repository versus selected-area
   scope only when the scope is not already clear, then hand off to
   `idd-intent-bootstrap`.

The question is an offer, not permission to infer or write product intent.
`idd-project-init` itself must not create, update, or delete current
`IDD-NNNN` documents.

Do not offer bootstrap when:

- the repository is empty or represents a new product;
- current numbered intent already exists;
- the user explicitly requested initialization only;
- the user already chose `idd-intent-import`, `idd-intent-brainstorm`, or another
  explicit initial-intent workflow;
- bootstrap was already offered and declined during the current initialization.

If the user accepts, pass the original request, detected repository scope, user
guidance, and explicit include or exclude boundaries to
`idd-intent-bootstrap`. Do not summarize implementation findings as established
product truth during the handoff.

## Rules

- Copy bootstrap files from `assets/bootstrap/.idd/intent/` without semantic rewriting.
- Never replace an existing project file wholesale. Initialization authorizes only adding missing bootstrap files, normalizing `.idd/plugins.json`, and creating or updating the single managed IDD block in the active Coding Agent instruction file.
- Do not create agent-specific skill directories in the user project.
- Do not copy plugin skills into the user project.
- Do not create generated plugin delivery artifacts. The root `AGENTS.md` or `CLAUDE.md` instruction file is project-owned and is intentionally maintained by the agent.
- Do not implement instruction-file installation through program code.
- Do not say that `.idd/plugins.json` installs plugins. It is a project-level IDD declaration for people and IDD workflows.
- Do not create `.idd/factory` unless Factory work is explicitly requested.
- Do not create `.idd/engineering` by default and do not add a separate
  Engineering plugin declaration to `.idd/plugins.json`.
- Product intent lives only under `.idd/intent`. Optional durable
  implementation guardrails live separately under `.idd/engineering`.
- Factory working data, when used, is temporary and belongs under `.idd/factory`.
- `.idd/execution.yaml`, when present, is persistent project-owned Factory
  execution policy; it is not Product Intent and does not belong under
  `.idd/factory/current/`.
- Optional bootstrap discovery remains a separate skill with a separate semantic
  confirmation gate.
- Declining bootstrap is a successful initialization result.
- Re-running initialization must not trigger semantic rewrites of existing intent.

## Existing Projects

When `.idd/intent` already exists, preserve existing documents. Add only missing bootstrap files.

Always inspect and normalize the active Coding Agent instruction file so that it contains exactly one minimal managed IDD block while preserving unrelated instructions.

Normalize legacy declarations as follows:

- replace `idd` with `idd-intent`;
- replace `idd-core` with `idd-intent`;
- preserve `idd-factory` only when it is already declared or the user explicitly enables it;
- remove duplicate plugin names.

A project using only durable product memory should contain:

```json
{
  "plugins": [
    "idd-intent"
  ]
}
```

A project that explicitly uses Factory may contain:

```json
{
  "plugins": [
    "idd-intent",
    "idd-factory"
  ]
}
```

Do not otherwise rewrite existing intent documents during initialization.

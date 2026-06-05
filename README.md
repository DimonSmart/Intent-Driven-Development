# Intent-Driven Development

Intent-Driven Development is a practical way to use AI coding agents without
turning the project into a pile of disconnected prompts, generated plans, and
tool-specific instruction files.

The idea is simple:

> Keep the product intent in one living specification.
> Generate agent-specific instructions from that source.
> Let AI help with implementation, but keep engineering judgment in the loop.

This is close to Spec-Driven Development, but with a different emphasis.

The specification is not a magic executable artifact. It does not replace
architecture, code review, testing, or human responsibility. It is a stable
description of what the product should become.

## Why This Exists

AI coding agents are useful, but they have a weak memory model.

A chat can contain the right decision today and lose it tomorrow. One agent can
know the project rules while another agent sees only a local prompt. A generated
instruction file can drift from the real product intent. After several
iterations, nobody is completely sure which file is the source of truth.

Intent-Driven Development fixes that by separating three things:

```text
product intent      durable product knowledge
agent instructions  generated target formats
implementation      code, tests, scripts, and concrete changes
```

The important part is the first one.

Product intent should survive tool changes, agent changes, and implementation
attempts.

## How It Differs from Spec-Driven Development

Spec-Driven Development has a good core idea: describe what should be built
before asking an AI agent to build it.

The problem starts when the specification becomes too many things at once:

```text
- product description
- task tracker
- implementation plan
- generated checklist
- temporary chat memory
- tool-specific command input
```

That works for small demos. It becomes noisy in a real project.

Intent-Driven Development keeps the useful part and removes the rest.

| Spec-Driven Development                                   | Intent-Driven Development                                     |
| --------------------------------------------------------- | ------------------------------------------------------------- |
| The spec often drives a feature workflow                  | The spec describes the target product state                   |
| Tasks may become part of the spec flow                    | Tasks are temporary and should not become product memory      |
| The workflow is often tied to one agent or command system | Agents are target formats generated from one canonical source |
| Generated plans can look authoritative                    | Engineering judgment stays explicit                          |
| The spec can become a process artifact                    | The spec remains product knowledge                            |

This is why the method is called Intent-Driven Development.

The point is not to worship the spec. The point is to keep the intent stable.

## Core Idea

A specification should answer one question:

> If we delete the implementation, can we rebuild the product from these files?

If the answer is yes, the specification is useful.

If the answer is no, it is probably just a task list, a note, or a chat summary.

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
- generated agent output
- chat history
- duplicated instruction files
```

A task says what to do next.

A specification says what should remain true after the task is done.

## How Agents Fit In

Codex, Claude, Gemini, GitHub Copilot, and other AI coding agents have different
instruction formats. That is a tooling detail.

The project should not make one agent's format the source of truth.

IDD keeps canonical methodology and project rules in source files, then
generates agent-specific output from them.

```text
canonical source -> adapters -> generated agent files
```

The generated files are useful. They are just not authoritative.

If something important changes, update the canonical source and regenerate the
target files.

## Repository Layout

```text
src/canonical/      canonical methodology, project files, skills, and packs
src/adapters/       target-specific entry points and skill front matter
generated/          generated files for each AI coding agent system
tools/generate/     C# generator
tools/smoke-tests/  smoke tests for generated output
scripts/            local check and release helper scripts
```

Edit files under `src/canonical/` and `src/adapters/`.

Then run:

```powershell
.\scripts\Check.ps1
```

The `generated/` directory is intentionally ignored by git.

It is reproducible output from the canonical source and adapters. Do not edit it
as product knowledge.

## Workflow

The usual workflow is:

```text
1. Update canonical methodology or adapters.
2. Run the local check.
3. Review generated output.
4. Use generated files with the target AI coding agent.
5. Keep durable decisions in canonical specs, not in chat.
```

In practice, this means:

```powershell
.\scripts\Check.ps1
```

The check should prove that the generated agent files are still reproducible and
valid enough to use.

## What This Method Optimizes For

IDD is useful when the project has:

```text
- more than one AI coding agent
- long-lived product rules
- repeated implementation sessions
- architectural constraints that should not be rediscovered every time
- generated agent instructions
- a need to keep project knowledge outside chat history
```

It is less useful for one-off experiments where the code will be thrown away.

## Non-Goals

IDD deliberately does not try to do several things.

```text
- Do not turn specifications into a task tracker.
- Do not store agent-specific instruction copies as the source of truth.
- Do not build Claude or Gemini instructions on top of Codex AGENTS.md.
- Do not update CopilotInstructions as a canonical source.
- Do not create a pull request back to CopilotInstructions.
- Do not use legacy terminology in canonical methodology or skills.
```

The method is intentionally boring here.

There should be one canonical source. Everything else is generated, adapted, or
temporary.

Migration notes from the older project model are in
`src/canonical/methodology/migration-from-copilotinstructions.md`.

## Release

Pull requests and pushes to `main` run:

```text
.github/workflows/idd-smoke.yml
```

Release publication follows the tag-based flow:

```powershell
.\publish-next-version.ps1
```

The script runs the local check, creates the next `vMAJOR.MINOR.PATCH` tag, and
pushes it.

Then `.github/workflows/publish-package.yml` packs
`DimonSmart.IntentDrivenDevelopment`, creates a GitHub Release, and publishes
the package to NuGet.

## Summary

Intent-Driven Development is Spec-Guided Dev without turning the spec into a
task tracker or an AI command script.

The specification is the product memory.

The adapters are translation layers.

The generated files are delivery formats for specific agents.

The engineer still owns the result.

# Intent-Driven Development

Intent-Driven Development is an AI-assisted development method where a living
specification guides implementation without replacing engineering judgment.

In AI development, the key skill is no longer just writing code, but describing
intent precisely enough that both humans and AI agents can act on it.

## Intent

Intent is a stable statement of what future implementations must preserve.

Intent describes what should remain true about the product after tasks,
refactorings, bug fixes, agent sessions, and tool changes.

Intent is not the current task, current implementation shape, temporary chat
memory, generated agent output, or local development status.

Examples:

- "Users authenticate with email and password" is intent.
- "Users may enable OTP as a second authentication step" is intent.
- "Add the login page tomorrow" is a task.
- "LoginForm.tsx currently uses React Hook Form" is an implementation detail
  unless that library choice is an accepted product-defining constraint.
- "The agent generated a checklist for login implementation" is generated
  output, not intent.
- "The current implementation forgot password reset" is an implementation gap,
  not intent by itself.

## Specification

A specification is a durable description of the product.

If the implementation is deleted, but the specifications remain, it should be
possible to rebuild the product from the specifications.

Specifications include:

- product behavior;
- domain contracts;
- architectural shape;
- important implementation patterns;
- important library/framework choices when they define the product;
- compatibility expectations;
- non-goals;
- acceptance criteria;
- verification rules;
- shared behavior.

Specifications do not include:

- local tasks;
- temporary implementation status;
- ordinary dependency updates;
- formatting;
- small refactoring;
- generated agent output;
- current implementation gaps.

## Decision Flow

Before changing `.specs/`, decide:

1. Does the change affect future product behavior, domain contracts, accepted
   architecture, compatibility, non-goals, acceptance criteria, or verification
   rules?
2. If yes, update the smallest relevant current specification or ADR.
3. If the change is only a task, temporary status, formatting, refactoring, or
   incidental implementation detail, do not update `.specs/`.
4. If implementation and specification disagree, do not assume the
   implementation is the new intent.
5. If intent is unclear, keep the uncertainty visible and create a spike or ask
   for confirmation.

## Project Directory

IDD projects use `.specs/` for current normative product intent:

```text
.specs/
  README.md
  INDEX.md
  _templates/
    spec.md
    adr.md
    spike.md
  archive/
```

Use these meanings:

```text
.specs/              current normative product intent
.specs/archive/      old normative product intent
```

Small product-neutral changes belong in commit messages, not in `.specs/`.

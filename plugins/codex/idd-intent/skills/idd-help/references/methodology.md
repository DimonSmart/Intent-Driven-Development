# Intent-Driven Development

Intent-Driven Development is an AI-assisted development method where a living
specification guides implementation without replacing engineering judgment.

## CodingAgent Routing

Use IDD skills when a request involves durable product intent, implementation
based on current intent, conformance checking, or Factory orchestration. Missing
`invocation` means automatic routing; `invocation: "manual"` means user-invoked
only. `idd-skip` is manual-only because automatic selection would defeat its
purpose, `idd-help` is manual-only because explanation must not become an
automatic pre-step, and `idd-glossary-build` is manual-only because encountering
terminology must not trigger glossary maintenance.

Use `idd-code-implement` for one focused implementation change covered by
current intent. Use Factory workflows for temporary multi-task planning,
sequencing, or coordinated execution across bounded tasks. Factory
may be selected automatically for those conditions, but it never becomes
product intent and must stop when current intent is missing or insufficient.

For Factory terminology, a Request is the original user instruction that
defines one complete Task. A Factory planner repeatedly decomposes that Task
into bounded ordered batches. Executors complete one task each, and strict
runtime-owned final verification validates the integrated result.

Guiding rule:

> Specifications should be complete enough to rebuild the product from scratch,
> and strict enough not to become a task tracker.

In AI development, the key skill is no longer just writing code, but describing
intent precisely enough that both humans and CodingAgents can act on it.

## Intent

Intent is a stable statement of what future implementations must preserve.

Intent describes what should remain true about the product after tasks,
refactorings, bug fixes, CodingAgent sessions, and tool changes.

Intent is not the current task, current implementation shape, temporary chat
memory, generated CodingAgent output, or local development status.

Examples:

- "Users authenticate with email and password" is intent.
- "Users may enable OTP as a second authentication step" is intent.
- "Add the login page tomorrow" is a task.
- "LoginForm.tsx currently uses React Hook Form" is an implementation detail
  unless that library choice is an accepted product-defining constraint.
- "The CodingAgent generated a checklist for login implementation" is generated
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
- durable architecture boundaries and important technical constraints;
- architecture decisions that define product properties;
- framework or library choices only when they define compatibility, public
  contracts, security, operability, or an accepted architecture decision;
- compatibility expectations;
- non-goals;
- acceptance criteria;
- verification rules that state the required evidence, scenario, and property;
- shared behavior.

Specifications do not include:

- local tasks;
- temporary implementation status;
- ordinary dependency updates;
- formatting;
- small refactoring;
- generated CodingAgent output;
- current implementation gaps.

Specifications also do not include private type or method names, source files,
constructor signatures, dependency-injection wiring, implementation order,
migration mechanics, temporary workarounds, build or test commands, one-off
source scans, test locations, or progress status.

## Optional Project Glossary

A project may optionally keep `.idd/intent/GLOSSARY.md` as a small shared
vocabulary support file.

> The glossary contains not all project terms, but only terms whose incorrect
> interpretation could change the understanding of product intent.

The glossary is appropriate when a familiar word has a project-specific meaning,
several names denote the same concept, similar concepts must be distinguished,
or translations and legacy terms create a material ambiguity risk. Ordinary
technical and domain terms used in their ordinary meaning do not belong there.

The file is absent by default. Its absence is valid and means that the project
does not use a managed glossary. `idd-project-init` does not create an empty
file. `idd-intent-bootstrap` and `idd-intent-import` may identify material
candidates, but they must ask for explicit consent and hand off to
`idd-glossary-build`; they do not write the glossary themselves.

`GLOSSARY.md`:

- is not an IDD document type;
- has no `IDD-NNNN` identifier;
- is not listed in `INDEX.md`;
- is created or changed only by the manual-only `idd-glossary-build` workflow;
- is read by other skills only when its terminology is relevant;
- defines vocabulary, not product behavior.

Each entry contains a canonical term, a short definition, and optionally
`Aliases`. Aliases may include synonyms, legacy names, abbreviations, spelling
variants, transliterations, and equivalent names in other languages. The entry
heading remains the canonical project term, and every alias must denote the same
concept.

The ownership boundary is:

```text
What does Aspect mean?          -> GLOSSARY.md
How must the system use Aspect? -> spec
Why was this model chosen?      -> ADR when the decision is durable
```

Git history stores glossary revisions just as it stores specification revisions.

## Durable Constraint vs Implementation Detail

Ask: **Would a different correct implementation still be allowed?** If yes, the
specific detail is probably not intent.

Durable technical constraints, durable architecture boundaries, and verification
rules describe what future implementations must preserve. Current code structure
and local execution mechanics do not.

Durable constraint:

```text
The comparison engine must process large files with bounded memory use.
```

Implementation detail:

```text
The engine must allocate a 64 KB byte array in CompareAsync().
```

Durable architecture boundary:

```text
Application-owned UI rendering must be coordinated through one composition
lifecycle so overlays and underlying surfaces redraw consistently.
```

Implementation detail:

```text
UiCompositionHost must be instantiated in Bootstrap.cs and passed through a
ModalDialogHost constructor.
```

Durable verification rule:

```text
Automated tests must cover both viewport growth and shrink while nested modals
are visible.
```

Process command:

```text
Run dotnet test.
```

Build and test commands belong in developer documentation, CI, task
instructions, or PR workflow; they are not product intent. Test names and test
locations may change without changing intent. A Verification section states the
evidence category, scenario, and required property.

## Decision Flow

Before changing `.idd/intent/`, decide:

1. Does the change affect future product behavior, domain contracts, accepted
   architecture, compatibility, non-goals, acceptance criteria, or verification
   rules?
2. If yes, update the smallest relevant current specification or ADR.
3. If the change is only a task, temporary status, formatting, refactoring, or
   incidental implementation detail, do not update `.idd/intent/`.
4. If implementation and specification disagree, do not assume the
   implementation is the new intent.
5. If intent is unclear, keep the uncertainty visible and create a spike or ask
   for confirmation.
6. If the request is only to establish project-specific terminology, use the
   explicit `idd-glossary-build` workflow rather than treating vocabulary as a
   product change.

## Project Directory

IDD projects use `.idd/intent/` for current product intent and current
decision/research records, with an optional project glossary:

```text
.idd/intent/
  README.md
  INDEX.md
  GLOSSARY.md        optional, created only by idd-glossary-build
  _templates/
    spec.md
    adr.md
    spike.md
```

Use these meanings:

```text
.idd/intent/              current product intent, ADRs, active spikes,
                          and optional glossary support
```

Small product-neutral changes belong in commit messages, not in `.idd/intent/`.

## Document Lifecycle

`.idd/intent/` contains only the current working model of product intent, current
decision/research records, and the optional current glossary.

Git is the only history mechanism. Do not preserve obsolete specs in an
archive directory.

When product intent evolves within the same product area, update the existing
spec directly.

When a product area is replaced by a substantially different product area,
delete the old spec and create a new owning spec.

When a document is obsolete, duplicated, task-like, process-only, incorrect, or
no longer useful as current product intent, delete it.

ADRs are decision records. They are not archived when superseded. If a durable
decision changes, keep the old ADR in place, mark it as `Superseded`, and
create a new ADR that replaces or supersedes it.

Spikes are research records. When a spike is resolved, either convert its
outcome into a spec or ADR and delete the spike, or keep the spike only if it
is still useful as active research.

The glossary is edited in place only through explicit `idd-glossary-build` work.
Delete it when an explicitly approved update removes its final entry.

Deleted documents remain available through Git history.

### Spec Lifecycle

A spec document has no lifecycle status. Its presence in the current intent
directory means that it is current. Do not mark a spec as Current, Completed,
Deprecated, Retired, or Superseded.

When intent changes within the same product area, edit the owning spec in place.
When a spec becomes obsolete or is absorbed by another document, migrate any
remaining current intent and delete the obsolete spec. Git history is the only
history of spec revisions.

ADR status is part of the decision record lifecycle and does not apply to specs.
A spike remains only while the question is active.

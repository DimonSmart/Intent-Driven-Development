---
name: idd-glossary-build
description: Manual-only workflow to create or deliberately update the optional project glossary with only terminology whose incorrect interpretation could change the understanding of product intent.
---

# idd-glossary-build

Use this manual-only skill to create or deliberately update the optional project
terminology glossary at `.idd/intent/GLOSSARY.md`.

```text
idd-glossary-build =
    explicit user request
    + focused terminology discovery
    + strict ambiguity filter
    + explicit proposal approval
    + minimal glossary write
```

## Invocation Boundary

Run this skill only when one of these conditions is true:

- the user explicitly invokes `idd-glossary-build`;
- the user explicitly asks to create, build, rebuild, review, or update the
  project glossary;
- the user explicitly accepts a glossary offer made by
  `idd-intent-bootstrap` or `idd-intent-import`.

Do not infer glossary work from ordinary product changes, new terminology in a
request, unfamiliar code identifiers, or the mere presence of domain language.
Do not run this skill as an automatic post-step of another IDD workflow.

If `.idd/intent/GLOSSARY.md` does not exist, the project does not use a managed
project glossary. Other skills must not create one implicitly.

## Core Inclusion Rule

> The glossary contains not all project terms, but only terms whose incorrect
> interpretation could change the understanding of product intent.

A candidate belongs in the glossary only when at least one concrete ambiguity
risk exists, such as:

- several names are used for the same project concept;
- one familiar word has a project-specific meaning;
- two similar concepts must be distinguished to interpret intent correctly;
- a translation, abbreviation, legacy name, or spelling variant may be mistaken
  for a different concept;
- plausible interpretations would lead to different product behavior,
  contracts, constraints, or implementation decisions.

Exclude:

- ordinary technical terms used in their ordinary meaning;
- ordinary domain terms whose meaning is already clear to a competent reader;
- private class, method, file, variable, or configuration names;
- task-local wording, temporary labels, and implementation status;
- terms added only because they occur frequently;
- terms whose explanation would merely repeat a specification sentence.

For every proposed entry, answer:

```text
What likely misunderstanding does this entry prevent?
```

If there is no concrete answer, omit the entry.

## Ownership Boundary

`GLOSSARY.md` defines shared project vocabulary. It does not own product
behavior.

```text
What does Aspect mean?          -> GLOSSARY.md
How must the system use Aspect? -> an IDD-NNNN spec
Why was this model chosen?      -> an ADR when the decision is durable
```

Do not place requirements, acceptance criteria, verification rules,
architecture decisions, tasks, or implementation instructions in the glossary.
Do not rewrite numbered intent documents, source code, identifiers, or user
documentation from this skill.

When the request also requires terminology consolidation elsewhere:

- use `idd-intent-normalize-current` for focused meaning-preserving wording
  normalization in current intent;
- use `idd-intent-change` when the terminology change changes product meaning;
- use normal implementation work for code or public API renaming.

## Inputs

Accept natural-language input. Useful explicit scope includes:

- named candidate terms;
- selected product areas or `IDD-NNNN` documents;
- source documentation to inspect;
- known competing names or translations;
- whether the user wants creation, a focused update, or an explicit full review.

Do not scan the whole repository when the request names a sufficient focused
scope. Do not treat codebase-wide vocabulary extraction as the default.

## Required Reading

1. Read `.idd/intent/README.md` and `.idd/intent/INDEX.md`.
2. Read `.idd/intent/GLOSSARY.md` when it exists.
3. Read only the relevant current `IDD-NNNN` documents and explicitly supplied
   terminology sources.
4. Inspect implementation only when the user explicitly includes it as evidence
   and the terminology cannot be understood from current intent or source
   documentation.
5. Do not inspect Git history unless the user explicitly asks to investigate
   legacy terminology.

## Entry Format

The file heading is:

```md
# Project Glossary
```

Use one second-level heading per canonical project term:

```md
## Aspect

A distinct perspective within a Topic used to create diversity between
planned tickets.

- Aliases: Subtopic, Подтема, Аспект
```

Each entry has exactly:

- a canonical term in the `##` heading;
- one short definition;
- optionally one `Aliases` line.

`Aliases` may contain synonyms, legacy names, abbreviations, spelling variants,
transliterations, and equivalent names in other languages. The heading remains
the canonical project term. Every alias must denote the same concept; do not use
aliases to merge distinct meanings.

Do not add mandatory fields such as owner, status, rationale, examples, history,
source, or related specifications. Add explanatory prose only when it is needed
to distinguish the concept, and keep the definition short.

## Proposal And Confirmation

Before writing, present a compact proposal containing only accepted candidates:

```text
canonical term
definition
aliases, if any
misunderstanding prevented
```

Use the structured user-question tool exposed by the current host when
available. Ask for explicit approval, revision, or cancellation of the proposed
entry set. Do not auto-resolve the decision and do not write while approval is
pending.

When no structured question tool is available, ask one concise plain-text
approval question and end the turn. Do not convert silence into approval.

If no candidate survives the strict inclusion rule, report that no glossary is
needed for the inspected scope and do not create an empty file.

## Write Rules

After approval:

- create `GLOSSARY.md` only when at least one approved entry exists;
- update an existing glossary in place;
- preserve unrelated existing entries;
- replace or remove entries only when the user approved that change;
- keep canonical terms unique, case-insensitively;
- do not assign an `IDD-NNNN` identifier;
- do not add the glossary to `INDEX.md`;
- use Git history as the history of glossary revisions;
- run or simulate the glossary checks in `idd-intent-lint`.

If the approved result removes the final entry, delete `GLOSSARY.md` rather than
keeping an empty managed glossary.

## Output

Report:

```md
# IDD Glossary Build

Result: created | updated | deleted | unchanged
Glossary: .idd/intent/GLOSSARY.md

## Entries added
## Entries changed
## Entries removed
## Candidates rejected
## Follow-up terminology work
```

For rejected candidates, give only the short reason, such as `ordinary meaning`,
`implementation identifier`, `task-local term`, or `no material ambiguity`.

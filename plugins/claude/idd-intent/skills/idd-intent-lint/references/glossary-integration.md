# Optional Glossary Integration

This reference defines how project initialization, initial intent bootstrap, and
intent import interact with the optional `.idd/intent/GLOSSARY.md` support file.

## Shared Rule

> The glossary contains not all project terms, but only terms whose incorrect
> interpretation could change the understanding of product intent.

The glossary is optional. Its absence means that the project does not use a
managed glossary. Absence is not an error, warning, incomplete initialization,
or reason to create an empty file.

Only `idd-glossary-build` creates or changes `GLOSSARY.md`.

## Project Initialization

`idd-project-init` must not:

- create an empty `GLOSSARY.md`;
- add a glossary template to project bootstrap files;
- ask whether every initialized project needs a glossary;
- treat glossary absence as incomplete setup.

Initialization may preserve an existing glossary but must not rewrite it.

## Bootstrap And Import Discovery

`idd-intent-bootstrap` and `idd-intent-import` may identify glossary candidates
while performing their primary workflow, but candidate discovery must remain
rare and secondary.

A candidate is material only when there is concrete evidence that:

- several terms denote the same project concept;
- a familiar term has a project-specific meaning;
- two similar concepts must be distinguished to interpret current intent;
- a translation, abbreviation, legacy term, or spelling variant creates a real
  ambiguity risk;
- different plausible interpretations could change understood product behavior,
  contracts, constraints, or accepted architecture.

Do not collect ordinary technical vocabulary, ordinary domain terminology,
private identifiers, every noun in source documents, or frequently used words
without a concrete ambiguity risk.

Track candidates as temporary workflow evidence. Do not write them into specs,
ADRs, spikes, import reports, discovery reports under `.idd/intent/`, or an
implicitly created glossary.

## Offer And Handoff

When no material candidates exist, do not mention or offer glossary creation.

When material candidates exist:

1. Show a compact candidate list with the proposed canonical term, short
   definition, optional aliases, and the misunderstanding each entry prevents.
2. Complete the primary bootstrap or import semantic gates independently.
3. Ask one explicit optional decision using the owning skill's structured user
   input protocol:
   - build or update the glossary from these candidates;
   - skip glossary work.
4. Skipping glossary work must not fail or invalidate bootstrap or import.
5. On affirmative consent, hand off the approved candidates, relevant evidence,
   scope, and existing glossary state to `idd-glossary-build`.
6. Do not create or edit `GLOSSARY.md` directly from bootstrap or import.

For a proposal-only import, report candidates as an optional follow-up but do not
start a write workflow that exceeds the requested scope.

If `GLOSSARY.md` already exists, use the same explicit decision before proposing
additions, changed definitions, aliases, or removals. The existing file is never
authorization for continuous automatic maintenance.

## Alias Meaning

The optional `Aliases` field may include synonyms, legacy names, abbreviations,
spelling variants, transliterations, and equivalent names in other languages.
The entry heading remains the canonical project term. Every alias must refer to
the same concept.

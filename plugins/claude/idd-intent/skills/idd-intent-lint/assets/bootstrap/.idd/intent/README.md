# IDD Intent

This directory contains the current working model of product intent and current
decision/research records.

Read `INDEX.md` first. If `GLOSSARY.md` exists and project-specific terminology
may affect the current work, read its relevant entries. Then read the current
`IDD-NNNN` documents directly under this directory that apply to the change.

Every intent document uses a stable `IDD-NNNN` identifier and the canonical
`IDD-NNNN.type-short-title.md` filename. Its first Markdown heading starts with
the same identifier and document type. Bare numeric document identifiers are not
valid.

Current numbered documents may be specs, ADRs, or active spikes.

`GLOSSARY.md` is an optional unnumbered project vocabulary support file. It is
created only through explicit `idd-glossary-build` work. Its absence is valid,
it is not listed in `INDEX.md`, and it does not own product behavior.

Do not treat templates, support files, generated reports, or deleted Git history
as current product intent.

There is no `.idd/intent` archive lifecycle. Deleted or previous document
versions are available through Git history.

A spec document has no lifecycle status: its presence here means it is current.
Do not mark specs as Current, Completed, Deprecated, Retired, or Superseded.
Edit an owning spec in place or migrate its remaining current intent and delete
it. ADR status remains part of ADR decision records; a spike remains only while
its question is active.

## Intent and Engineering boundary

Product behavior, externally observable properties, public/domain/compatibility
contracts, and product-significant operational constraints belong here.

Implementation-only durable constraints used to preserve architecture,
consistency, maintainability, or engineering conventions belong in the optional
`.idd/engineering/` layer.

A useful test is: if a different implementation could fully preserve the product
contract but would still be forbidden by the rule, that rule usually belongs in
Engineering.

Shared/cross-cutting product behavior still belongs in Intent. Use `Area`,
`Notes`, or equivalent INDEX discovery metadata to make such intent visible to
planners without requiring a schema migration.

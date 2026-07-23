# IDD Intent

This directory contains the current working model of product intent and current
decision/research records.

Read `INDEX.md` first, then read the current `IDD-NNNN` documents directly under
this directory that apply to the change.

Every intent document uses a stable `IDD-NNNN` identifier and the canonical
`IDD-NNNN.type-short-title.md` filename. Its first Markdown heading starts with
the same identifier and document type. Bare numeric document identifiers are not
valid.

Current documents may be specs, ADRs, or active spikes.

Do not treat templates, support files, generated reports, or deleted Git history
as current product intent.

There is no `.idd/intent` archive lifecycle. Deleted or previous document
versions are available through Git history.

A spec document has no lifecycle status: its presence here means it is current.
Do not mark specs as Current, Completed, Deprecated, Retired, or Superseded.
Edit an owning spec in place or migrate its remaining current intent and delete
it. ADR status remains part of ADR decision records; a spike remains only while
its question is active.

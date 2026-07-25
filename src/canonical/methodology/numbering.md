# Document IDs and Numbering

## Why intent documents are numbered

Projects evolve. At the beginning, the full product intent is rarely known.

A namespaced document ID gives each piece of intent a stable and unambiguous
identity while the system changes. Titles and filenames may change, documents
may be deleted or replaced by a new owner, but references by ID remain stable.

The sequence records document creation order without turning specifications
into task logs.

Use one increasing numeric sequence across intent documents directly under
`.idd/intent/`. Every document ID starts with the `IDD-` namespace:

```text
IDD-NNNN
```

The canonical filename is:

```text
.idd/intent/IDD-NNNN.type-short-title.md
```

Examples:

```text
.idd/intent/IDD-0001.spec-initial-product-model.md
.idd/intent/IDD-0002.adr-rendering-architecture.md
.idd/intent/IDD-0003.spike-input-layer-feasibility.md
```

The first Markdown heading must start with the same identifier and document type
as the filename, for example:

```md
# IDD-0001.spec-initial-product-model
```

Use `IDD-NNNN` in normative relations and prose references. A bare four-digit
number such as `0019` is not an IDD document identifier.

## Index references

`INDEX.md` identifies current intent documents by their stable `IDD-NNNN`
identifier, not by filename. The `Document` column must contain exactly the
stable identifier as plain text.

For example:

```md
| Document | Role | Area | Notes | Replaces |
| --- | --- | --- | --- | --- |
| IDD-0001 | Spec | Agentic chat | ... | — |
```

Do not put the canonical filename, a file path, or a Markdown link in the
`Document` column. A document ID resolves to the unique current file matching
`.idd/intent/IDD-NNNN.*.md`. The filename is storage representation and may
change without changing document identity.

When finding the next number, inspect current `IDD-NNNN.type-short-title.md`
files and previously assigned `IDD-NNNN` identifiers in Git history, then use
the maximum `NNNN` value plus one. Do not scan or create an archive directory.
Deleted document numbers are never reused. Do not include lifecycle markers such
as `active` or `retired` in filenames.
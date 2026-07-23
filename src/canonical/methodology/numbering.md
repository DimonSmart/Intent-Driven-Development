# Document IDs and Numbering

## Why intent documents are numbered

Projects evolve. At the beginning, the full product intent is rarely known.

A namespaced document ID gives each piece of intent a stable and unambiguous
identity while the system changes. Titles and filenames may change, documents
may be deleted or replaced by a new owner, but references by ID remain stable.

The sequence records document creation order without turning specifications
into task logs.

Use one increasing numeric sequence across current intent documents directly
under `.idd/intent/`. Every document ID starts with the `IDD-` namespace:

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

Use `IDD-NNNN` in normative relations and prose references. A bare four-digit
number such as `0019` is not an IDD document identifier.

When finding the next number, scan current files directly under `.idd/intent/`
whose names match the `IDD-NNNN.type-short-title.md` convention, then use the
maximum `NNNN` value plus one. Do not scan or create an archive directory.
Deleted document numbers are not reused. Do not include lifecycle markers such
as `active` or `retired` in filenames.

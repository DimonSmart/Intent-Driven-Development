# Numbering

## Why specifications are numbered

Projects evolve. At the beginning, the full product intent is rarely known.

Numbered specifications give each piece of intent a stable identity while the
system changes. Titles and file names may change, documents may be deleted or
replaced by a new owner, but references by number remain stable.

The sequence records document creation order without turning specifications
into task logs.

Use one increasing numeric sequence across current numbered documents directly
under `.specs/`:

```text
.specs/NNNN.type-short-title.md
```

Examples:

```text
.specs/0001.spec-initial-product-model.md
.specs/0002.adr-rendering-architecture.md
.specs/0003.spike-input-layer-feasibility.md
```

When finding the next number, scan current numbered files directly under
`.specs/`, then use the maximum `NNNN` prefix plus one. Do not scan or create
an archive directory. Deleted document numbers are not reused. Do not include
lifecycle markers such as `active` or `retired` in file names.

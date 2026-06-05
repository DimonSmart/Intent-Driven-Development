# Numbering

## Why specifications are numbered

Projects evolve. At the beginning, the full product intent is rarely known.

Numbered specifications give each piece of intent a stable identity while the
system changes. Titles and file names may change, documents may be archived or
replaced, but references by number remain stable.

The sequence records how product understanding evolved without turning
specifications into task logs.

Use one increasing numeric sequence across current and archived documents:

```text
.specs/NNNN.type-short-title.md
.specs/archive/NNNN.type-short-title.md
```

Examples:

```text
.specs/0001.spec-initial-product-model.md
.specs/0002.adr-rendering-architecture.md
.specs/0003.spike-input-layer-feasibility.md
.specs/archive/0004.spec-old-dialog-model.md
```

When finding the next number, scan numbered files in `.specs/` and
`.specs/archive/`, then use the maximum `NNNN` prefix plus one. Do not include
lifecycle markers such as `active` or `retired` in file names.

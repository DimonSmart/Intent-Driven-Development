# Intent-Driven Development

Intent-Driven Development is an AI-assisted development method where a living
specification guides implementation without replacing engineering judgment.

In AI development, the key skill is no longer just writing code, but describing
intent precisely enough that both humans and AI agents can act on it.

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
- current implementation gaps.

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

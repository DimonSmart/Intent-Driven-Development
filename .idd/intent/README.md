# IDD Intent

This directory contains the current normative product intent for Intent-Driven Development itself. Read `INDEX.md` first, then only the relevant `IDD-NNNN` documents.

Numbered documents are current specs, ADRs, or active spikes. Git is the only history mechanism; there is no intent archive. `GLOSSARY.md`, when present, is an optional unnumbered vocabulary file containing only terms whose ambiguous interpretation could materially change understanding of product intent.

## Self-hosting warning

This repository both defines IDD and uses IDD to describe itself. Keep the authority layers distinct:

- `.idd/intent/` describes the durable intent of the IDD product;
- `src/canonical/` is the canonical implementation/source material used to generate platform plugins;
- generated plugin and marketplace artifacts are derived output, not a second source of product intent.

When IDD itself changes, decide once whether durable product intent changed, update the smallest owning intent document when needed, then update the canonical implementation. Do not recursively bootstrap IDD from its own generated plugins, regenerate `.idd/intent/` from implementation, or promote temporary Factory state into durable intent merely because the project documents itself.

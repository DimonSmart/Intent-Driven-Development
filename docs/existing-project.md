# Start Using IDD in an Existing Project

Use this path when the repository already contains implementation, documentation, requirements, ADRs, tests, or other sources that describe the product.

The goal is not to copy every existing document into IDD. The goal is to extract the current durable product truth and place it under `.idd/intent/`.

## 1. Install IDD

Claude Code:

```bash
claude plugin marketplace add DimonSmart/Intent-Driven-Development@marketplace
claude plugin install idd-intent@intent-driven-development
```

Codex:

```bash
codex plugin marketplace add DimonSmart/Intent-Driven-Development --ref marketplace
codex plugin add idd-intent@intent-driven-development
```

## 2. Initialize the Repository

Run in the repository root:

```text
idd-project-init
```

This creates the minimal project-owned IDD structure:

```text
.idd/
  intent/
  plugins.json
```

It also adds one small managed IDD section to `AGENTS.md` for Codex or `CLAUDE.md` for Claude Code while preserving unrelated project instructions.

## 3. Import Existing Product Knowledge

Invoke:

```text
idd-intent-import
```

A more explicit request can identify the best source areas:

```text
Use idd-intent-import to propose current product intent from ./docs, the public API, relevant tests, and confirmed application behavior.
```

Import treats existing material as evidence, not unquestionable truth. Historical plans, stale requirements, accidental implementation details, and obsolete documentation should not become current product intent merely because they exist.

## 4. Review the Proposed Intent

Check that the imported documents capture:

- user-visible behavior;
- important constraints;
- compatibility expectations;
- durable architectural decisions;
- meaningful verification rules;
- explicit non-goals where they prevent misunderstanding.

Do not preserve temporary migration plans, task status, old review notes, or a chronology of previous implementation decisions.

When a product decision remains unclear, use:

```text
idd-intent-brainstorm
```

When current product intent needs a confirmed change, use:

```text
idd-intent-change
```

## 5. Verify the Structure

Run:

```text
idd-intent-lint
```

For a broader diagnostic review:

```text
idd-intent-audit
```

## 6. Start Normal Development

For a focused implementation from current intent:

```text
Use idd-code-implement for <product area or requested behavior>.
```

To verify an existing implementation:

```text
Use idd-code-check-implementation for <product area>.
```

For a large task requiring several coordinated implementation stages:

```text
Use idd-factory-run to implement the task described in <file or request>.
```

Factory requires the optional `idd-factory` plugin.

## What Happens to Existing Documentation?

IDD does not require deleting existing documentation immediately.

Use the following rule:

- keep documents that still serve a clear audience or operational purpose;
- move durable product truth into `.idd/intent/`;
- avoid maintaining two competing sources of product truth;
- remove or archive stale plans and obsolete specifications when it is safe to do so;
- let Git preserve historical versions.

The target state is a small, current intent model—not a second copy of the repository's entire documentation history.

## Next

- [Browse common IDD use cases](using-idd.md)
- [Understand the methodology](methodology.md)
- [Use Factory for larger work](factory-workflow.md)

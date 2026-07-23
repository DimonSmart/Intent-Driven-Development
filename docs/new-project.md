# Start a New Project with IDD

Use this path when you have an idea, a problem to solve, or an early product vision but no established intent structure yet.

IDD does not require a complete specification before work begins. Start with the smallest useful product understanding, make important decisions explicit, and refine current intent as the product becomes clearer.

## 1. Describe the Vision Informally

Begin in ordinary language. A useful first description includes:

- who the product is for;
- what problem it solves;
- the most important user outcome;
- known constraints;
- what should deliberately remain out of scope.

You can explore this with ChatGPT or another conversational assistant before opening the coding repository.

Example:

```text
I want to build a keyboard-oriented local file manager for developers. It should work in a terminal, remain responsive in very large directories, and make destructive operations difficult to trigger accidentally. Help me clarify the first useful version and its boundaries.
```

The purpose is not to generate a large permanent specification. It is to discover the durable product decisions that should survive implementation changes.

## 2. Create or Open the Repository

Create the repository with only the normal files needed by the chosen technology. Do not create a large planning hierarchy in advance.

## 3. Install IDD

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

## 4. Initialize IDD

Run in the repository root:

```text
idd-project-init
```

This creates the minimal `.idd/intent/` structure and connects the repository to the installed IDD workflows.

## 5. Clarify the First Product Area

Invoke:

```text
Use idd-intent-brainstorm to help me clarify the first useful version of this product.
```

Provide the informal vision from step 1. The brainstorming workflow should focus on product meaning, boundaries, trade-offs, and missing decisions—not implementation planning.

## 6. Record Current Product Intent

When the first product area is sufficiently clear:

```text
Use idd-intent-change to record the confirmed behavior as current product intent.
```

If no existing intent document owns the area, IDD may route to:

```text
idd-intent-new-document
```

Prefer a small number of clear owning documents over one document for every feature or conversation.

## 7. Implement the Smallest Useful Slice

For focused work:

```text
Use idd-code-implement for the first confirmed product behavior.
```

For a larger task with several implementation stages:

```text
Use idd-factory-run to implement the task described in <request or file>.
```

Factory is optional and requires the `idd-factory` plugin.

## 8. Keep Intent Current

As the product changes:

- update current intent before implementing changed product behavior;
- keep implementation-only refactoring out of product specifications;
- record durable decisions, not temporary execution steps;
- let Git preserve old versions;
- remove obsolete current intent instead of accumulating historical layers.

## A Practical Minimal Loop

```text
clarify product meaning
→ record current intent
→ implement
→ verify
→ update intent when the product changes
```

This loop can remain small even as the implementation grows.

## Next

- [Browse common IDD use cases](using-idd.md)
- [Understand the methodology](methodology.md)
- [Use Factory for larger work](factory-workflow.md)

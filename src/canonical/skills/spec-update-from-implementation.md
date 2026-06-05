# spec-update-from-implementation

Use this skill to update a current specification from verified implementation
behavior when the user explicitly confirms that the implementation represents
current product intent.

Implementation evidence is not product intent by itself.

## Rules

- Do not treat incidental implementation details as requirements.
- Do not copy code structure, private helper names, temporary workarounds, or
  framework defaults into product intent unless they define the product.
- Do not update a specification from implementation merely because the
  implementation exists.
- Require explicit user confirmation before making semantic changes.
- Preserve the distinction between observable behavior, domain contracts,
  architecture, verification rules, and local implementation mechanics.
- If implementation and specification differ but intent is unclear, report the
  difference and ask for confirmation instead of editing the specification.

## Workflow

1. Read `.specs/README.md`, `.specs/INDEX.md`, and relevant current numbered
   documents directly under `.specs/`.
2. Inspect the implementation and verification evidence.
3. Identify observable behavior and durable architecture that may represent
   current product intent.
4. Exclude incidental implementation details and temporary state.
5. Summarize the proposed semantic specification changes for user confirmation.
6. After confirmation, update the smallest set of current specification files.
7. Run relevant verification.

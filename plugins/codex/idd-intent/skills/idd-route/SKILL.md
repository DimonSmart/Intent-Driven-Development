---
name: idd-route
description: Use a read-only routing step to classify an IDD-related request and select the smallest safe end-to-end workflow before handing off to the appropriate IDD skill.
---

# idd-route

Read-only **automatic** routing workflow, not a public command. Select the
smallest safe IDD route, but never execute the selected workflow or change
project files, Intent, Engineering, Factory state or implementation.

## Required references and lazy detailed reading

1. **Always first read** `references/routing-core.md`, the single
   authority for dimensions, classification, scope and permission, first skill,
   lifecycle, handoff, stopping, special gates and the detailed-reference index.
2. Apply the Core's standalone Factory model-policy shortcut before ordinary
   classification; for mixed requests retain all independent workflow stages.
3. Classify the **original user's request**, not the automatic caller's
   "output only the route" or "do not invoke" formatting/safety instructions.
   Such instructions apply to this read-only router, not to `Requested scope`.
4. Make the complete decision from Core when possible. Consult
   `references/common-workflows.md` only for an unresolved/special decision,
   approval gate or lifecycle detail identified in Core's section index.
   A clear ordinary route does **not** read details just for reassurance.
5. Find each required **exact Markdown heading**, verify it exists, and read
   only that section. For a `##` heading include its nested `###` headings
   and stop **before the next `##`**. For a `###` heading stop before the
   next `###` or `##`. No padded `sed` ranges or adjacent sections.
   Read related sections only if the task depends on them.
6. If a potential manual-only skill has ambiguous host invocation metadata,
   read `references/skill-descriptions.json` for its actual `invocation`
   and `exposure`; that reference is not a routing-policy duplicate.
7. If exact partial reads are unsupported, load the whole detailed file **only
   when required**. Missing mandatory Core or a needed detailed section is
   an incomplete-plugin blocker; do not reconstruct it from memory.

When a POSIX shell is available, a bounded `##` section can be extracted
without line-number guesses. Run from the installed `idd-route` skill directory
(where `SKILL.md` lives), or substitute the **absolute installed reference
path** for the final argument; do not assume the project workspace is the
plugin directory. Replace the heading with the exact indexed heading:

```sh
awk -v heading='## Workflow Family: Intent Import' '
  $0 == heading {inside = 1}
  inside && $0 != heading && /^## / {exit}
  inside {print}
' references/common-workflows.md
```

This prints the selected section and its child headings, not the next family.
Other read tools may be used if they respect the same heading boundaries.
Do not open downstream skill instructions just to decide which skill to use.

## Inputs

Accept natural-language user requests. The request may include a product area,
specification, code area, observed mismatch, required result, repository
bootstrap request, or an explicit limit such as "classify only", "update intent
only", or "do not change specs".

Do not require JSON or a special parameter structure.

## Forked Request Contract

This skill may run in an isolated child context. Classify only the request
explicitly supplied with the invocation; do not assume the caller's conversation
history is available. Direct invocation arguments are part of that request.

For automatic invocation, the caller must supply the complete available
original user request, including relevant constraints, prohibitions, requested
scope, and preservation boundaries. Do not replace it with a lossy summary.
For file or source references, pass resolvable references rather than inventing
their contents. Passing input to a child does not imply byte-for-byte identity
or access to unprovided parent context.

If no meaningful request reaches this skill and it cannot be recovered from
available input, do not guess a route. Ask for the missing request or return
an explicit missing-input diagnostic to the caller.

## Minimal project context

Choose a route from the original request and Core first. Read project files
only when needed to resolve ownership, missing current Intent, preservation
or a possible durable-decision gate. Begin with `.idd/intent/README.md` and
`.idd/intent/INDEX.md`, then only relevant current `IDD-NNNN` owners.
For Engineering-owner questions read `.idd/engineering/README.md` /
`INDEX.md` only when necessary; missing Engineering is legitimate.
For bootstrap use only the cheap check distinguishing an existing implemented
product from an empty or already-documented one; bootstrap owns broad discovery.

Do not scan history, the entire Intent tree or repository code. Do not read
`.idd/execution.yaml` for standalone Factory model policy: the configure
skill owns it. Do not assume `idd-factory` is installed because a repository
references it; report conditional or known-unavailable handoff accurately.

## Routing result and caller-owned handoff

Derive the six ordinary classification fields and all decisions from Core.
Before returning, validate the ordinary fields against Core, then fix any
inconsistent field **before** producing the final Markdown:
- `Classification` is an exact Core classification token, never a skill name
  or a shortened variant such as `intent-change`. `Operation` is
  `not-applicable` except for `product-change` and `engineering-change`.
- `intent-import` and `intent-bootstrap` have
  `Execution depth: not-applicable`; so does policy-only
  `engineering-change` and an `unclear` Product discovery gate.
- A desired Product improvement without an identifiable behavior/capability
  uses `unclear`, not an invented `product-change` / `modify`.
- A question only about the manual optional glossary workflow recommends
  `idd-glossary-build` with `Classification: unknown` and
  `Current handoff: none`, never an invented classification.
- Implementing already-approved Product Intent is not a normative Product
  change. Preserve Core's scope and special gates.
Resolve these constraints from Core, not from the worked output example below.
Standalone Factory model-policy routing **omits** the ordinary fields
altogether. Keep `Expected complete workflow` separate from the permitted
`Current handoff` and `Stop after` boundary. A blocked or `route-only`
result has `Current handoff: none`.

Preserve the complete **original request** with every independent task,
restriction and approval, its resolvable file references (not fabricated
contents), the route fields and temporary preservation or discovery boundary.
Never replace this input with a lossy summary. For lint preserve `mode: check
| fix` and the exact permission; for bootstrap preserve inclusion/exclusions
and compatibility context. Caller decides if it may invoke the next skill;
the isolated router never starts it, including manual-only commands.

## Output Format

Use compact Markdown, not JSON. For ordinary classified workflows, return the
semantic fields below; the direct Factory model-policy route uses the compact
examples following this section:

```md
# IDD Route

Classification: `product-change`
Operation: `modify`
Clarity: `clear`
Execution depth: `focused`
Requested scope: `end-to-end`
Audit scope: `not-applicable`

Recommended first skill: `idd-intent-change`
Expected complete workflow: `idd-intent-change -> idd-code-implement -> idd-intent-drift-audit`
Current handoff: `idd-intent-change`
Stop after: `the requested end-to-end workflow completes, or an intent or verification gate blocks progress`

Preservation boundary:
- Behavior expected to change:
- Behavior expected to remain unchanged:
- Public contracts to preserve:
- Compatibility or data constraints:
- Unresolved preservation questions:

Why:
- Short rationale grounded in `references/routing-core.md` and any specifically selected detailed workflow.

Handoff:
- Recommend that the caller invoke the current handoff skill with the original
  request, route fields, and preservation or discovery boundary, or state that
  no handoff is allowed for `route-only`. The caller waits for this routing
  result, applies the scope and clarity gates, and owns any subsequent invocation.
```

For `intent-bootstrap`, use a discovery boundary instead of inventing a product
preservation boundary:

```md
Discovery boundary:
- Repository or product areas included:
- Areas excluded or probably non-product:
- User-provided temporary context:
- Known public or compatibility contracts:
- Unresolved scope questions:
```

## Direct Factory Model-Policy Output Examples

These are semantic examples, not byte-for-byte templates. Set `Stop after`
based on the actual scope, blockers, and available skills. No artificial
classification, operation, clarity, execution-depth, or audit-scope fields apply.

Standalone authorized model change:

```text
# IDD Route

Recommended first skill: idd-factory-configure
Expected complete workflow: idd-factory-configure
Requested scope: end-to-end
Current handoff: idd-factory-configure
Stop after: requested configuration workflow completes or a required decision blocks it

Handoff: pass the complete original request and its constraints to idd-factory-configure
```

Route recommendation only:

```text
# IDD Route

Recommended first skill: idd-factory-configure
Expected complete workflow: idd-factory-configure
Requested scope: route-only
Current handoff: none
Stop after: routing recommendation
```

For a requested read-only review with `end-to-end`, use the first format but
pass the read-only restriction unchanged; the selected skill must not write.
When `idd-factory` is known to be missing, keep the recommended skill name
but set `Current handoff: none` and stop at the dependency diagnostic.

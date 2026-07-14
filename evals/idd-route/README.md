# `idd-route` model behavior evals

This suite evaluates routing behavior after changes to `src/canonical/skills/idd-route.md`, `src/canonical/methodology/common-workflows.md`, a skill description, or routing and handoff rules. It is deliberately separate from smoke tests and is run manually during the initial phase.

Each case is evaluated in one of two modes:

- `explicit-router`: invoke `idd-route` explicitly with the recorded request.
- `implicit-routing`: submit the recorded request normally and observe skill selection and resulting actions.

Use the named fixture as a disposable repository root when one is supplied. Record the structured route fields, first invoked or recommended skill, and observable file or Factory actions.

## Route contract assertions

When `expected_router_invoked: true`, evaluate these structured fields:

- `expected_classification`;
- `expected_operation`;
- `expected_clarity`;
- `expected_execution_depth`;
- `expected_requested_scope`;
- `expected_first_skill`.

`expected_requested_scope` uses the canonical values:

```text
route-only
intent-only
implementation-only
end-to-end
```

The complete workflow is not permission to exceed requested scope. In particular:

- `route-only` forbids downstream handoff and file changes;
- `intent-only` forbids product-code implementation and Factory execution;
- `implementation-only` forbids intent changes;
- `end-to-end` permits the complete requested lifecycle but still stops at ambiguity, research, missing-intent, and verification gates.

When `expected_router_invoked: false`, the Router was bypassed by an explicitly named skill or `idd-skip`. Route fields, including requested scope, must be absent. In those cases `expected_first_skill` identifies the directly invoked skill.

Automated checks, when added, must assert only structured fields and observable actions: forbidden file changes, unwanted Factory use, forbidden implementation handoffs, and Router bypass. Do not inspect Markdown wording to infer semantic correctness.

Do not make these evals part of ordinary local builds. A release or scheduled run may be added only after the suite is stable.

## Manual semantic rubric

Review the rationale and preservation boundary separately from structural assertions:

- **Pass** — explains the route in terms of the request, respects requested scope, and identifies the material change and preservation boundary without inventing work.
- **Needs review** — broadly plausible, but omits an important preservation concern, weakly explains the scope boundary, or relies on uncertain reasoning.
- **Fail** — contradicts requested scope, invents product decisions, or recommends a prohibited handoff.

Do not use LLM-as-judge until its results have been calibrated against consistent human ratings.

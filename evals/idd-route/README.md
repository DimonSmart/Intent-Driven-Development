# `idd-route` model behavior evals

This suite evaluates routing behavior after changes to `src/canonical/skills/idd-route.md`, `src/canonical/methodology/common-workflows.md`, a skill description, or routing and handoff rules. It is deliberately separate from smoke tests and is run manually during the initial phase.

Each case is evaluated twice where its mode requires it:

- `explicit-router`: invoke `idd-route` explicitly with the recorded request.
- `implicit-routing`: submit the recorded request normally and observe the skill selection and resulting actions.

Use the named fixture as a disposable repository root when one is supplied. Record the structured route fields, first invoked skill, and observable file or Factory actions. Automated checks, when added, must assert only those fields and actions: classification fields, first skill, forbidden file changes, unwanted Factory use, forbidden implementation handoffs for `route-only` and `intent-only`, and Router bypass for an explicitly named skill or `idd-skip`.

Do not make these evals part of ordinary local builds. A release or scheduled run may be added only after the suite is stable.

## Manual semantic rubric

Review the rationale and preservation boundary separately from structural assertions:

- **Pass** — explains the route in terms of the request and identifies the material change and preservation boundary without inventing work.
- **Needs review** — broadly plausible, but omits an important preservation concern or relies on weak reasoning.
- **Fail** — contradicts the requested scope, invents product decisions, or recommends a prohibited handoff.

Do not use LLM-as-judge until its results have been calibrated against consistent human ratings.

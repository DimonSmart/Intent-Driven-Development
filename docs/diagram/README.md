# IDD Interactive Workflow Maps

The [GitHub Pages site](https://dimonsmart.github.io/Intent-Driven-Development/) is a four-view guide to the current IDD methodology.

| View | Editable Archify v2 source | Focus |
| --- | --- | --- |
| Overview | [idd-overview.workflow.json](idd-overview.workflow.json) | Request routing, durable truth, direct/Factory execution and audits |
| Intent Lifecycle | [idd-intent.workflow.json](idd-intent.workflow.json) | Discover, confirm and update the current Intent owner |
| Factory Execution | [idd-factory.workflow.json](idd-factory.workflow.json) | Durable preflight, fresh contexts, verification and refinement review |
| Quality & Conformance | [idd-quality.workflow.json](idd-quality.workflow.json) | Lint, structure audit, drift audit and technical verification |

The page navigation and responsive shell are in [site-shell.html](site-shell.html). Archify generates each diagram independently as a standalone HTML page under `site/views/`. Generated HTML is intentionally untracked, because it embeds the third-party runtime and conflicts with a legacy-name repository check.

## Build locally

Install or check out the [pinned Archify revision](../../.github/workflows/deploy-idd-diagram.yml) as `.archify/`, then run:

```bash
node scripts/Build-Idd-Diagram-Site.mjs --archify .archify/archify/bin/archify.mjs
```

The builder checks graph references and isolated nodes. Archify `finalize --quality standard` checks schema, readable layout and delivery. It stamps each temporary diagram input with the actual Git commit SHA for accurate source links; **the checked-in source files are not rewritten**.

GitHub Actions publishes `site/index.html` and the four generated views on edits to `docs/diagram/**`, the builder or the deployment workflow.

The source diagrams describe methodology, not a mandatory runtime state machine. Factory is optional; direct coding and read-only audits are independent routes. Product Intent is the current source of truth. Engineering Rules and project verification configuration are separate optional layers.

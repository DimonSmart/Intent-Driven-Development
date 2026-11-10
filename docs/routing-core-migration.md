# Routing Core extraction — ownership and baseline

Baseline `main`: `b555ed4efee43e5bc21d127fe42890abb643c775` (2026-11-10).
The baseline reference and route instructions are preserved at that Git commit.
No old route LiveEval or baseline model responses were present/captured. A
source-preserving move alone does not prove semantic equivalence.

| Policy / scenario | Baseline location | Owner after extraction | Consumers / verification |
| --- | --- | --- | --- |
| Classification, operations, clarity, requested scope, execution depth | `common-workflows.md / Routing Dimensions`, `idd-route.md / Apply Common Classification` | `routing-core.md / Routing Dimensions` | `idd-route`, `idd-help`; route live scenarios |
| Audit scope and lint/structure/drift selection | `idd-route.md / Apply Common Classification`, `common-workflows.md / Audit and Verification Separation` | `routing-core.md / Audit scope, decisive distinctions and safety gates` for selection; detailed audit mechanics stay with owners | route live scenarios |
| Factory standalone model-policy and mixed-task shortcut | `idd-route.md / Direct Factory Model-Policy Route`, `common-workflows.md / Factory Model Configuration and Review` | `routing-core.md / Direct Factory Model-Policy Route`; configure skill still owns policy writes | route/help; Factory untouched |
| Manual-only, automatic vs direct selection | `idd-route.md / Automatic Selection`, `skill-descriptions.json` | Core route prohibition; JSON machine metadata remains authoritative | route + generation |
| Visual clarity, confirmed-intent, screenshot exceptions | `idd-route.md / Visual Reference Clarity Gate`, visual workflow | Core for gate; common-workflows for the inside of discovery | route live scenarios |
| First skill, expected lifecycle, handoff, stop | `idd-route.md / First Skill, Handoff Rules`, `common-workflows.md / Complete Workflow and Current Handoff` | `routing-core.md / First Skill, Complete workflows` | route/help |
| Workflow procedures and completion checks | `common-workflows.md / Workflow Family: *`, `Workflow Completion Rules` | Same detailed source; selectable by Core index | route on demand/help |
| Shared invariants, Engineering and Factory preflight | `common-workflows.md / Shared Invariants, Focused and Orchestrated Execution`, `intent-preflight.md` | Existing detailed workflow/owner contracts; Core retains only routing safety gates | engineering-change retains its packaged detailed reference |
| Router input, fork, read-only and output format | `idd-route.md / Inputs, Forked Request Contract, Output Format` | `idd-route.md` | automatic Claude/Codex workflow |
| Packaging and invocation | `plugin-manifest.json`, `skill-descriptions.json` | Same machine-readable owners, new Core references in manifest | generation and both adapters |

## Reading, metrics and limits

`idd-route` now loads its mandatory Core, then only detailed sections whose
exact headings are selected by the Core index. For a plain route, it need not
load `common-workflows.md`. `idd-help` reads Core for route questions and
workflow details on demand. The `idd-engineering-change` packaged
`common-workflows.md` remains intact.

Baseline source sizes: 372 lines (`idd-route.md`) plus 823
(`common-workflows.md`), or 1195 lines of formerly mandatory reading.
Comparing these with the new route + Core gives a **potential Core-only
source-text** reduction; it is **not** observed prompt-token consumption.
A mixed task can legitimately read more sections or even the full document
when the host has no partial-read operation. Host trace instrumentation,
actual token costs, baseline model responses and comparative route LiveEval
must be collected separately before claiming behavioral equivalence.

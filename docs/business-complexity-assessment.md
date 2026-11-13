# Business Complexity Assessment (MVP)

Use the read-only **idd-intent-estimate** skill to explain to a product owner how demanding the **currently required** product is, based on the current Product Intent rather than its implementation. It can run before any code has been written. An installed core **idd-intent** plugin is sufficient; **idd-factory** is not needed.

## Run

- **Claude Code:** explicitly invoke /idd-intent-estimate, optionally with a functional area or IDD-NNNN identifiers. An ordinary natural-language request may also select the skill.
- **Codex:** choose the installed $idd-intent-estimate Skill, or request a business complexity assessment from current Product Intent in natural language. Host UI command visibility differs between Codex and Claude; do not assume a slash menu looks identical.
- **Default scope:** all accessible current .idd/intent/ requirements. You may select a named feature or a set of IDs. The skill includes materially related dependencies but does not extrapolate the chosen area to the whole product.

Example request:

> Assess the business complexity of the current product from .idd/intent/. Return a business-readable report with major drivers, uncertainties, evidence and COSMIC Preparation. Do not inspect code or change files.

The result is Markdown in the **response**. It is not written to .idd/ and does not become Product Intent. No source code, Git history, implementation reports, temporary Factory plans or Engineering Rules are used to guess product complexity.

## Interpreting the assessment

The evaluator first normalizes the product's confirmed functional areas and independent scenarios, merging duplicate descriptions from multiple documents. It then assesses **seven independent dimensions**:

| Dimension | Business question |
| --- | --- |
| Functional Scope | How many meaningfully distinct product capabilities and processes exist? |
| Business Logic | How difficult are the rules, algorithms, states and exceptions? |
| Integrations | What confirmed external interactions and failures matter? |
| Data Complexity | How challenging are relationships, lifecycles and consistency? |
| Reliability & Security | What are the supported protection and failure-impact demands? |
| User Interaction | How complex are the confirmed interactions and UI states? |
| Operational Constraints | What platform, performance, availability or compatibility constraints apply? |

Each dimension is rated **Low**, **Medium**, **High**, **Unknown**, or **Not applicable** with a source-backed explanation. **Unknown** is not Low: silence in the specs cannot demonstrate that integrations, data or security are simple. **Not applicable** requires affirmative support.

The business summary reports **three separate judgments**:

- **Product Scope:** Small / Medium / Large / Undetermined. Diversity and breadth of required capabilities, **not** number of Markdown files.
- **Implementation Complexity:** Low / Medium / High / Undetermined. Expected difficulty of realizing the target behavior, **not** remaining development effort. A product with a single demanding optimization algorithm can be Small and High simultaneously.
- **Assessment Confidence:** Low / Medium / High, **for the specified scope**. Confidence depends on actual coverage, evidence, contradictions and important unknowns. High confidence in currently described Intent does **not** imply every unstated real-world requirement has been discovered.

No weighted average, arbitrary points, delivery percent, time estimate, price, team size or person-days are generated.

## COSMIC Preparation is not COSMIC Measurement

The final section identifies possible software pieces, measurement scope, boundary, functional users, events, input/output, persistent data and functional process **candidates** where requirements allow. Data movement labels **Entry (E), Exit (X), Read (R), Write (W)** can appear only when the boundary and supporting requirements justify them. A REST call or SQL query is **not** automatically a COSMIC movement. Without a defined boundary the analysis reports **Unresolved** rather than inventing movements. It never calculates CFP, and a preliminary process candidate is **not** a verified COSMIC Functional Process.

Future COSMIC measurement and empirical effort estimation are deliberately separate later stages. See the [official COSMIC measurement process](https://cosmic-sizing.org/cosmic-sizing/intro/measurement-process/).

## Demo A — before any implementation

The following is an **illustrative, source-backed example** using a synthetic fixture from the versioned live-evaluation corpus, not a recorded successful model run. The fixture has no executable product implementation.

**Intent:** [P03-algorithm](../tests/Idd.Intent.LiveTests/Scenarios/estimate-cases.json) defines one operation: compute an optimal allocation of 200 deliveries under capacity, time-window and exclusivity constraints, minimizing a global penalty.

| Summary characteristic | Illustrative judgment | Why |
| --- | --- | --- |
| Product Scope | Small | One confirmed independent function |
| Implementation Complexity | High | Global optimization with interacting hard constraints |
| Assessment Confidence | Medium | The core algorithmic challenge is clear, but detailed constraints and other dimensions remain unspecified |

**Functional Area Map:** Delivery allocation — user triggers one optimization workflow; alternatives, errors and input validation are not sufficiently described. Coverage: **partially assessed**. Evidence: fixture IDD-0001.spec-product.md.

**Seven dimensions:** Business Logic **High** on the specified optimization requirement; Functional Scope **Low** for the narrow documented capability; Integrations, Data Complexity, Reliability & Security, User Interaction and Operational Constraints **Unknown** where the fixture gives no positive basis for Low or Not applicable. These are illustrative, not actual measured scores.

**Main driver:** combinatorial allocation with multiple interdependent constraints. **Uncertainty:** expected response time, inputs, data durability and failure behavior are not specified. **Simplification:** no justified removal recommendation without owner confirmation.

**COSMIC Preparation:** Purpose and broad scenario are known, but the software boundary, Functional User, data groups and persistent storage are not sufficiently defined. Status: **Unresolved**; one candidate optimization workflow may be listed as **High-level only**. No E/X/R/W movements, CFP, effort or implementation progress are asserted.

## Demo B — current Intent-Driven-Development product

This is a **manual evidence-based demonstration** grounded in the repository's current Intent, prepared to show what a real assessment should cover. It is **not** presented as an executed or passed live Claude/Codex evaluation. The opt-in corpus includes P21-self-IDD to evaluate the actual installed skill.

**Assessment basis:** current [IDD-0001 — Factory orchestration](../.idd/intent/IDD-0001.spec-factory-orchestration.md), [IDD-0003 — core and distribution](../.idd/intent/IDD-0003.spec-core-intent-and-generation.md), and [IDD-0006 — native-agent Factory ADR](../.idd/intent/IDD-0006.adr-native-agent-factory-orchestration.md). This illustration evaluates their documented target product only, not source code or delivery status.

| Functional area | Confirmed examples | Coverage |
| --- | --- | --- |
| Core Product Intent lifecycle | Current Intent, initialization, change/import, optional Engineering Rules, lint/structure/drift audit, plugin generation for Claude/Codex — IDD-0003 | Assessed at summary level |
| Factory orchestration | Preflight, fresh planner/worker cycles, task batches, native-agent capability, model mapping, interruption/recovery and final verification — IDD-0001 | Assessed at summary level |
| Architectural constraints | Native subagents, blocking waits, recovery semantics, no custom durable Factory runtime — IDD-0006 | Included as constraints, **not** independent features |

Illustrative overall characterization:

- **Product Scope: Medium (preliminary).** Several distinct functional families, including durable knowledge and temporary Factory execution, are documented. IDD-0001 and IDD-0003 describe different responsibilities rather than interchangeable counts of files.
- **Implementation Complexity: High (preliminary).** IDD-0001 requires context-isolated native workers, strict preflight/authorization gates, model/effort inheritance, at-least-once recovery and final verification; IDD-0006 clarifies the adopted architecture and trade-offs. This difficulty exists in the target requirements regardless of how much code already exists.
- **Assessment Confidence: Medium (illustrative).** These are the relevant indexed current documents, but detailed operational and integration environment assumptions would need confirming for a definitive classification. Confidence is about this described product, not all possible future capabilities.

**Primary complexity drivers:** native host capabilities and isolation (IDD-0001, "Native child-agent capability and internal Skills"); multi-stage safety/approval gates (IDD-0001, "Factory entry preflight"); interrupted task recovery and provenance (IDD-0001, "Immutable Git baseline, provenance and safe recovery"); adapter-specific native-agent architecture (IDD-0006, "Decision"). IDD-0003 establishes separate durable Product Intent and implementation/policy domains, as well as generated adapter packaging.

**Uncertainty:** external host support and exact measurement boundary across the methodology, plugin packaging and native-agent orchestration must not be fabricated. **COSMIC Preparation:** candidate activities may be discussed, but a single measured software boundary/layer is **Unresolved** at this stage, so no E/X/R/W count or CFP is produced.

A fresh actual invocation must re-read .idd/intent/INDEX.md and **current** numbered documents; these examples and IDs are **not** hardcoded into the Skill.

## Validation and limits

Ordinary generation/contract tests check canonical metadata, routing policy and package production, **not** a model's reasoning. The separate [opt-in live evaluation suite](../tests/Idd.Intent.LiveTests/README.md) seeds 20 synthetic cases plus the current IDD product, uses an isolated Codex install, saves response and judge evidence, and checks read-only workspace snapshots. It can be run with a selected subset for a quick smoke check.

**Known MVP limits:** qualitative reasoning depends on the coding agent and on incomplete source documents; a careful Unknown/Undetermined is preferable to a fabricated answer. Host and adapter invocation features vary. Neither the examples nor static contract tests prove Claude/Codex semantic performance; any unexecuted host checks remain **not evaluated**.

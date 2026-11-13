# COSMIC Functional Size Measurement (IDD stage 2)

The independent read-only **idd-intent-cosmic** command measures verified Functional User Requirements (FUR) from the *current* Product Intent. It is distributed inside the existing **idd-intent** plugin for Codex and Claude Code; it is not a third plugin, a code-complexity estimator, or a way to turn CFP directly into development effort.

## Run

Ask: "Measure the current documented product in COSMIC v5.0 CFP", "Count COSMIC Function Points for the order module", or invoke idd-intent-cosmic directly. The optional named area limits *the requested scope*; the skill still reads related requirements and reports areas omitted or inaccessible.

idd-intent-estimate remains a separate qualitative Business Complexity Assessment; its COSMIC Preparation is optional context, never measured CFP. The commands may be used independently, in either order.

No source code, implemented product or previous business assessment is required. The agent reads the actual target project .idd/intent/ current specs, indexes and supporting accepted ADRs, without editing them. Source code, ephemeral Factory state, Git history and templates do not supply requirements. It defines measurement scope, software boundary, one layer per measured piece, decomposition level, FUR granularity and external functional users before mapping processes.

For each FUR-supported triggering event, the skill identifies the complete functional process, Data Groups and Entry/Exit/Read/Write (E/X/R/W) movements. Each movement counts one CFP, but only after the full process passes COSMIC Rule 10c and can be explained with existing source paths plus exact headings/quotes. It cannot count field names, API calls, screens, loops or repeated invocation as extra CFP.

## Validation and host requirements

The canonical guide and JavaScript validator are shipped into **each installed** skill directory:

- references/cosmic-measurement-guide.md
- references/cosmic-validator.mjs

Node.js **20+** is required for the local dependency-free validator. It accepts a model JSON file stored in an OS temp folder (outside the target project) and a target workspace root:

~~~sh
node "/absolute/path/to/installed/idd-intent-cosmic/references/cosmic-validator.mjs" "/tmp/measurement.json" "/path/to/product"
~~~

The exact installed path is host-specific. A supplied CLI path is not itself shell authorization: the user/host must permit the **specific safe validator command**, without broadening the agent's general command permission. The script only reads its JSON model and numbered existing Intent documents; it does not write project or other files or call outside services. A denied command, unavailable Node, temp-file failure, or missing source produces a diagnostic rather than a fabricated validated result.

The **JSON model contract** is documented under the packaged guide, including model version, measurement unit ID, FUR evidence, process IDs, movement IDs, coverage, aggregation and status. The deterministic validator exposes validate(model) and calculate(model) and returns path-specific diagnostics, per-process movement counts, per-unit subtotals and status. It cannot prove semantic correctness of a COSMIC object or process from text alone.

Source inventory is verified against the actual filesystem. Source paths outside .idd/intent/ or missing target documents are rejected. The runner must not trust values such as claimedCfp. Mechanical check + semantic FUR review are **distinct**.

## Partial and complete results

- **Complete:** all declared *documented* FUR in scope were actually inventoried and measured, all boundaries and aggregates are compatible, numerical checks passed, and the method is applicable. This never asserts that unwritten real-world product needs are absent.
- **Partial:** a compatible set of processes is measurable but other requirements/coverage or applicability issues remain. Show **Measured subtotal: N CFP (v5.0); Overall size: Not determined**.
- **Diagnostic only:** no defensible numeric result, such as unknown boundary, missing current FUR, or unavailable mechanical validator. Do not report zero as the size of an unknown product. Zero is allowed only when the scope was documented and proven empty.

Algorithm complexity, AI reasoning and continuous media are not sized by counting computation. The functional data movements can be sized, but methodological limitations must remain explicit even with fully documented FUR. A complete CFP result is *not* a project estimate, certificate, audit approval, cost forecast or schedule.

## Concrete examples

### Full documented scope: C-Reg Add a Professor

The official COSMIC Measurement Manual v5.0, combined Sept 2024, Part 3d §4.1 (May 2022 example), describes registrar entering professor details, reading existing professor data, storing new data and an error message. The published structure is:

| Movement | Data group / recipient |
|---|---|
| E | Professor details from Registrar |
| R | Professor details from persistent storage |
| W | Professor details to persistent storage |
| X | Error/confirmation message to Registrar |

For **this exact documented process**, the published golden result is **4 CFP (v5.0)**. The test fixture fixes these movements and value before running the validator; it is not a generic template for all create operations.

A real result would be Complete only if this were the entire declared documented scope and the original current FUR were verified in the measurement workspace. The publication example alone does not certify any actual application.

### Partial documented scope

Suppose current specs unambiguously require the above 4-CFP process plus another "Synchronize payment" capability that does not specify its software boundary, functional user, triggering event or movements. The only measured process is Add a Professor. The required output is:

~~~text
Coverage: Partial
Measured: Add a Professor — 4 CFP (v5.0)
Needs clarification: Synchronize payment — boundary, actor and event
Measured subtotal: 4 CFP (v5.0)
Overall size: Not determined
~~~

No average CFP, assumed CRUD pattern or extrapolated total is permitted. If the two processes belong to incompatible boundaries, the 4-CFP number must be labeled for its individual measurement unit, not the entire product.

### Experimental self-measurement of IDD

See [IDD scope/coverage diagnostic](examples/cosmic-idd-diagnostic.md) for a conservative initial application to the repository's current .idd/intent/. It deliberately does **not** assert an unverified total: Factory orchestration, layers and external agent interfaces require a precise measurement perspective.

## Tests and evaluation

- tests/Idd.Cosmic.Tests/validator.test.mjs: deterministic input contract and arithmetic, empty/partial/incompatible scope, negative security and structural scenarios.
- tests/Idd.Cosmic.Tests/golden-cases.json: frozen COSMIC Manual Part 3d reference values and movement identities.
- tests/Idd.Generation.Tests/CosmicFunctionalSizeContractTests.cs: canonical metadata, route classification, generated file packaging and **actual Node invocation from the generated Claude and Codex skill paths**.
- tests/Idd.Intent.LiveTests/Scenarios/cosmic-cases.json and CosmicFunctionalSizeLiveTests.cs: opt-in 20-case semantic evaluation of the agent, each with isolated temporary FUR, source checks, workspace hash comparison and independent semantic judgment of process/movement *sets*, not merely totals. Cases are separated as pass, semantic-failure or host-unavailable. Host evaluations need real Codex/Claude access and are **not run in standard CI**.

Normal fast validation runs Node unit tests plus .NET generation tests. Live semantic tests are explicitly enabled by IDD_RUN_LIVE_COSMIC_EVALS=1 and support IDD_COSMIC_EVAL_MAX_CASES or IDD_COSMIC_EVAL_CASE_IDS, IDD_COSMIC_EVAL_MODEL, IDD_COSMIC_EVAL_TIMEOUT_MINUTES and IDD_COSMIC_EVAL_OUTPUT_DIR. They generate and install the plugin in temporary locations and write reports outside the fixture workspace.

## Method and reporting authority

[COSMIC Measurement Manual v5.0 (combined September 2024)](https://cosmic-sizing.org/wp-content/uploads/2022/11/Combination-of-Parts-v5.0-Sep-2024.pdf); [official measurement process](https://cosmic-sizing.org/cosmic-sizing/intro/measurement-process/). The report should give purpose, date, exact source inventory, boundaries, layer, decomposition, FUR granularity, functional users, complete process inventory, per-movement source anchors, mechanical diagnostics, per-unit CFP, coverage limitations and audit caveats. No private repository data is transmitted by this skill.

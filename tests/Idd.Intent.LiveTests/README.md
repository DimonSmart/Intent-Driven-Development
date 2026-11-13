# Idd.Intent.LiveTests

This project contains opt-in token-consuming end-to-end evaluations for
`idd-intent-import`. It is separate from `Idd.Factory.LiveTests` and is
skipped unless `IDD_RUN_LIVE_INTENT_EVALS=1`.

The harness builds the current generator, generates the Codex marketplace,
installs the generated `idd-intent` plugin into an isolated Codex home, and
invokes the generated `$idd-intent-import` skill against isolated temporary
repositories.

It exercises six scenarios:

- mixed Product Intent + explicit Engineering decisions;
- Product Intent with no Engineering material;
- unresolved technical alternatives;
- existing equivalent Engineering Rule;
- conflict with an existing current Rule;
- explicit replacement of an existing Rule while preserving its stable ID.

Run explicitly from the repository root (requires installed and authenticated
Codex; these evaluations consume tokens and must never run in normal CI).

Windows PowerShell:

```powershell
$env:IDD_RUN_LIVE_INTENT_EVALS = "1"
dotnet test tests/Idd.Intent.LiveTests/Idd.Intent.LiveTests.csproj --filter "Category=LiveIntentEval"
```

When finished, optionally run `Remove-Item Env:IDD_RUN_LIVE_INTENT_EVALS`.

macOS/Linux:

```bash
IDD_RUN_LIVE_INTENT_EVALS=1 dotnet test \
  tests/Idd.Intent.LiveTests/Idd.Intent.LiveTests.csproj \
  --filter 'Category=LiveIntentEval'
```

The opt-in variable applies to the current process/session only. Existing
`IDD_INTENT_EVAL_TIMEOUT_MINUTES` and other `IDD_INTENT_EVAL_*` overrides
continue to work; the default timeout is 20 minutes. Intent Live Evals
are separate from Factory Live Evals.

## Opt-in mechanical lint check/fix evaluation

`IntentLintEndToEndLiveTests` invokes the generated Codex
`$idd-intent-lint` skill in a disposable initialized Git repository.
It checks that `mode: check` leaves the project files unchanged,
`mode: fix` repairs a legacy filename/H1/INDEX/link group, missing
canonical README is restored from packaged lint assets, and a second
`fix` makes no further changes. It also verifies that an invalid
user-owned Glossary is reported but not modified.

This is a real LLM behavior test, not a generated-skill text assertion.
It runs only with `IDD_RUN_LIVE_INTENT_EVALS=1` and a working Codex CLI
and credentials, using the same environment overrides as import live evals.
It is deliberately excluded from ordinary CI and may report nondeterministic
host/model behavior. The broader lint acceptance scenarios (collision IDs,
external references, dirty trees, failed writes, Engineering allocator
read-only, and scoped reporting) require further isolated live evaluations.

## Isolated IDD route LiveEval

`RouteRoutingLiveTests` is a **separate opt-in** suite. It does not run with
`IDD_RUN_LIVE_INTENT_EVALS=1`, ordinary `dotnet test`, or
`scripts/Check.ps1 -Mode Live` (the latter runs Factory evaluations).

The scenarios live in `Scenarios/route-cases.json` (46 cases). They check
typed classification, scope, clarity, execution depth and handoff fields plus
unchanged workspace snapshots. The import suite has both a deliberately
missing-source blocker and a scenario with an actual supplied source.
By default they also use a separate LLM semantic judge for scope, ordering and
gates.
The generated Codex plugin is installed into an isolated `CODEX_HOME`, and
the route is triggered through a **natural-language automatic workflow**
request rather than changing `idd-route` into a user-facing public command.
`IDD_ROUTE_EVAL_CASE_IDS` selects unique comma-separated scenario IDs.
With `IDD_ROUTE_EVAL_SKIP_JUDGE=1`, only typed structural fields are checked
automatically; lifecycle semantics, free-text stop boundaries and exact
reference-read ranges require review of saved answers and JSONL traces.

A host that does not trigger the automatic workflow, an unavailable model,
an unsupported direct-invocation test or an intentionally damaged reference
must be reported as `not evaluated / host invocation unavailable`, not a
semantic regression. The directly invoked-skill and missing-reference
scenarios are explicitly tracked but require their own host/incomplete-plugin
integration harness and are **not** pretended to have run.

To run on the current checkout:

```powershell
$env:IDD_RUN_LIVE_ROUTE_EVALS = "1"
$env:IDD_ROUTE_EVAL_MODEL = "gpt-6"
$env:IDD_ROUTE_EVAL_REASONING = "medium"
# Optional: read-only (default) or workspace-write
$env:IDD_ROUTE_EVAL_SANDBOX = "read-only"
dotnet test tests/Idd.Intent.LiveTests/Idd.Intent.LiveTests.csproj --filter "Category=LiveRouteEval"
Remove-Item Env:IDD_RUN_LIVE_ROUTE_EVALS
Remove-Item Env:IDD_ROUTE_EVAL_MODEL
Remove-Item Env:IDD_ROUTE_EVAL_REASONING
Remove-Item Env:IDD_ROUTE_EVAL_SANDBOX
```

For comparative baseline evaluation, supply
`IDD_ROUTE_EVAL_BASELINE_DIR` pointing to a **separate clean checkout**
at exact commit `b555ed4efee43e5bc21d127fe42890abb643c775`.
The harness verifies `git rev-parse HEAD` before running. Each revision
gets a distinct generated marketplace, home and workspace, with identical
model, reasoning and prompts.

Useful overrides: `IDD_ROUTE_EVAL_CODEX`,
`IDD_ROUTE_EVAL_TIMEOUT_MINUTES` (default 15 per host call),
`IDD_ROUTE_EVAL_MAX_CASES`, `IDD_ROUTE_EVAL_OUTPUT_DIR`, and
`IDD_ROUTE_EVAL_SANDBOX` (`read-only` by default; `workspace-write` is also
supported), and `IDD_ROUTE_EVAL_SKIP_JUDGE=1` for troubleshooting. The report
(`route-report.json`) records exit codes, process errors, `exec_command`
failures, route failure reasons, the selected sandbox, and whether captured
tool traces confirm reads of `idd-route/SKILL.md` and `routing-core.md`. Raw
responses, JSONL events, and stderr live in the output directory; preserve them
when investigating discrepancies. Use the same host/provider, authenticated
model, timeout and installed plugins when comparing revisions, and review
changed decisions rather than only comparing prose.

**Context measurement:** the canonical source sizes provide only a
Core-only *estimated upper bound* compared to the old unconditional
route + full workflow read. The harness saves JSONL events, but it does not
invent actual prompt-token counts or reference-read traces when the host does
not expose them. Consult `docs/routing-core-migration.md` for the baseline
ownership map and explicitly unverified cases.

## Opt-in Clarify / Brainstorm behavioral evaluations

The discovery suite contains five Clarify scenarios (C1–C5) and eight Brainstorm
scenarios (B1–B8) in Scenarios/discovery-cases.json. A dedicated opt-in live
test invokes the **generated and installed Codex skills** in isolated read-only
workspaces, checks for unauthorized file mutations, and asks a separate
semantic judge to evaluate the requested problem coverage, meaningful idea
diversity, assumptions, counterarguments, user confirmation and handoff
boundaries. Judges assess substance, not fixed words, headings or quotas.

These live tests are **not** executed in ordinary CI or standard dotnet test
runs: they consume model tokens, require local dotnet, Codex CLI and working
credentials, and are nondeterministic. Full runs have not been executed through
the GitHub connector; run in an authenticated environment for behavioral
acceptance:

```powershell
$env:IDD_RUN_LIVE_DISCOVERY_EVALS = "1"
dotnet test tests/Idd.Intent.LiveTests/Idd.Intent.LiveTests.csproj --filter "Category=LiveDiscoveryEval"
```

Options: IDD_DISCOVERY_EVAL_CASE_IDS=C1,B1 for a focused run;
IDD_DISCOVERY_EVAL_MAX_CASES=2 for initial smoke;
IDD_DISCOVERY_EVAL_MODEL, IDD_DISCOVERY_EVAL_REASONING,
IDD_DISCOVERY_EVAL_TIMEOUT_MINUTES, IDD_DISCOVERY_EVAL_OUTPUT_DIR,
IDD_DISCOVERY_EVAL_CODEX and IDD_DISCOVERY_EVAL_SKIP_JUDGE=1 for manual review.
Raw responses, event logs and semantic verdicts are written to report.json
under the specified output directory. Failed evaluations must be inspected and
fixed rather than weakening the rubric.


## Opt-in Business Complexity Assessment evaluation

BusinessComplexityLiveTests exercises the **idd-intent-estimate** skill in
**33 version-controlled scenarios** in Scenarios/estimate-cases.json: 20
original synthetic cases, 12 additional E01–E12 evidence/coverage/driver
scenarios, plus one scenario using this repository's **current**
.idd/intent/ documents. The new cases exercise staged inventory, read/unread
coverage states, justified High ratings, independent dimensions, ranked
complexity drivers, portable references, scoped assessment and conservative
confidence.

It builds the generator, installs the generated **idd-intent** plugin into
an isolated Codex home, seeds disposable workspaces, invokes the skill
**explicitly** in a read-only sandbox, and checks that no workspace file
changed. By default it uses a separate Codex semantic judge against
the case-specific qualitative expectations, with response/traces/verdicts
saved in report.json. This checks behavior, not merely wording in SKILL.md.

**Not part of ordinary CI.** To run on an authenticated Codex installation
(requires tokens and network):

    $env:IDD_RUN_LIVE_ESTIMATE_EVALS = "1"
    $env:IDD_ESTIMATE_EVAL_CASE_IDS = "P01-pre-code,P03-algorithm,P21-self-IDD"
    dotnet test tests/Idd.Intent.LiveTests/Idd.Intent.LiveTests.csproj --filter "Category=LiveEstimateEval"

Remove the CASE_IDS variable to execute all 33. Other overrides:
IDD_ESTIMATE_EVAL_CODEX, IDD_ESTIMATE_EVAL_MODEL (default gpt-6),
IDD_ESTIMATE_EVAL_REASONING (medium), IDD_ESTIMATE_EVAL_MAX_CASES,
IDD_ESTIMATE_EVAL_TIMEOUT_MINUTES (15), IDD_ESTIMATE_EVAL_OUTPUT_DIR,
and IDD_ESTIMATE_EVAL_SKIP_JUDGE=1 (skip semantic judging, **not** evidence
of semantic success). The JSON report identifies selected case IDs, model,
source revision, host, result and errors. Inspect recorded claims/citations
manually when a conclusion depends on nuanced source content.

Claude Code live execution and host-level natural-language auto-invocation
are **not evaluated** by this Codex-only suite; route-cases.json covers
routing decisions separately, and the ordinary generation tests verify
Claude/Codex package metadata. Run a manual Claude smoke on a disposable
project with only current specs; do not report success until it is actually
observed. Host unavailability is **not evaluated**, not "pass".

## COSMIC v5.0 hierarchical owner/connector evaluations

`CosmicFunctionalSizeLiveTests` is a separate **opt-in real LLM** evaluation using generated installed skills, not a text-only contract test. It requires a working authenticated Codex or Claude host and native child-agent capability. Select targeted cases before attempting the full suite:

- `C03-shared-fur`, `C21-multiple-owners`: one normative owner; other specifications are connector references, not duplicate process definitions.
- `C22-full-scope-exhaustive`: 28 specs with 18 process owners and ten connector specs.
- `C23-hierarchical-60-specs`: 60 specs, 20 owning processes and 40 connector specs; checks coverage, bounded hierarchy and **no CFP increase** from references.
- `C24-single-owner-coupling`: three owner-defined calling processes reference another one; coupling must be separated from CFP.
- `C25-large-spec-sections`: a single 75k-character spec; verifies exhaustive chunked range reading and consolidation.

Use `IDD_COSMIC_EVAL_CASE_IDS=C24-single-owner-coupling,C25-large-spec-sections` with `IDD_RUN_LIVE_COSMIC_EVALS=1` and `dotnet test tests/Idd.Intent.LiveTests/Idd.Intent.LiveTests.csproj --filter Category=LiveCosmicEval`. Both frameworks' model availability and runtime limits are host-dependent. These cases are **not** claims of executed LLM results.

The separate optional, deterministic state helper can be checked without an LLM:

```sh
python3 -m unittest discover -s src/canonical/cosmic-state-support -p 'test_*.py'
```

It verifies checksum freshness, owner/link references, fan-in counts and CFP arithmetic only; it does **not** judge COSMIC semantics or the real native-agent trace. A complete acceptance run should additionally inspect spawn/reader/reducer/measurer traces and successful recovery on an interrupted large real project.

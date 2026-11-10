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

The scenarios live in `Scenarios/route-cases.json` (39 cases). They check
typed route fields, handoff constraints, and unchanged workspace snapshots.
By default they also use a separate LLM semantic judge for scope, ordering and
gates.
The generated Codex plugin is installed into an isolated `CODEX_HOME`, and
the route is triggered through a **natural-language automatic workflow**
request rather than changing `idd-route` into a user-facing public command.

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

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

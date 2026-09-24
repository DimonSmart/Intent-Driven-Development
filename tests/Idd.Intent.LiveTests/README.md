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

Run explicitly with `run-live-intent-evals.bat` or set
`IDD_RUN_LIVE_INTENT_EVALS=1` and run this test project directly.

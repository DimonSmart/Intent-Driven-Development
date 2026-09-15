# Idd.Factory.LiveTests

This project contains one intentionally expensive, token-consuming Factory end-to-end evaluation:

`FactoryEndToEndLiveTests.TwoStepCatalog_CompletesThroughOneBlockingFactoryCall`

The scenario installs the generated current Factory plugin and runs the prepared `TwoStepCatalog` workspace through a real Codex host, real `factory_run`, production Factory Runtime, real semantic workers, and the configured semantic model. The task is deliberately multi-step and the evaluation requires at least two completed work items.

The workspace also contains an executor-discovery efficiency fixture. After the baseline Git commit, the harness creates 600 ignored files under `src/MiniCatalog/obj/idd-discovery-noise` plus one untracked non-ignored probe under `src/MiniCatalog`. The executor must use Git-visible, scoped discovery rather than broad physical recursive scans. The evaluation reads executor `stdout.log` command events, rejects broad recursive discovery and ignore-bypassing searches, verifies scoped `git ls-files --cached --others --exclude-standard` usage, and checks that ignored noise does not inflate Git discovery output. A compact `executor-discovery-evidence.json` records the observed commands and per-command output sizes; the raw attempt logs remain authoritative.

Run it manually with:

```bat
run-live-factory-evals.bat
```

`Check.ps1 -Mode Live` enables `IDD_RUN_LIVE_FACTORY_EVALS=1` and selects only:

```text
Category=LiveFactoryEval
```

A normal `[Fact]` added to this project is therefore not part of the token-consuming live run. Without the live opt-in, `LiveFactoryEvalFact` remains skipped.

The evaluation checks observable behavior: the prepared product baseline does not yet satisfy the requested behavior, Factory reaches `COMPLETED`, at least two work items complete, final verification passes, independent final build/tests pass, and durable intent plus protected verification input remain unchanged. It also keeps the public blocking transport assertions: one `factory_run`, no `factory_status` polling, and no completed model turn while the blocking call is active.

Process execution, cancellation, timeout, process-tree termination, transport, workflow/state transitions, retries, continuation, persistence/recovery, verification mechanics, intent propagation, and other mechanical contracts belong in deterministic `Idd.Factory.Tests`. LiveTests should not grow separate real-model scenarios for behavior that can be checked reliably without an LLM.

On failure, raw evidence is retained under `artifacts/factory-evals/<run-id>/`, including the workspace, Factory current/result state, Codex `events.jsonl`, stderr/final response, verification logs, progress log, executor discovery evidence, and git status/diff.

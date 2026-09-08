# Idd.Factory.LiveTests

This project is intentionally a small safety net around behavior that requires a real Codex host or real OS sandbox/process integration. Deterministic Factory state, scheduling, parsing, retry, persistence, MCP mapping, and process-termination behavior belong in `Idd.Factory.Tests`.

The ordinary live suite contains two token-consuming scenarios:

- `FactoryEndToEndLiveTests`: installs the generated plugin and runs the TwoStepCatalog case through real Codex, `factory_run`, Factory Runtime, semantic workers, final product verification, and the blocking-transport assertions.
- `WorkspaceWriteLiveTests`: proves the supported `workspace-write` Codex launch can create and modify real workspace files.

Blocking transport is deliberately asserted from the same TwoStepCatalog invocation: one `factory_run`, zero `factory_status` calls, and zero completed model turns while `factory_run` is active. `MinimalCodexTraceReader` reads only those events.

Cancellation/process-tree cleanup does not have a separate token-consuming Codex scenario. Production termination behavior is exercised directly and deterministically by `Idd.Factory.Tests` (including `BatchRuntimeTests` termination diagnostics and MCP/runtime process tests), which provides a cheaper and more precise regression signal.

On failure the run directory under `artifacts/factory-evals/` retains raw evidence: Codex `events.jsonl`, stderr and final response, Factory current/result state, verification command output, progress log, and git status/diff. LiveTests intentionally generate no agent-trace, efficiency, rollout, launch-profile, or derived report artifacts.

Before this simplification, the LiveTests source had 31 Infrastructure files, 7 Models files, 2 Environment files, and 18 test source files. The target structure removes the Models and Environments layers and keeps only a handful of process/workspace/install/trace helpers plus the two real live scenarios.

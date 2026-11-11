# Idd.Factory.LiveTests

These are explicit token-consuming Codex end-to-end evaluations. They are
skipped unless `IDD_RUN_LIVE_FACTORY_EVALS=1` (core cases) or
`IDD_RUN_LIVE_FACTORY_MARKETPLACE_EVALS=1` (read-only probe).

The main `TwoStepCatalog` scenario creates a disposable workspace and isolated
`CODEX_HOME`, runs ordinary marketplace generation, installs both plugins and
uses `idd-factory-run` to execute a multi-task project. It does **not** copy
or register custom Planner/Worker agent definitions under `.codex/agents/`.
The test checks the exact execution-profile mappings, agent correlation,
terminal results, independent .NET project tests and final Factory verification.
A separate missing-model scenario checks no silent fallback; a recovery case
checks a new Planner after final verification failure.

```powershell
$env:IDD_RUN_LIVE_FACTORY_EVALS = "1"
dotnet test tests/Idd.Factory.LiveTests/Idd.Factory.LiveTests.csproj --filter "Category=LiveFactoryEval"
```

The separate `Factory_MarketplaceRunsGenericInternalSkillsWithoutRegisteredRoles`
probe verifies that generated Planner/Worker `SKILL.md` files exist in the
installed Factory package, that fresh generic child spawn arguments contain
installed Skill paths and `fork_turns: none`, and that both children can be
spawned without role registration. Each is intentionally given an invalid
assignment and should return `BLOCKED` without modifying project files.

```powershell
$env:IDD_RUN_LIVE_FACTORY_MARKETPLACE_EVALS = "1"
dotnet test tests/Idd.Factory.LiveTests/Idd.Factory.LiveTests.csproj --filter "Category=LiveFactoryMarketplace"
```

The smoke probe alone does not replace full E2E evidence of child Skill reads,
correct native model/effort overrides and safe sequential workers. Live traces
and reports are stored under `artifacts/factory-evals/`. A real Desktop test
with no `agent_type` is required to verify the original Desktop regression;
Codex CLI and Claude require their own live acceptance runs. No passing Live
result should be inferred from a static generation test.

from pathlib import Path


def read(path: str) -> str:
    return Path(path).read_text(encoding="utf-8")


def write(path: str, text: str) -> None:
    Path(path).write_text(text, encoding="utf-8", newline="\n")


def replace_once(path: str, old: str, new: str) -> None:
    text = read(path)
    count = text.count(old)
    if count != 1:
        raise RuntimeError(f"Expected one match in {path}, got {count}: {old[:100]!r}")
    write(path, text.replace(old, new))


# Fix test closure: use the invocation's workspace instead of capturing the scenario
# before its fluent declaration has completed.
replace_once(
    "tests/Idd.Factory.Tests/TaskRelatedIntentTests.cs",
    'File.WriteAllText(Path.Combine(scenario.WorkspacePath, "first-attempt.txt"), "first");',
    'File.WriteAllText(Path.Combine(invocation.Workspace, "first-attempt.txt"), "first");')
replace_once(
    "tests/Idd.Factory.Tests/TaskRelatedIntentTests.cs",
    'File.WriteAllText(Path.Combine(scenario.WorkspacePath, "semantic-retry-ready.txt"), "ready");',
    'File.WriteAllText(Path.Combine(invocation.Workspace, "semantic-retry-ready.txt"), "ready");')
replace_once(
    "tests/Idd.Factory.Tests/TaskRelatedIntentTests.cs",
    'Assert.Single(exhausted.Invocations.Where(x => x.WorkItemId == "W000001"));',
    'Assert.Single(exhausted.Invocations, x => x.WorkItemId == "W000001");')

# Token analyzer: distinguish all agent invocations, semantic attempts, and technical restarts.
analyzer = "tools/factory-token-analysis/Program.cs"
replace_once(analyzer, "if (baseline.SchemaVersion != 1)", "if (baseline.SchemaVersion != 2)")
replace_once(
    analyzer,
    '''        var baseline = new BaselineDocument(\n            1,''',
    '''        var baseline = new BaselineDocument(\n            2,''')
replace_once(
    analyzer,
    '''        var metrics = new RunMetrics(\n            attempts.Length,\n            attempts.Sum(x => x.ToolBatches),''',
    '''        var metrics = new RunMetrics(\n            attempts.Length,\n            attempts.Count(x => x.Role == "executor"\n                && !string.IsNullOrWhiteSpace(x.WorkItem)\n                && !string.Equals(x.InvocationKind, "TechnicalRestart", StringComparison.Ordinal)),\n            attempts.Count(x => string.Equals(x.InvocationKind, "TechnicalRestart", StringComparison.Ordinal)),\n            attempts.Sum(x => x.ToolBatches),''')
replace_once(
    analyzer,
    '''        var workItem = String(invocation, "workItemId");\n        var launchReason = LaunchReason(role, workItem);''',
    '''        var workItem = String(invocation, "workItemId");\n        var invocationKind = String(invocation, "invocationKind");\n        var semanticAttempt = Int64(invocation, "semanticAttemptNumber");\n        var technicalRestart = Int64(invocation, "technicalRestartNumber");\n        var launchReason = LaunchReason(role, workItem, invocationKind);''')
replace_once(
    analyzer,
    '''            workItem,\n            launchReason,\n            inputChars,''',
    '''            workItem,\n            invocationKind,\n            semanticAttempt,\n            technicalRestart,\n            launchReason,\n            inputChars,''')
replace_once(
    analyzer,
    '''    private static string LaunchReason(string role, string workItem)\n    {\n        if (role == "planner") return "planning";\n        if (role == "executor")\n            return string.IsNullOrWhiteSpace(workItem) ? "execution" : $"work item {workItem}";\n        return role;\n    }''',
    '''    private static string LaunchReason(string role, string workItem, string invocationKind)\n    {\n        if (role == "planner") return "planning";\n        if (role == "executor")\n        {\n            var target = string.IsNullOrWhiteSpace(workItem) ? "execution" : $"work item {workItem}";\n            return string.IsNullOrWhiteSpace(invocationKind) ? target : $"{target} / {invocationKind}";\n        }\n        return role;\n    }''')
replace_once(
    analyzer,
    '''        Console.WriteLine("Attempt  Role             Reason                    InChars Batches Cmds ToolChars FailChars    Input   Cached      New Output Seconds");\n        Console.WriteLine("-------  ---------------  ------------------------ ------- ------- ---- --------- --------- -------- -------- -------- ------ -------");''',
    '''        Console.WriteLine("Attempt  Role      WorkItem  Kind             Sem Tech  InChars Batches Cmds ToolChars FailChars    Input   Cached      New Output Seconds");\n        Console.WriteLine("-------  --------  --------  --------------- ---- ---- ------- ------- ---- --------- --------- -------- -------- -------- ------ -------");''')
replace_once(
    analyzer,
    '''            Console.WriteLine(\n                $"{item.AttemptId,-7}  {Trim(item.Role, 15),-15}  {Trim(item.LaunchReason, 24),-24} " +\n                $"{item.InputChars,7} {item.ToolBatches,7} {item.Commands,4} {item.ToolOutputChars,9} {item.FailedToolOutputChars,9} " +\n                $"{item.InputTokens,8} {item.CachedInputTokens,8} {item.NewInputTokens,8} {item.OutputTokens,6} {FormatSeconds(item.Seconds),7}");''',
    '''            Console.WriteLine(\n                $"{item.AttemptId,-7}  {Trim(item.Role, 8),-8}  {Trim(item.WorkItem, 8),-8}  {Trim(item.InvocationKind, 15),-15} " +\n                $"{item.SemanticAttempt,4} {item.TechnicalRestart,4} {item.InputChars,7} {item.ToolBatches,7} {item.Commands,4} " +\n                $"{item.ToolOutputChars,9} {item.FailedToolOutputChars,9} {item.InputTokens,8} {item.CachedInputTokens,8} " +\n                $"{item.NewInputTokens,8} {item.OutputTokens,6} {FormatSeconds(item.Seconds),7}");''')
replace_once(
    analyzer,
    '''        Console.WriteLine($"Semantic attempts : {metrics.SemanticAttempts}");\n        Console.WriteLine($"Tool batches      : {metrics.ToolBatches}");''',
    '''        Console.WriteLine($"Agent invocations : {metrics.AgentInvocations}");\n        Console.WriteLine($"Semantic attempts : {metrics.SemanticAttempts}");\n        Console.WriteLine($"Technical restarts: {metrics.TechnicalRestarts}");\n        Console.WriteLine($"Tool batches      : {metrics.ToolBatches}");''')
replace_once(
    analyzer,
    '''    private static RunMetrics MedianMetrics(RunMetrics[] values) => new(\n        Median(values.Select(x => x.SemanticAttempts)),\n        Median(values.Select(x => x.ToolBatches)),''',
    '''    private static RunMetrics MedianMetrics(RunMetrics[] values) => new(\n        Median(values.Select(x => x.AgentInvocations)),\n        Median(values.Select(x => x.SemanticAttempts)),\n        Median(values.Select(x => x.TechnicalRestarts)),\n        Median(values.Select(x => x.ToolBatches)),''')
replace_once(
    analyzer,
    '''    string WorkItem,\n    string LaunchReason,''',
    '''    string WorkItem,\n    string InvocationKind,\n    long SemanticAttempt,\n    long TechnicalRestart,\n    string LaunchReason,''')
replace_once(
    analyzer,
    '''internal sealed record RunMetrics(\n    long SemanticAttempts,\n    long ToolBatches,''',
    '''internal sealed record RunMetrics(\n    long AgentInvocations,\n    long SemanticAttempts,\n    long TechnicalRestarts,\n    long ToolBatches,''')

# Workflow documentation.
workflow = "docs/factory-workflow.md"
replace_once(
    workflow,
    '''Runtime records actual changed paths, attempts, timestamps, exit codes, and\nverification evidence independently of the worker report.''',
    '''Runtime records actual changed paths, executor invocation identities, semantic-attempt\nand technical-restart counters, timestamps, exit codes, and verification evidence\nindependently of the worker report.''')
replace_once(
    workflow,
    '''Required task verification is deterministic. Failure retries the same immutable\ntask with its prior report and authoritative failure evidence. The same ordered\n`TaskRelatedIntentIds` are preserved for ordinary retries, verification retries,\ncommand-timeout/incomplete-command retries, and retry-budget extension. Planning\nis not invoked for an ordinary task-check failure.''',
    '''Required task verification is deterministic. An unexpected authoritative\nverification failure schedules a **Semantic Retry** of the same immutable task\nwith its prior trusted result and failure evidence. This starts a new semantic\nattempt and consumes `maxAttemptsPerTask`. Planning is not invoked for that retry.\n\nA restartable implementation execution-layer failure instead schedules a\n**Technical Restart**. `AGENT_COMMAND_TIMEOUT`, `AGENT_COMMAND_INCOMPLETE`, and a\ntransport failure without an already observed complete trusted result are the\nrestartable allowlist. The next executor receives bounded technical diagnostics\nand runs against the current workspace with a new unique `AttemptId`, but it keeps\nthe same semantic-attempt number and does not consume `maxAttemptsPerTask`. Its\ncumulative `TechnicalRestartCount` consumes the independent\n`maxTechnicalRestartsPerTask` budget. Exhaustion stops with\n`TECHNICAL_RESTART_BUDGET_EXHAUSTED`. Arbitrary protocol failures are not\nautomatically Technical Restarts.\n\nBoth Semantic Retry and Technical Restart preserve exactly the same ordered\n`TaskRelatedIntentIds`; neither invokes the planner to reconsider relevance.''')
replace_once(
    workflow,
    '''Factory state schema 13 introduces persisted related-intent metadata and is\nintentionally incompatible with active schema-12 runs. Missing metadata on an\nolder active work item is not interpreted as an authoritative empty set. The\nexisting legacy cancellation/restart policy applies; no implicit migration is\nintroduced.''',
    '''Factory state schema 14 persists separate `SemanticAttemptCount`,\n`TechnicalRestartCount`, `AdditionalSemanticAttemptBudget`, and the exact\n`NextInvocationKind` (`Initial`, `SemanticRetry`, or `TechnicalRestart`) in\naddition to related-intent metadata. This reason is saved before a replacement\nexecutor starts, so process recovery cannot reinterpret a scheduled Technical\nRestart as a Semantic Retry. Active older-schema runs follow the existing\n`LEGACY_FACTORY_STATE` cancellation/restart policy; no implicit migration is\nintroduced.''')
replace_once(
    workflow,
    '''Runtime budgets bound planning cycles, total work items, and attempts per task.''',
    '''Runtime budgets independently bound planning cycles, total work items, semantic\nattempts per task, and technical restarts per task.''')

# Token-efficiency documentation.
token_doc = "docs/factory-token-efficiency.md"
replace_once(token_doc, "six semantic attempts", "six agent invocations")
replace_once(
    token_doc,
    '''The analyzer reports per attempt:\n\n- role, skill, work item, and launch reason;''',
    '''The analyzer reports per executor/planner invocation:\n\n- role, skill, work item, invocation kind, semantic-attempt number, technical-restart number, and launch reason;''')
replace_once(
    token_doc,
    '''It also aggregates the same cost by role.\n\nThe most useful regression signals are:\n\n1. **Semantic attempts** — unexpected workers are expensive because every worker starts a fresh Codex agent.''',
    '''It also aggregates the same cost by role. Run-level structural metrics now distinguish\n`AgentInvocations`, `SemanticAttempts`, and `TechnicalRestarts`. Planner invocations\nare agent invocations but are not work-item semantic attempts; a Technical Restart\nis an executor invocation but is not a new semantic attempt.\n\nThe most useful regression signals are:\n\n1. **Agent invocations / semantic attempts / technical restarts** — unexpected workers are expensive because every worker starts a fresh Codex agent, while the split identifies whether cost came from new implementation reasoning or execution-layer instability.''')
replace_once(
    token_doc,
    '''semantic attempts\ntool batches''',
    '''agent invocations\nsemantic attempts\ntechnical restarts\ntool batches''')
replace_once(
    token_doc,
    '''The baseline stores the median of the structural and token metrics. Median is preferred to mean because one unusual agent trajectory should not redefine the expected cost.''',
    '''The baseline stores the median of the structural and token metrics. Baseline schema 2 records agent invocations, semantic attempts, and technical restarts separately; recreate older schema-1 baselines because their `SemanticAttempts` field counted invocation directories rather than the new semantic identity. Median is preferred to mean because one unusual agent trajectory should not redefine the expected cost.''')

# Canonical durable intent: replace the old rule that technical command failure consumed
# the ordinary semantic attempt budget.
intent1 = ".idd/intent/IDD-0001.spec-factory-runtime.md"
replace_once(
    intent1,
    '''- Every retry of the same work item preserves exactly the same ordered\n  `TaskRelatedIntentIds`, including ordinary semantic retry, verification-driven\n  retry, shell-command timeout/incomplete-command retry, and retry after budget\n  extension. Each invocation resolves those same IDs against current durable\n  intent and injects current complete document contents. Retry never invokes the\n  planner to reconsider semantic intent relevance.\n- A timed-out or abandoned shell command from an implementation executor also\n  consumes an ordinary attempt and retries the same immutable task within that\n  budget. The next executor receives the preserved command-failure evidence and\n  must treat diagnosis and removal of the hang as part of completing the current\n  task before rerunning or trusting the affected check. This failure does not\n  create a worker-selected task or invoke planning between tasks.''',
    '''- Every replacement executor invocation for the same work item preserves exactly\n  the same ordered `TaskRelatedIntentIds`, whether it is a Semantic Retry, a\n  Technical Restart, or a retry after semantic-budget extension. Each invocation\n  resolves those same IDs against current durable intent and injects current\n  complete document contents. Neither retry kind invokes the planner to\n  reconsider semantic intent relevance.\n- Runtime distinguishes **Semantic Retry** from **Technical Restart**\n  deterministically. An unexpected authoritative task-verification failure occurs\n  after a trusted semantic result and starts a new semantic implementation attempt,\n  consuming the ordinary `maxAttemptsPerTask` budget. A restartable technical\n  execution failure means no trusted semantic result was produced and starts a new\n  executor invocation for the same semantic attempt instead.\n- Restartable Technical Restart outcomes for implementation execution are\n  `AGENT_COMMAND_TIMEOUT`, `AGENT_COMMAND_INCOMPLETE`, and\n  `AGENT_TRANSPORT_FAILURE` only when no complete trusted result was observed.\n  Arbitrary protocol exceptions are not restartable merely because they are\n  protocol exceptions. Technical Restart never consumes the semantic attempt\n  budget, remains available when that semantic budget is fully used, and consumes\n  a separate cumulative `maxTechnicalRestartsPerTask` budget. Exhaustion is the\n  terminal `TECHNICAL_RESTART_BUDGET_EXHAUSTED` blocker and cannot be extended by\n  `factory_retry`.''')
replace_once(
    intent1,
    '''- State retains only what is necessary for the active run, current batch,\n  current task, immutable completed work, planning cycles, attempts,\n  verification, continuations, recovery, budgets, final verification, and\n  deterministic blockers.''',
    '''- State retains only what is necessary for the active run, current batch,\n  current task, immutable completed work, planning cycles, separate semantic and\n  technical execution counters, verification, continuations, recovery, budgets,\n  final verification, and deterministic blockers. For a current work item it\n  persists `SemanticAttemptCount`, cumulative `TechnicalRestartCount`,\n  `AdditionalSemanticAttemptBudget`, and the exact `NextInvocationKind`.''')
replace_once(
    intent1,
    '''- Semantic dispatch persists runtime-owned invocation identity before backend\n  execution. Worker text cannot supply or alter runtime identity. Persisted\n  results are bound to the exact invocation and consumed at most once.''',
    '''- Semantic dispatch persists runtime-owned invocation identity before backend\n  execution. Every executor invocation has a new unique `AttemptId` plus its work\n  item, invocation kind, semantic-attempt number, and technical-restart number.\n  Worker text cannot supply or alter runtime identity. Persisted results are bound\n  to the exact invocation and consumed at most once.\n- A restartable technical failure preserves runtime-observed workspace changes and\n  bounded typed diagnostics but does not promote partial semantic output to a\n  trusted prior result or send it to verification. Technical Restart runs against\n  the current workspace; it is not an automatic rollback. The restart reason is\n  persisted before the replacement invocation so process recovery resumes the\n  same invocation kind without heuristic reconstruction.''')
replace_once(
    intent1,
    '''- This work-item metadata changes active-state semantics. Runtime schema 13 does\n  not implicitly migrate schema-12 active state or interpret missing\n  `TaskRelatedIntentIds` on an older existing task as an authoritative empty set.\n  Existing legacy cancellation/restart behavior remains the upgrade path.''',
    '''- This work-item invocation metadata changes active-state semantics. Runtime\n  schema 14 does not implicitly migrate schema-13 active state or reinterpret its\n  old `AttemptCount` as either semantic or technical history. Existing legacy\n  cancellation/restart behavior remains the upgrade path.''')
replace_once(
    intent1,
    '''- Planning cycles, total work items, and attempts per task are bounded and stop\n  infinite execution with diagnostic state.''',
    '''- Planning cycles, total work items, semantic attempts per task, and cumulative\n  technical restarts per task are independently bounded and stop infinite execution\n  with diagnostic state.''')
replace_once(
    intent1,
    '''- A semantic worker cannot complete successfully while one of its shell\n  commands is still active. A command timeout or abandoned command terminates\n  the worker process tree, preserves bounded diagnostic evidence, rejects any\n  partial semantic result, and retries the same work item before later batch\n  work can run.''',
    '''- A semantic worker cannot complete successfully while one of its shell\n  commands is still active. A command timeout or abandoned command terminates\n  the worker process tree, preserves bounded diagnostic evidence and observed\n  workspace effects, rejects any partial semantic result, and schedules a\n  Technical Restart of the same semantic attempt before later batch work can run.''')

intent2 = ".idd/intent/IDD-0002.adr-runtime-owns-workflow.md"
replace_once(
    intent2,
    '''A semantic worker shell command is bounded independently from the worker's\nfinal response. If the command times out, or the worker exits while the command\nis still incomplete, runtime terminates the worker process tree, rejects partial\nresults, preserves bounded command diagnostics, and retries the same immutable\ntask with that evidence. It does not continue the batch or ask the planner to\ninvent a replacement task for work that has not completed.''',
    '''A semantic worker shell command is bounded independently from the worker's\nfinal response. If the implementation executor command times out, or the worker\nexits while the command is still incomplete, runtime terminates the worker process\ntree, rejects partial semantic results, preserves bounded command diagnostics and\nobserved workspace effects, and schedules a **Technical Restart**. A restartable\ntransport failure without a complete trusted result follows the same path. The\nreplacement executor receives a new `AttemptId` but continues the same semantic\nattempt against the current workspace. It does not consume the semantic attempt\nbudget, continue the batch, or ask the planner to invent a replacement task.''')
replace_once(
    intent2,
    '''Authoritative state is the minimum machine-owned representation of immutable\ncompleted history, zero or one current task, the ordered current batch,\nplanning-cycle and attempt budgets, verification, continuations, blockers, and\nrecovery.''',
    '''Authoritative state is the minimum machine-owned representation of immutable\ncompleted history, zero or one current task, the ordered current batch, planning\ncycles, separate semantic-attempt and technical-restart budgets and counters, the\nexact next invocation kind, verification, continuations, blockers, and recovery.''')
replace_once(
    intent2,
    '''- Runtime state and budgets remain semantic-neutral: maximum planning cycles,\n  total work items, and attempts per task replace specialized corrective and\n  replan counters.''',
    '''- Runtime state and budgets remain deterministic: maximum planning cycles, total\n  work items, semantic attempts per task, and cumulative technical restarts per\n  task replace specialized corrective and replan counters. Verification failure\n  consumes semantic budget; restartable technical execution failure consumes only\n  technical-restart budget. `factory_retry` extends only semantic budget.''')
replace_once(
    intent2,
    '''- A hung development check remains failure evidence for the current immutable\n  task. Same-task retry provides a fresh executor context for diagnosing the\n  hang without adding a second worker-controlled or timeout-specific planning\n  protocol.''',
    '''- A hung development check remains technical failure evidence for the current\n  immutable task. Technical Restart provides a fresh executor context for the same\n  semantic attempt without adding a worker-controlled or timeout-specific planning\n  protocol, and its independent bounded budget prevents execution-layer instability\n  from being misreported as semantic retry exhaustion.''')

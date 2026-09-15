from pathlib import Path


def replace_between(path: str, start_marker: str, end_marker: str, replacement: str) -> None:
    file = Path(path)
    text = file.read_text(encoding="utf-8")
    start = text.index(start_marker)
    end = text.index(end_marker, start)
    text = text[:start] + replacement + text[end:]
    file.write_text(text, encoding="utf-8", newline="\n")


def replace_once(path: str, old: str, new: str) -> None:
    file = Path(path)
    text = file.read_text(encoding="utf-8")
    if text.count(old) != 1:
        raise RuntimeError(f"Expected exactly one match in {path}, found {text.count(old)}")
    file.write_text(text.replace(old, new), encoding="utf-8", newline="\n")


state_machine = "src/runtime/Idd.Factory/Runtime/FactoryStateMachine.cs"
catch_start = '''        catch (AgentProtocolException exception) when (
            exception.Code is "AGENT_COMMAND_TIMEOUT" or "AGENT_COMMAND_INCOMPLETE")
        {'''
catch_end = '''

        CompleteSemanticAttempt(state, item, semantic);'''
new_catch = '''        catch (AgentProtocolException exception) when (
            TechnicalFailureClassifier.Classify(
                SemanticOperationKind.WorkItemExecution,
                exception) == TechnicalFailureClassification.RestartableTechnicalFailure)
        {
            var failedChangedPaths = await semanticExecution.RecoverFailedAttemptWorkspaceChangesAsync(
                state,
                item,
                attemptId,
                CancellationToken.None);
            ApplyChangedPaths(state, item, failedChangedPaths);

            var diagnosticReference = await execution.PersistCommandFailureDiagnosticAsync(
                attemptId,
                exception,
                CancellationToken.None);
            var boundedMessage = exception.Message.Length <= 4096
                ? exception.Message
                : exception.Message[..4096] + " [truncated]";
            var failure = new TechnicalFailureDiagnostic(
                attemptId,
                exception.Code,
                diagnosticReference,
                boundedMessage);
            if (!item.PriorTechnicalFailures.Any(x => x.FailedAttemptId == attemptId))
                item.PriorTechnicalFailures.Add(failure);
            if (!item.PriorAttemptDiagnosticRefs.Contains(
                    diagnosticReference,
                    StringComparer.Ordinal))
            {
                item.PriorAttemptDiagnosticRefs.Add(diagnosticReference);
            }

            item.NextInvocationKind = WorkItemInvocationKind.TechnicalRestart;
            state.CurrentAttemptId = null;
            item.CurrentAttemptId = null;
            state.PendingContinuation = null;
            state.Blocker = null;
            state.RunStatus = FactoryRunStatus.Running;
            state.CurrentPhase = CurrentWorkPhase.Ready;

            var technicalRestartBudget = context.Configuration.Limits.MaxTechnicalRestartsPerTask;
            if (item.TechnicalRestartCount >= technicalRestartBudget)
            {
                await context.Events.WriteAsync(
                    state.RunId,
                    "technical-restart-budget-exhausted",
                    new
                    {
                        workItemId = item.Id,
                        failedAttemptId = attemptId,
                        failureCode = exception.Code,
                        diagnosticReference,
                        technicalRestartCount = item.TechnicalRestartCount,
                        technicalRestartBudget
                    },
                    CancellationToken.None);
                var budgetException = new AgentProtocolException(
                    "TECHNICAL_RESTART_BUDGET_EXHAUSTED",
                    $"Work item {item.Id} exhausted its technical restart budget ({item.TechnicalRestartCount}/{technicalRestartBudget}) after {exception.Code} in {attemptId}. Diagnostic: {diagnosticReference}.");
                var outcome = await BlockAsync(
                    state,
                    stop.FromAgentProtocolException(state, budgetException),
                    CancellationToken.None);
                return new(
                    FactoryRuntimeState.Executing,
                    FactoryRuntimeState.Blocked,
                    "technical-restart-budget-exhausted",
                    outcome);
            }

            await context.Events.WriteAsync(
                state.RunId,
                "technical-restart-scheduled",
                new
                {
                    workItemId = item.Id,
                    failedAttemptId = attemptId,
                    failureCode = exception.Code,
                    semanticAttemptNumber = item.SemanticAttemptCount,
                    technicalRestartNumber = item.TechnicalRestartCount + 1,
                    technicalRestartBudget,
                    diagnosticReference
                },
                CancellationToken.None);
            await context.SaveAsync(state, CancellationToken.None);
            return TransitionFromCurrent(
                FactoryRuntimeState.Executing,
                state,
                "technical-restart-scheduled");
        }'''
replace_between(state_machine, catch_start, catch_end, new_catch)

# The old test exhausted semantic attempts with command timeouts. Timeouts now consume
# only the technical restart budget, so exercise factory_retry with verification-driven
# semantic retry instead.
task_intent = "tests/Idd.Factory.Tests/TaskRelatedIntentTests.cs"
method_start = '''    [Fact]
    public async Task RetryAfterBudgetExtensionReloadsCurrentIntentContentsWithoutReselection()
    {'''
method_end = '''    [Fact]
    public async Task StateRoundTripPreservesCurrentAndRemainingRelatedIntent()'''
new_method = '''    [Fact]
    public async Task RetryAfterBudgetExtensionReloadsCurrentIntentContentsWithoutReselection()
    {
        const string contract = "Implement current durable truth.";
        const string intentPath = ".idd/intent/IDD-0009.spec-current.md";
        var semanticCheck = OperatingSystem.IsWindows()
            ? "if (Test-Path semantic-retry-ready.txt) { exit 0 } else { exit 1 }"
            : "test -f semantic-retry-ready.txt";
        using var scenario = FactoryScenario.Create()
            .WithLimits(maxAttemptsPerTask: 1)
            .WithFile(intentPath, "ORIGINAL-INTENT-CONTENT")
            .WithVerificationCheck("semantic-retry-check", semanticCheck)
            .Planner($"# Task\\n{contract}\\n# TaskRelatedIntent\\nIDD-0009")
            .Execute(contract, invocation =>
            {
                Assert.Contains("ORIGINAL-INTENT-CONTENT", invocation.Input, StringComparison.Ordinal);
                File.WriteAllText(Path.Combine(scenario.WorkspacePath, "first-attempt.txt"), "first");
                return "First semantic implementation attempt.";
            })
            .Execute(contract, invocation =>
            {
                Assert.Contains("UPDATED-INTENT-CONTENT", invocation.Input, StringComparison.Ordinal);
                Assert.DoesNotContain("ORIGINAL-INTENT-CONTENT", invocation.Input, StringComparison.Ordinal);
                File.WriteAllText(Path.Combine(scenario.WorkspacePath, "semantic-retry-ready.txt"), "ready");
                return "Implemented after semantic budget extension.";
            })
            .Done();

        var exhausted = await scenario.Run();
        exhausted.ShouldBeBlockedBy("RETRY_BUDGET_EXHAUSTED");
        Assert.Single(exhausted.Invocations.Where(x => x.WorkItemId == "W000001"));
        Assert.Equal(1, exhausted.State.Current!.SemanticAttemptCount);
        Assert.Equal(0, exhausted.State.Current.TechnicalRestartCount);

        scenario.WithFile(intentPath, "UPDATED-INTENT-CONTENT");
        var result = await scenario.RetryExhausted(1);

        result.ShouldComplete();
        result.ShouldHaveAttemptCount("W000001", 2);
        result.ShouldHavePlanningCycles(2);
        var invocations = result.Invocations.Where(x => x.WorkItemId == "W000001").ToArray();
        Assert.Equal(WorkItemInvocationKind.Initial, invocations[0].InvocationKind);
        Assert.Equal(WorkItemInvocationKind.SemanticRetry, invocations[1].InvocationKind);
        Assert.Equal(2, invocations[1].SemanticAttemptNumber);
        Assert.Equal(0, invocations[1].TechnicalRestartNumber);
        Assert.Equal(["IDD-0009"], Assert.Single(result.State.Completed).TaskRelatedIntentIds);
    }

'''
replace_between(task_intent, method_start, method_end, new_method)

replace_once(
    "tests/Idd.Factory.Tests/BatchProtocolTests.cs",
    '''        Assert.Equal(3, configuration.SchemaVersion);
        Assert.Equal(4, configuration.Limits.MaxAttemptsPerTask);
        Assert.Equal(12, configuration.Limits.MaxPlanningCycles);''',
    '''        Assert.Equal(4, configuration.SchemaVersion);
        Assert.Equal(4, configuration.Limits.MaxAttemptsPerTask);
        Assert.Equal(1, configuration.Limits.MaxTechnicalRestartsPerTask);
        Assert.Equal(12, configuration.Limits.MaxPlanningCycles);''')

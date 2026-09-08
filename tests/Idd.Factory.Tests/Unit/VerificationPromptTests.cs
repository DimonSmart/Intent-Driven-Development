using System.Text.Json;
using Idd.Factory.Domain;
using Idd.Factory.Persistence;
using Idd.Factory.State;
using Idd.Factory.Verification;

namespace Idd.Factory.Tests;

public sealed class VerificationPromptTests
{
    private static readonly DateTimeOffset EvidenceTime = DateTimeOffset.Parse("2026-01-01T00:00:00Z");

    [Fact]
    public async Task RetryPromptShowsCurrentFailuresAndOnlyReferencesHistoricalFailures()
    {
        using var temp = new TestWorkspace();
        var oldA = await WriteEvidenceAsync(temp, "V-old-a", "check-a", "failed", "OLD_A_OUTPUT", 1);
        var oldB = await WriteEvidenceAsync(temp, "V-old-b", "check-b", "failed", "OLD_B_OUTPUT", 1);
        var currentA = await WriteEvidenceAsync(temp, "V-current-a", "check-a", "passed", "CURRENT_A_PASSED_OUTPUT", 0);
        var currentB = await WriteEvidenceAsync(temp, "V-current-b", "check-b", "failed", "CURRENT_B_FAILURE_OUTPUT", 1);
        var currentC = await WriteEvidenceAsync(temp, "V-current-c", "check-c", "failed", "CURRENT_C_FAILURE_OUTPUT", 1);
        var item = CreateItem([oldA, oldB, currentA, currentB, currentC], [currentA, currentB, currentC]);

        var text = Normalize(await FactoryTestRuntime.Create(temp.Path, new ScriptedAgentBackend())
            .BuildVerificationObservationsAsync(item, default));
        var current = Between(text, "Current authoritative verification failures:\n", "\n\nHistorical verification failures:");
        var historical = text[text.IndexOf("Historical verification failures:", StringComparison.Ordinal)..];

        Assert.DoesNotContain("check-a", current, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Check: check-b", current, StringComparison.Ordinal);
        Assert.Contains("CURRENT_B_FAILURE_OUTPUT", current, StringComparison.Ordinal);
        Assert.Contains("Check: check-c", current, StringComparison.Ordinal);
        Assert.Contains("CURRENT_C_FAILURE_OUTPUT", current, StringComparison.Ordinal);
        Assert.DoesNotContain("CURRENT_A_PASSED_OUTPUT", text, StringComparison.Ordinal);
        Assert.DoesNotContain("OLD_A_OUTPUT", text, StringComparison.Ordinal);
        Assert.DoesNotContain("OLD_B_OUTPUT", text, StringComparison.Ordinal);
        Assert.Contains($"Evidence: {oldA}", historical, StringComparison.Ordinal);
        Assert.Contains($"Evidence: {oldB}", historical, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CurrentOutputIsBoundedAndHistoricalOutputIsNeverRepeated()
    {
        using var temp = new TestWorkspace();
        var historical = await WriteEvidenceAsync(
            temp, "V-old", "check-a", "failed", "HISTORICAL_UNIQUE_MARKER_" + new string('H', 20_000), 1);
        var current = await WriteEvidenceAsync(
            temp, "V-current", "check-a", "failed", "CURRENT_BEGIN_" + new string('C', 20_000) + "_CURRENT_TAIL_MUST_BE_TRUNCATED", 1);

        var text = await FactoryTestRuntime.Create(temp.Path, new ScriptedAgentBackend())
            .BuildVerificationObservationsAsync(CreateItem([historical, current], [current]), default);

        Assert.Contains("CURRENT_BEGIN_", text, StringComparison.Ordinal);
        Assert.Contains("[verification output truncated; see evidence artifact]", text, StringComparison.Ordinal);
        Assert.DoesNotContain("CURRENT_TAIL_MUST_BE_TRUNCATED", text, StringComparison.Ordinal);
        Assert.DoesNotContain("HISTORICAL_UNIQUE_MARKER_", text, StringComparison.Ordinal);
        Assert.Contains($"Evidence: {historical}", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task HistoricalGrowthAddsMetadataRatherThanRepeatingOutputBlocks()
    {
        using var temp = new TestWorkspace();
        var historical = new List<string>();
        for (var index = 0; index < 5; index++)
            historical.Add(await WriteEvidenceAsync(temp, $"V-old-{index}", $"check-old-{index}", "failed", $"HISTORICAL_OUTPUT_{index}_" + new string('X', 6_000), 1));
        var current = await WriteEvidenceAsync(temp, "V-current", "check-current", "failed", "CURRENT_" + new string('C', 6_000), 1);
        var runtime = FactoryTestRuntime.Create(temp.Path, new ScriptedAgentBackend());

        var oneHistorical = await runtime.BuildVerificationObservationsAsync(CreateItem([historical[0], current], [current]), default);
        var fiveHistorical = await runtime.BuildVerificationObservationsAsync(CreateItem([.. historical, current], [current]), default);

        Assert.True(fiveHistorical.Length - oneHistorical.Length < 2_000,
            $"Historical metadata grew by {fiveHistorical.Length - oneHistorical.Length} characters.");
        for (var index = 0; index < historical.Count; index++)
            Assert.DoesNotContain($"HISTORICAL_OUTPUT_{index}_", fiveHistorical, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PersistedLatestCycleProducesIdenticalPromptAfterRestart()
    {
        using var temp = new TestWorkspace();
        var historical = await WriteEvidenceAsync(temp, "V-old", "check-a", "failed", "OLD_OUTPUT", 1);
        var current = await WriteEvidenceAsync(temp, "V-current", "check-b", "failed", "CURRENT_OUTPUT", 1);
        var item = CreateItem([historical, current], [current]);
        var configuration = FactoryTestRuntime.Configuration();
        var state = new FactoryState
        {
            MethodologyVersion = "test",
            RuntimeVersion = "test",
            RunId = "restart-test",
            FactoryConfigurationHash = configuration.Hash,
            RequestPath = "request.md",
            PlanningCycleCount = 1,
            Current = item,
            CurrentPhase = CurrentWorkPhase.Ready
        };
        var currentDirectory = Path.Combine(temp.Path, ".idd", "factory", "current");
        var store = new FileFactoryStateStore(currentDirectory, new FactoryStateValidator());
        await store.CreateAsync(state, default);

        var before = await FactoryTestRuntime.Create(temp.Path, new ScriptedAgentBackend(), configuration: configuration)
            .BuildVerificationObservationsAsync(item, default);
        var reloaded = await store.LoadAsync(default);
        var after = await FactoryTestRuntime.Create(temp.Path, new ScriptedAgentBackend(), configuration: configuration)
            .BuildVerificationObservationsAsync(reloaded!.Current!, default);

        Assert.Equal(before, after);
        Assert.Equal([current], reloaded.Current!.LastVerificationEvidenceRefs);
        Assert.Equal([historical, current], reloaded.Current.VerificationEvidenceRefs);
    }

    [Fact]
    public async Task PlannerReceivesWorkspaceResolvableEvidenceButStateKeepsRunRelativeReferences()
    {
        using var scenario = FactoryScenario.Create();
        scenario.WithVerificationCheck("subtask-pass", "exit 0")
            .Plan("Implement A.")
            .Execute("Implement A.")
            .Planner(invocation =>
            {
                Assert.Contains("Verification evidence: .idd/factory/current/verification/", invocation.Input, StringComparison.Ordinal);
                Assert.Contains("Authoritative verification evidence summaries:", invocation.Input, StringComparison.Ordinal);
                Assert.Contains("Check: subtask-pass", invocation.Input, StringComparison.Ordinal);
                Assert.Contains("Exit code: 0", invocation.Input, StringComparison.Ordinal);
                return "# Done";
            });

        var result = await scenario.Run("Implement and verify A.");

        result.ShouldComplete();
        result.ShouldExecute("Implement A.");
        Assert.NotEmpty(result.State.VerificationEvidenceRefs);
        Assert.All(result.State.VerificationEvidenceRefs,
            reference => Assert.StartsWith("verification/", reference, StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("verification/missing.json", "missing.json")]
    [InlineData("../../outside.json", "outside Factory run directory")]
    public async Task InvalidEvidenceReferenceFailsBeforePlannerInvocation(string evidenceReference, string expectedMessage)
    {
        using var temp = new TestWorkspace();
        if (evidenceReference.StartsWith("..", StringComparison.Ordinal)) temp.Write(".idd/outside.json", "{}");
        await SeedFailedFinalPlanningStateAsync(temp, evidenceReference);
        var backend = new ScriptedAgentBackend();

        var error = await Assert.ThrowsAsync<FactoryStateException>(() =>
            FactoryTestRuntime.Create(temp.Path, backend).ContinueAsync(default));

        Assert.Equal("CORRUPT_FACTORY_STATE", error.Code);
        Assert.Contains(expectedMessage, error.Message, StringComparison.Ordinal);
        Assert.Empty(backend.Invocations);
    }

    [Fact]
    public async Task PlanningWithoutEvidenceUsesAnExplicitEmptyEvidenceContext()
    {
        using var temp = new TestWorkspace();
        await SeedFailedFinalPlanningStateAsync(temp, null);
        var backend = new ScriptedAgentBackend();
        backend.Reply(invocation =>
        {
            Assert.Contains("Authoritative verification evidence summaries:\nnone\n", Normalize(invocation.Input), StringComparison.Ordinal);
            return "# Done";
        });

        var outcome = await FactoryTestRuntime.Create(temp.Path, backend).ContinueAsync(default);

        Assert.Equal("COMPLETED", outcome.FactoryOutcome);
        Assert.Single(backend.Invocations);
    }

    [Fact]
    public async Task RuntimeTracksLatestVerificationCycleWithoutDeletingHistory()
    {
        var checkA = OperatingSystem.IsWindows()
            ? "if (Test-Path retry-1.txt) { exit 0 } else { Write-Output 'A_FIRST_FAILURE'; exit 1 }"
            : "if test -f retry-1.txt; then exit 0; else echo A_FIRST_FAILURE; exit 1; fi";
        var checkB = OperatingSystem.IsWindows()
            ? "if (Test-Path retry-2.txt) { exit 0 } elseif (Test-Path retry-1.txt) { Write-Output 'B_SECOND_FAILURE'; exit 1 } else { Write-Output 'B_FIRST_FAILURE'; exit 1 }"
            : "if test -f retry-2.txt; then exit 0; elif test -f retry-1.txt; then echo B_SECOND_FAILURE; exit 1; else echo B_FIRST_FAILURE; exit 1; fi";
        using var scenario = FactoryScenario.Create();
        scenario.WithVerification($$"""
            version: 1
            checks:
              check-a:
                run: >-
                  {{checkA}}
              check-b:
                run: >-
                  {{checkB}}
            default:
              use: []
            subtask:
              use:
                - check-a
                - check-b
            final:
              use: []
            """)
            .Plan("Implement A.")
            .Execute("Implement A.", _ =>
            {
                File.WriteAllText(Path.Combine(scenario.WorkspacePath, "initial-change.txt"), "initial");
                return "Initial implementation.";
            })
            .Execute("Implement A.", invocation =>
            {
                Assert.Contains("A_FIRST_FAILURE", invocation.Input, StringComparison.Ordinal);
                Assert.Contains("B_FIRST_FAILURE", invocation.Input, StringComparison.Ordinal);
                File.WriteAllText(Path.Combine(scenario.WorkspacePath, "retry-1.txt"), "fixed-a");
                return "Fixed check A.";
            })
            .Execute("Implement A.", invocation =>
            {
                Assert.Contains("B_SECOND_FAILURE", invocation.Input, StringComparison.Ordinal);
                Assert.DoesNotContain("A_FIRST_FAILURE", invocation.Input, StringComparison.Ordinal);
                File.WriteAllText(Path.Combine(scenario.WorkspacePath, "retry-2.txt"), "fixed-b");
                return "Fixed check B.";
            })
            .Done();

        var result = await scenario.Run("Implement A and make verification pass.");

        result.ShouldComplete();
        result.ShouldHaveAttemptCount("W000001", 3);
        var completed = Assert.Single(result.State.Completed);
        Assert.Equal(6, completed.VerificationEvidenceRefs.Count);
        Assert.Equal(completed.VerificationEvidenceRefs, result.State.VerificationEvidenceRefs);
    }

    private static PlannedWorkItem CreateItem(IEnumerable<string> evidenceRefs, IEnumerable<string> lastCycleRefs)
    {
        var item = new PlannedWorkItem { Id = "W000001", ContractPath = "work-items/W000001/contract.md" };
        item.VerificationEvidenceRefs.AddRange(evidenceRefs);
        item.LastVerificationEvidenceRefs.AddRange(lastCycleRefs);
        return item;
    }

    private static async Task<string> WriteEvidenceAsync(
        TestWorkspace temp,
        string evidenceId,
        string checkId,
        string status,
        string output,
        int exitCode)
    {
        var reference = $"verification/{evidenceId}.json";
        var path = Path.Combine(temp.Path, ".idd", "factory", "current", reference);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var evidence = new
        {
            schemaVersion = 2,
            evidenceId,
            checkId,
            checkDefinitionHash = "definition-hash",
            startedAt = EvidenceTime,
            finishedAt = EvidenceTime.AddMilliseconds(1),
            exitCode,
            status,
            output
        };
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(evidence, FactoryJson.Options));
        return reference;
    }

    private static async Task SeedFailedFinalPlanningStateAsync(TestWorkspace temp, string? evidenceReference)
    {
        temp.Write(".idd/verification.yaml", VerificationPolicyFixture.Empty());
        temp.Write(".idd/factory/current/request.md", "Reassess the failed final verification.");
        var state = new FactoryState
        {
            MethodologyVersion = "test",
            RuntimeVersion = "test",
            RunId = Guid.NewGuid().ToString("N"),
            FactoryConfigurationHash = "test-config-hash",
            RequestPath = "request.md",
            PlanningCycleCount = 1,
            FinalVerificationPlanRevision = 0,
            FinalVerificationPassed = false
        };
        if (evidenceReference is not null) state.VerificationEvidenceRefs.Add(evidenceReference);
        await new FileFactoryStateStore(
            Path.Combine(temp.Path, ".idd", "factory", "current"), new FactoryStateValidator()).CreateAsync(state, default);
    }

    private static string Normalize(string value) => value.Replace("\r\n", "\n");

    private static string Between(string value, string start, string end)
    {
        var startIndex = value.IndexOf(start, StringComparison.Ordinal);
        Assert.True(startIndex >= 0, $"Missing start marker: {start}");
        startIndex += start.Length;
        var endIndex = value.IndexOf(end, startIndex, StringComparison.Ordinal);
        Assert.True(endIndex >= 0, $"Missing end marker: {end}");
        return value[startIndex..endIndex];
    }
}

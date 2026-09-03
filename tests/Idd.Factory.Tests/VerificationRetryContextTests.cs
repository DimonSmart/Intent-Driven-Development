using System.Text.Json;
using Idd.Factory.Domain;
using Idd.Factory.Persistence;
using Idd.Factory.State;
using Idd.Factory.Verification;

namespace Idd.Factory.Tests;

public sealed class VerificationRetryContextTests
{
    private static readonly DateTimeOffset EvidenceTime = DateTimeOffset.Parse("2026-01-01T00:00:00Z");

    [Fact]
    public async Task FirstFailedVerificationIsCurrentAndIncludesFullOutput()
    {
        using var temp = new TestWorkspace();
        var current = await WriteEvidenceAsync(temp, "V-current", "check-a", "failed", "CURRENT_FAILURE_OUTPUT", 1);
        var item = CreateItem([current], [current]);

        var text = await FactoryRuntimeTestHarness.CreateRuntime(temp.Path, new FakeAgentBackend())
            .BuildVerificationObservationsAsync(item, default);

        Assert.Contains("Current authoritative verification failures:", text);
        Assert.Contains("Check: check-a", text);
        Assert.Contains($"Evidence: {current}", text);
        Assert.Contains("CURRENT_FAILURE_OUTPUT", text);
        Assert.Contains("Historical verification failures:\nnone", Normalize(text));
    }

    [Fact]
    public async Task RepeatedFailureKeepsOnlyLatestOutputAndHistoricalReference()
    {
        using var temp = new TestWorkspace();
        var old = await WriteEvidenceAsync(temp, "V-old", "check-a", "failed", "OLD_FAILURE_OUTPUT_MUST_NOT_BE_IN_RETRY_PROMPT", 1);
        var current = await WriteEvidenceAsync(temp, "V-current", "check-a", "failed", "CURRENT_FAILURE_OUTPUT", 2);
        var item = CreateItem([old, current], [current]);

        var text = await FactoryRuntimeTestHarness.CreateRuntime(temp.Path, new FakeAgentBackend())
            .BuildVerificationObservationsAsync(item, default);

        Assert.Contains("CURRENT_FAILURE_OUTPUT", text);
        Assert.DoesNotContain("OLD_FAILURE_OUTPUT_MUST_NOT_BE_IN_RETRY_PROMPT", text);
        Assert.Contains($"Evidence: {old}", text);
        Assert.Contains($"Evidence: {current}", text);
    }

    [Fact]
    public async Task MultipleFailuresFromLatestCycleAllKeepFullOutput()
    {
        using var temp = new TestWorkspace();
        var failedA = await WriteEvidenceAsync(temp, "V-a", "check-a", "failed", "CURRENT_A_OUTPUT", 1);
        var failedB = await WriteEvidenceAsync(temp, "V-b", "check-b", "failed", "CURRENT_B_OUTPUT", 2);
        var passedC = await WriteEvidenceAsync(temp, "V-c", "check-c", "passed", "PASSED_OUTPUT_MUST_NOT_BE_INCLUDED", 0);
        var item = CreateItem([failedA, failedB, passedC], [failedA, failedB, passedC]);

        var text = await FactoryRuntimeTestHarness.CreateRuntime(temp.Path, new FakeAgentBackend())
            .BuildVerificationObservationsAsync(item, default);

        Assert.Contains("CURRENT_A_OUTPUT", text);
        Assert.Contains("CURRENT_B_OUTPUT", text);
        Assert.DoesNotContain("PASSED_OUTPUT_MUST_NOT_BE_INCLUDED", text);
        Assert.Contains("Historical verification failures:\nnone", Normalize(text));
    }

    [Fact]
    public async Task ResolvedPreviousFailureIsHistoricalNotCurrent()
    {
        using var temp = new TestWorkspace();
        var oldA = await WriteEvidenceAsync(temp, "V-old-a", "check-a", "failed", "OLD_A_OUTPUT", 1);
        var oldB = await WriteEvidenceAsync(temp, "V-old-b", "check-b", "failed", "OLD_B_OUTPUT", 1);
        var currentA = await WriteEvidenceAsync(temp, "V-current-a", "check-a", "passed", "CURRENT_A_PASSED_OUTPUT", 0);
        var currentB = await WriteEvidenceAsync(temp, "V-current-b", "check-b", "failed", "CURRENT_B_FAILURE_OUTPUT", 1);
        var item = CreateItem([oldA, oldB, currentA, currentB], [currentA, currentB]);

        var text = Normalize(await FactoryRuntimeTestHarness.CreateRuntime(temp.Path, new FakeAgentBackend())
            .BuildVerificationObservationsAsync(item, default));
        var currentSection = Between(text, "Current authoritative verification failures:\n", "\n\nHistorical verification failures:");
        var historicalSection = text[(text.IndexOf("Historical verification failures:", StringComparison.Ordinal))..];

        Assert.DoesNotContain("Check: check-a", currentSection);
        Assert.Contains("Check: check-b", currentSection);
        Assert.Contains("CURRENT_B_FAILURE_OUTPUT", currentSection);
        Assert.DoesNotContain("OLD_A_OUTPUT", text);
        Assert.DoesNotContain("OLD_B_OUTPUT", text);
        Assert.Contains($"Evidence: {oldA}", historicalSection);
        Assert.Contains($"Evidence: {oldB}", historicalSection);
    }

    [Fact]
    public async Task CurrentOutputRemainsBoundedWhileHistoricalOutputIsNeverIncluded()
    {
        using var temp = new TestWorkspace();
        var historical = await WriteEvidenceAsync(
            temp,
            "V-old",
            "check-a",
            "failed",
            "HISTORICAL_UNIQUE_MARKER_" + new string('H', 20_000),
            1);
        var current = await WriteEvidenceAsync(
            temp,
            "V-current",
            "check-a",
            "failed",
            "CURRENT_BEGIN_" + new string('C', 20_000) + "_CURRENT_TAIL_MUST_BE_TRUNCATED",
            1);
        var item = CreateItem([historical, current], [current]);

        var text = await FactoryRuntimeTestHarness.CreateRuntime(temp.Path, new FakeAgentBackend())
            .BuildVerificationObservationsAsync(item, default);

        Assert.Contains("CURRENT_BEGIN_", text);
        Assert.Contains("[verification output truncated; see evidence artifact]", text);
        Assert.DoesNotContain("CURRENT_TAIL_MUST_BE_TRUNCATED", text);
        Assert.DoesNotContain("HISTORICAL_UNIQUE_MARKER_", text);
        Assert.Contains($"Evidence: {historical}", text);
    }

    [Fact]
    public async Task HistoricalMetadataGrowthDoesNotRepeatHistoricalOutputBlocks()
    {
        using var temp = new TestWorkspace();
        var historical = new List<string>();
        for (var index = 0; index < 5; index++)
        {
            historical.Add(await WriteEvidenceAsync(
                temp,
                $"V-old-{index}",
                $"check-old-{index}",
                "failed",
                $"HISTORICAL_OUTPUT_{index}_" + new string('X', 6_000),
                1));
        }
        var current = await WriteEvidenceAsync(temp, "V-current", "check-current", "failed", "CURRENT_" + new string('C', 6_000), 1);
        var runtime = FactoryRuntimeTestHarness.CreateRuntime(temp.Path, new FakeAgentBackend());

        var oneHistorical = await runtime.BuildVerificationObservationsAsync(CreateItem([historical[0], current], [current]), default);
        var fiveHistorical = await runtime.BuildVerificationObservationsAsync(CreateItem([.. historical, current], [current]), default);

        Assert.True(fiveHistorical.Length - oneHistorical.Length < 2_000, $"Historical metadata grew by {fiveHistorical.Length - oneHistorical.Length} characters.");
        for (var index = 0; index < historical.Count; index++)
            Assert.DoesNotContain($"HISTORICAL_OUTPUT_{index}_", fiveHistorical);
        Assert.Contains("CURRENT_", fiveHistorical);
    }

    [Fact]
    public async Task PersistedLatestCycleProducesIdenticalContextAfterRuntimeRestart()
    {
        using var temp = new TestWorkspace();
        var historical = await WriteEvidenceAsync(temp, "V-old", "check-a", "failed", "OLD_OUTPUT", 1);
        var current = await WriteEvidenceAsync(temp, "V-current", "check-b", "failed", "CURRENT_OUTPUT", 1);
        var item = CreateItem([historical, current], [current]);
        var configuration = FactoryRuntimeTestHarness.CreateConfiguration();
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

        var before = await FactoryRuntimeTestHarness.CreateRuntime(temp.Path, new FakeAgentBackend(), configuration: configuration)
            .BuildVerificationObservationsAsync(item, default);
        var reloaded = await store.LoadAsync(default);
        var after = await FactoryRuntimeTestHarness.CreateRuntime(temp.Path, new FakeAgentBackend(), configuration: configuration)
            .BuildVerificationObservationsAsync(reloaded!.Current!, default);

        Assert.Equal(before, after);
        Assert.Equal([current], reloaded.Current!.LastVerificationEvidenceRefs);
        Assert.Equal([historical, current], reloaded.Current.VerificationEvidenceRefs);
    }

    [Fact]
    public async Task RuntimeTracksLatestCycleAcrossRetriesWithoutDeletingEvidenceHistory()
    {
        using var temp = new TestWorkspace();
        var checkA = OperatingSystem.IsWindows()
            ? "if (Test-Path retry-1.txt) { exit 0 } else { Write-Output 'A_FIRST_FAILURE'; exit 1 }"
            : "if test -f retry-1.txt; then exit 0; else echo A_FIRST_FAILURE; exit 1; fi";
        var checkB = OperatingSystem.IsWindows()
            ? "if (Test-Path retry-2.txt) { exit 0 } elseif (Test-Path retry-1.txt) { Write-Output 'B_SECOND_FAILURE'; exit 1 } else { Write-Output 'B_FIRST_FAILURE'; exit 1 }"
            : "if test -f retry-2.txt; then exit 0; elif test -f retry-1.txt; then echo B_SECOND_FAILURE; exit 1; else echo B_FIRST_FAILURE; exit 1; fi";
        temp.Write(".idd/verification.yaml", $$"""
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
            """);

        var backend = new FakeAgentBackend();
        backend.Enqueue(_ => "# Task\n\nImplement A.");
        backend.Enqueue(_ =>
        {
            File.WriteAllText(Path.Combine(temp.Path, "initial-change.txt"), "initial");
            return "Initial implementation.";
        });
        backend.Enqueue(invocation =>
        {
            Assert.Contains("A_FIRST_FAILURE", invocation.Input);
            Assert.Contains("B_FIRST_FAILURE", invocation.Input);
            Assert.Contains("Historical verification failures:\nnone", Normalize(invocation.Input));
            File.WriteAllText(Path.Combine(temp.Path, "retry-1.txt"), "fixed-a");
            return "Fixed check A and continued investigating check B.";
        });
        backend.Enqueue(invocation =>
        {
            Assert.Contains("B_SECOND_FAILURE", invocation.Input);
            Assert.DoesNotContain("A_FIRST_FAILURE", invocation.Input);
            Assert.DoesNotContain("B_FIRST_FAILURE", invocation.Input);
            Assert.Contains("Historical verification failures:", invocation.Input);
            File.WriteAllText(Path.Combine(temp.Path, "retry-2.txt"), "fixed-b");
            return "Fixed the remaining current verification failure.";
        });
        backend.Enqueue(_ => "# Done");

        var outcome = await FactoryRuntimeTestHarness.CreateRuntime(temp.Path, backend)
            .RunRequestAsync("Implement A and make verification pass.", "test", default);

        Assert.Equal("COMPLETED", outcome.FactoryOutcome);
        Assert.Equal(3, backend.Invocations.Count(x => x.Capability == "implementation"));
        var finalState = JsonSerializer.Deserialize<FactoryState>(
            await File.ReadAllTextAsync(Path.Combine(outcome.ResultDirectory!, "state.json")),
            FactoryJson.Options)!;
        var completed = Assert.Single(finalState.Completed);
        Assert.Equal(6, completed.VerificationEvidenceRefs.Count);
        Assert.Equal(completed.VerificationEvidenceRefs, finalState.VerificationEvidenceRefs);
        Assert.All(completed.VerificationEvidenceRefs, reference => Assert.True(File.Exists(Path.Combine(outcome.ResultDirectory!, reference)), reference));
    }

    private static PlannedWorkItem CreateItem(IEnumerable<string> evidenceRefs, IEnumerable<string> lastCycleRefs)
    {
        var item = new PlannedWorkItem
        {
            Id = "W000001",
            ContractPath = "work-items/W000001/contract.md"
        };
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
        var evidence = new VerificationEvidence(
            2,
            evidenceId,
            checkId,
            "definition-hash",
            EvidenceTime,
            EvidenceTime.AddMilliseconds(1),
            exitCode,
            status,
            output);
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(evidence, FactoryJson.Options));
        return reference;
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

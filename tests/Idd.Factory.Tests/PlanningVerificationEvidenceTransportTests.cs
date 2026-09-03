using System.Text.Json;
using Idd.Factory.Domain;
using Idd.Factory.Persistence;
using Idd.Factory.State;

namespace Idd.Factory.Tests;

public sealed class PlanningVerificationEvidenceTransportTests
{
    [Fact]
    public async Task SecondPlannerReceivesWorkspaceResolvableEvidencePathsWithoutChangingStoredReferences()
    {
        using var temp = new TestWorkspace();
        temp.Write(".idd/verification.yaml", """
            version: 1
            checks:
              subtask-pass:
                run: exit 0
            default:
              use: []
            subtask:
              use:
                - subtask-pass
            final:
              use: []
            """);
        var backend = new FakeAgentBackend();
        backend.Enqueue(_ => "# Task\n\nImplement A.");
        backend.Enqueue(_ => "Implemented A.");
        backend.Enqueue(invocation =>
        {
            var evidencePaths = ExtractEvidencePaths(invocation.Input);
            Assert.NotEmpty(evidencePaths);
            Assert.All(evidencePaths, path =>
            {
                Assert.True(path.StartsWith(".idd/factory/current/verification/", StringComparison.Ordinal), path);
                Assert.True(File.Exists(Path.Combine(temp.Path, path.Replace('/', Path.DirectorySeparatorChar))), path);
            });
            Assert.Contains("Verification evidence: .idd/factory/current/verification/", invocation.Input);
            Assert.DoesNotContain("Verification evidence: verification/", invocation.Input);
            Assert.DoesNotContain("\n- verification/", invocation.Input.Replace("\r\n", "\n"));
            return "";
        });

        var outcome = await FactoryRuntimeTestHarness.CreateRuntime(temp.Path, backend)
            .RunRequestAsync("Implement and verify A.", "test", default);

        Assert.Equal("COMPLETED", outcome.FactoryOutcome);
        Assert.Equal(["planning", "implementation", "planning"], backend.Invocations.Select(x => x.Capability));

        var state = JsonSerializer.Deserialize<FactoryState>(
            await File.ReadAllTextAsync(Path.Combine(outcome.ResultDirectory!, "state.json")),
            FactoryJson.Options)!;
        Assert.NotEmpty(state.VerificationEvidenceRefs);
        Assert.All(state.VerificationEvidenceRefs, reference => Assert.True(reference.StartsWith("verification/", StringComparison.Ordinal), reference));
        Assert.All(
            state.Completed.SelectMany(x => x.VerificationEvidenceRefs),
            reference => Assert.True(reference.StartsWith("verification/", StringComparison.Ordinal), reference));
    }

    [Fact]
    public async Task MissingEvidenceFailsBeforePlannerInvocation()
    {
        using var temp = new TestWorkspace();
        await SeedFailedFinalPlanningStateAsync(temp, "verification/missing.json");
        var backend = new FakeAgentBackend();
        backend.Enqueue(_ => "");

        var exception = await Assert.ThrowsAsync<FactoryStateException>(() =>
            FactoryRuntimeTestHarness.CreateRuntime(temp.Path, backend).ContinueAsync(default));

        Assert.Equal("CORRUPT_FACTORY_STATE", exception.Code);
        Assert.Contains("verification/missing.json", exception.Message);
        Assert.Contains(
            Path.Combine(temp.Path, ".idd", "factory", "current", "verification", "missing.json"),
            exception.Message);
        Assert.Empty(backend.Invocations);
    }

    [Fact]
    public async Task EvidencePathTraversalFailsBeforePlannerInvocation()
    {
        using var temp = new TestWorkspace();
        temp.Write(".idd/outside.json", "{}");
        await SeedFailedFinalPlanningStateAsync(temp, "../../outside.json");
        var backend = new FakeAgentBackend();
        backend.Enqueue(_ => "");

        var exception = await Assert.ThrowsAsync<FactoryStateException>(() =>
            FactoryRuntimeTestHarness.CreateRuntime(temp.Path, backend).ContinueAsync(default));

        Assert.Equal("CORRUPT_FACTORY_STATE", exception.Code);
        Assert.Contains("../../outside.json", exception.Message);
        Assert.Contains("outside Factory run directory", exception.Message);
        Assert.True(File.Exists(Path.Combine(temp.Path, ".idd", "outside.json")));
        Assert.Empty(backend.Invocations);
    }

    [Fact]
    public async Task PlanningWithoutEvidenceKeepsEmptyEvidenceContext()
    {
        using var temp = new TestWorkspace();
        await SeedFailedFinalPlanningStateAsync(temp, null);
        var backend = new FakeAgentBackend();
        backend.Enqueue(invocation =>
        {
            Assert.Contains("Authoritative verification evidence references:\n\n", invocation.Input.Replace("\r\n", "\n"));
            return "";
        });

        var outcome = await FactoryRuntimeTestHarness.CreateRuntime(temp.Path, backend).ContinueAsync(default);

        Assert.Equal("COMPLETED", outcome.FactoryOutcome);
        Assert.Single(backend.Invocations);
        Assert.Equal("planning", backend.Invocations[0].Capability);
    }

    private static async Task SeedFailedFinalPlanningStateAsync(TestWorkspace temp, string? evidenceReference)
    {
        temp.Write(".idd/verification.yaml", "version: 1\nchecks: {}\ndefault:\n  use: []\n");
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

        var current = Path.Combine(temp.Path, ".idd", "factory", "current");
        await new FileFactoryStateStore(current, new FactoryStateValidator()).CreateAsync(state, default);
    }

    private static string[] ExtractEvidencePaths(string input)
    {
        const string completedPrefix = "Verification evidence: ";
        var paths = new List<string>();
        foreach (var line in input.Replace("\r\n", "\n").Split('\n'))
        {
            if (line.StartsWith("- ", StringComparison.Ordinal))
            {
                var value = line[2..].Trim();
                if (value.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) paths.Add(value);
                continue;
            }

            if (!line.StartsWith(completedPrefix, StringComparison.Ordinal)) continue;
            foreach (var value in line[completedPrefix.Length..].Split(", ", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                if (value.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) paths.Add(value);
        }
        return paths.Distinct(StringComparer.Ordinal).ToArray();
    }
}

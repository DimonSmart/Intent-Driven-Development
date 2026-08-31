using System.Text.Json;
using Idd.Factory.Domain;
using static Idd.Factory.Tests.FactoryRuntimeTestHarness;

namespace Idd.Factory.Tests;

public sealed class VerificationAndReviewTests
{
    [Fact]
    public async Task ExpectedRedCompletesWorkWithoutSemanticClassifier()
    {
        using var temp = new TestWorkspace();
        temp.Write(".idd/verification.yaml", """
            version: 1
            checks:
              expected-red:
                run: dotnet build definitely-missing.csproj --nologo
            default:
              use: []
            final:
              use: []
            """);
        var backend = new FakeAgentBackend();
        backend.Enqueue(invocation => Envelope(invocation, "ready", new
        {
            workItems = new[]
            {
                new
                {
                    id = "A", sequence = 1, kind = "subtask", definitionState = "executable", capability = "implementation",
                    contractMarkdown = "# A", dependencies = Array.Empty<string>(), verificationCheckIds = new[] { "expected-red" },
                    verificationExpectations = new Dictionary<string, string> { ["expected-red"] = "may-fail" }
                }
            }
        }));
        backend.Enqueue(invocation => Envelope(invocation, "completed"));
        backend.Enqueue(invocation =>
        {
            var state = JsonSerializer.Deserialize<FactoryState>(File.ReadAllText(Path.Combine(temp.Path, ".idd", "factory", "current", "state.json")), FactoryJson.Options)!;
            var work = state.WorkItems.Single(x => x.Id == "A");
            Assert.Equal(WorkItemStatus.Completed, work.Status);
            Assert.Equal(VerificationDecision.ExpectedFailure, work.LastVerificationDecision);
            return Envelope(invocation, "approved");
        });

        var outcome = await CreateRuntime(temp.Path, backend).RunRequestAsync("Expected red is intentional", "test", default);

        Assert.Equal("COMPLETED", outcome.FactoryOutcome);
        Assert.Equal(3, backend.Invocations.Count);
    }

    [Fact]
    public async Task UnexpectedRegressionBlocksIntermediateWork()
    {
        using var temp = new TestWorkspace();
        temp.Write(".idd/verification.yaml", """
            version: 1
            checks:
              expected-red:
                run: dotnet build definitely-missing-a.csproj --nologo
              regression:
                run: dotnet build definitely-missing-b.csproj --nologo
            default:
              use: []
            final:
              use: []
            """);
        var backend = new FakeAgentBackend();
        backend.Enqueue(invocation => Envelope(invocation, "ready", new
        {
            workItems = new[]
            {
                new
                {
                    id = "A", sequence = 1, kind = "subtask", definitionState = "executable", capability = "implementation",
                    contractMarkdown = "# A", dependencies = Array.Empty<string>(), verificationCheckIds = new[] { "expected-red", "regression" },
                    verificationExpectations = new Dictionary<string, string> { ["expected-red"] = "may-fail" }
                }
            }
        }));
        backend.Enqueue(invocation => Envelope(invocation, "completed"));

        var outcome = await CreateRuntime(temp.Path, backend).RunRequestAsync("Unexpected red must block", "test", default);

        Assert.Equal("UNEXPECTED_VERIFICATION_FAILURE", outcome.FactoryOutcome);
        var state = await LoadState(temp.Path);
        Assert.Equal(VerificationDecision.UnexpectedFailure, state.WorkItems.Single().LastVerificationDecision);
        Assert.Equal(WorkItemStatus.Blocked, state.WorkItems.Single().Status);
    }

    [Fact]
    public async Task MayFailExpectationNeverWeakensFinalVerification()
    {
        using var temp = new TestWorkspace();
        temp.Write(".idd/verification.yaml", """
            version: 1
            checks:
              red:
                run: dotnet build definitely-missing.csproj --nologo
            default:
              use: []
            final:
              use:
                - red
            """);
        var backend = new FakeAgentBackend();
        backend.Enqueue(invocation => Envelope(invocation, "ready", new
        {
            workItems = new[]
            {
                new
                {
                    id = "A", sequence = 1, kind = "subtask", definitionState = "executable", capability = "implementation",
                    contractMarkdown = "# A", dependencies = Array.Empty<string>(), verificationCheckIds = new[] { "red" },
                    verificationExpectations = new Dictionary<string, string> { ["red"] = "may-fail" }
                }
            }
        }));
        backend.Enqueue(invocation => Envelope(invocation, "completed"));

        var outcome = await CreateRuntime(temp.Path, backend).RunRequestAsync("Final verification stays strict", "test", default);

        Assert.Equal("UNEXPECTED_VERIFICATION_FAILURE", outcome.FactoryOutcome);
        Assert.DoesNotContain(backend.Invocations, x => x.Role == "final-reviewer");
        var state = await LoadState(temp.Path);
        Assert.False(state.FinalVerificationPassed);
    }

    [Fact]
    public async Task FinalReviewCorrectionCreatesNewWorkAndNewReviewWithoutMutatingOldReview()
    {
        using var temp = new TestWorkspace();
        var backend = new FakeAgentBackend();
        backend.Enqueue(invocation => Envelope(invocation, "ready", new { workItems = new[] { Work("A", "implementation") } }));
        backend.Enqueue(invocation => Envelope(invocation, "completed"));
        backend.Enqueue(invocation => Envelope(invocation, "correction-required", new
        {
            correction = new { capability = "implementation", contractMarkdown = "# Correct the integrated defect" }
        }, "Integrated defect remains."));
        backend.Enqueue(invocation => Envelope(invocation, "completed"));
        backend.Enqueue(invocation => Envelope(invocation, "approved"));

        var outcome = await CreateRuntime(temp.Path, backend).RunRequestAsync("Review and correct", "test", default);

        Assert.Equal("COMPLETED", outcome.FactoryOutcome);
        var reviews = backend.Invocations.Where(x => x.Role == "final-reviewer").ToArray();
        Assert.Equal(2, reviews.Length);
        Assert.NotEqual(reviews[0].WorkItemId, reviews[1].WorkItemId);
        using var graph = JsonDocument.Parse(File.ReadAllText(Path.Combine(outcome.ResultDirectory!, "decomposition", "decomposition.json")));
        var items = graph.RootElement.GetProperty("workItems").EnumerateArray().ToArray();
        Assert.Contains(items, x => x.GetProperty("id").GetString() == reviews[0].WorkItemId && x.GetProperty("status").GetString() == "Completed");
        Assert.Contains(items, x => x.GetProperty("kind").GetString() == "corrective-subtask");
        Assert.Contains(items, x => x.GetProperty("id").GetString() == reviews[1].WorkItemId && x.GetProperty("status").GetString() == "Completed");
    }
}

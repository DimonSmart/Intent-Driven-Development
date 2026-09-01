using Idd.Factory.Agents;
using Idd.Factory.Domain;

namespace Idd.Factory.Tests;

public sealed class CanonicalSemanticResultContractTests
{
    private static readonly string RepositoryRoot = FindRepositoryRoot();

    public static TheoryData<string, string, string[]> SkillOutcomes => new()
    {
        { "planning", "src/canonical/skills/idd-factory-decompose-task.md", ["ready", "needs-clarification", "intent-required", "focused-handoff", "blocked"] },
        { "implementation", "src/canonical/skills/idd-factory-execute-subtask.md", ["completed", "additional-work-required", "global-replan-required", "intent-required", "blocked"] },
        { "research", "src/canonical/skills/idd-factory-research.md", ["completed", "additional-work-required", "global-replan-required", "intent-required", "blocked"] },
        { "semantic-review", "src/canonical/skills/idd-factory-review-checkpoint.md", ["approved", "correction-required", "additional-work-required", "global-replan-required", "intent-required", "blocked"] },
        { "final-review", "src/canonical/skills/idd-factory-review-task.md", ["approved", "correction-required", "additional-work-required", "global-replan-required", "intent-required", "blocked"] }
    };

    [Theory]
    [MemberData(nameof(SkillOutcomes))]
    public void CanonicalSkillOutcomeSetMatchesRuntimeContract(string capability, string skillPath, string[] expectedOutcomes)
    {
        var contract = SemanticResultContracts.Resolve(Invocation(capability));
        var skill = Read(skillPath);

        Assert.Equal(expectedOutcomes.OrderBy(x => x, StringComparer.Ordinal), contract.Outcomes.Keys.OrderBy(x => x, StringComparer.Ordinal));
        Assert.All(expectedOutcomes, outcome => Assert.Contains($"`{outcome}`", skill, StringComparison.Ordinal));
    }

    [Fact]
    public void PlanningReadyContractMatchesCanonicalTasksShape()
    {
        var skill = Read("src/canonical/skills/idd-factory-decompose-task.md");
        var outcome = SemanticResultContracts.Resolve(Invocation("planning")).ResolveOutcome("ready");

        Assert.Contains("top-level `tasks`", skill, StringComparison.Ordinal);
        Assert.Contains("tasks", outcome.AllowedFields);
        Assert.Contains("tasks", outcome.RequiredFields);
    }

    [Fact]
    public void ImplementationCompletedContractMatchesCanonicalSemanticFields()
    {
        var skill = Read("src/canonical/skills/idd-factory-execute-subtask.md");
        var outcome = SemanticResultContracts.Resolve(Invocation("implementation")).ResolveOutcome("completed");

        foreach (var field in new[] { "summary", "declaredChanges", "concerns", "verificationClaims" })
        {
            Assert.Contains($"`{field}", skill, StringComparison.Ordinal);
            Assert.Contains(field, outcome.AllowedFields);
        }
        Assert.Contains("summary", outcome.RequiredFields);
    }

    [Fact]
    public void ResearchCompletedContractMatchesCanonicalSemanticFields()
    {
        var skill = Read("src/canonical/skills/idd-factory-research.md");
        var outcome = SemanticResultContracts.Resolve(Invocation("research")).ResolveOutcome("completed");

        foreach (var field in new[] { "summary", "concerns", "payload" })
        {
            Assert.Contains($"`{field}", skill, StringComparison.Ordinal);
            Assert.Contains(field, outcome.AllowedFields);
        }
        Assert.Contains("summary", outcome.RequiredFields);
    }

    [Theory]
    [InlineData("semantic-review", "src/canonical/skills/idd-factory-review-checkpoint.md")]
    [InlineData("final-review", "src/canonical/skills/idd-factory-review-task.md")]
    public void ApprovedReviewHasNoUndocumentedSemanticPayload(string capability, string skillPath)
    {
        var skill = Read(skillPath);
        var outcome = SemanticResultContracts.Resolve(Invocation(capability)).ResolveOutcome("approved");

        Assert.Contains("`approved`", skill, StringComparison.Ordinal);
        Assert.Empty(outcome.AllowedFields);
        Assert.Empty(outcome.RequiredFields);
    }

    private static AgentInvocation Invocation(string capability)
    {
        var agent = FactoryCapabilityCatalog.Resolve(capability).Agent;
        return new AgentInvocation
        {
            RunId = "run",
            AttemptId = "A000001",
            Capability = capability,
            Role = agent.Role,
            Workspace = RepositoryRoot,
            RawResultPath = Path.Combine(RepositoryRoot, "raw-result.json"),
            SkillName = agent.SkillName,
            ExecutionProfile = agent.ExecutionProfile,
            SemanticResultSchema = SemanticResultContracts.SchemaForCapability(capability),
            Input = "test",
            StartedAt = DateTimeOffset.UnixEpoch
        };
    }

    private static string Read(string relativePath) =>
        File.ReadAllText(Path.Combine(RepositoryRoot, relativePath.Replace('/', Path.DirectorySeparatorChar)));

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Intent-Driven-Development.slnx"))) return directory.FullName;
        }

        throw new DirectoryNotFoundException("Could not locate the repository root.");
    }
}

using Xunit;

namespace Idd.Factory.LiveTests.Tests;

public sealed class CanonicalFactoryContractTests
{
    [Fact]
    public void RunSkillLaunchesPackagedRuntimeWithoutCoordinatorLoop()
    {
        var content = ReadRepoFile("src", "canonical", "skills", "idd-factory-run.md");
        Assert.Contains("runtime/idd-factory.dll", content);
        Assert.Contains("Do not select work items", content);
        Assert.Contains("Do not spawn semantic or coordinator agents", content);
        Assert.DoesNotContain("factory-step-coordinator", content);
    }

    [Fact]
    public void ObsoleteCoordinatorSourcesAreRemoved()
    {
        Assert.False(RepoFileExists("src", "canonical", "skills", "idd-factory-coordinate-step.md"));
        Assert.False(RepoFileExists("src", "canonical", "factory", "roles", "factory-step-coordinator.md"));
        Assert.False(RepoFileExists("src", "adapters", "codex", "factory-dispatch.md"));
        Assert.False(RepoFileExists("src", "adapters", "claude", "factory-dispatch.md"));
    }

    [Fact]
    public void DecomposerAndImplementerUseStructuredBoundedContracts()
    {
        var decomposition = ReadRepoFile("src", "canonical", "skills", "idd-factory-decompose-task.md");
        Assert.Contains("self-contained contract", decomposition);
        Assert.Contains("payload.workItems", decomposition);
        Assert.Contains("verificationCheckIds[]", decomposition);
        var implementation = ReadRepoFile("src", "canonical", "skills", "idd-factory-execute-subtask.md");
        Assert.Contains("Do not\n+read the full request".Replace("\n+", "\n"), implementation);
        Assert.Contains("Runtime verification is authoritative", implementation);
        Assert.Contains("`completed`, `needs-replan`, `blocked`, or `intent-required`", implementation);
    }

    [Fact]
    public void ReplannerCannotMutateRuntimeState()
    {
        var content = ReadRepoFile("src", "canonical", "skills", "idd-factory-replan.md");
        Assert.Contains("Do not modify completed work", content);
        Assert.Contains("Do not change operational status", content);
        Assert.Contains("payload.operations", content);
    }

    [Fact]
    public void RuntimeDefaultWorkflowHasRequiredRolesAndLimits()
    {
        var content = ReadRepoFile("src", "runtime", "Idd.Factory", "factory-workflow.yaml");
        foreach (var role in new[] { "task-decomposer", "implementer", "checkpoint-reviewer", "final-reviewer", "factory-replanner" }) Assert.Contains(role, content);
        Assert.Contains("maxAgentAttempts: 3", content); Assert.Contains("maxReplans: 3", content); Assert.Contains("maxCorrectiveCycles: 5", content);
        Assert.DoesNotContain("factory-step-coordinator", content);
    }

    private static bool RepoFileExists(params string[] parts)
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine([directory.FullName, .. parts]))) return true;
        return false;
    }

    private static string ReadRepoFile(params string[] parts)
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var path = Path.Combine([directory.FullName, .. parts]);
            if (File.Exists(path)) return File.ReadAllText(path).ReplaceLineEndings("\n");
        }
        throw new InvalidOperationException($"Could not locate repository file: {Path.Combine(parts)}");
    }
}

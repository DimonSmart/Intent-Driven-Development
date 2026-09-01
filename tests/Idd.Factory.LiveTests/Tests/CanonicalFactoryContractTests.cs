using Xunit;

namespace Idd.Factory.LiveTests.Tests;

public sealed class CanonicalFactoryContractTests
{
    // Keep only deterministic artifact-presence invariants here. Semantic contract
    // meaning and runtime behavior are covered by semantic evals and executable tests.

    [Fact]
    public void ObsoleteCoordinatorSourcesAreRemoved()
    {
        Assert.False(RepoFileExists("src", "canonical", "skills", "idd-factory-coordinate-step.md"));
        Assert.False(RepoFileExists("src", "canonical", "factory", "roles", "factory-step-coordinator.md"));
        Assert.False(RepoFileExists("src", "adapters", "codex", "factory-dispatch.md"));
        Assert.False(RepoFileExists("src", "adapters", "claude", "factory-dispatch.md"));
    }

    [Fact]
    public void FactoryWorkersHaveNoSeparateRoleContracts()
    {
        foreach (var role in new[] { "task-decomposer.md", "implementer.md", "researcher.md", "checkpoint-reviewer.md", "final-reviewer.md" })
            Assert.False(RepoFileExists("src", "canonical", "factory", "roles", role));
    }

    private static bool RepoFileExists(params string[] parts)
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine([directory.FullName, .. parts]))) return true;
        return false;
    }
}

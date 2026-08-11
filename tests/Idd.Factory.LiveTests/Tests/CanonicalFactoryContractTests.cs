using Idd.Factory.LiveTests.Infrastructure;
using Xunit;

namespace Idd.Factory.LiveTests.Tests;

public sealed class CanonicalFactoryContractTests
{
    [Fact]
    public void RootRunner_DelegatesInitializationToStepCoordinator()
    {
        var content = ReadRepoFile("src", "canonical", "skills", "idd-factory-run.md");

        Assert.Contains("Action: INITIALIZE", content);
        Assert.Contains("factory initialization", content);
        Assert.DoesNotContain("create `current/request.md`", content);
        Assert.DoesNotContain("create `current/` and `results/`", content);
    }

    [Fact]
    public void RootRunner_IsReadOnlyAndDoesNotOwnFactoryStateWrites()
    {
        var content = ReadRepoFile("src", "canonical", "skills", "idd-factory-run.md");

        Assert.Contains("The root context is read-only", content);
        Assert.Contains("must never modify repository or Factory-state files", content);
        Assert.DoesNotContain("write `request.md`", content, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("rename work-item", content, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CoordinatorOwnsInitializationAndStepTransitions()
    {
        var content = ReadRepoFile("src", "canonical", "skills", "idd-factory-coordinate-step.md");

        Assert.Contains("Action: INITIALIZE", content);
        Assert.Contains("Action: CONTINUE", content);
        Assert.Contains("Create `.idd/factory/current/` and `.idd/factory/results/`", content);
        Assert.Contains("activate the lowest `ready` item", content);
    }

    [Fact]
    public void DecomposerRequiresSelfContainedSubtasks()
    {
        var content = ReadRepoFile("src", "canonical", "skills", "idd-factory-decompose-task.md");

        Assert.Contains("concrete public contract or invariant", content);
        Assert.Contains("must remain self-contained", content);
        Assert.Contains("must not\n  require its worker to read the earlier work item", content);
    }

    [Fact]
    public void PlatformDispatchesDeclareExactRoleToSkillBindings()
    {
        var codex = ReadRepoFile("src", "adapters", "codex", "factory-dispatch.md");
        var claude = ReadRepoFile("src", "adapters", "claude", "factory-dispatch.md");

        foreach (var content in new[] { codex, claude })
        {
            Assert.Contains("`task-decomposer`", content);
            Assert.Contains("idd-factory-decompose-task", content);
            Assert.Contains("`factory-step-coordinator`", content);
            Assert.Contains("idd-factory-coordinate-step", content);
            Assert.Contains("`implementer`", content);
            Assert.Contains("idd-factory-execute-subtask", content);
            Assert.Contains("`checkpoint-reviewer`", content);
            Assert.Contains("idd-factory-review-checkpoint", content);
            Assert.Contains("`final-reviewer`", content);
            Assert.Contains("idd-factory-review-task", content);
        }

        Assert.Contains("<factory-skills-root>/idd-factory-coordinate-step", codex);
        Assert.DoesNotContain(".agents/skills/idd-factory-coordinate-step", codex);
        Assert.DoesNotContain("<factory-skills-root>/factory-step-coordinator", codex);
    }

    [Fact]
    public void CodexDispatch_DefinesUnambiguousContinueResumeRequest()
    {
        var content = ReadRepoFile("src", "adapters", "codex", "factory-dispatch.md");
        const string resumeRequest = "Resume request: Continue the current Factory run from persisted state and process exactly one next logical action.";

        Assert.Contains("Use this shape for `factory-step-coordinator` `INITIALIZE` dispatches", content);
        Assert.Contains("Use this shape for every `factory-step-coordinator` `CONTINUE` dispatch", content);
        Assert.Contains(resumeRequest, content);
        Assert.DoesNotContain("<resume-request", content, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Resume request:\n<", content, StringComparison.Ordinal);
    }

    [Fact]
    public void StepCoordinator_DoesNotDuplicateWorkerScope()
    {
        var content = ReadRepoFile("src", "canonical", "skills", "idd-factory-coordinate-step.md");

        Assert.Contains("Do not perform implementation, checkpoint review, or final review in this coordinator context", content);
        Assert.Contains("worker role by passing its skill and role-reference paths", content);
    }

    [Fact]
    public void Finalizer_IsCoordinatorOwnedNotRootOwned()
    {
        var content = ReadRepoFile("src", "canonical", "skills", "idd-factory-finalize-run.md");

        Assert.Contains("Factory step coordinator", content);
        Assert.DoesNotContain("root coordinator", content, StringComparison.OrdinalIgnoreCase);
    }

    private static string ReadRepoFile(params string[] pathParts)
        => File.ReadAllText(Path.Combine(RepositoryRootFinder.Find(), Path.Combine(pathParts)));
}

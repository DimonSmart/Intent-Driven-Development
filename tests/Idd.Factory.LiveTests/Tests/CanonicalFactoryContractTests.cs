using Xunit;

namespace Idd.Factory.LiveTests.Tests;

public sealed class CanonicalFactoryContractTests
{
    [Fact]
    public void CoordinateStep_DefinesImplicitFinalReviewTransition()
    {
        var content = ReadRepoFile("src", "canonical", "skills", "idd-factory-coordinate-step.md");

        Assert.Contains("the state is valid and\nfinal-review-ready", content);
        Assert.Contains("return `ADVANCED` with `Next: final review`", content);
        Assert.Contains("The following fresh `CONTINUE` performs it.", content);
        Assert.Contains("do not require or create a final\n  review work-item file", content);
    }

    [Fact]
    public void CoordinateStep_DefinesMutationRecoveryAndStructuralUniqueness()
    {
        var content = ReadRepoFile("src", "canonical", "skills", "idd-factory-coordinate-step.md");

        Assert.Contains("re-list `current/` and reread the affected file", content);
        Assert.Contains("Never repeat the same mutation until observed state proves it\n  did not already take effect", content);
        Assert.Contains("at most one `## Completion` section and at most one\n  `## Blocker` section", content);
    }

    [Fact]
    public void PlatformDispatchesDeclareExactRoleToSkillBindings()
    {
        var codex = ReadRepoFile("src", "adapters", "codex", "factory-dispatch.md");
        var claude = ReadRepoFile("src", "adapters", "claude", "factory-dispatch.md");

        foreach (var content in new[] { codex, claude })
        {
            Assert.Contains("`task-decomposer`", content);
            Assert.Contains("`idd-factory-decompose-task`", content);
            Assert.Contains("`factory-step-coordinator`", content);
            Assert.Contains("`idd-factory-coordinate-step`", content);
            Assert.Contains("`implementer`", content);
            Assert.Contains("`idd-factory-execute-subtask`", content);
            Assert.Contains("`checkpoint-reviewer`", content);
            Assert.Contains("`idd-factory-review-checkpoint`", content);
            Assert.Contains("`final-reviewer`", content);
            Assert.Contains("`idd-factory-review-task`", content);
        }

        Assert.Contains(".agents/skills/idd-factory-coordinate-step", codex);
        Assert.DoesNotContain(".agents/skills/factory-step-coordinator", codex);
    }

    private static string ReadRepoFile(params string[] parts)
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var path = Path.Combine([directory.FullName, .. parts]);
            if (File.Exists(path))
                return File.ReadAllText(path).ReplaceLineEndings("\n");
        }

        throw new InvalidOperationException($"Could not locate repository file: {Path.Combine(parts)}");
    }
}

using Idd.Factory.Workflow;

namespace Idd.Factory.Tests;

public sealed class WorkflowTests
{
    private const string Valid = """
        schemaVersion: 1
        name: test
        limits:
          maxAgentAttempts: 3
          maxReplans: 2
          maxCorrectiveCycles: 2
        steps:
          - id: decompose
            uses: factory.decompose
            agent: task-decomposer
            on:
              ready: execute
              blocked: $stop
          - id: execute
            uses: factory.execute
            handlers:
              subtask: implementer
              review-checkpoint: checkpoint-reviewer
            on:
              advanced: execute
              exhausted: final
              blocked: $stop
          - id: final
            uses: factory.final-review
            agent: final-reviewer
            on:
              approved: finalize
              blocked: $stop
          - id: finalize
            uses: factory.finalize
        """;

    [Fact] public void ValidWorkflowLoadsAndHashIsStable()
    {
        using var temp = new TestWorkspace(); var path = temp.Write("workflow.yaml", Valid); var loader = new WorkflowDefinitionLoader();
        var one = loader.Load(temp.Path, path); var two = loader.Load(temp.Path, path);
        Assert.Equal("test", one.Name); Assert.Equal(one.Hash, two.Hash); Assert.Equal(4, one.Steps.Count);
    }

    [Theory]
    [InlineData("schemaVersion: 2", "UNSUPPORTED_WORKFLOW_SCHEMA")]
    [InlineData("uses: factory.decompose", "UNKNOWN_WORKFLOW_HANDLER")]
    [InlineData("agent: task-decomposer", "UNKNOWN_AGENT_ROLE")]
    public void InvalidWorkflowIsRejected(string oldText, string expectedCode)
    {
        using var temp = new TestWorkspace(); var changed = oldText switch
        {
            "schemaVersion: 2" => Valid.Replace("schemaVersion: 1", oldText),
            "uses: factory.decompose" => Valid.Replace("uses: factory.execute", "uses: factory.unknown"),
            _ => Valid.Replace(oldText, "agent: mystery")
        };
        var exception = Assert.Throws<WorkflowException>(() => new WorkflowDefinitionLoader().Load(temp.Path, temp.Write("workflow.yaml", changed)));
        Assert.Equal(expectedCode, exception.Code);
    }

    [Fact] public void DuplicateStepAndMissingTargetAreRejected()
    {
        using var temp = new TestWorkspace(); var duplicate = Valid.Replace("- id: execute", "- id: decompose");
        Assert.Equal("DUPLICATE_WORKFLOW_STEP", Assert.Throws<WorkflowException>(() => new WorkflowDefinitionLoader().Load(temp.Path, temp.Write("duplicate.yaml", duplicate))).Code);
        var missing = Valid.Replace("ready: execute", "ready: absent");
        Assert.Equal("MISSING_TRANSITION_TARGET", Assert.Throws<WorkflowException>(() => new WorkflowDefinitionLoader().Load(temp.Path, temp.Write("missing.yaml", missing))).Code);
    }

    public static string ValidText => Valid;
}

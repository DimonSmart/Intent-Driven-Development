using Idd.Factory.LiveTests.Infrastructure;
using Xunit;

namespace Idd.Factory.LiveTests.Tests;

public sealed class FactoryDispatchContractTests
{
    [Fact]
    public void Validate_RejectsActionForNonCoordinatorRole()
    {
        var violations = FactoryDispatchContract.Validate(
            "task-decomposer",
            "Role:\ntask-decomposer\nAction:\nINITIALIZE\n");

        Assert.Contains(violations, violation => violation.Code == "DISPATCH_ACTION_FORBIDDEN");
    }

    [Fact]
    public void Validate_RejectsPhaseHintedContinueRequest()
    {
        var violations = FactoryDispatchContract.Validate(
            "factory-step-coordinator",
            "Role:\nfactory-step-coordinator\nAction:\nCONTINUE\nResume request:\nContinue through final integrated review and finalization.\n");

        Assert.Contains(violations, violation => violation.Code == "DISPATCH_CONTINUE_REQUEST_INVALID");
    }

    [Fact]
    public void Validate_RejectsLegacyRoleReferenceName()
    {
        var violations = FactoryDispatchContract.Validate(
            "task-decomposer",
            """
            Role:
            task-decomposer

            Read and follow:
            - .agents/skills/idd-factory-decompose-task/SKILL.md
            - .agents/skills/idd-factory-decompose-task/references/task-decomposer-role.md
            - .agents/skills/idd-factory-decompose-task/references/project-verification.md
            """);

        Assert.Contains(violations, violation => violation.Code == "DISPATCH_REFERENCE_CONTRACT");
    }

    [Fact]
    public void Validate_RejectsMissingAbsoluteGeneratedReference()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var skill = Path.Combine(root, ".agents", "skills", "idd-factory-decompose-task");
        var violations = FactoryDispatchContract.Validate(
            "task-decomposer",
            $"""
            Role:
            task-decomposer

            Read and follow:
            - {Path.Combine(skill, "SKILL.md")}
            - {Path.Combine(skill, "references", "roles", "task-decomposer.md")}
            - {Path.Combine(skill, "references", "project-verification.md")}
            """);

        Assert.Contains(violations, violation => violation.Code == "DISPATCH_REFERENCE_MISSING");
    }

    [Fact]
    public void Validate_AcceptsNeutralContinueWithGeneratedReferenceLayout()
    {
        var violations = FactoryDispatchContract.Validate(
            "factory-step-coordinator",
            $$"""
            Role:
            factory-step-coordinator

            Action:
            CONTINUE

            Resume request:
            {{FactoryDispatchContract.NeutralContinueRequest}}

            Read and follow:
            - .agents/skills/idd-factory-coordinate-step/SKILL.md
            - .agents/skills/idd-factory-coordinate-step/references/roles/factory-step-coordinator.md
            - .agents/skills/idd-factory-coordinate-step/references/project-verification.md
            """);

        Assert.Empty(violations);
    }
}

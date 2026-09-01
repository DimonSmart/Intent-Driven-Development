using Idd.Factory.LiveTests.Infrastructure;
using Xunit;

namespace Idd.Factory.LiveTests.Tests;

public sealed class FactoryDispatchContractTests
{
    [Fact]
    public void ValidateRejectsCoordinatorDispatch()
    {
        var violations = FactoryDispatchContract.Validate("factory-step-coordinator", "Role:\nfactory-step-coordinator\nAction:\nCONTINUE\n");
        Assert.Contains(violations, violation => violation.Code == "COORDINATOR_FORBIDDEN");
    }

    [Fact]
    public void ValidateRejectsActionForSemanticWorker()
    {
        var violations = FactoryDispatchContract.Validate("task-decomposer", "Role:\ntask-decomposer\nAction:\nINITIALIZE\n");
        Assert.Contains(violations, violation => violation.Code == "DISPATCH_ACTION_FORBIDDEN");
    }

    [Fact]
    public void ValidateAcceptsRoleOnlyDiagnosticPrompt()
    {
        Assert.Empty(FactoryDispatchContract.Validate("task-decomposer", "Role:\ntask-decomposer\n"));
    }
}

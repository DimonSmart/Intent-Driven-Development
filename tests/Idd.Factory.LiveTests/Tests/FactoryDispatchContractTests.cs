using Idd.Factory.LiveTests.Infrastructure;
using Xunit;

namespace Idd.Factory.LiveTests.Tests;

public sealed class FactoryDispatchContractTests
{
    [Fact]
    public void ValidateRejectsUnknownSemanticRole()
    {
        var violations = FactoryDispatchContract.Validate("reviewer", "Role:\nreviewer\n");
        Assert.Contains(violations, violation => violation.Code == "ROLE_FORBIDDEN");
    }

    [Fact]
    public void ValidateRejectsActionForSemanticWorker()
    {
        var violations = FactoryDispatchContract.Validate("planner", "Role:\nplanner\nAction:\nPLAN\n");
        Assert.Contains(violations, violation => violation.Code == "DISPATCH_ACTION_FORBIDDEN");
    }

    [Fact]
    public void ValidateAcceptsRoleOnlyDiagnosticPrompt()
    {
        Assert.Empty(FactoryDispatchContract.Validate("planner", "Role:\nplanner\n"));
    }
}

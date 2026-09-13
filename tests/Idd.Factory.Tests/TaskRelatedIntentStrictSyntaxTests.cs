using Idd.Factory.Agents;
using Idd.Factory.Runtime;

namespace Idd.Factory.Tests;

public sealed class TaskRelatedIntentStrictSyntaxTests
{
    [Theory]
    [InlineData(" IDD-0001")]
    [InlineData("IDD-0001 ")]
    [InlineData("\tIDD-0001")]
    public void ParserRejectsSurroundingWhitespaceInsteadOfNormalizingIt(string referenceLine)
    {
        var output = $"# Task\nImplement A.\n# TaskRelatedIntent\n{referenceLine}\n";

        var error = Assert.Throws<AgentProtocolException>(() => new PlannerMarkdownParser().Parse(output));

        Assert.Equal("MALFORMED_PLANNER_OUTPUT", error.Code);
    }
}

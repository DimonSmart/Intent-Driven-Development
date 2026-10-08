using System.Xml.Linq;
using Xunit;

namespace Idd.Factory.LiveTests;

public sealed class ProjectTestEvidenceTests
{
    [Theory]
    [InlineData(0, 0, 0, "Completed", false)]
    [InlineData(2, 1, 0, "Completed", false)]
    [InlineData(2, 2, 1, "Failed", false)]
    [InlineData(2, 2, 0, "Completed", true)]
    public void Requires_actual_execution_of_every_expected_test(int total, int executed, int failed,
        string outcome, bool valid)
    {
        XNamespace ns = "http://microsoft.com/schemas/VisualStudio/TeamTest/2010";
        var document = new XDocument(new XElement(ns + "TestRun",
            new XElement(ns + "ResultSummary", new XAttribute("outcome", outcome),
                new XElement(ns + "Counters", new XAttribute("total", total),
                    new XAttribute("executed", executed), new XAttribute("passed", executed - failed),
                    new XAttribute("failed", failed))),
            new XElement(ns + "Results", Enumerable.Range(0, executed).Select(index =>
                new XElement(ns + "UnitTestResult", new XAttribute("outcome",
                    index < failed ? "Failed" : "Passed"))))));
        var error = Record.Exception(() => ProjectTestEvidence.AssertPassed(document, expectedTests: 2));
        if (valid) Assert.Null(error);
        else Assert.NotNull(error);
    }
}

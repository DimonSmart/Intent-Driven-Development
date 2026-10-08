using System.Xml.Linq;
using Xunit;

namespace Idd.Factory.LiveTests;

internal static class ProjectTestEvidence
{
    public static void AssertPassed(string path, int expectedTests)
    {
        Assert.True(File.Exists(path), "Independent verification did not produce a TRX file.");
        AssertPassed(XDocument.Load(path), expectedTests);
    }

    internal static void AssertPassed(XDocument document, int expectedTests)
    {
        XNamespace ns = "http://microsoft.com/schemas/VisualStudio/TeamTest/2010";
        var summary = Assert.Single(document.Descendants(ns + "ResultSummary"));
        Assert.Equal("Completed", (string?)summary.Attribute("outcome"));
        var counters = Assert.Single(summary.Elements(ns + "Counters"));
        foreach (var name in new[] { "total", "executed", "passed" })
            Assert.Equal(expectedTests, (int?)counters.Attribute(name));
        Assert.Equal(0, (int?)counters.Attribute("failed"));
        var results = document.Descendants(ns + "UnitTestResult").ToArray();
        Assert.Equal(expectedTests, results.Length);
        Assert.All(results, result => Assert.Equal("Passed", (string?)result.Attribute("outcome")));
    }
}

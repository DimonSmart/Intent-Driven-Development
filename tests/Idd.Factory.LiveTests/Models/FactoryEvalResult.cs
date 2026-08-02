namespace Idd.Factory.LiveTests.Models;

public sealed class FactoryEvalResult
{
    public required string RunDirectory { get; init; }
    public required string Outcome { get; set; }
    public bool ProductPassed { get; set; }
    public bool FactoryPassed { get; set; }
    public bool CodexProcessPassed { get; set; }
    public bool ExecutionResponsePassed { get; set; }
    public bool FactoryResultExpected { get; set; }
    public string? FactoryOutcome { get; set; }
}

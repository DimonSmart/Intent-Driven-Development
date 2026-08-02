namespace Idd.Factory.LiveTests.Models;

public sealed class FactoryEvalResult
{
    public required string RunDirectory { get; init; }
    public required string Outcome { get; set; }
    public bool ProductPassed { get; set; }
    public bool FactoryPassed { get; set; }
}

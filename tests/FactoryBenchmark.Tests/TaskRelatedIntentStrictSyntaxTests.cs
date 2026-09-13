using Idd.Factory.Benchmark;
using Xunit;

namespace FactoryBenchmark.Tests;

public sealed class TaskRelatedIntentStrictSyntaxTests
{
    [Theory]
    [InlineData(" IDD-0001")]
    [InlineData("IDD-0001 ")]
    public void DecompositionRejectsSurroundingWhitespaceInsteadOfNormalizingIt(string referenceLine)
    {
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, $"# Task\n\nImplement A.\n# TaskRelatedIntent\n{referenceLine}\n");

            Assert.Throws<InvalidDataException>(() => BenchmarkRunner.ParseDecomposition(path));
        }
        finally
        {
            File.Delete(path);
        }
    }
}

using Idd.Factory.Benchmark;
using Xunit;

namespace FactoryBenchmark.Tests;

public sealed class TaskRelatedIntentBenchmarkTests
{
    [Fact]
    public void DecompositionRelatedIntentMetadataIsNotCapturedInContractMarkdown()
    {
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path,
                "# Task\n\nImplement A.\n\n# TaskRelatedIntent\nIDD-0001\nIDD-0002\n\n# Task\n\nImplement B.\n");

            var result = BenchmarkRunner.ParseDecomposition(path);

            Assert.Equal(2, result.Count);
            Assert.Equal("Implement A.", result[0].ContractMarkdown);
            Assert.Equal("Implement B.", result[1].ContractMarkdown);
            Assert.DoesNotContain("TaskRelatedIntent", result[0].ContractMarkdown, StringComparison.Ordinal);
            Assert.DoesNotContain("IDD-0001", result[0].ContractMarkdown, StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void DecompositionWithoutRelatedIntentRemainsUnchanged()
    {
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, "# Task\n\n# Work\n\nKeep this contract body.\n");

            var result = BenchmarkRunner.ParseDecomposition(path);

            Assert.Equal("# Work\n\nKeep this contract body.", Assert.Single(result).ContractMarkdown);
        }
        finally
        {
            File.Delete(path);
        }
    }
}

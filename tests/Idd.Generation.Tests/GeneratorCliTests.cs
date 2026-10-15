using Idd.Generation.Tests.Infrastructure;
using Xunit;

namespace Idd.Generation.Tests;

[Collection(GenerationCollection.Name)]
public sealed class GeneratorCliTests(GenerationFixture fixture)
{
    [Fact]
    public void CheckMode_AcceptsCurrentGeneratedOutput()
    {
        var result = fixture.RunGenerator(checkOnly: true);
        Assert.True(result.ExitCode == 0,
            $"Generator --check failed with exit code {result.ExitCode}.{Environment.NewLine}{result.Stderr}");
    }


    [Theory]
    [InlineData("claude")]
    [InlineData("codex")]
    public void StaleFactoryWorkerSkill_IsDetectedAndRemovedByOrdinaryGeneration(string platform)
    {
        var stale = Path.Combine(fixture.MarketplaceRoot, "plugins", platform, "idd-factory",
            "skills", "idd-factory-execute-subtask");
        Directory.CreateDirectory(stale);
        File.WriteAllText(Path.Combine(stale, "SKILL.md"), "# Obsolete public worker");

        try
        {
            var checkStale = fixture.RunGenerator(checkOnly: true);
            Assert.NotEqual(0, checkStale.ExitCode);
            Assert.Contains("Unexpected generated file", checkStale.Stdout + checkStale.Stderr);

            var generated = fixture.RunGenerator();
            Assert.Equal(0, generated.ExitCode);
            fixture.AssertMissing(stale);

            var checkClean = fixture.RunGenerator(checkOnly: true);
            Assert.Equal(0, checkClean.ExitCode);
        }
        finally
        {
            if (Directory.Exists(stale))
                Directory.Delete(stale, recursive: true);
            var restored = fixture.RunGenerator();
            Assert.Equal(0, restored.ExitCode);
        }
    }

    [Theory]
    [InlineData("claude")]
    [InlineData("codex")]
    public void StaleIntentNewDocumentSkill_IsDetectedAndRemovedByOrdinaryGeneration(string platform)
    {
        var intentRoot = Path.Combine(fixture.MarketplaceRoot, "plugins", platform, "idd-intent");
        var stale = Path.Combine(intentRoot, "skills", "idd-intent-new-document");
        Directory.CreateDirectory(stale);
        File.WriteAllText(Path.Combine(stale, "SKILL.md"), "# Obsolete public document creator");

        try
        {
            var checkStale = fixture.RunGenerator(checkOnly: true);
            Assert.NotEqual(0, checkStale.ExitCode);
            Assert.Contains("Unexpected generated file", checkStale.Stdout + checkStale.Stderr);

            var generated = fixture.RunGenerator();
            Assert.Equal(0, generated.ExitCode);
            fixture.AssertMissing(stale);
            fixture.AssertFile(Path.Combine(intentRoot, "skills", "idd-intent-change",
                "references", "new-intent-document.md"));

            var checkClean = fixture.RunGenerator(checkOnly: true);
            Assert.Equal(0, checkClean.ExitCode);
        }
        finally
        {
            if (Directory.Exists(stale))
                Directory.Delete(stale, recursive: true);
            var restored = fixture.RunGenerator();
            Assert.Equal(0, restored.ExitCode);
        }
    }

    [Fact]
    public void StaleCodexRouteAgentMetadata_IsDetectedAndRemovedWithoutDroppingWorkflow()
    {
        var codexRoot = Path.Combine(fixture.MarketplaceRoot, "plugins", "codex");
        var route = Path.Combine(codexRoot, "idd-intent", "skills", "idd-route");
        var stale = Path.Combine(route, "agents", "openai.yaml");
        var factoryBefore = fixture.SnapshotMarketplace()
            .Where(path => path.Contains("/idd-factory/", StringComparison.Ordinal))
            .ToArray();
        fixture.AssertFile(Path.Combine(route, "SKILL.md"));
        fixture.AssertFile(Path.Combine(route, "references", "common-workflows.md"));
        fixture.AssertMissing(stale);
        Directory.CreateDirectory(Path.GetDirectoryName(stale)!);
        File.WriteAllText(stale, "interface:\n  display_name: Obsolete IDD Route\n");

        try
        {
            var checkStale = fixture.RunGenerator(checkOnly: true);
            Assert.NotEqual(0, checkStale.ExitCode);
            Assert.Contains("Unexpected generated file", checkStale.Stdout + checkStale.Stderr);

            var generated = fixture.RunGenerator();
            Assert.Equal(0, generated.ExitCode);
            fixture.AssertMissing(stale);
            fixture.AssertFile(Path.Combine(route, "SKILL.md"));
            fixture.AssertFile(Path.Combine(route, "references", "common-workflows.md"));

            var checkClean = fixture.RunGenerator(checkOnly: true);
            Assert.Equal(0, checkClean.ExitCode);
            var cleanSnapshot = fixture.SnapshotMarketplace();
            var again = fixture.RunGenerator();
            Assert.Equal(0, again.ExitCode);
            Assert.Equal(cleanSnapshot, fixture.SnapshotMarketplace());
            Assert.Equal(factoryBefore, fixture.SnapshotMarketplace()
                .Where(path => path.Contains("/idd-factory/", StringComparison.Ordinal))
                .ToArray());
        }
        finally
        {
            if (File.Exists(stale))
                File.Delete(stale);
            var restored = fixture.RunGenerator();
            Assert.Equal(0, restored.ExitCode);
        }
    }

    [Fact]
    public void Generation_IsDeterministicAndIdempotent()
    {
        var before = fixture.SnapshotMarketplace();
        var result = fixture.RunGenerator();
        Assert.True(result.ExitCode == 0,
            $"Second generator run failed with exit code {result.ExitCode}.{Environment.NewLine}{result.Stderr}");
        var after = fixture.SnapshotMarketplace();
        Assert.Equal(before, after);
    }
}

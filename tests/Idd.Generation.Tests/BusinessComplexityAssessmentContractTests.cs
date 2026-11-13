using System.Text.Json;
using Idd.Generation.Tests.Infrastructure;
using Xunit;

namespace Idd.Generation.Tests;

[Collection(GenerationCollection.Name)]
public sealed class BusinessComplexityAssessmentContractTests(GenerationFixture fixture)
{
    [Fact]
    public void Estimator_IsRegisteredOnlyWithCoreIntentAndHasAutomaticCommandMetadata()
    {
        var source = Path.Combine(fixture.RepoRoot, "src", "canonical");
        var canonical = Path.Combine(source, "skills", "idd-intent-estimate.md");
        fixture.AssertFile(canonical);

        using var manifest = JsonDocument.Parse(fixture.ReadText(
            Path.Combine(source, "plugins", "plugin-manifest.json")));
        var plugins = manifest.RootElement.GetProperty("plugins");
        var intent = plugins.GetProperty("idd-intent").GetProperty("skills")
            .EnumerateArray().Select(x => x.GetString()).ToArray();
        var factory = plugins.GetProperty("idd-factory").GetProperty("skills")
            .EnumerateArray().Select(x => x.GetString()).ToArray();
        Assert.Contains("idd-intent-estimate", intent);
        Assert.DoesNotContain("idd-intent-estimate", factory);
        Assert.Equal(1, intent.Count(x => x == "idd-intent-estimate"));

        using var descriptions = JsonDocument.Parse(fixture.ReadText(
            Path.Combine(source, "skills", "skill-descriptions.json")));
        var skill = descriptions.RootElement.GetProperty("idd-intent-estimate");
        Assert.Equal("auto", skill.GetProperty("invocation").GetString());
        Assert.Equal("command", skill.GetProperty("exposure").GetString());
        Assert.Equal("IDD Complexity Assessment", skill.GetProperty("interface")
            .GetProperty("displayName").GetString());
        var claude = skill.GetProperty("adapters").GetProperty("claude")
            .GetProperty("frontmatter");
        Assert.Equal("fork", claude.GetProperty("context").GetString());
        Assert.Equal("Explore", claude.GetProperty("agent").GetString());
        Assert.False(claude.GetProperty("background").GetBoolean());
        Assert.Equal("Read Glob Grep", claude.GetProperty("allowed-tools").GetString());
    }

    [Theory]
    [InlineData("claude")]
    [InlineData("codex")]
    public void BothAdapters_PackageCompleteReadOnlySkill(string platform)
    {
        var skillRoot = Path.Combine(fixture.MarketplaceRoot, "plugins", platform,
            "idd-intent", "skills", "idd-intent-estimate");
        var generated = fixture.ReadText(Path.Combine(skillRoot, "SKILL.md"));
        var canonical = fixture.ReadText(Path.Combine(fixture.RepoRoot, "src",
            "canonical", "skills", "idd-intent-estimate.md"));

        foreach (var section in new[]
        {
            "Strict read-only boundary", "Scope and authoritative evidence",
            "First build the functional model", "Seven dimensions",
            "Three independent overall judgments", "COSMIC Preparation",
            "Answer format"
        })
        {
            Assert.Contains(section, canonical);
            Assert.Contains(section, generated);
        }

        foreach (var term in new[]
        {
            "Functional Scope", "Business Logic", "Integrations",
            "Data Complexity", "Reliability & Security", "User Interaction",
            "Operational Constraints", "Product Scope",
            "Implementation Complexity", "Assessment Confidence",
            "Unknown", "Not applicable", "Entry (E)", "Exit (X)",
            "Read (R)", "Write (W)", "CFP", "not assessed",
            "read-only", "IDD-NNNN"
        })
        {
            Assert.Contains(term, canonical, StringComparison.OrdinalIgnoreCase);
            Assert.Contains(term, generated, StringComparison.OrdinalIgnoreCase);
        }

        if (platform == "codex")
            fixture.AssertFile(Path.Combine(skillRoot, "agents", "openai.yaml"));
        else
        {
            Assert.Contains("user-invocable: true", generated);
            Assert.Contains("disable-model-invocation: false", generated);
        }
    }

    [Fact]
    public void RoutingCore_ContainsIndependentReadOnlyEstimationClassification()
    {
        var core = fixture.ReadText(Path.Combine(fixture.RepoRoot, "src",
            "canonical", "methodology", "routing-core.md"));
        var route = fixture.ReadText(Path.Combine(fixture.RepoRoot, "src",
            "canonical", "skills", "idd-route.md"));
        Assert.Contains("intent-estimation", core);
        Assert.Contains("idd-intent-estimate", core);
        Assert.Contains("route-only", core);
        Assert.Contains("end-to-end", core);
        Assert.Contains("not-applicable", core);
        Assert.Contains("intent-estimation", route);
        Assert.Contains("idd-intent-estimate", route);
        Assert.Contains("source-code complexity", core);
    }

    [Fact]
    public void BehavioralCorpus_IsVersionedAndHasDistinctMaterialScenarios()
    {
        var path = Path.Combine(fixture.RepoRoot, "tests",
            "Idd.Intent.LiveTests", "Scenarios", "estimate-cases.json");
        using var suite = JsonDocument.Parse(fixture.ReadText(path));
        Assert.Equal(1, suite.RootElement.GetProperty("schemaVersion").GetInt32());
        var cases = suite.RootElement.GetProperty("scenarios").EnumerateArray().ToArray();
        Assert.True(cases.Length >= 20);
        var ids = cases.Select(x => x.GetProperty("id").GetString()!).ToArray();
        Assert.Equal(ids.Length, ids.Distinct(StringComparer.Ordinal).Count());
        foreach (var item in cases)
        {
            Assert.False(string.IsNullOrWhiteSpace(item.GetProperty("request").GetString()));
            Assert.True(item.GetProperty("expected").GetArrayLength() > 0);
            Assert.Equal(JsonValueKind.Object, item.GetProperty("documents").ValueKind);
        }
        Assert.Contains("P09-empty", ids);
        Assert.Contains("P17-injection", ids);
        Assert.Contains("P15-boundaries", ids);
        Assert.Contains("P21-self-IDD", ids);
    }
    [Fact]
    public void Estimator_MandatesEvidenceGatheringAndPortableSources()
    {
        var source = fixture.ReadText(Path.Combine(fixture.RepoRoot, "src",
            "canonical", "skills", "idd-intent-estimate.md"));
        foreach (var contract in new[]
        {
            "Stage A — Discovery", "Stage B — Requirements", "Stage C — Evidence review",
            "fully read", "partially read", "discovered but not read", "inaccessible",
            "Assessed", "Partially assessed", "Not assessed", "Undetermined",
            "Top Complexity Drivers", "Coverage and Confidence",
            "repository-relative", "seven-dimension", "specific requirement"
        })
            Assert.Contains(contract, source, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Do not artificially force a bell curve", source);
        Assert.Contains("never generate a Markdown link to a local absolute path", source,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains("INDEX-only discovery", source);
    }

    [Fact]
    public void NewBehavioralScenarios_HaveConcreteEvidenceAndSemanticExpectations()
    {
        using var suite = JsonDocument.Parse(fixture.ReadText(Path.Combine(
            fixture.RepoRoot, "tests", "Idd.Intent.LiveTests", "Scenarios",
            "estimate-cases.json")));
        var scenarios = suite.RootElement.GetProperty("scenarios")
            .EnumerateArray().ToArray();
        Assert.Equal(33, scenarios.Length);
        foreach (var number in Enumerable.Range(1, 12))
        {
            var prefix = $"E{number:D2}-";
            var scenario = Assert.Single(scenarios.Where(x =>
                x.GetProperty("id").GetString()!.StartsWith(prefix,
                    StringComparison.Ordinal)));
            Assert.True(scenario.GetProperty("documents").EnumerateObject().Any());
            Assert.All(scenario.GetProperty("expected").EnumerateArray(), criterion =>
                Assert.True(criterion.GetString()!.Length > 30,
                    "Expected behavior must be a meaningful semantic criterion"));
        }
        var limited = scenarios.Single(x =>
            x.GetProperty("id").GetString() == "E02-partial-documents");
        Assert.True(limited.GetProperty("documents").EnumerateObject().Count() >= 15);
        Assert.Contains(scenarios, x =>
            x.GetProperty("id").GetString() == "E04-all-high-legitimate");
        Assert.Contains(scenarios, x =>
            x.GetProperty("id").GetString() == "E09-portable-local-links");
    }

}

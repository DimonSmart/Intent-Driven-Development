using System.Text.Json;
using Idd.Generation.Tests.Infrastructure;
using Xunit;

namespace Idd.Generation.Tests;

[Collection(GenerationCollection.Name)]
public sealed class CosmicFunctionalSizeContractTests(GenerationFixture fixture)
{
    [Fact]
    public void CanonicalRegistration_UsesReadOnlyAnalyticalSkill()
    {
        using var manifest = JsonDocument.Parse(fixture.ReadText(Path.Combine(
            fixture.RepoRoot, "src", "canonical", "plugins", "plugin-manifest.json")));
        var plugins = manifest.RootElement.GetProperty("plugins");
        var intent = plugins.GetProperty("idd-intent");
        var skills = intent.GetProperty("skills").EnumerateArray()
            .Select(x => x.GetString()).ToArray();
        Assert.Equal(1, skills.Count(x => x == "idd-intent-cosmic"));
        Assert.DoesNotContain("idd-intent-cosmic", plugins.GetProperty("idd-factory")
            .GetProperty("skills").EnumerateArray().Select(x => x.GetString()));

        var references = intent.GetProperty("skillReferences").EnumerateArray()
            .Where(x => x.GetProperty("skill").GetString() == "idd-intent-cosmic")
            .Select(x => x.GetProperty("destination").GetString()).ToArray();
        Assert.Equal(new[] { "cosmic-measurement-guide.md", "cosmic-delegation-protocol.md",
            "cosmic-execution-state.md" }, references);

        using var descriptions = JsonDocument.Parse(fixture.ReadText(Path.Combine(
            fixture.RepoRoot, "src", "canonical", "skills", "skill-descriptions.json")));
        var cosmic = descriptions.RootElement.GetProperty("idd-intent-cosmic");
        Assert.Equal("auto", cosmic.GetProperty("invocation").GetString());
        Assert.Equal("command", cosmic.GetProperty("exposure").GetString());

        var claude = cosmic.GetProperty("adapters").GetProperty("claude")
            .GetProperty("frontmatter");
        Assert.Equal("fork", claude.GetProperty("context").GetString());
        Assert.Equal("general-purpose", claude.GetProperty("agent").GetString());
        Assert.False(claude.GetProperty("background").GetBoolean());
        Assert.Equal("Read Glob Grep Agent Write", claude.GetProperty("allowed-tools").GetString());

        var core = fixture.ReadText(Path.Combine(fixture.RepoRoot, "src",
            "canonical", "methodology", "routing-core.md"));
        var route = fixture.ReadText(Path.Combine(fixture.RepoRoot, "src",
            "canonical", "skills", "idd-route.md"));
        foreach (var text in new[] { core, route })
        {
            Assert.Contains("intent-cosmic-measurement", text);
            Assert.Contains("idd-intent-cosmic", text);
        }
        Assert.Contains("intent-estimation", core);
        Assert.Contains("idd-intent-estimate", core);
    }

    [Theory]
    [InlineData("claude")]
    [InlineData("codex")]
    public void GeneratedPlugin_PackagesGuideWithoutExecutableDependencies(string host)
    {
        var canonical = Path.Combine(fixture.RepoRoot, "src", "canonical");
        var skill = Path.Combine(fixture.MarketplaceRoot, "plugins", host,
            "idd-intent", "skills", "idd-intent-cosmic");
        var skillPath = Path.Combine(skill, "SKILL.md");
        var guidePath = Path.Combine(skill, "references", "cosmic-measurement-guide.md");
        var protocolPath = Path.Combine(skill, "references", "cosmic-delegation-protocol.md");
        var statePath = Path.Combine(skill, "references", "cosmic-execution-state.md");
        var helperPath = Path.Combine(skill, "assets", "cosmic-state", "cosmic_state.py");
        fixture.AssertFile(skillPath);
        fixture.AssertFile(guidePath);
        fixture.AssertFile(protocolPath);
        fixture.AssertFile(statePath);
        fixture.AssertFile(helperPath);
        Assert.False(File.Exists(Path.Combine(skill, "references", "cosmic-validator.mjs")));

        var skillText = fixture.ReadText(skillPath);
        var guideText = fixture.ReadText(guidePath);
        var protocolText = fixture.ReadText(protocolPath);
        var stateText = fixture.ReadText(statePath);
        var helperText = fixture.ReadText(helperPath);
        Assert.Contains("Phase 3", skillText);
        Assert.Contains("fresh native child reader", skillText);
        Assert.Contains("Measured subtotal", skillText);
        Assert.Contains("Completion contract", skillText);
        Assert.Contains("Full-text sources reviewed: X/Y", skillText);
        Assert.Contains("Execution incomplete / BLOCKED", skillText);
        Assert.Contains("coverage ledger", skillText);
        Assert.Contains("cosmic-delegation-protocol.md", skillText);
        Assert.Contains("6,000 tokens", skillText);
        Assert.Contains("20% of usable context", skillText);
        Assert.Contains("Recursive reducer", protocolText);
        Assert.Contains("IDD single-owner rule", protocolText);
        Assert.Contains("Coupling / Change Impact", protocolText);
        Assert.Contains("Process measurer", protocolText);
        Assert.Contains("bounded retries", stateText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("SHA-256", stateText);
        Assert.Contains("cfp-arithmetic:", helperText);
// The helper never attempts semantic process identification.
        Assert.DoesNotContain("guess process", helperText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("lossless", protocolText);
        Assert.Contains("outside the target repository", protocolText);
        Assert.Contains("BLOCKED", protocolText);
        Assert.Contains("Rule 10c", skillText);
        Assert.Contains("CFP = E + X + R + W", guideText);
        Assert.Contains("self-review", guideText);
        Assert.Contains("Exhaustive scope contract", guideText);
        Assert.Contains("Execution incomplete (BLOCKED)", guideText);
        Assert.Equal(fixture.ReadText(Path.Combine(canonical, "methodology",
                "cosmic-measurement-guide.md")).ReplaceLineEndings("\n").TrimEnd(),
            guideText.ReplaceLineEndings("\n").TrimEnd());
        Assert.Equal(fixture.ReadText(Path.Combine(canonical, "methodology",
                "cosmic-delegation-protocol.md")).ReplaceLineEndings("\n").TrimEnd(),
            protocolText.ReplaceLineEndings("\n").TrimEnd());
        Assert.Equal(fixture.ReadText(Path.Combine(canonical, "methodology",
                "cosmic-execution-state.md")).ReplaceLineEndings("\n").TrimEnd(),
            stateText.ReplaceLineEndings("\n").TrimEnd());

        if (host == "codex")
        {
            var metadata = Path.Combine(skill, "agents", "openai.yaml");
            fixture.AssertFile(metadata);
            Assert.Contains("allow_implicit_invocation: true", fixture.ReadText(metadata));
        }
        else
        {
            Assert.Contains("user-invocable: true", skillText);
            Assert.Contains("disable-model-invocation: false", skillText);
            Assert.Contains("allowed-tools: Read Glob Grep Agent Write", skillText);
        }
    }

    [Fact]
    public void LargeLiveScenario_RequiresFullCoverageNotOneProcessSubtotal()
    {
        using var corpus = JsonDocument.Parse(fixture.ReadText(Path.Combine(
            fixture.RepoRoot, "tests", "Idd.Intent.LiveTests", "Scenarios", "cosmic-cases.json")));
        var scenario = corpus.RootElement.GetProperty("scenarios").EnumerateArray()
            .Single(x => x.GetProperty("id").GetString() == "C22-full-scope-exhaustive");
        Assert.Equal("Complete", scenario.GetProperty("expectedStatus").GetString());
        Assert.Equal(28, scenario.GetProperty("documents").EnumerateObject().Count());
        Assert.Equal(18, scenario.GetProperty("expectedProcesses").GetArrayLength());
        Assert.Contains(scenario.GetProperty("expectedRestrictions").EnumerateArray(),
            r => r.GetString()!.Contains("28/28", StringComparison.Ordinal));
    }

    [Fact]
    public void HierarchicalLiveScenario_PreservesAllSourcesAndCrossGroupOwners()
    {
        using var corpus = JsonDocument.Parse(fixture.ReadText(Path.Combine(
            fixture.RepoRoot, "tests", "Idd.Intent.LiveTests",
            "Scenarios", "cosmic-cases.json")));
        var scenario = corpus.RootElement.GetProperty("scenarios").EnumerateArray()
            .Single(x => x.GetProperty("id").GetString() == "C23-hierarchical-60-specs");
        Assert.Equal("Complete", scenario.GetProperty("expectedStatus").GetString());
        var documents = scenario.GetProperty("documents").EnumerateObject().ToArray();
        Assert.Equal(60, documents.Length);
        Assert.Equal(60, documents.Select(x => x.Name).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(20, scenario.GetProperty("expectedProcesses").GetArrayLength());
        Assert.Equal(20, scenario.GetProperty("expectedMovements").GetArrayLength());
        Assert.Contains(scenario.GetProperty("expectedRestrictions").EnumerateArray(),
            x => x.GetString()!.Contains("60/60", StringComparison.Ordinal));
        Assert.Contains(scenario.GetProperty("expectedRestrictions").EnumerateArray(),
            x => x.GetString()!.Contains("connector", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void CouplingAndLargeSpecLiveCases_PreserveOwnershipWithoutInflatingCfp()
    {
        using var corpus = JsonDocument.Parse(fixture.ReadText(Path.Combine(
            fixture.RepoRoot, "tests", "Idd.Intent.LiveTests",
            "Scenarios", "cosmic-cases.json")));
        var scenarios = corpus.RootElement.GetProperty("scenarios").EnumerateArray().ToArray();
        var coupling = scenarios.Single(x => x.GetProperty("id").GetString()
            == "C24-single-owner-coupling");
        Assert.Equal(4, coupling.GetProperty("documents").EnumerateObject().Count());
        Assert.Equal(4, coupling.GetProperty("expectedProcesses").GetArrayLength());
        Assert.Contains(coupling.GetProperty("expectedRestrictions").EnumerateArray(),
            x => x.GetString()!.Contains("12 CFP", StringComparison.Ordinal));
        var large = scenarios.Single(x => x.GetProperty("id").GetString()
            == "C25-large-spec-sections");
        var spec = large.GetProperty("documents").EnumerateObject().Single();
        Assert.True(spec.Value.GetString()!.Length > 60_000);
        Assert.Equal(1, large.GetProperty("expectedProcesses").GetArrayLength());
        Assert.Contains(large.GetProperty("expectedRestrictions").EnumerateArray(),
            x => x.GetString()!.Contains("range-covered", StringComparison.Ordinal));
    }

    [Fact]
    public void GoldenExamples_RetainDocumentedMovementCounts()
    {
        using var fixtureJson = JsonDocument.Parse(fixture.ReadText(Path.Combine(
            fixture.RepoRoot, "tests", "Idd.Cosmic.Tests", "golden-cases.json")));
        var examples = fixtureJson.RootElement.GetProperty("cases").EnumerateArray().ToArray();
        Assert.Equal(3, examples.Length);
        var expected = new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["CReg-add-professor"] = 4,
            ["Rice-cooker-start"] = 3,
            ["Cleaning-display-list"] = 4
        };
        foreach (var example in examples)
        {
            var id = example.GetProperty("id").GetString()!;
            var movements = example.GetProperty("movements").EnumerateArray().ToArray();
            Assert.Equal(expected[id], example.GetProperty("expectedCfp").GetInt32());
            Assert.Equal(expected[id], movements.Length);
            Assert.Contains(movements, x => x.GetProperty("type").GetString() == "E");
            Assert.Contains(movements, x =>
                x.GetProperty("type").GetString() is "X" or "W");
        }
    }
}

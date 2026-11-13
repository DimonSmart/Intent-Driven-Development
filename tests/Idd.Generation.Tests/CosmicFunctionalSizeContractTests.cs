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
        Assert.Equal(new[] { "cosmic-measurement-guide.md" }, references);

        using var descriptions = JsonDocument.Parse(fixture.ReadText(Path.Combine(
            fixture.RepoRoot, "src", "canonical", "skills", "skill-descriptions.json")));
        var cosmic = descriptions.RootElement.GetProperty("idd-intent-cosmic");
        Assert.Equal("auto", cosmic.GetProperty("invocation").GetString());
        Assert.Equal("command", cosmic.GetProperty("exposure").GetString());

        var claude = cosmic.GetProperty("adapters").GetProperty("claude")
            .GetProperty("frontmatter");
        Assert.Equal("fork", claude.GetProperty("context").GetString());
        Assert.Equal("Explore", claude.GetProperty("agent").GetString());
        Assert.False(claude.GetProperty("background").GetBoolean());
        Assert.Equal("Read Glob Grep", claude.GetProperty("allowed-tools").GetString());

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
        fixture.AssertFile(skillPath);
        fixture.AssertFile(guidePath);
        Assert.False(File.Exists(Path.Combine(skill, "references", "cosmic-validator.mjs")));

        var skillText = fixture.ReadText(skillPath);
        var guideText = fixture.ReadText(guidePath);
        Assert.Contains("Phase 3", skillText);
        Assert.Contains("Read the full contents", skillText);
        Assert.Contains("Measured subtotal", skillText);
        Assert.Contains("Completion contract", skillText);
        Assert.Contains("Full-text sources reviewed: X/Y", skillText);
        Assert.Contains("Execution incomplete / BLOCKED", skillText);
        Assert.Contains("per-spec coverage ledger", skillText);
        Assert.Contains("Rule 10c", skillText);
        Assert.Contains("CFP = E + X + R + W", guideText);
        Assert.Contains("self-review", guideText);
        Assert.Contains("Exhaustive scope contract", guideText);
        Assert.Contains("Execution incomplete (BLOCKED)", guideText);
        Assert.Equal(fixture.ReadText(Path.Combine(canonical, "methodology",
                "cosmic-measurement-guide.md")).ReplaceLineEndings("\n").TrimEnd(),
            guideText.ReplaceLineEndings("\n").TrimEnd());

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
            Assert.Contains("allowed-tools: Read Glob Grep", skillText);
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

using System.Diagnostics;
using System.Text.Json;
using Idd.Generation.Tests.Infrastructure;
using Xunit;

namespace Idd.Generation.Tests;

[Collection(GenerationCollection.Name)]
public sealed class CosmicFunctionalSizeContractTests(GenerationFixture fixture)
{
    [Fact]
    public void CanonicalRegistrationAndRouting_AreIndependentFromComplexity()
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

        var refs = intent.GetProperty("skillReferences").EnumerateArray()
            .Where(x => x.GetProperty("skill").GetString() == "idd-intent-cosmic")
            .Select(x => x.GetProperty("destination").GetString()).ToArray();
        Assert.Equal(2, refs.Length);
        Assert.Contains("cosmic-measurement-guide.md", refs);
        Assert.Contains("cosmic-validator.mjs", refs);

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
        Assert.Contains("cosmic-validator.mjs", claude.GetProperty("allowed-tools").GetString());
        Assert.DoesNotContain("Bash", claude.GetProperty("allowed-tools")
            .GetString()!.Replace("Bash(node *cosmic-validator.mjs *)", ""));

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
    public async Task InstalledGeneratedPlugin_PackagesAndRunsRealValidator(string host)
    {
        var canonical = Path.Combine(fixture.RepoRoot, "src", "canonical");
        var skill = Path.Combine(fixture.MarketplaceRoot, "plugins", host,
            "idd-intent", "skills", "idd-intent-cosmic");
        var skillPath = Path.Combine(skill, "SKILL.md");
        var validator = Path.Combine(skill, "references", "cosmic-validator.mjs");
        var guide = Path.Combine(skill, "references", "cosmic-measurement-guide.md");
        fixture.AssertFile(skillPath);
        fixture.AssertFile(validator);
        fixture.AssertFile(guide);
        Assert.Contains("Phase 3", fixture.ReadText(skillPath));
        Assert.Contains("schemaVersion", fixture.ReadText(guide));
        Assert.Equal(fixture.ReadText(Path.Combine(canonical, "methodology",
            "cosmic-validator.mjs")).ReplaceLineEndings("\n").TrimEnd(),
            fixture.ReadText(validator).ReplaceLineEndings("\n").TrimEnd());

        if (host == "codex")
        {
            var metadata = Path.Combine(skill, "agents", "openai.yaml");
            fixture.AssertFile(metadata);
            Assert.Contains("allow_implicit_invocation: true", fixture.ReadText(metadata));
        }
        else
        {
            var md = fixture.ReadText(skillPath);
            Assert.Contains("user-invocable: true", md);
            Assert.Contains("disable-model-invocation: false", md);
        }

        var temporary = Path.Combine(Path.GetTempPath(), "idd-cosmic-",
            Guid.NewGuid().ToString("N"));
        try
        {
            var workspace = Path.Combine(temporary, "workspace");
            var docs = Path.Combine(workspace, ".idd", "intent");
            Directory.CreateDirectory(docs);
            File.WriteAllText(Path.Combine(docs, "IDD-0001.spec-task.md"),
                "# Task\nUser submits and stores a task.\n");
            var modelPath = Path.Combine(temporary, "model.json");
            File.WriteAllText(modelPath, CreateModel());
            var original = File.ReadAllText(Path.Combine(docs, "IDD-0001.spec-task.md"));

            var process = new ProcessStartInfo("node")
            {
                WorkingDirectory = fixture.RepoRoot,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                UseShellExecute = false
            };
            process.ArgumentList.Add(validator);
            process.ArgumentList.Add(modelPath);
            process.ArgumentList.Add(workspace);
            using var launched = Process.Start(process) ??
                throw new InvalidOperationException("Node.js is required for COSMIC validator");
            var stdout = launched.StandardOutput.ReadToEndAsync();
            var stderr = launched.StandardError.ReadToEndAsync();
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            await launched.WaitForExitAsync(deadline.Token);
            Assert.True(launched.ExitCode == 0, await stderr);
            using var calculated = JsonDocument.Parse(await stdout);
            var result = calculated.RootElement;
            Assert.True(result.GetProperty("mechanicallyValidated").GetBoolean());
            Assert.Equal(2, result.GetProperty("overallSize").GetInt32());
            Assert.Equal(2, result.GetProperty("measuredSubtotal").GetInt32());
            Assert.Equal("Complete", result.GetProperty("status").GetString());
            Assert.Equal(original, File.ReadAllText(Path.Combine(docs,
                "IDD-0001.spec-task.md")));
            Assert.Single(Directory.GetFiles(workspace, "*", SearchOption.AllDirectories),
                x => x.EndsWith(".md", StringComparison.Ordinal));
        }
        finally
        {
            if (Directory.Exists(temporary))
                Directory.Delete(temporary, recursive: true);
        }
    }

    private static string CreateModel()
    {
        const string path = ".idd/intent/IDD-0001.spec-task.md";
        var ev = new[] { new { path, quote = "User submits and stores a task." } };
        var movementCommon = "Supported by FUR";
        var m1 = new
        {
            id = "M1", functionalProcessId = "P1", measurementUnitId = "U1",
            type = "E", dataGroup = "Task", objectOfInterest = "Task",
            direction = "in", interactingFunctionalUser = "User",
            evidenceType = "regular FUR", semanticStatus = movementCommon,
            justification = "Task entered", evidence = ev
        };
        var m2 = new
        {
            id = "M2", functionalProcessId = "P1", measurementUnitId = "U1",
            type = "W", dataGroup = "Task", objectOfInterest = "Task",
            persistenceJustification = "Task persists after process",
            evidenceType = "regular FUR", semanticStatus = movementCommon,
            justification = "Task persisted", evidence = ev
        };
        // Anonymous types differ between E and W; use JsonElement to avoid
        // erasing the distinct boundary/persistence fields in serialization.
        object[] movements = [m1, m2];
        var model = new
        {
            schemaVersion = 1,
            methodVersion = "COSMIC Measurement Manual v5.0",
            purpose = "Measure current product",
            scope = "Task creation",
            measurementDate = "2026-11-13",
            requestedFullTotal = true,
            measurementUnits = new[]
            {
                new
                {
                    measurementUnitId = "U1", softwarePiece = "Task app",
                    boundary = "Task app", layer = "Application",
                    decompositionLevel = "whole", furGranularity = "functional process",
                    scope = "Task creation",
                    functionalUsers = new[] { new { id = "User", kind = "human" } }
                }
            },
            sourceDocuments = new[] { new { path, read = true } },
            coverage = new
            {
                documentationCoverage = new { inventoryComplete = true, unreadPaths = Array.Empty<string>() },
                functionalCoverage = new { processInventoryComplete = true },
                methodApplicability = new { status = "Applicable" },
                coverageItems = new[] { new { name = "Task", status = "covered" } }
            },
            processes = new[]
            {
                new
                {
                    id = "P1", measurementUnitId = "U1", name = "Create task",
                    functionalUser = "User", triggeringEvent = "User submits task",
                    triggeringEntryId = "M1", status = "Measured",
                    semanticStatus = "Supported by FUR",
                    semanticCompletenessJustification = "FUR contains E and W",
                    evidence = ev, dataMovements = movements
                }
            },
            aggregation = new { mode = "single-unit" }
        };
        return JsonSerializer.Serialize(model);
    }
}

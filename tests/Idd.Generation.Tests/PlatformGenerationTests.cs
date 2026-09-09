using System.Text.Json;
using Idd.Generation.Tests.Infrastructure;
using Xunit;

namespace Idd.Generation.Tests;

[Collection(GenerationCollection.Name)]
public sealed class PlatformGenerationTests(GenerationFixture fixture)
{
    [Theory]
    [InlineData("claude", ".claude-plugin")]
    [InlineData("codex", ".codex-plugin")]
    public void PlatformPlugins_HaveExpectedSkillsAssetsAndMetadata(string platform, string manifestDirectory)
    {
        var intentRoot = Path.Combine(fixture.MarketplaceRoot, "plugins", platform, "idd-intent");
        var factoryRoot = Path.Combine(fixture.MarketplaceRoot, "plugins", platform, "idd-factory");

        AssertPluginManifest(intentRoot, manifestDirectory, "idd-intent", platform);
        AssertPluginManifest(factoryRoot, manifestDirectory, "idd-factory", platform);

        foreach (var skill in new[]
        {
            "idd-project-init",
            "idd-verification-configure",
            "idd-intent-change",
            "idd-code-implement",
            "idd-code-check-implementation"
        })
            fixture.AssertFile(Path.Combine(intentRoot, "skills", skill, "SKILL.md"));

        fixture.AssertMissing(Path.Combine(intentRoot, "skills", "idd-factory-create-work-plan"));
        fixture.AssertMissing(Path.Combine(intentRoot, "skills", "idd-factory-execute-work-plan"));
        fixture.AssertFile(Path.Combine(intentRoot, "assets", "bootstrap", ".idd", "intent", "README.md"));
        foreach (var skill in new[]
        {
            "idd-project-init",
            "idd-verification-configure",
            "idd-code-implement",
            "idd-code-check-implementation"
        })
            fixture.AssertFile(Path.Combine(intentRoot, "skills", skill, "references", "project-verification.md"));

        fixture.AssertFile(Path.Combine(
            intentRoot, "skills", "idd-project-init", "assets", "bootstrap", ".idd", "intent", "README.md"));

        foreach (var skill in new[]
        {
            "idd-factory-run",
            "idd-factory-decompose-task",
            "idd-factory-execute-subtask"
        })
        {
            fixture.AssertFile(Path.Combine(factoryRoot, "skills", skill, "SKILL.md"));
            var verificationReference = Path.Combine(factoryRoot, "skills", skill, "references", "project-verification.md");
            if (skill == "idd-factory-run") fixture.AssertFile(verificationReference);
            else fixture.AssertMissing(verificationReference);
        }

        foreach (var obsoleteSkill in new[]
        {
            "idd-factory-create-work-plan",
            "idd-factory-execute-work-plan",
            "idd-factory-decompose-work",
            "idd-factory-execute-task",
            "idd-factory-review-work-result",
            "idd-factory-finish-work",
            "idd-project-init",
            "idd-intent-change",
            "idd-factory-coordinate-step",
            "idd-factory-finalize-run"
        })
            fixture.AssertMissing(Path.Combine(factoryRoot, "skills", obsoleteSkill));

        fixture.AssertFile(Path.Combine(factoryRoot, "runtime", "idd-factory.dll"));
        fixture.AssertFile(Path.Combine(factoryRoot, "runtime", "factory.yaml"));
        fixture.AssertMissing(Path.Combine(factoryRoot, "runtime", "factory-workflow.yaml"));
        fixture.AssertDirectory(Path.Combine(factoryRoot, "assets", "bootstrap", ".idd", "factory"));
        fixture.AssertFile(Path.Combine(factoryRoot, "assets", "bootstrap", ".idd", "factory", ".gitignore"));

        using (var methodology = JsonDocument.Parse(fixture.ReadText(
                   Path.Combine(factoryRoot, "skills", "idd-factory-run", "references", "methodology-version.json"))))
        {
            Assert.Equal(1, methodology.RootElement.GetProperty("schemaVersion").GetInt32());
            AssertString(methodology.RootElement, "methodologyVersion", fixture.Version);
        }

        fixture.AssertMissing(Path.Combine(
            factoryRoot, "skills", "idd-factory-decompose-task", "references", "roles", "planner.md"));
        fixture.AssertMissing(Path.Combine(
            factoryRoot, "skills", "idd-factory-execute-subtask", "references", "roles", "executor.md"));

        var runFrontMatter = GenerationFixture.ReadFrontMatter(fixture.ReadText(
            Path.Combine(factoryRoot, "skills", "idd-factory-run", "SKILL.md")));
        var workerSkills = new[] { "idd-factory-decompose-task", "idd-factory-execute-subtask" };

        if (platform == "claude")
        {
            Assert.False(runFrontMatter.Contains("context: fork", StringComparison.Ordinal));
            foreach (var skill in workerSkills)
            {
                var frontMatter = GenerationFixture.ReadFrontMatter(fixture.ReadText(
                    Path.Combine(factoryRoot, "skills", skill, "SKILL.md")));
                Assert.Contains("context: fork", frontMatter);
            }
        }
        else
        {
            foreach (var skill in workerSkills.Prepend("idd-factory-run"))
            {
                var frontMatter = GenerationFixture.ReadFrontMatter(fixture.ReadText(
                    Path.Combine(factoryRoot, "skills", skill, "SKILL.md")));
                foreach (var claudeField in new[] { "context:", "agent:", "allowed-tools:", "argument-hint:" })
                    Assert.False(frontMatter.Contains(claudeField, StringComparison.Ordinal),
                        $"Codex {skill} contains Claude-specific frontmatter '{claudeField}'.");
            }

            var runSkill = fixture.ReadText(Path.Combine(factoryRoot, "skills", "idd-factory-run", "SKILL.md"));
            foreach (var required in new[] { "mcp__factory", "factory_run", "factory_continue", "factory_cancel", "factory_status" })
                Assert.Contains(required, runSkill);
            Assert.DoesNotContain("runtime/idd-factory.dll", runSkill);
            Assert.DoesNotContain("write_stdin", runSkill);
            Assert.Contains("Do not spawn semantic or coordinator agents", runSkill);
        }

        AssertIddMetadata(intentRoot, [], ".idd/intent");
        AssertIddMetadata(factoryRoot, ["idd-intent"], ".idd/factory");
    }

    [Fact]
    public void FactoryMetadata_HasNoObsoleteRoleSurfaceAndKeepsDiagnosticContract()
    {
        foreach (var platform in new[] { "claude", "codex" })
        {
            var root = Path.Combine(fixture.MarketplaceRoot, "plugins", platform, "idd-factory");
            using var metadata = JsonDocument.Parse(fixture.ReadText(Path.Combine(root, "idd-plugin.json")));
            if (metadata.RootElement.TryGetProperty("roleDefinitions", out var roleDefinitions))
                Assert.Equal(0, roleDefinitions.GetArrayLength());

            var runSkill = fixture.ReadText(Path.Combine(root, "skills", "idd-factory-run", "SKILL.md"));
            foreach (var required in new[]
            {
                "minimal primary-failure payload",
                "Check: <checkId>",
                "Cause: <failureKind> at <failureStage>",
                "Evidence: <evidencePath, when present>",
                "Reason: <runtime Reason>",
                "Resume when: <runtime ResumeWhen>",
                "inspect the referenced verification evidence",
                "must not analyze stdout or stderr\nto choose Factory workflow"
            })
                Assert.Contains(required.ReplaceLineEndings("\n"), runSkill.ReplaceLineEndings("\n"));

            foreach (var obsolete in new[]
            {
                "Primary check: <primaryCheckId>",
                "Primary cause: <failureKind> at <failureStage>: <summary>",
                "Termination: requested=",
                "`metadataTruncated`",
                "`omittedCheckCount`",
                "Full stderr:",
                "Full stdout:"
            })
                Assert.DoesNotContain(obsolete, runSkill);
        }

        var executorClaudeSkill = GenerationFixture.ReadFrontMatter(fixture.ReadText(Path.Combine(
            fixture.MarketplaceRoot, "plugins", "claude", "idd-factory", "skills", "idd-factory-execute-subtask", "SKILL.md")));
        Assert.Contains("allowed-tools: [Read, Glob, Grep, Edit, Write, Bash]", executorClaudeSkill);

        var codexRoot = Path.Combine(fixture.MarketplaceRoot, "plugins", "codex", "idd-factory");
        using var codexMetadata = JsonDocument.Parse(fixture.ReadText(Path.Combine(codexRoot, "idd-plugin.json")));
        Assert.Equal(0, codexMetadata.RootElement.GetProperty("roleDefinitions").GetArrayLength());
        Assert.Equal(0, codexMetadata.RootElement.GetProperty("skillRoleBindings").GetArrayLength());
        fixture.AssertMissing(Path.Combine(codexRoot, "agents"));
        fixture.AssertMissing(Path.Combine(fixture.MarketplaceRoot, "plugins", "codex", "idd-intent", "agents"));
    }

    [Fact]
    public void FactoryTransport_IsPlatformSpecificAndExact()
    {
        var codexIntent = Path.Combine(fixture.MarketplaceRoot, "plugins", "codex", "idd-intent");
        var codexFactory = Path.Combine(fixture.MarketplaceRoot, "plugins", "codex", "idd-factory");
        var claudeFactory = Path.Combine(fixture.MarketplaceRoot, "plugins", "claude", "idd-factory");

        using (var manifest = JsonDocument.Parse(fixture.ReadText(Path.Combine(codexFactory, ".codex-plugin", "plugin.json"))))
            AssertString(manifest.RootElement, "mcpServers", "./.mcp.json");

        using (var intentManifest = JsonDocument.Parse(fixture.ReadText(Path.Combine(codexIntent, ".codex-plugin", "plugin.json"))))
            Assert.False(intentManifest.RootElement.TryGetProperty("mcpServers", out _));

        fixture.AssertMissing(Path.Combine(codexIntent, ".mcp.json"));
        fixture.AssertMissing(Path.Combine(claudeFactory, ".mcp.json"));

        using (var mcp = JsonDocument.Parse(fixture.ReadText(Path.Combine(codexFactory, ".mcp.json"))))
        {
            var factory = mcp.RootElement.GetProperty("mcpServers").GetProperty("factory");
            AssertString(factory, "command", "dotnet");
            AssertString(factory, "cwd", ".");
            Assert.Equal(10800, factory.GetProperty("tool_timeout_sec").GetInt32());
            Assert.Equal(["runtime/idd-factory.dll", "mcp"],
                factory.GetProperty("args").EnumerateArray().Select(value => value.GetString()).ToArray());
            Assert.Equal(["deferred", "code_mode"],
                factory.GetProperty("omit_tools_from").EnumerateArray().Select(value => value.GetString()).ToArray());
            Assert.Equal(
                [
                    "IDD_FACTORY_CODEX_EXECUTABLE",
                    "IDD_FACTORY_MODEL",
                    "IDD_FACTORY_REASONING_EFFORT",
                    "IDD_FACTORY_INHERIT_USER_SKILLS",
                    "IDD_FACTORY_CAPABILITY_PROFILE"
                ],
                factory.GetProperty("env_vars").EnumerateArray().Select(value => value.GetString()).ToArray());
        }

        var claudeSkill = fixture.ReadText(Path.Combine(claudeFactory, "skills", "idd-factory-run", "SKILL.md"));
        foreach (var required in new[]
        {
            "runtime/idd-factory.dll",
            "--request-stdin true",
            "Factory Runtime outside\nthe parent agent OS sandbox",
            "$OutputEncoding",
            "[Console]::OutputEncoding"
        })
            Assert.Contains(required.ReplaceLineEndings("\n"), claudeSkill.ReplaceLineEndings("\n"));

        foreach (var codexOnly in new[] { "mcp__factory", "factory_run", "omit_tools_from", "Code Mode" })
            Assert.DoesNotContain(codexOnly, claudeSkill);

        var canonical = fixture.ReadText(Path.Combine(fixture.RepoRoot, "src", "canonical", "skills", "idd-factory-run.md"));
        foreach (var transport in new[] { "mcp__factory", "factory_run", "runtime/idd-factory.dll", "PowerShell", "write_stdin", "wait" })
            Assert.DoesNotContain(transport, canonical);
    }

    private void AssertPluginManifest(string pluginRoot, string manifestDirectory, string pluginName, string platform)
    {
        using var document = JsonDocument.Parse(fixture.ReadText(Path.Combine(pluginRoot, manifestDirectory, "plugin.json")));
        AssertString(document.RootElement, "name", pluginName);
        AssertString(document.RootElement, "version", fixture.Version);

        if (platform == "codex")
        {
            AssertString(document.RootElement.GetProperty("interface"), "displayName",
                pluginName == "idd-intent" ? "IDD Intent" : "IDD Factory");
            AssertString(document.RootElement, "skills", "./skills/");
        }
    }

    private void AssertIddMetadata(string pluginRoot, string[] expectedDependencies, string expectedAssetDestination)
    {
        using var document = JsonDocument.Parse(fixture.ReadText(Path.Combine(pluginRoot, "idd-plugin.json")));
        var root = document.RootElement;
        AssertString(root, "version", fixture.Version);
        Assert.False(root.TryGetProperty("skillReferences", out _));
        Assert.Equal(expectedDependencies,
            root.GetProperty("dependencies").EnumerateArray().Select(value => value.GetString()).ToArray());
        Assert.Equal([expectedAssetDestination],
            root.GetProperty("assets").EnumerateArray().Select(asset => asset.GetProperty("destination").GetString()).ToArray());
    }

    private static void AssertString(JsonElement element, string propertyName, string expected)
    {
        Assert.True(element.TryGetProperty(propertyName, out var property), $"Missing '{propertyName}'.");
        Assert.Equal(JsonValueKind.String, property.ValueKind);
        Assert.Equal(expected, property.GetString());
    }
}

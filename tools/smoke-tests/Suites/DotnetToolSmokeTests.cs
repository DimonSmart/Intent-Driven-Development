using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;

internal sealed partial class SmokeTestSuite
{
    void ExpectFactoryGeneratedShape()
    {
        var manifest = ReadPackManifest();
        if (manifest?.Packs is null || !manifest.Packs.TryGetValue("factory", out var factoryPack))
        {
            failures.Add("Pack manifest is missing factory pack.");
            return;
        }

        foreach (var relativePath in factoryPack.Skills.SelectMany(GeneratedSkillPaths))
        {
            ExpectFile(relativePath);
        }
    }

    void ExpectFactoryRolePromptReferences()
    {
        var manifest = ReadPackManifest();
        if (manifest?.Packs is null || !manifest.Packs.TryGetValue("factory", out var factoryPack))
        {
            failures.Add("Pack manifest is missing factory pack.");
            return;
        }

        var roots = new[]
        {
            "generated/codex/.agents/skills",
            "generated/claude/.claude/skills",
            "generated/copilot/.github/skills"
        };

        foreach (var root in roots)
        {
            foreach (var skill in factoryPack.Skills)
            {
                var expectedRolePrompts = factoryPack.SkillRoleReferences.GetValueOrDefault(skill) ?? [];
                foreach (var rolePrompt in expectedRolePrompts)
                {
                    ExpectFile($"{root}/{skill}/references/roles/{rolePrompt}.md");
                }

                var roleRoot = Path.Combine(repoRoot, $"{root}/{skill}/references/roles".Replace('/', Path.DirectorySeparatorChar));
                if (!Directory.Exists(roleRoot))
                {
                    continue;
                }

                var actualRolePrompts = Directory
                    .GetFiles(roleRoot, "*.md")
                    .Select(path => Path.GetFileNameWithoutExtension(path)!)
                    .ToArray();
                foreach (var rolePrompt in actualRolePrompts.Except(expectedRolePrompts, StringComparer.Ordinal))
                {
                    failures.Add($"Factory skill has unexpected role prompt reference: {root}/{skill}/references/roles/{rolePrompt}.md");
                }
            }

            var specImplementRoles = Path.Combine(repoRoot, $"{root}/idd-code-implement/references/roles".Replace('/', Path.DirectorySeparatorChar));
            if (Directory.Exists(specImplementRoles))
            {
                failures.Add($"{root}/idd-code-implement must not contain factory role prompt references.");
            }
        }
    }

    void ExpectClaudeSkillMetadata()
    {
        var specAuditPath = Path.Combine(repoRoot, "generated/claude/.claude/skills/idd-intent-audit/SKILL.md".Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(specAuditPath))
        {
            failures.Add("Missing Claude idd-intent-audit skill for frontmatter check.");
        }
        else
        {
            var content = File.ReadAllText(specAuditPath);
            foreach (var text in new[]
            {
                "context: fork",
                "agent: Explore",
                "argument-hint: \"[scope or audit focus]\"",
                "allowed-tools: Read Glob Grep"
            })
            {
                if (!content.Contains(text, StringComparison.Ordinal))
                {
                    failures.Add($"Claude idd-intent-audit skill is missing frontmatter '{text}'.");
                }
            }
        }

        var specChangePath = Path.Combine(repoRoot, "generated/claude/.claude/skills/idd-intent-change/SKILL.md".Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(specChangePath))
        {
            failures.Add("Missing Claude idd-intent-change skill for frontmatter check.");
        }
        else
        {
            var content = File.ReadAllText(specChangePath);
            if (content.Contains("context: fork", StringComparison.Ordinal))
            {
                failures.Add("Claude idd-intent-change skill unexpectedly has context: fork.");
            }
        }

    }

    void ExpectSkillInvocationMetadata()
    {
        var descriptionPath = Path.Combine(repoRoot, "src", "canonical", "skills", "skill-descriptions.json");
        var original = File.ReadAllText(descriptionPath);

        using (var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(repoRoot, "manifest.json"))))
        {
            var skills = manifest.RootElement.GetProperty("skills");
            if (!StringComparer.Ordinal.Equals(skills.GetProperty("idd-intent-change").GetProperty("invocation").GetString(), "auto"))
            {
                failures.Add("Missing invocation did not default to auto in manifest.json.");
            }

            if (!StringComparer.Ordinal.Equals(skills.GetProperty("idd-skip").GetProperty("invocation").GetString(), "manual"))
            {
                failures.Add("idd-skip is not manual in manifest.json.");
            }

            if (!StringComparer.Ordinal.Equals(skills.GetProperty("idd-factory-create-work-plan").GetProperty("invocation").GetString(), "auto"))
            {
                failures.Add("Factory skill invocation is not auto in manifest.json.");
            }
        }

        try
        {
            var descriptions = System.Text.Json.Nodes.JsonNode.Parse(original)!.AsObject();
            descriptions["idd-intent-brainstorm"]!.AsObject()["invocation"] = "auto";
            File.WriteAllText(descriptionPath, descriptions.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
            if (RunProcess("dotnet", $"exec \"{generatorDll}\" --check") != 0)
            {
                failures.Add("Explicit invocation: auto was not accepted.");
            }

            descriptions["idd-intent-brainstorm"]!.AsObject()["invocation"] = "sometimes";
            File.WriteAllText(descriptionPath, descriptions.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
            var result = RunProcessResult("dotnet", $"exec \"{generatorDll}\" --check", echoOutput: false);
            if (result.ExitCode == 0)
            {
                failures.Add("Invalid invocation value was accepted.");
            }

            if (!result.StandardError.Contains("Unsupported invocation value for skill 'idd-intent-brainstorm': 'sometimes'. Allowed values: auto, manual.", StringComparison.Ordinal))
            {
                failures.Add("Invalid invocation value did not report a clear error.");
            }
        }
        finally
        {
            File.WriteAllText(descriptionPath, original);
        }
    }

    void ExpectPublicSkillNameValidation()
    {
        var descriptionPath = Path.Combine(repoRoot, "src", "canonical", "skills", "skill-descriptions.json");
        var original = File.ReadAllText(descriptionPath);
        try
        {
            var descriptions = System.Text.Json.Nodes.JsonNode.Parse(original)!.AsObject();
            foreach (var invalidName in new[] { "idd-skip-anything", "idd-foo-bar", "idd" })
            {
                descriptions[invalidName] = new System.Text.Json.Nodes.JsonObject
                {
                    ["description"] = "invalid test skill"
                };
                File.WriteAllText(descriptionPath, descriptions.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
                var result = RunProcessResult("dotnet", $"exec \"{generatorDll}\" --check", echoOutput: false);
                if (result.ExitCode == 0 || !result.StandardError.Contains("expected idd-skip or idd-<area>-<action>", StringComparison.Ordinal))
                {
                    failures.Add($"Invalid public skill name '{invalidName}' was accepted or reported unclearly.");
                }
                descriptions.Remove(invalidName);
            }
        }
        finally
        {
            File.WriteAllText(descriptionPath, original);
        }
    }

    void ExpectGeneratorCheckPasses()
    {
        var exitCode = RunProcess("dotnet", $"exec \"{generatorDll}\" --check");
        if (exitCode != 0)
        {
            failures.Add("Generator check failed.");
        }
    }

    void ExpectListPacks()
    {
        var result = RunProcessResult("dotnet", $"exec \"{toolDll}\" list-packs");
        var expected = string.Join(Environment.NewLine, new[] { "core", "factory" });
        var actual = result.StandardOutput.Trim().ReplaceLineEndings(Environment.NewLine);

        if (result.ExitCode != 0)
        {
            failures.Add("list-packs failed.");
        }

        if (!StringComparer.Ordinal.Equals(actual, expected))
        {
            failures.Add($"list-packs returned unexpected output: {actual}");
        }
    }

    void ExpectListCodingAgents()
    {
        var result = RunProcessResult("dotnet", $"exec \"{toolDll}\" list-coding-agents");
        var expected = string.Join(Environment.NewLine, new[] { "claude", "codex", "copilot", "gemini" });
        var actual = result.StandardOutput.Trim().ReplaceLineEndings(Environment.NewLine);

        if (result.ExitCode != 0)
        {
            failures.Add("list-coding-agents failed.");
        }

        if (!StringComparer.Ordinal.Equals(actual, expected))
        {
            failures.Add($"list-coding-agents returned unexpected output: {actual}");
        }
    }

    void ExpectDefaultInstallCoreOnly()
    {
        WithToolInstall("install --target claude", installRoot =>
        {
            ExpectTempFile(installRoot, "CLAUDE.md", "default install did not create CLAUDE.md.");
            ExpectTempFile(installRoot, ".claude/skills/idd-intent-new-document/SKILL.md", "default install did not install core skill.");
            ExpectTempFile(installRoot, ".idd/intent/README.md", "default install did not install .idd/intent.");
            ExpectTempFile(installRoot, ".idd/intent/INDEX.md", "default install did not install .idd/intent index.");
            ExpectTempMissing(installRoot, SpecsDirectory, "default install created .specs directory.");

            if (File.Exists(Path.Combine(installRoot, ".claude/skills/idd-factory-create-work-plan/SKILL.md".Replace('/', Path.DirectorySeparatorChar))))
            {
                failures.Add("default install installed factory skills.");
            }

        });

        WithToolInstall("install --coding-agent claude", installRoot =>
        {
            ExpectTempFile(installRoot, "CLAUDE.md", "install --coding-agent did not create CLAUDE.md.");
            ExpectTempFile(installRoot, ".claude/skills/idd-intent-new-document/SKILL.md", "install --coding-agent did not install core skill.");
        });
    }

    void ExpectFactoryInstall()
    {
        foreach (var target in new[] { "claude" })
        {
            WithToolInstall($"install --target {target} --pack factory", installRoot =>
            {
                var skillRoot = target == "claude" ? ".claude/skills" : ".agents/skills";
                var entry = target == "claude" ? "CLAUDE.md" : "AGENTS.md";
                ExpectTempFile(installRoot, entry, $"factory install for {target} did not create {entry}.");
                ExpectTempFile(installRoot, $"{skillRoot}/idd-intent-new-document/SKILL.md", $"factory install for {target} did not install core skill.");
                ExpectTempFile(installRoot, $"{skillRoot}/idd-factory-create-work-plan/SKILL.md", $"factory install for {target} did not install factory skill.");
                ExpectTempFile(installRoot, ".idd/intent/README.md", $"factory install for {target} did not install .idd/intent.");
                ExpectTempFile(installRoot, ".idd/intent/INDEX.md", $"factory install for {target} did not install .idd/intent index.");
                ExpectTempFile(installRoot, ".idd/factory/.gitignore", $"factory install for {target} did not install factory .gitignore.");
                ExpectTempMissing(installRoot, SpecsDirectory, $"factory install for {target} created .specs directory.");

                if (Directory.Exists(Path.Combine(installRoot, ".idd/factory/work".Replace('/', Path.DirectorySeparatorChar))))
                {
                    failures.Add($"factory install for {target} created work directory.");
                }

            });
        }
    }

    void ExpectFactoryUnsupportedTargetRejected()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "idd-smoke-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);

        try
        {
            var result = RunProcessResult("dotnet", $"exec \"{toolDll}\" install --target gemini --pack factory", tempRoot, echoOutput: false);
            if (result.ExitCode != 0)
            {
                failures.Add("Factory install for Gemini failed despite omitting unsupported manual-only skills.");
            }
            if (File.Exists(Path.Combine(tempRoot, "GEMINI.md")) &&
                File.ReadAllText(Path.Combine(tempRoot, "GEMINI.md")).Contains("idd-skip/SKILL.md", StringComparison.Ordinal))
            {
                failures.Add("Gemini install exposed unsupported idd-skip skill.");
            }
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }

        tempRoot = Path.Combine(Path.GetTempPath(), "idd-smoke-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);

        try
        {
            var result = RunProcessResult("dotnet", $"exec \"{toolDll}\" install --target codex --pack factory", tempRoot, echoOutput: false);
            if (result.ExitCode != 0)
            {
                failures.Add("Factory install for Codex failed despite omitting unsupported manual-only skills.");
            }

            if (File.Exists(Path.Combine(tempRoot, ".agents/skills/idd-skip/SKILL.md".Replace('/', Path.DirectorySeparatorChar))))
            {
                failures.Add("Codex install exposed unsupported idd-skip skill.");
            }
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }

    void WithToolInstall(string arguments, Action<string> assertions)
    {
        var installRoot = Path.Combine(Path.GetTempPath(), "idd-smoke-install-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(installRoot);

        try
        {
            var result = RunProcessResult("dotnet", $"exec \"{toolDll}\" {arguments}", installRoot);
            if (result.ExitCode != 0)
            {
                failures.Add($"{arguments} failed.");
                return;
            }

            assertions(installRoot);
        }
        finally
        {
            if (Directory.Exists(installRoot))
            {
                Directory.Delete(installRoot, recursive: true);
            }
        }
    }

    void ExpectInstallEntryNone()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "idd-smoke-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);

        try
        {
            var exitCode = RunProcess("dotnet", $"exec \"{toolDll}\" install --target claude --entry none", tempRoot);
            if (exitCode != 0)
            {
                failures.Add("Install with --entry none failed.");
                return;
            }

            if (File.Exists(Path.Combine(tempRoot, "CLAUDE.md")))
            {
                failures.Add("Install with --entry none created CLAUDE.md.");
            }

            if (!File.Exists(Path.Combine(tempRoot, ".claude", "skills", "idd-intent-new-document", "SKILL.md")))
            {
                failures.Add("Install with --entry none did not install skills.");
            }

            if (!File.Exists(Path.Combine(tempRoot, ".idd/intent", "README.md")))
            {
                failures.Add("Install with --entry none did not install .idd/intent.");
            }
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }

    void ExpectInstallGeminiEntryNoneRejected()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "idd-smoke-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);

        try
        {
            var result = RunProcessResult("dotnet", $"exec \"{toolDll}\" install --target gemini --entry none", tempRoot, echoOutput: false);
            if (result.ExitCode == 0)
            {
                failures.Add("Gemini install with --entry none succeeded unexpectedly.");
            }

            if (!result.StandardError.Contains("CodingAgent gemini does not support generated skills", StringComparison.Ordinal))
            {
                failures.Add("Gemini install with --entry none did not report unsupported generated skills.");
            }
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }

    void ExpectInstallAllAfterInit()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "idd-smoke-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);

        try
        {
            var initExitCode = RunProcess("dotnet", $"exec \"{toolDll}\" init", tempRoot);
            if (initExitCode != 0)
            {
                failures.Add("Init failed before install --all.");
                return;
            }

            var installExitCode = RunProcess("dotnet", $"exec \"{toolDll}\" install --all", tempRoot);
            if (installExitCode != 0)
            {
                failures.Add("Install --all failed after init.");
                return;
            }

            foreach (var relativePath in new[]
            {
                "CLAUDE.md",
                "AGENTS.md",
                "GEMINI.md",
                ".github/copilot-instructions.md"
            })
            {
                if (!File.Exists(Path.Combine(tempRoot, relativePath)))
                {
                    failures.Add($"Install --all after init did not create {relativePath}.");
                }
            }
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }
}

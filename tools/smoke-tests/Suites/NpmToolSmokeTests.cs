using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;

internal sealed partial class SmokeTestSuite
{
    void ExpectNpmListTargets()
    {
        WithNpmFixture(fixtureRoot =>
        {
            var script = Path.Combine(fixtureRoot, "bin", "intent-driven-development.js");
            var result = RunProcessResult("node", $"\"{script}\" list-targets", fixtureRoot);
            var expected = string.Join(Environment.NewLine, new[] { "claude", "codex", "copilot", "gemini" });
            var actual = result.StandardOutput.Trim().ReplaceLineEndings(Environment.NewLine);

            if (result.ExitCode != 0)
            {
                failures.Add("npm list-targets failed.");
            }

            if (!StringComparer.Ordinal.Equals(actual, expected))
            {
                failures.Add($"npm list-targets returned unexpected output: {actual}");
            }
        });
    }

    void ExpectNpmListCodingAgents()
    {
        WithNpmFixture(fixtureRoot =>
        {
            var script = Path.Combine(fixtureRoot, "bin", "intent-driven-development.js");
            var result = RunProcessResult("node", $"\"{script}\" list-coding-agents", fixtureRoot);
            var expected = string.Join(Environment.NewLine, new[] { "claude", "codex", "copilot", "gemini" });
            var actual = result.StandardOutput.Trim().ReplaceLineEndings(Environment.NewLine);

            if (result.ExitCode != 0)
            {
                failures.Add("npm list-coding-agents failed.");
            }

            if (!StringComparer.Ordinal.Equals(actual, expected))
            {
                failures.Add($"npm list-coding-agents returned unexpected output: {actual}");
            }
        });
    }

    void ExpectNpmListPacks()
    {
        WithNpmFixture(fixtureRoot =>
        {
            var script = Path.Combine(fixtureRoot, "bin", "intent-driven-development.js");
            var result = RunProcessResult("node", $"\"{script}\" list-packs", fixtureRoot);
            var expected = string.Join(Environment.NewLine, new[] { "core", "factory" });
            var actual = result.StandardOutput.Trim().ReplaceLineEndings(Environment.NewLine);

            if (result.ExitCode != 0)
            {
                failures.Add("npm list-packs failed.");
            }

            if (!StringComparer.Ordinal.Equals(actual, expected))
            {
                failures.Add($"npm list-packs returned unexpected output: {actual}");
            }
        });
    }

    void ExpectNpmInstallDefaultMinimal()
    {
        WithNpmInstall("install --target claude", installRoot =>
        {
            ExpectTempFile(installRoot, "CLAUDE.md", "npm default minimal install did not create CLAUDE.md.");
            ExpectTempFile(installRoot, ".claude/skills/idd-intent-new-document/SKILL.md", "npm default minimal install did not install skills.");
            ExpectTempFile(installRoot, ".idd/intent/README.md", "npm default minimal install did not install .idd/intent.");
            ExpectTempFile(installRoot, ".idd/intent/INDEX.md", "npm default minimal install did not install .idd/intent index.");
            ExpectTempMissing(installRoot, LegacySpecsDirectory, "npm default minimal install created legacy specs directory.");
        });

        WithNpmInstall("install --coding-agent claude", installRoot =>
        {
            ExpectTempFile(installRoot, "CLAUDE.md", "npm install --coding-agent did not create CLAUDE.md.");
            ExpectTempFile(installRoot, ".claude/skills/idd-intent-new-document/SKILL.md", "npm install --coding-agent did not install skills.");
        });
    }

    void ExpectNpmInstallDefaultCoreOnly()
    {
        WithNpmInstall("install --target claude", installRoot =>
        {
            if (File.Exists(Path.Combine(installRoot, ".claude/skills/idd-factory-create-work-plan/SKILL.md".Replace('/', Path.DirectorySeparatorChar))))
            {
                failures.Add("npm default install installed factory skills.");
            }

            ExpectTempFile(installRoot, ".idd/intent/README.md", "npm default install did not install .idd/intent.");
            ExpectTempFile(installRoot, ".idd/intent/INDEX.md", "npm default install did not install .idd/intent index.");
            ExpectTempMissing(installRoot, LegacySpecsDirectory, "npm default install created legacy specs directory.");
        });
    }

    void ExpectNpmInstallFactory()
    {
        WithNpmInstall("install --target codex --pack factory", installRoot =>
        {
            ExpectTempFile(installRoot, "AGENTS.md", "npm factory install did not create AGENTS.md.");
            ExpectTempFile(installRoot, ".agents/skills/idd-intent-new-document/SKILL.md", "npm factory install did not install core skill.");
            ExpectTempFile(installRoot, ".agents/skills/idd-factory-create-work-plan/SKILL.md", "npm factory install did not install factory skill.");
            ExpectTempFile(installRoot, ".idd/intent/README.md", "npm factory install did not install .idd/intent.");
            ExpectTempFile(installRoot, ".idd/intent/INDEX.md", "npm factory install did not install .idd/intent index.");
            ExpectTempFile(installRoot, ".idd/factory/.gitignore", "npm factory install did not install factory .gitignore.");
            ExpectTempMissing(installRoot, LegacySpecsDirectory, "npm factory install created legacy specs directory.");

            if (Directory.Exists(Path.Combine(installRoot, ".idd/factory/work".Replace('/', Path.DirectorySeparatorChar))))
            {
                failures.Add("npm factory install created work directory.");
            }
        });
    }

    void ExpectNpmInstallEntryNone()
    {
        WithNpmInstall("install --target claude --entry none", installRoot =>
        {
            if (File.Exists(Path.Combine(installRoot, "CLAUDE.md")))
            {
                failures.Add("npm install with --entry none created CLAUDE.md.");
            }

            ExpectTempFile(installRoot, ".claude/skills/idd-intent-new-document/SKILL.md", "npm install with --entry none did not install skills.");
            ExpectTempFile(installRoot, ".idd/intent/README.md", "npm install with --entry none did not install .idd/intent.");
            ExpectTempFile(installRoot, ".idd/intent/INDEX.md", "npm install with --entry none did not install .idd/intent index.");
            ExpectTempMissing(installRoot, LegacySpecsDirectory, "npm install with --entry none created legacy specs directory.");
        });
    }

    void ExpectNpmInstallEntryFull()
    {
        WithNpmInstall("install --target claude --entry full", installRoot =>
        {
            var entryPath = Path.Combine(installRoot, "CLAUDE.md");
            ExpectTempFile(installRoot, "CLAUDE.md", "npm install with --entry full did not create CLAUDE.md.");
            if (File.Exists(entryPath))
            {
                var lineCount = File.ReadAllText(entryPath).ReplaceLineEndings("\n").Split('\n').Length;
                if (lineCount <= 80)
                {
                    failures.Add($"npm install with --entry full created an unexpectedly short entry point: {lineCount} lines.");
                }
            }

            ExpectTempFile(installRoot, ".claude/skills/idd-intent-new-document/SKILL.md", "npm install with --entry full did not install skills.");
            ExpectTempFile(installRoot, ".idd/intent/README.md", "npm install with --entry full did not install .idd/intent.");
            ExpectTempFile(installRoot, ".idd/intent/INDEX.md", "npm install with --entry full did not install .idd/intent index.");
            ExpectTempMissing(installRoot, LegacySpecsDirectory, "npm install with --entry full created legacy specs directory.");
        });
    }

    void ExpectNpmRejectsGeminiEntryNone()
    {
        WithNpmFixture(fixtureRoot =>
        {
            var script = Path.Combine(fixtureRoot, "bin", "intent-driven-development.js");
            var installRoot = Path.Combine(Path.GetTempPath(), "idd-smoke-npm-install-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(installRoot);

            try
            {
                var result = RunProcessResult("node", $"\"{script}\" install --target gemini --entry none", installRoot);
                if (result.ExitCode == 0)
                {
                    failures.Add("npm Gemini install with --entry none succeeded unexpectedly.");
                }

                if (!result.StandardError.Contains("CodingAgent gemini does not support generated skills", StringComparison.Ordinal))
                {
                    failures.Add("npm Gemini install with --entry none did not report unsupported generated skills.");
                }
            }
            finally
            {
                if (Directory.Exists(installRoot))
                {
                    Directory.Delete(installRoot, recursive: true);
                }
            }
        });
    }

    void ExpectNpmRejectsFactoryForGemini()
    {
        WithNpmFixture(fixtureRoot =>
        {
            var script = Path.Combine(fixtureRoot, "bin", "intent-driven-development.js");
            var installRoot = Path.Combine(Path.GetTempPath(), "idd-smoke-npm-install-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(installRoot);

            try
            {
                var result = RunProcessResult("node", $"\"{script}\" install --target gemini --pack factory", installRoot);
                if (result.ExitCode == 0)
                {
                    failures.Add("npm factory install for Gemini succeeded unexpectedly.");
                }

                if (!result.StandardError.Contains("Factory pack requires generated skills", StringComparison.Ordinal))
                {
                    failures.Add("npm factory install for Gemini did not report unsupported generated skills.");
                }
            }
            finally
            {
                if (Directory.Exists(installRoot))
                {
                    Directory.Delete(installRoot, recursive: true);
                }
            }
        });
    }

    void ExpectNpmRejectsUnknownPack()
    {
        WithNpmFixture(fixtureRoot =>
        {
            var script = Path.Combine(fixtureRoot, "bin", "intent-driven-development.js");
            var installRoot = Path.Combine(Path.GetTempPath(), "idd-smoke-npm-install-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(installRoot);

            try
            {
                var result = RunProcessResult("node", $"\"{script}\" install --target claude --pack bogus", installRoot);
                if (result.ExitCode == 0)
                {
                    failures.Add("npm install with unknown pack succeeded unexpectedly.");
                }

                if (!result.StandardError.Contains("Unknown pack: bogus", StringComparison.Ordinal))
                {
                    failures.Add("npm install with unknown pack did not report the invalid pack.");
                }
            }
            finally
            {
                if (Directory.Exists(installRoot))
                {
                    Directory.Delete(installRoot, recursive: true);
                }
            }
        });
    }

    void ExpectNpmRejectsUnknownEntryMode()
    {
        WithNpmFixture(fixtureRoot =>
        {
            var script = Path.Combine(fixtureRoot, "bin", "intent-driven-development.js");
            var installRoot = Path.Combine(Path.GetTempPath(), "idd-smoke-npm-install-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(installRoot);

            try
            {
                var result = RunProcessResult("node", $"\"{script}\" install --target claude --entry compact", installRoot);
                if (result.ExitCode == 0)
                {
                    failures.Add("npm install with unknown entry mode succeeded unexpectedly.");
                }

                if (!result.StandardError.Contains("Unknown entry mode: compact", StringComparison.Ordinal))
                {
                    failures.Add("npm install with unknown entry mode did not report the invalid mode.");
                }
            }
            finally
            {
                if (Directory.Exists(installRoot))
                {
                    Directory.Delete(installRoot, recursive: true);
                }
            }
        });
    }

    void WithNpmInstall(string arguments, Action<string> assertions)
    {
        WithNpmFixture(fixtureRoot =>
        {
            var script = Path.Combine(fixtureRoot, "bin", "intent-driven-development.js");
            var installRoot = Path.Combine(Path.GetTempPath(), "idd-smoke-npm-install-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(installRoot);

            try
            {
                var result = RunProcessResult("node", $"\"{script}\" {arguments}", installRoot);
                if (result.ExitCode != 0)
                {
                    failures.Add($"npm {arguments} failed.");
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
        });
    }

    void WithNpmFixture(Action<string> action)
    {
        var fixtureRoot = Path.Combine(Path.GetTempPath(), "idd-smoke-npm-fixture-" + Guid.NewGuid().ToString("N"));

        try
        {
            Directory.CreateDirectory(fixtureRoot);
            Directory.CreateDirectory(Path.Combine(fixtureRoot, "package-content"));
            File.Copy(Path.Combine(repoRoot, "npm", "package.json"), Path.Combine(fixtureRoot, "package.json"));
            CopyDirectoryRecursive(Path.Combine(repoRoot, "npm", "bin"), Path.Combine(fixtureRoot, "bin"));
            CopyDirectoryRecursive(Path.Combine(repoRoot, "npm", "lib"), Path.Combine(fixtureRoot, "lib"));
            if (!File.Exists(Path.Combine(repoRoot, "manifest.json")))
            {
                failures.Add("manifest.json is missing before npm fixture copy. RunGenerator must create it.");
                return;
            }

            File.Copy(Path.Combine(repoRoot, "manifest.json"), Path.Combine(fixtureRoot, "package-content", "manifest.json"));
            CopyDirectoryRecursive(Path.Combine(repoRoot, "generated"), Path.Combine(fixtureRoot, "package-content", "generated"));
            CopyDirectoryRecursive(Path.Combine(repoRoot, "src"), Path.Combine(fixtureRoot, "package-content", "src"));
            File.Copy(Path.Combine(repoRoot, "README.md"), Path.Combine(fixtureRoot, "package-content", "README.md"));
            File.Copy(Path.Combine(repoRoot, "LICENSE"), Path.Combine(fixtureRoot, "package-content", "LICENSE"));

            action(fixtureRoot);
        }
        finally
        {
            if (Directory.Exists(fixtureRoot))
            {
                Directory.Delete(fixtureRoot, recursive: true);
            }
        }
    }
}

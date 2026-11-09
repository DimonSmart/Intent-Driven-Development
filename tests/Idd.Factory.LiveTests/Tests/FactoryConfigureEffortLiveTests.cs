using YamlDotNet.RepresentationModel;
using Xunit;

namespace Idd.Factory.LiveTests.Tests;

public sealed partial class NativeFactoryEndToEndLiveTests
{
    [LiveFactoryEvalFact]
    [Trait("Category", "LiveFactoryConfigure")]
    public async Task FactoryConfigure_UpdatesAndClearsEffortWithoutLosingOtherMappings()
    {
        var repo = FindRepositoryRoot();
        var tempRoot = Path.Combine(Path.GetTempPath(), "idd-factory-configure-effort", Guid.NewGuid().ToString("N"));
        var workspace = Path.Combine(tempRoot, "workspace");
        var marketplace = Path.Combine(tempRoot, "marketplace");
        var codexHome = Path.Combine(tempRoot, "codex-home");
        var artifacts = Path.Combine(repo, "artifacts", "factory-evals", "configure-effort-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(artifacts);
        Directory.CreateDirectory(Path.Combine(workspace, ".idd"));
        var policyPath = Path.Combine(workspace, ".idd", "execution.yaml");
        const string initialPolicy = """
            version: 1
            factory:
              executionProfiles:
                economy:
                  codex:
                    model: model-a
                    reasoningEffort: low
                standard:
                  codex:
                    model: model-b
                    reasoningEffort: medium
                  claude:
                    model: claude-model
                    effort: high
                strong:
                  codex:
                    model: model-c
            """;
        await File.WriteAllTextAsync(policyPath, initialPolicy);
        var modelSettings = LiveEvalSettings.Resolve(
            repo,
            Environment.GetEnvironmentVariable("IDD_FACTORY_EVAL_MODEL"),
            Environment.GetEnvironmentVariable("IDD_FACTORY_EVAL_REASONING_EFFORT"),
            Environment.GetEnvironmentVariable("IDD_FACTORY_EVAL_MODEL_SOURCE"),
            Environment.GetEnvironmentVariable("IDD_FACTORY_EVAL_REASONING_EFFORT_SOURCE"),
            profileEnvironment: Environment.GetEnvironmentVariable);
        var env = new Dictionary<string, string?> { ["CODEX_HOME"] = codexHome };
        var codex = Environment.GetEnvironmentVariable("IDD_FACTORY_EVAL_CODEX") ?? "codex";
        var timeoutMinutes = int.TryParse(Environment.GetEnvironmentVariable("IDD_FACTORY_EVAL_TIMEOUT_MINUTES"), out var minutes)
            ? minutes : 20;
        try
        {
            await InitializeGitRepositoryAsync(workspace, artifacts);
            PrepareCodexHome(codexHome);
            await RunAsync("dotnet",
                ["build", "tools/generate/Generate.csproj", "--nologo"],
                repo, null, null, artifacts, "06-generator-build");
            var generator = Path.Combine(repo, "tools", "generate", "bin", "Debug", "net10.0", "Generate.dll");
            await RunAsync("dotnet",
                ["exec", generator, "--version", "0.0.0-live", "--output", marketplace],
                repo, null, null, artifacts, "07-generate-marketplace");
            await RunAsync(codex,
                ["plugin", "marketplace", "add", marketplace, "--json"],
                repo, env, null, artifacts, "08-codex-marketplace-add");
            await RunAsync(codex,
                ["plugin", "add", "idd-factory@intent-driven-development", "--json"],
                repo, env, null, artifacts, "09-codex-plugin-add");

            var expected = FlattenExecutionPolicy(initialPolicy);
            await ConfigureAsync("Use the installed idd-factory-configure skill. This is an exact authorized edit: " +
                "set only standard.codex.reasoningEffort to high. Preserve all other mappings and fields.", "10-effort-edit");
            expected["factory.executionProfiles.standard.codex.reasoningEffort"] = "high";
            Assert.Equal(expected.ToArray(), FlattenExecutionPolicy(await File.ReadAllTextAsync(policyPath)).ToArray());

            await ConfigureAsync("Use the installed idd-factory-configure skill. Exact authorized edit: " +
                "set strong.codex.model to model-d and strong.codex.reasoningEffort to low. " +
                "Leave all other fields unchanged.", "11-model-and-effort-edit");
            expected["factory.executionProfiles.strong.codex.model"] = "model-d";
            expected["factory.executionProfiles.strong.codex.reasoningEffort"] = "low";
            Assert.Equal(expected.ToArray(), FlattenExecutionPolicy(await File.ReadAllTextAsync(policyPath)).ToArray());

            await ConfigureAsync("Use the installed idd-factory-configure skill. Exact authorized edit: " +
                "clear only strong.codex.reasoningEffort so the selected model uses the host default. " +
                "Do not remove the model mapping or change any other fields.", "12-effort-clear");
            expected.Remove("factory.executionProfiles.strong.codex.reasoningEffort");
            Assert.Equal(expected.ToArray(), FlattenExecutionPolicy(await File.ReadAllTextAsync(policyPath)).ToArray());

            var malformed = (await File.ReadAllTextAsync(policyPath))
                .Replace("reasoningEffort: high", "reasoningEffort:", StringComparison.Ordinal);
            await File.WriteAllTextAsync(policyPath, malformed);
            await ConfigureAsync("Use the installed idd-factory-configure skill. Try to update only " +
                "economy.codex.model to model-z. The existing YAML has a malformed blank reasoning " +
                "field elsewhere. Do not repair it without authorization.", "13-invalid-policy");
            Assert.Equal(malformed, await File.ReadAllTextAsync(policyPath));

            async Task ConfigureAsync(string request, string artifactName)
            {
                await RunAsync(codex,
                    [
                        "exec", "--json", "--ignore-rules",
                        "--enable", "plugins", "--disable", "remote_plugin",
                        "-c", "approval_policy=never",
                        "-c", $"model_reasoning_effort={modelSettings.ReasoningEffort}",
                        "--model", modelSettings.Model,
                        "--sandbox", "danger-full-access",
                        "--cd", workspace, "-"
                    ],
                    workspace, env, request, artifacts, artifactName,
                    timeout: TimeSpan.FromMinutes(timeoutMinutes));
            }
        }
        finally
        {
            await TrySaveWorkspaceArtifactsAsync(workspace, artifacts);
            await TrySaveReportSourceAsync(codexHome, artifacts);
            if (Directory.Exists(tempRoot) &&
                Environment.GetEnvironmentVariable("IDD_FACTORY_EVAL_KEEP_TEMP") != "1")
                Directory.Delete(tempRoot, recursive: true);
        }
    }

    private static SortedDictionary<string, string?> FlattenExecutionPolicy(string yaml)
    {
        var stream = new YamlStream();
        stream.Load(new StringReader(yaml));
        var flattened = new SortedDictionary<string, string?>(StringComparer.Ordinal);
        Visit(stream.Documents.Single().RootNode, "");

        void Visit(YamlNode node, string prefix)
        {
            if (node is YamlMappingNode map)
            {
                foreach (var entry in map.Children)
                {
                    var key = ((YamlScalarNode)entry.Key).Value;
                    Visit(entry.Value, string.IsNullOrEmpty(prefix) ? key! : prefix + "." + key);
                }
            }
            else if (node is YamlScalarNode scalar)
            {
                flattened.Add(prefix, scalar.Value);
            }
            else
            {
                throw new InvalidOperationException("Unexpected YAML sequence in execution policy.");
            }
        }

        return flattened;
    }
}

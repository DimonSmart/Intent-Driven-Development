using System.Text.Json;
using Idd.Factory.Agents;
using Idd.Factory.Domain;
using Idd.Factory.LiveTests.Infrastructure;
using Idd.Factory.Runtime;
using Xunit;

namespace Idd.Factory.LiveTests.Tests;

[Collection("Live Factory Evals")]
public sealed class CodexSemanticWorkerShellSmokeLiveTests
{
    [LiveFactoryEvalFact]
    [Trait("Category", "LiveFactoryEval")]
    public async Task DecomposerWithoutVerificationPolicyUsesEmptyStableCheckIds()
    {
        var pluginRoot = RequiredEnvironment("IDD_FACTORY_SMOKE_PLUGIN_ROOT");
        var outputDirectory = Path.GetFullPath(RequiredEnvironment("IDD_FACTORY_SMOKE_OUTPUT"));
        var codexExecutable = RequiredEnvironment("IDD_FACTORY_SMOKE_CODEX_EXECUTABLE");
        var workspace = Path.Combine(outputDirectory, "missing-verification-policy-workspace");
        var intentDirectory = Path.Combine(workspace, ".idd", "intent");
        var attemptDirectory = Path.Combine(workspace, ".idd", "factory", "current", "attempts", "NOPOLICY01");
        Directory.CreateDirectory(intentDirectory);
        Directory.CreateDirectory(attemptDirectory);
        await File.WriteAllTextAsync(Path.Combine(intentDirectory, "IDD-0001.spec.md"), "# Catalog intent\n\nThe catalog supports durable file storage and automated behavioral verification.\n");
        var resultPath = Path.Combine(attemptDirectory, "planning-output.md");
        var invocation = new AgentInvocation
        {
            RunId = "missing-policy-smoke",
            AttemptId = "NOPOLICY01",
            Capability = "planning",
            Role = "planner",
            Workspace = workspace,
            SemanticOutputPath = resultPath,
            SkillName = "idd-factory-decompose-task",
            ExecutionProfile = AgentExecutionProfile.ReadOnly,
            Input = "Decompose this Factory request: implement durable file-backed catalog storage, add automated behavioral tests, and require successful repository build and test verification. The workspace intentionally has no .idd/verification.yaml. Do not create one and do not implement the task.",
            StartedAt = DateTimeOffset.UtcNow,
        };
        var backend = CreateBackend(pluginRoot, codexExecutable, "missing-policy-smoke");

        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(5));
        var handle = await backend.StartAsync(invocation, timeout.Token);
        var processResult = await backend.WaitAsync(handle, timeout.Token);
        var result = await File.ReadAllTextAsync(resultPath, timeout.Token);

        Assert.Equal(0, processResult.ExitCode);
        var workItems = PlannerBatchParser.Parse(result);
        Assert.NotEmpty(workItems);
        Assert.DoesNotContain("capability", result, StringComparison.OrdinalIgnoreCase);
    }

    [LiveFactoryEvalFact]
    [Trait("Category", "LiveFactoryEval")]
    public async Task CodexCliBackend_CanLaunchARealSandboxedShellCommand()
    {
        var pluginRoot = RequiredEnvironment("IDD_FACTORY_SMOKE_PLUGIN_ROOT");
        var outputDirectory = Path.GetFullPath(RequiredEnvironment("IDD_FACTORY_SMOKE_OUTPUT"));
        var codexExecutable = RequiredEnvironment("IDD_FACTORY_SMOKE_CODEX_EXECUTABLE");
        var workspace = Path.Combine(outputDirectory, "workspace");
        var attemptDirectory = Path.Combine(workspace, ".idd", "factory", "current", "attempts", "SMOKE0001");
        Directory.CreateDirectory(attemptDirectory);
        var resultPath = Path.Combine(attemptDirectory, "planning-output.md");
        var invocation = new AgentInvocation
        {
            RunId = "sandbox-smoke",
            AttemptId = "SMOKE0001",
            Capability = "planning",
            Role = "planner",
            Workspace = workspace,
            SemanticOutputPath = resultPath,
            SkillName = "idd-factory-decompose-task",
            ExecutionProfile = AgentExecutionProfile.ReadOnly,
            Input = "This is an isolated transport smoke test. Execute the real shell command `Write-Output IDD_SANDBOX_OK`. Do not modify files. After it succeeds, return an empty planning response because no Factory run will consume it.",
            StartedAt = DateTimeOffset.UtcNow,
        };
        var backend = CreateBackend(pluginRoot, codexExecutable, "sandbox-smoke");

        AgentProcessResult? processResult = null;
        Exception? failure = null;
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(5));
            var handle = await backend.StartAsync(invocation, timeout.Token);
            processResult = await backend.WaitAsync(handle, timeout.Token);
        }
        catch (Exception exception)
        {
            failure = exception;
        }

        var stdout = File.Exists(Path.Combine(attemptDirectory, "stdout.log"))
            ? await File.ReadAllTextAsync(Path.Combine(attemptDirectory, "stdout.log"))
            : string.Empty;
        var stderr = File.Exists(Path.Combine(attemptDirectory, "stderr.log"))
            ? await File.ReadAllTextAsync(Path.Combine(attemptDirectory, "stderr.log"))
            : string.Empty;
        var telemetryPath = Path.Combine(attemptDirectory, "attempt-telemetry.json");
        using var telemetry = JsonDocument.Parse(await File.ReadAllTextAsync(telemetryPath));
        var resolvedShell = TryReadResolvedShell(stdout);
        var smokeResult = new
        {
            semanticWorkerStarted = processResult is not null,
            shellCommandStarted = stdout.Contains("Write-Output IDD_SANDBOX_OK", StringComparison.Ordinal),
            markerObserved = stdout.Contains("IDD_SANDBOX_OK", StringComparison.Ordinal),
            createProcessAsUserFailureAbsent = !stderr.Contains("CreateProcessAsUserW failed", StringComparison.OrdinalIgnoreCase),
            exitCode = processResult?.ExitCode,
            resolvedShellExecutable = resolvedShell,
            resolvedShellUsesWindowsApps = resolvedShell?.Contains("WindowsApps", StringComparison.OrdinalIgnoreCase),
            windowsSandbox = telemetry.RootElement.GetProperty("windowsSandbox").GetString(),
            windowsAppsPathEntriesRemoved = telemetry.RootElement.GetProperty("windowsAppsPathEntriesRemoved").GetInt32(),
            failure = failure?.ToString()
        };
        await File.WriteAllTextAsync(
            Path.Combine(outputDirectory, "sandbox-smoke-result.json"),
            JsonSerializer.Serialize(smokeResult, new JsonSerializerOptions { WriteIndented = true }) + "\n");

        Assert.Null(failure);
        Assert.NotNull(processResult);
        Assert.Equal(0, processResult!.ExitCode);
        Assert.Contains("Write-Output IDD_SANDBOX_OK", stdout, StringComparison.Ordinal);
        Assert.Contains("IDD_SANDBOX_OK", stdout, StringComparison.Ordinal);
        Assert.DoesNotContain("CreateProcessAsUserW failed", stderr, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("WindowsApps", resolvedShell ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("unelevated", telemetry.RootElement.GetProperty("windowsSandbox").GetString());
        Assert.True(telemetry.RootElement.GetProperty("windowsAppsPathEntriesRemoved").GetInt32() > 0);
    }

    private static string RequiredEnvironment(string name) =>
        Environment.GetEnvironmentVariable(name)
        ?? throw new InvalidOperationException($"{name} must be configured for the semantic-worker smoke test.");

    private static CodexCliBackend CreateBackend(string pluginRoot, string codexExecutable, string profile) =>
        new(
            pluginRoot,
            codexExecutable,
            new(
                Environment.GetEnvironmentVariable("IDD_FACTORY_EVAL_MODEL") ?? "gpt-5.6-sol",
                Environment.GetEnvironmentVariable("IDD_FACTORY_EVAL_REASONING_EFFORT") ?? "low",
                "unelevated"),
            new(false, profile));

    private static string? TryReadResolvedShell(string stdout)
    {
        foreach (var line in stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            try
            {
                using var item = JsonDocument.Parse(line);
                if (!item.RootElement.TryGetProperty("item", out var payload)
                    || !payload.TryGetProperty("type", out var type)
                    || type.GetString() != "command_execution"
                    || !payload.TryGetProperty("command", out var commandValue))
                    continue;
                var command = commandValue.GetString();
                if (string.IsNullOrWhiteSpace(command)) continue;
                if (command[0] == '"')
                {
                    var closingQuote = command.IndexOf('"', 1);
                    if (closingQuote > 1) return command[1..closingQuote];
                }
                var firstSpace = command.IndexOf(' ');
                return firstSpace > 0 ? command[..firstSpace] : command;
            }
            catch (JsonException) { }
        }
        return null;
    }
}

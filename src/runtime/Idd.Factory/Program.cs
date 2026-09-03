using System.Text;
using System.Text.Json;
using Idd.Factory.Agents;
using Idd.Factory.Configuration;
using Idd.Factory.Domain;
using Idd.Factory.Persistence;
using Idd.Factory.Runtime;
using Idd.Factory.State;
using Idd.Factory.Telemetry;
using Idd.Factory.Verification;

return await FactoryProgram.RunAsync(args);

internal static class FactoryProgram
{
    internal static Task<int> RunAsync(string[] args, Func<Task<int>>? runMcp = null) =>
        args is ["mcp"]
            ? (runMcp ?? (() => FactoryMcpServer.RunAsync()))()
            : FactoryCli.RunAsync(args);
}

internal static class FactoryCli
{
    public static async Task<int> RunAsync(string[] args)
    {
        FactoryCliOutcome outcome;
        try
        {
            Console.InputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
            if (args.Length == 0 || args[0] is "-h" or "--help")
            {
                Console.WriteLine("idd-factory run --workspace <path> (--request-file <path> | --request-stdin true)\nidd-factory continue --workspace <path> [--confirmation approve|decline] [--verification-result passed|failed]\nidd-factory cancel --workspace <path>");
                return 0;
            }

            var command = args[0];
            var options = Parse(args.Skip(1).ToArray());
            var workspace = Path.GetFullPath(Required(options, "workspace"));
            var baseDirectory = AppContext.BaseDirectory;
            var pluginRoot = options.TryGetValue("plugin-root", out var configuredRoot)
                ? Path.GetFullPath(configuredRoot)
                : Directory.GetParent(baseDirectory.TrimEnd(Path.DirectorySeparatorChar))?.FullName ?? baseDirectory;
            var packagedConfiguration = options.TryGetValue("configuration", out var configurationPath)
                ? Path.GetFullPath(configurationPath)
                : Path.Combine(baseDirectory, "factory.yaml");
            var configuration = new FactoryConfigurationLoader().Load(workspace, packagedConfiguration);
            var current = Path.Combine(workspace, ".idd", "factory", "current");
            var clock = new SystemClock();
            var store = new FileFactoryStateStore(current, new FactoryStateValidator());
            var windowsSandbox = ResolveWindowsSandbox(Environment.GetEnvironmentVariable("IDD_FACTORY_WINDOWS_SANDBOX"), OperatingSystem.IsWindows());
            var backend = new CodexCliBackend(
                pluginRoot,
                options.GetValueOrDefault("codex"),
                new AgentExecutionConfiguration(
                    Environment.GetEnvironmentVariable("IDD_FACTORY_MODEL"),
                    Environment.GetEnvironmentVariable("IDD_FACTORY_REASONING_EFFORT"),
                    windowsSandbox),
                new AgentCapabilityPolicy(
                    !string.Equals(Environment.GetEnvironmentVariable("IDD_FACTORY_INHERIT_USER_SKILLS"), "false", StringComparison.OrdinalIgnoreCase),
                    Environment.GetEnvironmentVariable("IDD_FACTORY_CAPABILITY_PROFILE") ?? "production-default"));
            var runtime = new FactoryRuntime(
                workspace,
                configuration,
                store,
                new FactoryAgentExecutor(backend),
                new VerificationEngine(workspace, current),
                new FactoryEventWriter(current, clock),
                clock);

            var factoryDirectory = Path.Combine(workspace, ".idd", "factory");
            Directory.CreateDirectory(factoryDirectory);
            var cancellationMarker = Path.Combine(factoryDirectory, "cancellation.requested");
            FactoryRuntimeLock runLock;
            try { runLock = FactoryRuntimeLock.Acquire(Path.Combine(factoryDirectory, "runtime.lock"), command, clock.UtcNow); }
            catch (FactoryStateException) when (command == "cancel")
            {
                await File.WriteAllTextAsync(cancellationMarker, DateTimeOffset.UtcNow.ToString("O"));
                outcome = new("CANCELLATION_REQUESTED", "unknown", "The active Factory runtime was asked to stop; product changes will be preserved.");
                Console.WriteLine(JsonSerializer.Serialize(outcome, FactoryJson.Options));
                return ExitCode(outcome.FactoryOutcome);
            }

            await using var heldLock = runLock;
            using var cancellation = new CancellationTokenSource();
            Console.CancelKeyPress += (_, eventArgs) => { eventArgs.Cancel = true; cancellation.Cancel(); };
            using var monitorStop = new CancellationTokenSource();
            var monitor = command == "cancel" ? Task.CompletedTask : MonitorCancellationAsync(cancellationMarker, cancellation, monitorStop.Token);
            try
            {
                if (command == "run" && options.ContainsKey("request-file") == (options.GetValueOrDefault("request-stdin") == "true"))
                    throw new ArgumentException("run requires exactly one request input: --request-file <path> or --request-stdin true.");
                outcome = command switch
                {
                    "run" when options.ContainsKey("request-file") => await runtime.RunAsync(Path.GetFullPath(Required(options, "request-file")), ReadMethodologyVersion(pluginRoot), cancellation.Token),
                    "run" when options.GetValueOrDefault("request-stdin") == "true" => await runtime.RunRequestAsync(await Console.In.ReadToEndAsync(cancellation.Token), ReadMethodologyVersion(pluginRoot), cancellation.Token),
                    "run" => throw new ArgumentException("run requires exactly one request input: --request-file <path> or --request-stdin true."),
                    "continue" => await runtime.ContinueAsync(
                        cancellation.Token,
                        options.TryGetValue("confirmation", out var confirmation) ? confirmation switch
                        {
                            "approve" => VerificationConfirmation.Approve,
                            "decline" => VerificationConfirmation.Decline,
                            _ => throw new ArgumentException("--confirmation must be approve or decline.")
                        } : VerificationConfirmation.None,
                        options.TryGetValue("verification-result", out var verificationResult) ? verificationResult switch
                        {
                            "passed" => true,
                            "failed" => false,
                            _ => throw new ArgumentException("--verification-result must be passed or failed.")
                        } : null),
                    "cancel" => await runtime.CancelAsync(cancellation.Token),
                    _ => throw new ArgumentException($"Unknown command '{command}'.")
                };
            }
            catch (OperationCanceledException) when (File.Exists(cancellationMarker))
            {
                outcome = await runtime.CancelAsync(CancellationToken.None);
                File.Delete(cancellationMarker);
            }
            finally
            {
                monitorStop.Cancel();
                try { await monitor; } catch (OperationCanceledException) { }
            }
        }
        catch (FactoryStateException exception) { outcome = new(exception.Code, "unknown", exception.Message); }
        catch (FactoryConfigurationException exception) { outcome = new(exception.Code, "unknown", exception.Message); }
        catch (AgentProtocolException exception) { outcome = new(exception.Code, "unknown", exception.Message); }
        catch (VerificationException exception) { outcome = new(exception.Code, "unknown", exception.Message); }
        catch (OperationCanceledException) { outcome = new("INTERRUPTED", "unknown", "Runtime execution was interrupted; persisted state was preserved."); }
        catch (Exception exception) { outcome = new("RUNTIME_ERROR", "unknown", exception.Message); }
        Console.WriteLine(JsonSerializer.Serialize(outcome, FactoryJson.Options));
        return ExitCode(outcome.FactoryOutcome);
    }

    internal static string? ResolveWindowsSandbox(string? configured, bool isWindows)
    {
        if (!isWindows) return null;
        if (string.IsNullOrWhiteSpace(configured)) return "unelevated";
        var normalized = configured.Trim().ToLowerInvariant();
        if (normalized is "unelevated" or "elevated") return normalized;
        throw new AgentProtocolException("INVALID_WINDOWS_SANDBOX", $"IDD_FACTORY_WINDOWS_SANDBOX must be 'unelevated' or 'elevated' on Windows, but was '{configured}'.");
    }

    private static Dictionary<string, string> Parse(string[] args)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var i = 0; i < args.Length; i += 2)
        {
            if (!args[i].StartsWith("--") || i + 1 >= args.Length) throw new ArgumentException($"Invalid option near '{args[i]}'.");
            result.Add(args[i][2..], args[i + 1]);
        }
        return result;
    }

    private static string Required(IReadOnlyDictionary<string, string> options, string name) =>
        options.TryGetValue(name, out var value) && !string.IsNullOrWhiteSpace(value) ? value : throw new ArgumentException($"--{name} is required.");

    private static string ReadMethodologyVersion(string pluginRoot)
    {
        var path = Path.Combine(pluginRoot, "skills", "idd-factory-run", "references", "methodology-version.json");
        if (!File.Exists(path)) return "development";
        using var json = JsonDocument.Parse(File.ReadAllText(path));
        return json.RootElement.TryGetProperty("methodologyVersion", out var value) ? value.GetString() ?? "unknown" : "unknown";
    }

    private static async Task MonitorCancellationAsync(string marker, CancellationTokenSource execution, CancellationToken stop)
    {
        while (!stop.IsCancellationRequested)
        {
            if (File.Exists(marker)) { execution.Cancel(); return; }
            await Task.Delay(250, stop);
        }
    }

    private static int ExitCode(string outcome) => outcome switch
    {
        "COMPLETED" => 0,
        "BLOCKED" or "CANCELLED" or "CANCELLATION_REQUESTED" or
        "FACTORY_CONFIGURATION_CHANGED" or "FACTORY_ALREADY_RUNNING" or "VERIFICATION_CONFIRMATION_REQUIRED" or "VERIFICATION_RESULT_REQUIRED" => 2,
        "LEGACY_FACTORY_STATE" or "CORRUPT_FACTORY_STATE" => 3,
        _ => 1
    };
}

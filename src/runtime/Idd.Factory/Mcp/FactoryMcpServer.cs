using System.ComponentModel;
using System.Reflection;
using System.Text.Json;
using Idd.Factory.Domain;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

internal static class FactoryMcpServer
{
    public static async Task<int> RunAsync(CancellationToken cancellationToken = default)
    {
        var builder = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings());
        builder.Logging.AddConsole(options => options.LogToStandardErrorThreshold = LogLevel.Trace);
        builder.Services.AddSingleton<IFactoryProcessInvoker, SystemFactoryProcessInvoker>();
        builder.Services.AddSingleton<FactoryRuntimeProcessRunner>();
        builder.Services.AddSingleton<FactoryStatusReader>();
        builder.Services
            .AddMcpServer(options => options.ServerInfo = new()
            {
                Name = "idd-factory",
                Version = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "unknown"
            })
            .WithStdioServerTransport()
            .WithTools<FactoryMcpTools>();

        await builder.Build().RunAsync(cancellationToken);
        return 0;
    }
}

[McpServerToolType]
internal sealed class FactoryMcpTools(FactoryRuntimeProcessRunner runner, FactoryStatusReader statusReader)
{
    private static readonly TimeSpan ProgressPollInterval = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan ProgressHeartbeatInterval = TimeSpan.FromSeconds(15);

    [McpServerTool(Name = "factory_run", ReadOnly = false, Destructive = true, Idempotent = false, OpenWorld = true, UseStructuredContent = true)]
    [Description("Run an explicitly requested IDD Factory workflow and block until the packaged runtime returns a structured outcome. Progress notifications describe the active work item/attempt when the MCP client supplies a progress token. A host/tool timeout is transport loss, not a Factory outcome; use factory_status once to determine whether the runtime is still active.")]
    public Task<FactoryMcpResult> FactoryRunAsync(
        [Description("Absolute path to the target workspace.")] string workspace,
        [Description("Complete Factory request text, passed unchanged as UTF-8.")] string request,
        IProgress<ProgressNotificationValue> progress,
        CancellationToken cancellationToken) =>
        RunWithProgressAsync(FactoryRuntimeCommand.Run, workspace, request, null, progress, cancellationToken);

    [McpServerTool(Name = "factory_continue", ReadOnly = false, Destructive = true, Idempotent = false, OpenWorld = true, UseStructuredContent = true)]
    [Description("Continue an explicitly requested IDD Factory workflow, optionally supplying its clarification answer. Progress notifications describe the active work item/attempt when the MCP client supplies a progress token. A host/tool timeout is transport loss, not a Factory outcome; use factory_status once before deciding whether another continue is safe.")]
    public Task<FactoryMcpResult> FactoryContinueAsync(
        [Description("Absolute path to the target workspace.")] string workspace,
        IProgress<ProgressNotificationValue> progress,
        [Description("Optional clarification answer, passed unchanged as UTF-8.")] string? answer = null,
        CancellationToken cancellationToken = default) =>
        RunWithProgressAsync(FactoryRuntimeCommand.Continue, workspace, null, answer, progress, cancellationToken);

    [McpServerTool(Name = "factory_cancel", ReadOnly = false, Destructive = true, Idempotent = false, OpenWorld = true, UseStructuredContent = true)]
    [Description("Request explicit cancellation of an IDD Factory workflow while preserving its product changes.")]
    public Task<FactoryMcpResult> FactoryCancelAsync(
        [Description("Absolute path to the target workspace.")] string workspace,
        IProgress<ProgressNotificationValue> progress,
        CancellationToken cancellationToken) =>
        RunWithProgressAsync(FactoryRuntimeCommand.Cancel, workspace, null, null, progress, cancellationToken);

    [McpServerTool(Name = "factory_status", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false, UseStructuredContent = true)]
    [Description("Read Factory runtime and persisted-run status without starting, continuing, cancelling, or polling the workflow. Use once after a blocking Factory MCP response is lost or times out, or for an explicit user status request.")]
    public Task<FactoryStatusResult> FactoryStatusAsync(
        [Description("Absolute path to the target workspace.")] string workspace,
        CancellationToken cancellationToken = default) =>
        statusReader.ReadAsync(workspace, cancellationToken);

    private async Task<FactoryMcpResult> RunWithProgressAsync(
        FactoryRuntimeCommand command,
        string workspace,
        string? request,
        string? answer,
        IProgress<ProgressNotificationValue> progress,
        CancellationToken cancellationToken)
    {
        long sequence = 0;
        void Report(string message) => progress.Report(new ProgressNotificationValue
        {
            Progress = Interlocked.Increment(ref sequence),
            Message = message
        });

        Report($"Factory {command.ToString().ToLowerInvariant()} started.");
        using var monitorCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var monitor = MonitorProgressAsync(workspace, Report, monitorCancellation.Token);
        try
        {
            var result = await runner.RunAsync(command, workspace, request, answer, cancellationToken);
            Report($"Factory finished with {result.FactoryOutcome}.");
            return result;
        }
        finally
        {
            monitorCancellation.Cancel();
            try { await monitor; }
            catch (OperationCanceledException) when (monitorCancellation.IsCancellationRequested) { }
        }
    }

    private async Task MonitorProgressAsync(
        string workspace,
        Action<string> report,
        CancellationToken cancellationToken)
    {
        string? previousSnapshot = null;
        var lastReportAt = DateTimeOffset.MinValue;
        while (true)
        {
            await Task.Delay(ProgressPollInterval, cancellationToken);
            var status = await statusReader.ReadAsync(workspace, cancellationToken);
            if (!StringComparer.Ordinal.Equals(status.Status, "ACTIVE")) continue;

            var snapshot = string.Join('|',
                status.RunId,
                status.CurrentWorkItemId,
                status.CurrentAttemptId,
                status.CurrentPhase,
                status.CompletedWorkCount,
                status.RemainingWorkCount);
            var now = DateTimeOffset.UtcNow;
            if (StringComparer.Ordinal.Equals(snapshot, previousSnapshot)
                && now - lastReportAt < ProgressHeartbeatInterval)
                continue;

            report(FormatActiveProgress(status, now));
            previousSnapshot = snapshot;
            lastReportAt = now;
        }
    }

    internal static string FormatActiveProgress(FactoryStatusResult status, DateTimeOffset now)
    {
        var activity = status.CurrentWorkItemId is { Length: > 0 } workItem
            ? $"work item {workItem}"
            : "runtime work";
        if (status.CurrentAttemptId is { Length: > 0 } attempt)
            activity += $", attempt {attempt}";
        if (status.CurrentPhase is { Length: > 0 } phase)
            activity += $", {phase.ToLowerInvariant()}";

        var elapsed = status.RuntimeStartedAt is { } startedAt && now >= startedAt
            ? $"; active {FormatElapsed(now - startedAt)}"
            : string.Empty;
        return $"Factory {status.RuntimeOperation ?? "run"}: {activity}; completed {status.CompletedWorkCount}, remaining {status.RemainingWorkCount}{elapsed}.";
    }

    private static string FormatElapsed(TimeSpan elapsed) =>
        elapsed.TotalHours >= 1
            ? $"{(int)elapsed.TotalHours}:{elapsed.Minutes:00}:{elapsed.Seconds:00}"
            : $"{elapsed.Minutes}:{elapsed.Seconds:00}";
}

internal sealed record FactoryMcpResult(
    string FactoryOutcome,
    string RunId,
    string? Reason,
    string? ResumeWhen,
    string? ResultDirectory,
    JsonElement? Payload = null);

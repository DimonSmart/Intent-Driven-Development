using System.ComponentModel;
using System.Reflection;
using System.Text.Json;
using Idd.Factory.Domain;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;

internal static class FactoryMcpServer
{
    public static async Task<int> RunAsync(CancellationToken cancellationToken = default)
    {
        var builder = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings());
        builder.Logging.AddConsole(options => options.LogToStandardErrorThreshold = LogLevel.Trace);
        builder.Services.AddSingleton<IFactoryProcessInvoker, SystemFactoryProcessInvoker>();
        builder.Services.AddSingleton<FactoryRuntimeProcessRunner>();
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
internal sealed class FactoryMcpTools(FactoryRuntimeProcessRunner runner)
{
    [McpServerTool(Name = "factory_run", ReadOnly = false, Destructive = true, Idempotent = false, OpenWorld = true, UseStructuredContent = true)]
    [Description("Run an explicitly requested IDD Factory workflow and block until the packaged runtime returns a structured outcome.")]
    public Task<FactoryMcpResult> FactoryRunAsync(
        [Description("Absolute path to the target workspace.")] string workspace,
        [Description("Complete Factory request text, passed unchanged as UTF-8.")] string request,
        CancellationToken cancellationToken) =>
        runner.RunAsync(FactoryRuntimeCommand.Run, workspace, request, null, cancellationToken);

    [McpServerTool(Name = "factory_continue", ReadOnly = false, Destructive = true, Idempotent = false, OpenWorld = true, UseStructuredContent = true)]
    [Description("Continue an explicitly requested IDD Factory workflow, optionally supplying its clarification answer.")]
    public Task<FactoryMcpResult> FactoryContinueAsync(
        [Description("Absolute path to the target workspace.")] string workspace,
        [Description("Optional clarification answer, passed unchanged as UTF-8.")] string? answer = null,
        CancellationToken cancellationToken = default) =>
        runner.RunAsync(FactoryRuntimeCommand.Continue, workspace, null, answer, cancellationToken);

    [McpServerTool(Name = "factory_cancel", ReadOnly = false, Destructive = true, Idempotent = false, OpenWorld = true, UseStructuredContent = true)]
    [Description("Request explicit cancellation of an IDD Factory workflow while preserving its product changes.")]
    public Task<FactoryMcpResult> FactoryCancelAsync(
        [Description("Absolute path to the target workspace.")] string workspace,
        CancellationToken cancellationToken) =>
        runner.RunAsync(FactoryRuntimeCommand.Cancel, workspace, null, null, cancellationToken);
}

internal sealed record FactoryMcpResult(
    string FactoryOutcome,
    string RunId,
    string? Reason,
    string? ResumeWhen,
    string? ResultDirectory,
    JsonElement? Payload = null);

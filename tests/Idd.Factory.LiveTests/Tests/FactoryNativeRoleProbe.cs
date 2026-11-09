using System.Text.Json;
using Idd.Factory.Report;
using Xunit;

namespace Idd.Factory.LiveTests.Tests;

public sealed partial class NativeFactoryEndToEndLiveTests
{
    private const string NativeRoleProbePrompt = """
        This is an isolated native-agent registration probe, not a Factory run.
        Probe exactly these two registered agent types sequentially:
        idd-internal-factory-planner and idd-internal-factory-worker.
        Use native spawn_agent with the exact agent_type, a unique task_name,
        fork_turns "none", and a short message saying there is NO valid Factory
        assignment and the child must return BLOCKED without modifying files.
        Wait for each child to finish. Do not use generic agents, construct
        role prompts, edit project files or run Factory.
        If a type is missing, report its native rejection without substitution.
        """;

    private static async Task ProbeProjectAgentRegistrationAsync(
        string codex, string workspace, string probeHome, string artifacts,
        string model, string reasoning, int timeoutMinutes)
    {
        PrepareCodexHome(probeHome);
        var env = new Dictionary<string, string?> { ["CODEX_HOME"] = probeHome };
        try
        {
            var result = await RunAsync(codex,
                [
                    "exec", "--json", "--ignore-rules",
                    "--enable", "multi_agent", "--enable", "multi_agent_v2",
                    "-c", "agents.max_depth=2",
                    "-c", "agents.max_threads=4",
                    "-c", "approval_policy=never",
                    "-c", $"model_reasoning_effort={reasoning}",
                    "--model", model,
                    "--sandbox", "read-only",
                    "--cd", workspace, "-"
                ],
                workspace, env, NativeRoleProbePrompt, artifacts, "09-project-agent-probe",
                timeout: TimeSpan.FromMinutes(Math.Min(timeoutMinutes, 4)));

            await File.WriteAllTextAsync(Path.Combine(artifacts, "project-agent-probe-trace.jsonl"), result.Stdout);
            var root = FindRootRollout(probeHome, requireSpawn: false);
            VerifyRegisteredNativeRoleSpawns(root, artifacts, "project-local");
        }
        finally
        {
            await TrySaveReportSourceAsync(probeHome, Path.Combine(artifacts, "project-agent-probe"));
        }
    }

    private static void VerifyRegisteredNativeRoleSpawns(
        CodexRollout root, string artifacts, string registrationMode)
    {
        var calls = root.Events.Where(e => e.ToolName == "spawn_agent").ToArray();
        foreach (var role in FactoryEvalAgentRegistration.RequiredRoles)
        {
            var matches = calls.Where(e => GetRequestedAgentType(e.ToolArguments) == role).ToArray();
            Assert.True(matches.Length == 1,
                $"Native {registrationMode} role registration was not demonstrated for {role}: " +
                $"found {matches.Length} native spawn attempts. Evidence: {artifacts}");

            var call = matches[0];
            Assert.True(root.SpawnRecords.TryGetValue(call.ToolId ?? "", out var spawn),
                $"No native spawn result for {role}, call={call.ToolId}. Evidence: {artifacts}");
            Assert.NotNull(spawn);
            Assert.True(spawn.SpawnOutcome == "accepted" && spawn.Children.Any(),
                $"Host rejected {registrationMode} role {role}: " +
                $"outcome={spawn.SpawnOutcome}, code={spawn.RejectionCode}, " +
                $"reason={spawn.RejectionMessage}. Evidence: {artifacts}");
        }
    }

    private static string? GetRequestedAgentType(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;
        try
        {
            using var document = JsonDocument.Parse(json);
            return document.RootElement.ValueKind == JsonValueKind.Object &&
                   document.RootElement.TryGetProperty("agent_type", out var type) &&
                   type.ValueKind == JsonValueKind.String
                ? type.GetString()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

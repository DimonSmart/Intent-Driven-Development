using Idd.Factory.Report;
using System.Text.Json;
using Xunit;

namespace Idd.Factory.LiveTests.Tests;

public sealed partial class NativeFactoryEndToEndLiveTests
{
    private const string InternalSkillProbePrompt = """
        Test installed IDD Factory internal Skills without modifying the project.
        Resolve the currently installed idd-factory-run SKILL.md (not a source checkout)
        and its sibling internal Planner/Worker SKILL.md files. Sequentially spawn
        two fresh GENERIC child agents with native task_name idd_factory_planner_01
        and idd_factory_worker_01, fork_turns none, no Factory-specific agent_type.
        Give each child the exact installed absolute SKILL.md path and instruct
        it to read the whole Skill first. Supply an intentionally incomplete
        Factory assignment and require BLOCKED without project work. Wait for
        both terminal results. Do not install role definitions or modify files.
        """;

    private static void VerifyGenericInternalSkillSpawns(CodexRollout root, string artifacts)
    {
        var calls = root.Events.Where(e => e.ToolName == "spawn_agent").ToArray();
        foreach (var role in new[] { "planner", "worker" })
        {
            var name = "idd_factory_" + role + "_01";
            var call = Assert.Single(calls, item => HasArgument(item.ToolArguments, "task_name", name));
            Assert.True(HasArgument(call.ToolArguments, "fork_turns", "none"));
            Assert.True(HasMessageSkill(call.ToolArguments, role),
                $"No installed {role} SKILL.md path in child message. Evidence: {artifacts}");
            Assert.True(root.SpawnRecords.TryGetValue(call.ToolId ?? "", out var spawn));
            Assert.NotNull(spawn);
            Assert.True(spawn.SpawnOutcome == "accepted" && spawn.Children.Any(),
                $"Generic {role} spawn rejected: {spawn.RejectionCode} {spawn.RejectionMessage}");
        }
    }

    private static bool HasArgument(string? args, string property, string expected)
    {
        if (string.IsNullOrWhiteSpace(args)) return false;
        try
        {
            using var json = JsonDocument.Parse(args);
            return json.RootElement.TryGetProperty(property, out var value) &&
                value.ValueKind == JsonValueKind.String && value.GetString() == expected;
        }
        catch (JsonException) { return false; }
    }

    private static bool HasMessageSkill(string? args, string role)
    {
        if (string.IsNullOrWhiteSpace(args)) return false;
        try
        {
            using var json = JsonDocument.Parse(args);
            return json.RootElement.TryGetProperty("message", out var value) &&
                value.ValueKind == JsonValueKind.String &&
                value.GetString()!.Replace('\\', '/').Contains("idd-internal-factory-" + role + "/SKILL.md",
                    StringComparison.OrdinalIgnoreCase);
        }
        catch (JsonException) { return false; }
    }
}

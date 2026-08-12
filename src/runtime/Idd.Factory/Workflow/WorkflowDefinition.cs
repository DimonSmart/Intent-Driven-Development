using System.Security.Cryptography;
using System.Text;

namespace Idd.Factory.Workflow;

public sealed record WorkflowDefinition(
    int SchemaVersion,
    string Name,
    WorkflowLimits Limits,
    IReadOnlyList<WorkflowStepDefinition> Steps,
    string SourcePath,
    string Hash);

public sealed record WorkflowLimits(int MaxAgentAttempts, int MaxReplans, int MaxCorrectiveCycles);

public sealed record WorkflowStepDefinition(
    string Id,
    string Uses,
    string? Agent,
    IReadOnlyDictionary<string, string> Handlers,
    IReadOnlyDictionary<string, string> Transitions);

public sealed class WorkflowDefinitionLoader
{
    public WorkflowDefinition Load(string workspace, string packagedWorkflowPath)
    {
        var overridePath = Path.Combine(workspace, ".idd", "factory.yaml");
        var sourcePath = File.Exists(overridePath) ? overridePath : packagedWorkflowPath;
        if (!File.Exists(sourcePath)) throw new WorkflowException("WORKFLOW_NOT_FOUND", $"Workflow not found: {sourcePath}");
        var yaml = File.ReadAllText(sourcePath).Replace("\r\n", "\n").Trim() + "\n";
        var definition = RestrictedWorkflowYaml.Parse(yaml, Path.GetFullPath(sourcePath));
        new WorkflowValidator().Validate(definition);
        return definition with { Hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(yaml))).ToLowerInvariant() };
    }
}

public sealed class WorkflowValidator
{
    private static readonly HashSet<string> Handlers =
    ["factory.decompose", "factory.intent", "factory.execute", "factory.replan", "factory.final-review", "factory.finalize"];
    private static readonly HashSet<string> Roles =
    ["task-decomposer", "implementer", "checkpoint-reviewer", "final-reviewer", "factory-replanner"];

    public void Validate(WorkflowDefinition workflow)
    {
        if (workflow.SchemaVersion != 1) throw new WorkflowException("UNSUPPORTED_WORKFLOW_SCHEMA", $"Unsupported workflow schema {workflow.SchemaVersion}.");
        if (string.IsNullOrWhiteSpace(workflow.Name)) throw new WorkflowException("INVALID_WORKFLOW", "Workflow name is required.");
        if (workflow.Limits.MaxAgentAttempts is < 1 or > 10 || workflow.Limits.MaxReplans is < 0 or > 10 || workflow.Limits.MaxCorrectiveCycles is < 0 or > 20)
            throw new WorkflowException("INVALID_WORKFLOW_LIMITS", "Workflow limits exceed runtime safety ceilings.");
        if (workflow.Steps.Count == 0 || workflow.Steps[0].Uses != "factory.decompose")
            throw new WorkflowException("INVALID_WORKFLOW", "The first step must use factory.decompose.");
        var ids = workflow.Steps.Select(x => x.Id).ToArray();
        if (ids.Distinct(StringComparer.Ordinal).Count() != ids.Length)
            throw new WorkflowException("DUPLICATE_WORKFLOW_STEP", "Workflow step IDs must be unique.");
        var idSet = ids.ToHashSet(StringComparer.Ordinal);
        foreach (var step in workflow.Steps)
        {
            if (!Handlers.Contains(step.Uses)) throw new WorkflowException("UNKNOWN_WORKFLOW_HANDLER", $"Unknown handler {step.Uses}.");
            if (step.Agent is not null && !Roles.Contains(step.Agent)) throw new WorkflowException("UNKNOWN_AGENT_ROLE", $"Unknown role {step.Agent}.");
            if (step.Handlers.Values.Any(role => !Roles.Contains(role))) throw new WorkflowException("UNKNOWN_AGENT_ROLE", $"Step {step.Id} has an unknown role.");
            foreach (var target in step.Transitions.Values.Where(x => !x.StartsWith('$')))
                if (!idSet.Contains(target)) throw new WorkflowException("MISSING_TRANSITION_TARGET", $"Step {step.Id} targets missing step {target}.");
        }
        var reachable = Reachable(workflow.Steps[0].Id, workflow.Steps.ToDictionary(x => x.Id, StringComparer.Ordinal));
        if (!workflow.Steps.Where(x => x.Uses == "factory.finalize").Any(x => reachable.Contains(x.Id)))
            throw new WorkflowException("UNREACHABLE_TERMINAL", "No finalization step is reachable.");
        if (!workflow.Steps.Any(x => x.Transitions.Values.Any(v => v == "$stop")) && workflow.Steps.All(x => x.Uses != "factory.finalize"))
            throw new WorkflowException("UNBOUNDED_WORKFLOW", "Workflow has no reachable terminal.");
        foreach (var step in workflow.Steps.Where(x => x.Transitions.Count == 1 && x.Transitions.Values.Single() == x.Id))
            throw new WorkflowException("UNBOUNDED_WORKFLOW", $"Step {step.Id} has an unconditional self-cycle.");
    }

    private static HashSet<string> Reachable(string start, IReadOnlyDictionary<string, WorkflowStepDefinition> steps)
    {
        var result = new HashSet<string>(StringComparer.Ordinal);
        var queue = new Queue<string>(); queue.Enqueue(start);
        while (queue.TryDequeue(out var id))
        {
            if (!result.Add(id)) continue;
            foreach (var target in steps[id].Transitions.Values.Where(x => !x.StartsWith('$'))) queue.Enqueue(target);
        }
        return result;
    }
}

internal static class RestrictedWorkflowYaml
{
    public static WorkflowDefinition Parse(string yaml, string sourcePath)
    {
        int? schema = null; string? name = null; int attempts = 0, replans = 0, corrections = 0;
        var steps = new List<MutableStep>(); MutableStep? step = null; string section = ""; string stepSection = "";
        foreach (var raw in yaml.Split('\n'))
        {
            if (raw.Contains('\t')) throw new WorkflowException("INVALID_WORKFLOW_YAML", "Tabs are not supported.");
            var text = raw.Trim(); if (text.Length == 0 || text.StartsWith('#')) continue;
            var indent = raw.Length - raw.TrimStart().Length;
            if (indent == 0)
            {
                step = null; stepSection = "";
                if (TryPair(text, out var key, out var value) && key is "schemaVersion" or "name")
                { if (key == "schemaVersion") schema = Number(value, key); else name = Scalar(value); continue; }
                if (text == "limits:") { section = "limits"; continue; }
                if (text == "steps:") { section = "steps"; continue; }
                throw new WorkflowException("INVALID_WORKFLOW_YAML", $"Unsupported root entry: {text}");
            }
            if (section == "limits" && indent == 2 && TryPair(text, out var limit, out var limitValue))
            {
                switch (limit) { case "maxAgentAttempts": attempts = Number(limitValue, limit); break; case "maxReplans": replans = Number(limitValue, limit); break; case "maxCorrectiveCycles": corrections = Number(limitValue, limit); break; default: throw new WorkflowException("INVALID_WORKFLOW_YAML", $"Unknown limit {limit}."); }
                continue;
            }
            if (section != "steps") throw new WorkflowException("INVALID_WORKFLOW_YAML", $"Unexpected entry: {text}");
            if (indent == 2 && text.StartsWith("- id:")) { step = new MutableStep(Scalar(text[5..])); steps.Add(step); stepSection = ""; continue; }
            if (step is null) throw new WorkflowException("INVALID_WORKFLOW_YAML", "Step entry must start with '- id:'.");
            if (indent == 4 && TryPair(text, out var field, out var fieldValue))
            {
                switch (field) { case "uses": step.Uses = Scalar(fieldValue); break; case "agent": step.Agent = Scalar(fieldValue); break; case "on" when fieldValue.Length == 0: stepSection = "on"; break; case "handlers" when fieldValue.Length == 0: stepSection = "handlers"; break; default: throw new WorkflowException("INVALID_WORKFLOW_YAML", $"Unknown step field {field}."); }
                continue;
            }
            if (indent == 6 && TryPair(text, out var mapKey, out var mapValue) && stepSection is "on" or "handlers")
            { (stepSection == "on" ? step.Transitions : step.Handlers).Add(mapKey, Scalar(mapValue)); continue; }
            throw new WorkflowException("INVALID_WORKFLOW_YAML", $"Unsupported YAML structure: {text}");
        }
        return new WorkflowDefinition(schema ?? 0, name ?? "", new(attempts, replans, corrections),
            steps.Select(x => new WorkflowStepDefinition(x.Id, x.Uses, x.Agent, x.Handlers, x.Transitions)).ToArray(), sourcePath, "");
    }

    private static bool TryPair(string text, out string key, out string value)
    { var i = text.IndexOf(':'); if (i < 1) { key = value = ""; return false; } key = text[..i].Trim(); value = text[(i + 1)..].Trim(); return true; }
    private static string Scalar(string value) => value.Trim().Trim('"', '\'');
    private static int Number(string value, string name) => int.TryParse(Scalar(value), out var result) ? result : throw new WorkflowException("INVALID_WORKFLOW_YAML", $"{name} must be an integer.");
    private sealed class MutableStep(string id)
    { public string Id { get; } = id; public string Uses { get; set; } = ""; public string? Agent { get; set; } public Dictionary<string, string> Handlers { get; } = new(StringComparer.Ordinal); public Dictionary<string, string> Transitions { get; } = new(StringComparer.Ordinal); }
}

public sealed class WorkflowException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}

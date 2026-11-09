using System.Security.Cryptography;
using System.Text.Json;

namespace Idd.Factory.LiveTests.Tests;

internal static class FactoryEvalAgentRegistration
{
    internal static readonly string[] RequiredRoles =
    [
        "idd-internal-factory-planner",
        "idd-internal-factory-worker"
    ];

    internal static IReadOnlyList<string> StageProjectAgents(
        string generatedFactory, string workspace, string artifactRoot)
    {
        var sources = ResolveGeneratedAgents(generatedFactory);
        var destinationDirectory = Path.Combine(workspace, ".codex", "agents");
        Directory.CreateDirectory(destinationDirectory);

        var registeredPaths = new List<string>();
        var audit = new List<object>();
        foreach (var (name, source) in sources)
        {
            var destination = Path.Combine(destinationDirectory, name + ".toml");
            // No overwrite: a conflicting registration must be diagnosed, not shadowed.
            if (File.Exists(destination))
                throw new InvalidOperationException($"Factory eval agent already registered: {destination}");

            File.Copy(source, destination);
            var original = File.ReadAllBytes(source);
            var registered = File.ReadAllBytes(destination);
            if (!original.AsSpan().SequenceEqual(registered))
                throw new InvalidOperationException($"Factory eval agent copy differs from generated definition: {name}");

            registeredPaths.Add(destination);
            audit.Add(new
            {
                name,
                source,
                destination,
                sha256 = Convert.ToHexString(SHA256.HashData(registered))
            });
        }

        Directory.CreateDirectory(artifactRoot);
        File.WriteAllText(
            Path.Combine(artifactRoot, "agent-registration.json"),
            JsonSerializer.Serialize(new { mode = "project-local", agents = audit },
                new JsonSerializerOptions { WriteIndented = true }));

        return registeredPaths;
    }

    internal static IReadOnlyList<(string Name, string Source)> ResolveGeneratedAgents(string generatedFactory)
    {
        var manifestPath = Path.Combine(generatedFactory, ".codex-plugin", "plugin.json");
        using var manifest = JsonDocument.Parse(File.ReadAllText(manifestPath));
        var agentsPath = manifest.RootElement.GetProperty("agents").GetString();
        if (agentsPath != "./agents/")
            throw new InvalidOperationException($"Unexpected Factory agents manifest path: {agentsPath}");

        return RequiredRoles.Select(name =>
        {
            var path = Path.Combine(generatedFactory, "agents", name + ".toml");
            if (!File.Exists(path) || new FileInfo(path).Length == 0)
                throw new InvalidOperationException($"Missing generated Factory native agent: {path}");
            return (Name: name, Source: path);
        }).ToArray();
    }
}

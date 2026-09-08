using System.Text.Json;

namespace Idd.Factory.LiveTests.Infrastructure;

public sealed record FactoryRunResult(
    string Outcome,
    string MethodologyVersion,
    int CompletedWorkCount,
    string VerificationStatus,
    string? CommitMessagePath,
    string Path);

public static class FactoryResultReader
{
    public static FactoryRunResult ReadSingle(string workspace)
    {
        var resultsDirectory = Path.Combine(workspace, ".idd", "factory", "results");
        if (!Directory.Exists(resultsDirectory)) throw new InvalidOperationException("Factory results directory is missing.");
        var directories = Directory.GetDirectories(resultsDirectory);
        if (directories.Length != 1) throw new InvalidOperationException($"Expected exactly one Factory result directory, but found {directories.Length}.");
        var path = Path.Combine(directories[0], "factory-result.json");
        if (!File.Exists(path)) throw new InvalidOperationException("factory-result.json is missing.");
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object) throw new InvalidOperationException("factory-result.json root must be an object.");
        return new(
            RequiredString(root, "factoryOutcome"),
            RequiredString(root, "methodologyVersion"),
            RequiredInt(root, "completedWorkCount"),
            RequiredString(root, "verificationStatus"),
            OptionalString(root, "commitMessagePath"),
            path);
    }

    private static string RequiredString(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(value.GetString())
            ? value.GetString()!
            : throw new InvalidOperationException($"factory-result.json is missing string property '{name}'.");

    private static int RequiredInt(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.TryGetInt32(out var result)
            ? result
            : throw new InvalidOperationException($"factory-result.json is missing integer property '{name}'.");

    private static string? OptionalString(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
}

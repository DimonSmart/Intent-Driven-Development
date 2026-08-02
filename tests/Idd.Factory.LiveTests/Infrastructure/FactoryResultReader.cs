using System.Text.Json;

namespace Idd.Factory.LiveTests.Infrastructure;

public sealed record FactoryResult(JsonElement Json, string Path)
{
    public string? String(string name) => Json.TryGetProperty(name, out var value) ? value.GetString() : null;
    public int? Int(string name) => Json.TryGetProperty(name, out var value) && value.TryGetInt32(out var result) ? result : null;
}

public static class FactoryResultReader
{
    public static FactoryResult ReadSingle(string workspace)
    {
        var directories = Directory.Exists(Path.Combine(workspace, ".idd", "factory", "results"))
            ? Directory.GetDirectories(Path.Combine(workspace, ".idd", "factory", "results")) : [];
        if (directories.Length != 1) throw new InvalidOperationException($"Expected exactly one Factory result directory, but found {directories.Length}.");
        var path = Path.Combine(directories[0], "factory-result.json");
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        if (document.RootElement.ValueKind != JsonValueKind.Object || document.RootElement.EnumerateObject().Any(property => property.Value.ValueKind is JsonValueKind.Array or JsonValueKind.Object)) throw new InvalidOperationException("factory-result.json must be a flat JSON object.");
        return new(document.RootElement.Clone(), path);
    }
}

using System.Text.Json;

namespace Idd.Factory.LiveTests.Infrastructure;

public sealed record FactoryResult(JsonElement Json, string Path)
{
    public string? String(string name) => Json.TryGetProperty(name, out var value) ? value.GetString() : null;
    public int? Int(string name) => Json.TryGetProperty(name, out var value) && value.TryGetInt32(out var result) ? result : null;
}

public sealed record FactoryResultReadResult(FactoryResult? Result, string? Error)
{
    public bool IsSuccess => Result is not null;
}

public static class FactoryResultReader
{
    public static FactoryResultReadResult TryReadSingle(string workspace)
    {
        var resultsDirectory = Path.Combine(workspace, ".idd", "factory", "results");
        if (!Directory.Exists(resultsDirectory)) return new(null, "Factory results directory is missing.");
        var directories = Directory.GetDirectories(resultsDirectory);
        if (directories.Length != 1) return new(null, $"Expected exactly one Factory result directory, but found {directories.Length}.");
        var path = Path.Combine(directories[0], "factory-result.json");
        if (!File.Exists(path)) return new(null, "factory-result.json is missing.");
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            if (document.RootElement.ValueKind != JsonValueKind.Object) return new(null, "factory-result.json root must be a JSON object.");
            if (document.RootElement.EnumerateObject().Any(property => property.Value.ValueKind is JsonValueKind.Array or JsonValueKind.Object)) return new(null, "factory-result.json must be a flat JSON object.");
            return new(new(document.RootElement.Clone(), path), null);
        }
        catch (JsonException exception)
        {
            return new(null, $"factory-result.json is invalid JSON: {exception.Message}");
        }
    }
}

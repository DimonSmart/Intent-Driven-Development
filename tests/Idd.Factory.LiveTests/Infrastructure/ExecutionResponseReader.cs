namespace Idd.Factory.LiveTests.Infrastructure;

public sealed record ExecutionResponseReadResult(FactoryResponse? Response, string? Error)
{
    public bool IsSuccess => Response is not null;
}

public static class ExecutionResponseReader
{
    public static ExecutionResponseReadResult TryRead(string path, string workspace)
    {
        if (!File.Exists(path)) return new(null, "last-message.json is missing.");
        var parsed = FactoryResponseParser.TryParse(File.ReadAllText(path));
        if (!parsed.IsSuccess) return new(null, parsed.Error);

        var response = parsed.Response!;
        if (response.FactoryOutcome == "COMPLETED" &&
            !File.Exists(Path.Combine(workspace, response.FactoryResultPath!.Replace('/', Path.DirectorySeparatorChar))))
            return new(null, "COMPLETED factoryResultPath does not exist.");

        return new(response, null);
    }
}

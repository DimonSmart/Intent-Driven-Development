using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Idd.Factory.Benchmark;

public static class BenchmarkDefinitionLoader
{
    public static BenchmarkDefinition Load(string benchmarkDirectory)
    {
        var directory = Path.GetFullPath(benchmarkDirectory);
        var path = Path.Combine(directory, "benchmark.yaml");
        if (!File.Exists(path)) throw new FileNotFoundException("Benchmark definition was not found.", path);
        var deserializer = new DeserializerBuilder().WithNamingConvention(CamelCaseNamingConvention.Instance).Build();
        var definition = deserializer.Deserialize<BenchmarkDefinition>(File.ReadAllText(path)) ?? throw new InvalidDataException("Benchmark definition is empty.");
        Validate(definition, directory);
        return definition;
    }

    private static void Validate(BenchmarkDefinition definition, string directory)
    {
        if (string.IsNullOrWhiteSpace(definition.Name)) throw new InvalidDataException("Benchmark name is required.");
        if (string.IsNullOrWhiteSpace(definition.Model)) throw new InvalidDataException("Benchmark model is required.");
        if (definition.Repeat <= 0) throw new InvalidDataException("Benchmark repeat must be positive.");
        if (definition.TimeoutMinutes <= 0) throw new InvalidDataException("Benchmark timeoutMinutes must be positive.");
        if (definition.WindowsSandbox is not ("elevated" or "unelevated"))
            throw new InvalidDataException("Benchmark windowsSandbox must be 'elevated' or 'unelevated'.");
        RequireFile(directory, definition.Task, "task");
        foreach (var item in definition.IdealWorkItems) RequireFile(directory, item, "ideal work item");
        if (definition.IdealWorkItems.Count == 0) throw new InvalidDataException("At least one idealWorkItem is required.");
        if (string.IsNullOrWhiteSpace(definition.Acceptance.Command)) throw new InvalidDataException("Acceptance command is required.");
        if (definition.Modes.Count == 0) throw new InvalidDataException("At least one mode is required.");
        foreach (var mode in definition.Modes)
            if (!BenchmarkModes.All.Contains(mode, StringComparer.Ordinal)) throw new InvalidDataException($"Unknown benchmark mode '{mode}'.");
    }

    private static void RequireFile(string directory, string relativePath, string role)
    {
        var fullPath = Path.GetFullPath(Path.Combine(directory, relativePath));
        if (!fullPath.StartsWith(directory + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) || !File.Exists(fullPath))
            throw new InvalidDataException($"Benchmark {role} '{relativePath}' is missing or escapes the fixture directory.");
    }
}

public static class BenchmarkCliParser
{
    public static BenchmarkOptions Parse(string[] args)
    {
        if (args.Length < 2 || args[0] != "run") throw new ArgumentException(Usage);
        string? output = null, model = null, windowsSandbox = null;
        int? repeat = null, timeout = null;
        IReadOnlyList<string>? modes = null;
        var keep = false; var force = false;
        for (var index = 2; index < args.Length; index++)
        {
            switch (args[index])
            {
                case "--repeat": repeat = PositiveInt(Value(args, ref index), "repeat"); break;
                case "--model": model = Value(args, ref index); break;
                case "--output": output = Value(args, ref index); break;
                case "--modes":
                    modes = Value(args, ref index).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                    if (modes.Count == 0 || modes.Any(mode => !BenchmarkModes.All.Contains(mode, StringComparer.Ordinal))) throw new ArgumentException("--modes contains an unknown mode.");
                    break;
                case "--keep-workspaces": keep = true; break;
                case "--timeout-minutes": timeout = PositiveInt(Value(args, ref index), "timeout-minutes"); break;
                case "--windows-sandbox":
                    windowsSandbox = Value(args, ref index);
                    ValidateWindowsSandbox(windowsSandbox, "--windows-sandbox");
                    break;
                case "--force": force = true; break;
                default: throw new ArgumentException($"Unknown option '{args[index]}'.\n{Usage}");
            }
        }
        return new(Path.GetFullPath(args[1]), repeat, model, output is null ? null : Path.GetFullPath(output), modes, keep, timeout, windowsSandbox, force);
    }

    public const string Usage = "Usage: factory-benchmark run <benchmark-directory> [--repeat N] [--model MODEL] [--output PATH] [--modes mode1,mode2] [--keep-workspaces] [--timeout-minutes N] [--windows-sandbox elevated|unelevated] [--force]";
    private static string Value(string[] args, ref int index) => ++index < args.Length ? args[index] : throw new ArgumentException($"Missing value for {args[index - 1]}.");
    private static int PositiveInt(string value, string name) => int.TryParse(value, out var result) && result > 0 ? result : throw new ArgumentException($"--{name} must be a positive integer.");

    private static void ValidateWindowsSandbox(string value, string name)
    {
        if (value is not ("elevated" or "unelevated"))
            throw new ArgumentException($"{name} must be 'elevated' or 'unelevated'.");
    }
}

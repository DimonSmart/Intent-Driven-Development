using System.Diagnostics;
using System.Reflection;
using System.Text;
using System.Text.Json;

Console.InputEncoding = new UTF8Encoding(false);
Console.OutputEncoding = new UTF8Encoding(false);

if (args.Length == 0)
    return 2;

switch (args[0])
{
    case "echo":
    {
        var input = await Console.In.ReadToEndAsync();
        Console.Write(JsonSerializer.Serialize(new
        {
            Arguments = args.Skip(1).ToArray(),
            WorkingDirectory = Environment.CurrentDirectory,
            EnvironmentValue = Environment.GetEnvironmentVariable("IDD_PROCESS_TEST_VALUE"),
            StandardInput = input
        }));
        return 0;
    }
    case "unicode":
        Console.Write("Устранена → готово\n");
        Console.Error.Write("Ошибка → stderr\n");
        return 0;
    case "both":
    {
        var blocks = int.Parse(args[1]);
        var stdout = new string('o', 4096);
        var stderr = new string('e', 4096);
        for (var i = 0; i < blocks; i++)
        {
            Console.Out.Write(stdout);
            Console.Error.Write(stderr);
            if ((i & 31) == 0)
            {
                Console.Out.Flush();
                Console.Error.Flush();
            }
        }
        Console.Out.Flush();
        Console.Error.Flush();
        return 0;
    }
    case "lines":
    {
        var count = int.Parse(args[1]);
        for (var i = 0; i < count; i++)
            Console.WriteLine($"line-{i}");
        return 0;
    }
    case "sleep":
        await Task.Delay(int.Parse(args[1]));
        return 0;
    case "exit":
        return int.Parse(args[1]);
    case "spawn-child":
    {
        var pidPath = args[1];
        var childSleep = int.Parse(args[2]);
        using var child = Process.Start(SelfStart("sleep", childSleep.ToString()))
            ?? throw new InvalidOperationException("Could not start process-test child.");
        await File.WriteAllTextAsync(pidPath, child.Id.ToString());
        await Task.Delay(childSleep);
        return 0;
    }
    case "hold-pipe":
    {
        var childSleep = int.Parse(args[1]);
        using var child = Process.Start(SelfStart("sleep", childSleep.ToString()))
            ?? throw new InvalidOperationException("Could not start pipe-holding child.");
        Console.Write("parent-exited");
        return 0;
    }
    case "exec":
        return await RunCodexScenarioAsync(args);
    default:
        Console.Error.Write($"Unknown helper command: {args[0]}");
        return 3;
}

static ProcessStartInfo SelfStart(params string[] childArguments)
{
    var host = Environment.ProcessPath
        ?? throw new InvalidOperationException("Current process path is unavailable.");
    var start = new ProcessStartInfo(host)
    {
        UseShellExecute = false,
        CreateNoWindow = true
    };
    if (Path.GetFileNameWithoutExtension(host).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
        start.ArgumentList.Add(Assembly.GetExecutingAssembly().Location);
    foreach (var argument in childArguments)
        start.ArgumentList.Add(argument);
    return start;
}

static async Task<int> RunCodexScenarioAsync(string[] arguments)
{
    _ = await Console.In.ReadToEndAsync();
    var scenario = ValueAfter(arguments, "--model")
        ?? throw new InvalidOperationException("Codex test scenario is missing.");
    var resultPath = ValueAfter(arguments, "--output-last-message")
        ?? throw new InvalidOperationException("Codex result path is missing.");

    switch (scenario)
    {
        case "test-a000002":
            EmitCommand("item.started", "item_9", "ng test --watch=false");
            await Task.Delay(25);
            EmitCommand("item.started", "item_10", "npm cache verify");
            await Task.Delay(25);
            EmitCommand("item.completed", "item_10");
            await Task.Delay(100);
            EmitCommand("item.completed", "item_9");
            await WriteResultAsync(resultPath);
            return 0;

        case "test-result-active-command":
            EmitCommand("item.started", "A", "long command");
            await WriteResultAsync(resultPath);
            await Task.Delay(350);
            EmitCommand("item.completed", "A");
            return 0;

        case "test-command-after-result":
            await WriteResultAsync(resultPath);
            await Task.Delay(150);
            EmitCommand("item.started", "A", "post-result command");
            await Task.Delay(400);
            EmitCommand("item.completed", "A");
            return 0;

        case "test-command-timeout":
            EmitCommand("item.started", "A", "hung command");
            await Task.Delay(25);
            EmitCommand("item.started", "B", "short command");
            await Task.Delay(25);
            EmitCommand("item.completed", "B");
            await WriteResultAsync(resultPath);
            await Task.Delay(30_000);
            return 0;

        case "test-incomplete-exit":
            EmitCommand("item.started", "A", "abandoned command");
            await WriteResultAsync(resultPath);
            return 0;

        case "test-buffered-completion":
            EmitCommand("item.started", "A", "buffered completion command");
            for (var index = 0; index < 5_000; index++)
                Console.WriteLine($"{{\"type\":\"item.completed\",\"item\":{{\"id\":\"message-{index}\",\"type\":\"agent_message\"}}}}" );
            EmitCommand("item.completed", "A");
            await WriteResultAsync(resultPath);
            return 0;

        case "test-forced-after-result":
            await WriteResultAsync(resultPath);
            await Task.Delay(30_000);
            return 0;

        default:
            Console.Error.Write($"Unknown Codex test scenario: {scenario}");
            return 4;
    }
}

static void EmitCommand(string eventType, string id, string? command = null)
{
    var item = new Dictionary<string, object?>
    {
        ["id"] = id,
        ["type"] = "command_execution",
        ["status"] = eventType == "item.started" ? "in_progress" : "completed"
    };
    if (command is not null)
        item["command"] = command;
    Console.WriteLine(JsonSerializer.Serialize(new Dictionary<string, object?>
    {
        ["type"] = eventType,
        ["item"] = item
    }));
    Console.Out.Flush();
}

static async Task WriteResultAsync(string path)
{
    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
    await File.WriteAllTextAsync(path, "completed result");
}

static string? ValueAfter(string[] arguments, string name)
{
    for (var index = 0; index < arguments.Length - 1; index++)
        if (string.Equals(arguments[index], name, StringComparison.Ordinal))
            return arguments[index + 1];
    return null;
}

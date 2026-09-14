using System.Diagnostics;
using System.Reflection;
using System.Text;
using System.Text.Json;

Console.OutputEncoding = new UTF8Encoding(false);
Console.ErrorEncoding = new UTF8Encoding(false);

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

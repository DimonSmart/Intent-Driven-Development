internal sealed class CommandDispatcher
{
    public int Run(string[] args)
    {
        var command = args.FirstOrDefault();
        if (string.IsNullOrWhiteSpace(command) ||
            command is "help" or "--help" or "-h")
        {
            UsagePrinter.Print();
            return 0;
        }

        return command switch
        {
            "list-targets" => new ListCodingAgentsCommand().Run(),
            "list-coding-agents" => new ListCodingAgentsCommand().Run(),
            "list-packs" => new ListPacksCommand().Run(),
            "version" => new VersionCommand().Run(),
            "init" => new InitCommand().Run(args.Skip(1).ToArray()),
            "install" => new InstallCommand().Run(args.Skip(1).ToArray()),
            _ => Fail($"Unknown command: {command}")
        };
    }

    private static int Fail(string message)
    {
        Console.Error.WriteLine(message);
        return 1;
    }
}

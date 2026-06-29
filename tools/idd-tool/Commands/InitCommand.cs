internal sealed class InitCommand
{
    public int Run(IReadOnlyList<string> commandArgs)
    {
        EnsureNoUnknownOptions(commandArgs, "--force");
        var force = commandArgs.Contains("--force", StringComparer.Ordinal);
        var source = new ContentLayout(ContentRootLocator.Find()).IntentRoot;
        var destination = Path.Combine(Directory.GetCurrentDirectory(), ".idd/intent");

        if (!Directory.Exists(source))
        {
            return Fail($"Bundled canonical project files not found: {source}");
        }

        if (Directory.Exists(destination) && !force)
        {
            return Fail("File already exists: .idd/intent" + Environment.NewLine + "Use --force to overwrite.");
        }

        DirectoryCopier.Copy(source, destination, force);
        Console.WriteLine("Initialized .idd/intent.");
        return 0;
    }

    private static void EnsureNoUnknownOptions(IReadOnlyList<string> commandArgs, params string[] known)
    {
        foreach (var arg in commandArgs.Where(arg => arg.StartsWith("--", StringComparison.Ordinal)))
        {
            if (!known.Contains(arg, StringComparer.Ordinal))
            {
                throw new ToolException($"Unknown option: {arg}");
            }
        }
    }

    private static int Fail(string message)
    {
        Console.Error.WriteLine(message);
        return 1;
    }
}

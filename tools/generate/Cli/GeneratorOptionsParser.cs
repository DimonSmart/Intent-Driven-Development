internal static class GeneratorOptionsParser
{
    public static GeneratorOptions Parse(string[] args)
    {
        var checkOnly = false;
        string? manifestVersion = null;

        for (var index = 0; index < args.Length; index++)
        {
            var arg = args[index];
            if (StringComparer.Ordinal.Equals(arg, "--check"))
            {
                checkOnly = true;
                continue;
            }

            if (StringComparer.Ordinal.Equals(arg, "--version"))
            {
                if (index + 1 >= args.Length || args[index + 1].StartsWith("--", StringComparison.Ordinal))
                {
                    Console.Error.WriteLine("Missing value for --version.");
                    Environment.ExitCode = 1;
                    return new GeneratorOptions(checkOnly, "");
                }

                manifestVersion = args[++index];
                continue;
            }

            Console.Error.WriteLine($"Unknown option: {arg}");
            Environment.ExitCode = 1;
            return new GeneratorOptions(checkOnly, "");
        }

        if (string.IsNullOrWhiteSpace(manifestVersion))
        {
            Console.Error.WriteLine("Missing required --version MAJOR.MINOR.PATCH option.");
            Environment.ExitCode = 1;
            return new GeneratorOptions(checkOnly, "");
        }

        return new GeneratorOptions(checkOnly, manifestVersion);
    }
}

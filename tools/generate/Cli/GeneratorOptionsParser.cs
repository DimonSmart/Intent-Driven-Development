internal static class GeneratorOptionsParser
{
    public static GeneratorOptions Parse(string[] args)
    {
        var checkOnly = false;
        var manifestVersion = File.Exists("VERSION")
            ? File.ReadAllText("VERSION").Trim()
            : "0.0.0-local";

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
                    return new GeneratorOptions(checkOnly, manifestVersion);
                }

                manifestVersion = args[++index];
                continue;
            }

            Console.Error.WriteLine($"Unknown option: {arg}");
            Environment.ExitCode = 1;
            return new GeneratorOptions(checkOnly, manifestVersion);
        }

        return new GeneratorOptions(checkOnly, manifestVersion);
    }
}

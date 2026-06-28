internal static class InstallOptionsParser
{
    public static InstallOptions Parse(IReadOnlyList<string> commandArgs)
    {
        EnsureNoUnknownOptions(commandArgs, "--target", "--coding-agent", "--all", "--entry", "--force", "--pack");

        var force = commandArgs.Contains("--force", StringComparer.Ordinal);
        var installAll = commandArgs.Contains("--all", StringComparer.Ordinal);
        var target = ValueAfter(commandArgs, "--target");
        var codingAgentOption = ValueAfter(commandArgs, "--coding-agent");
        if (target is not null && codingAgentOption is not null)
        {
            throw new ToolException("Use either --target or --coding-agent, not both.");
        }

        var codingAgent = codingAgentOption ?? target;
        if (installAll && codingAgent is not null)
        {
            throw new ToolException("Use either --all or --target <coding-agent>, not both.");
        }

        if (!installAll && codingAgent is null)
        {
            throw new ToolException("Missing CodingAgent. Use --target <coding-agent>, --coding-agent <coding-agent>, or --all.");
        }

        return new InstallOptions(
            force,
            installAll,
            codingAgent,
            ParseEntryMode(ValueAfter(commandArgs, "--entry")),
            ValuesAfter(commandArgs, "--pack"));
    }

    private static EntryMode ParseEntryMode(string? value)
    {
        if (value is null)
        {
            return EntryMode.Minimal;
        }

        return value switch
        {
            "minimal" => EntryMode.Minimal,
            "none" => EntryMode.None,
            "full" => EntryMode.Full,
            _ => throw new ToolException($"Unknown entry mode: {value}" + Environment.NewLine + "Available entry modes: minimal, none, full")
        };
    }

    private static void EnsureNoUnknownOptions(IReadOnlyList<string> commandArgs, params string[] known)
    {
        for (var index = 0; index < commandArgs.Count; index++)
        {
            var arg = commandArgs[index];
            if (!arg.StartsWith("--", StringComparison.Ordinal))
            {
                continue;
            }

            if (!known.Contains(arg, StringComparer.Ordinal))
            {
                throw new ToolException($"Unknown option: {arg}");
            }

            if (arg is "--target" or "--coding-agent" or "--entry" or "--pack")
            {
                index++;
            }
        }
    }

    private static string? ValueAfter(IReadOnlyList<string> commandArgs, string option)
    {
        var index = Array.IndexOf(commandArgs.ToArray(), option);
        if (index < 0)
        {
            return null;
        }

        if (index + 1 >= commandArgs.Count || commandArgs[index + 1].StartsWith("--", StringComparison.Ordinal))
        {
            throw new ToolException($"Missing value for {option}.");
        }

        return commandArgs[index + 1];
    }

    private static IReadOnlyList<string> ValuesAfter(IReadOnlyList<string> commandArgs, string option)
    {
        var values = new List<string>();
        for (var index = 0; index < commandArgs.Count; index++)
        {
            if (!StringComparer.Ordinal.Equals(commandArgs[index], option))
            {
                continue;
            }

            if (index + 1 >= commandArgs.Count || commandArgs[index + 1].StartsWith("--", StringComparison.Ordinal))
            {
                throw new ToolException($"Missing value for {option}.");
            }

            values.Add(commandArgs[index + 1]);
            index++;
        }

        return values;
    }
}

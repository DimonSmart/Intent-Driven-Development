internal static class UsagePrinter
{
    public static void Print()
    {
        Console.WriteLine("""
            Usage:
              intent-driven-development init [--force]
              intent-driven-development install --target <coding-agent> [--pack <pack>]... [--entry minimal|none|full] [--force]
              intent-driven-development install --coding-agent <coding-agent> [--pack <pack>]... [--entry minimal|none|full] [--force]
              intent-driven-development install --all [--pack <pack>]... [--entry minimal|none|full] [--force]
              intent-driven-development list-targets
              intent-driven-development list-coding-agents
              intent-driven-development list-packs
              intent-driven-development version
            """);
    }
}

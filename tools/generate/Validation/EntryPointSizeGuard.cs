internal static class EntryPointSizeGuard
{
    private const int EntryPointLineLimit = 80;

    public static void Guard(string relativePath, string content)
    {
        var lineCount = content.ReplaceLineEndings("\n").Split('\n').Length;
        if (lineCount > EntryPointLineLimit)
        {
            throw new InvalidOperationException(
                $"Entry point is too large: {relativePath} has {lineCount} lines, limit is {EntryPointLineLimit}." +
                Environment.NewLine +
                "Move detailed workflow into skills or path-scoped instructions.");
        }
    }
}

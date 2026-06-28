internal static class ContentNormalizer
{
    public static string NormalizeContent(string content) => content.TrimEnd() + Environment.NewLine;

    public static string JoinBlocks(params string[] blocks) =>
        string.Join(Environment.NewLine + Environment.NewLine, blocks.Select(block => block.Trim()));
}

internal static class RequiredFileReader
{
    public static string Read(string path) =>
        File.Exists(path) ? File.ReadAllText(path) : throw new ToolException($"Required bundled file not found: {path}");
}

internal static class PlatformAdapterFactory
{
    public static IPlatformAdapter Create(string platform) =>
        platform switch
        {
            "claude" => new ClaudePlatformAdapter(),
            "codex" => new CodexPlatformAdapter(),
            _ => throw new InvalidOperationException($"Unsupported platform adapter: {platform}")
        };
}

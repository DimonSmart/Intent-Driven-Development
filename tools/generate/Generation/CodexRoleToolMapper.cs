internal sealed record CodexToolMapping(RoleTool Tool, IReadOnlyList<string> NativeCapabilities, bool PromptOnly);

internal static class CodexRoleToolMapper
{
    public static CodexToolMapping Map(RoleTool tool) => tool switch
    {
        // Codex 0.146.0 role TOML files configure a prompt and session settings,
        // but do not provide per-role tool ACLs. Keep that limitation explicit.
        RoleTool.RepositoryRead => PromptOnly(tool),
        RoleTool.RepositoryWrite => PromptOnly(tool),
        RoleTool.CommandExecute => PromptOnly(tool),
        RoleTool.AgentSpawn => PromptOnly(tool),
        RoleTool.AgentWait => PromptOnly(tool),
        RoleTool.FactoryStateRead => PromptOnly(tool),
        RoleTool.FactoryStateWrite => PromptOnly(tool),
        RoleTool.FactoryResultWrite => PromptOnly(tool),
        _ => throw new ArgumentOutOfRangeException(nameof(tool), tool, "Unknown role tool.")
    };

    public static IReadOnlyList<CodexToolMapping> Map(IReadOnlyList<RoleTool> tools) => tools.Select(Map).ToArray();

    private static CodexToolMapping PromptOnly(RoleTool tool) => new(tool, [], PromptOnly: true);
}

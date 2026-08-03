internal static class ClaudeRoleToolMapper
{
    public static IReadOnlyList<string> Map(IReadOnlyList<RoleDefinition> roles) => roles
        .SelectMany(role => role.Tools)
        .SelectMany(Map)
        .Distinct(StringComparer.Ordinal)
        .ToArray();

    private static IReadOnlyList<string> Map(RoleTool tool) => tool switch
    {
        RoleTool.RepositoryRead => ["Read", "Glob", "Grep"],
        RoleTool.RepositoryWrite => ["Edit", "Write"],
        RoleTool.CommandExecute => ["Bash"],
        RoleTool.AgentSpawn => ["Task"],
        RoleTool.AgentWait => ["TaskOutput"],
        RoleTool.FactoryStateRead => ["Read"],
        RoleTool.FactoryStateWrite => ["Edit", "Write"],
        RoleTool.FactoryResultWrite => ["Write"],
        _ => throw new ArgumentOutOfRangeException(nameof(tool), tool, "Unknown role tool.")
    };
}

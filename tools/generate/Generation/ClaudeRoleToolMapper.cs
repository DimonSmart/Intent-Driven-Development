internal static class ClaudeRoleToolMapper
{
    public static IReadOnlyList<string> Map(IReadOnlyList<RoleDefinition> roles) => roles
        .SelectMany(role => role.Tools)
        .SelectMany(Map)
        .Distinct(StringComparer.Ordinal)
        .ToArray();

    private static IReadOnlyList<string> Map(RoleTool tool) => tool switch
    {
        RoleTool.FileRead => ["Read", "Glob", "Grep"],
        RoleTool.FileWrite => ["Edit", "Write"],
        RoleTool.CommandExecute => ["Bash"],
        RoleTool.AgentSpawn => ["Task"],
        RoleTool.AgentWait => ["TaskOutput"],
        _ => throw new ArgumentOutOfRangeException(nameof(tool), tool, "Unknown role tool.")
    };
}

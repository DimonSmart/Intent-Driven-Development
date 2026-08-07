internal static class RoleToolNames
{
    private static readonly IReadOnlyDictionary<string, RoleTool> ToolsByName =
        new Dictionary<string, RoleTool>(StringComparer.Ordinal)
        {
            ["file.read"] = RoleTool.FileRead,
            ["file.write"] = RoleTool.FileWrite,
            ["command.execute"] = RoleTool.CommandExecute,
            ["agent.spawn"] = RoleTool.AgentSpawn,
            ["agent.wait"] = RoleTool.AgentWait
        };

    public static bool TryParse(string value, out RoleTool tool) => ToolsByName.TryGetValue(value, out tool);

    public static string GetName(RoleTool tool) => tool switch
    {
        RoleTool.FileRead => "file.read",
        RoleTool.FileWrite => "file.write",
        RoleTool.CommandExecute => "command.execute",
        RoleTool.AgentSpawn => "agent.spawn",
        RoleTool.AgentWait => "agent.wait",
        _ => throw new ArgumentOutOfRangeException(nameof(tool), tool, "Unknown role tool.")
    };
}

internal static class RoleToolNames
{
    private static readonly IReadOnlyDictionary<string, RoleTool> ToolsByName =
        new Dictionary<string, RoleTool>(StringComparer.Ordinal)
        {
            ["repository.read"] = RoleTool.RepositoryRead,
            ["repository.write"] = RoleTool.RepositoryWrite,
            ["command.execute"] = RoleTool.CommandExecute,
            ["agent.spawn"] = RoleTool.AgentSpawn,
            ["agent.wait"] = RoleTool.AgentWait,
            ["factory-state.read"] = RoleTool.FactoryStateRead,
            ["factory-state.write"] = RoleTool.FactoryStateWrite,
            ["factory-result.write"] = RoleTool.FactoryResultWrite
        };

    public static bool TryParse(string value, out RoleTool tool) => ToolsByName.TryGetValue(value, out tool);

    public static string GetName(RoleTool tool) => tool switch
    {
        RoleTool.RepositoryRead => "repository.read",
        RoleTool.RepositoryWrite => "repository.write",
        RoleTool.CommandExecute => "command.execute",
        RoleTool.AgentSpawn => "agent.spawn",
        RoleTool.AgentWait => "agent.wait",
        RoleTool.FactoryStateRead => "factory-state.read",
        RoleTool.FactoryStateWrite => "factory-state.write",
        RoleTool.FactoryResultWrite => "factory-result.write",
        _ => throw new ArgumentOutOfRangeException(nameof(tool), tool, "Unknown role tool.")
    };
}

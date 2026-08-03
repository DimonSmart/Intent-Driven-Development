internal sealed record RoleDefinition(
    string Name,
    IReadOnlyList<RoleTool> Tools,
    string Instructions);

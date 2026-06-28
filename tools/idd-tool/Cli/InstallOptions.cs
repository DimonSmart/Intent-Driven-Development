internal sealed record InstallOptions(
    bool Force,
    bool InstallAll,
    string? CodingAgent,
    EntryMode EntryMode,
    IReadOnlyList<string> RequestedPacks);

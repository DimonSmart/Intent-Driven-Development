internal sealed class InstallCommand
{
    public int Run(IReadOnlyList<string> commandArgs)
    {
        var options = InstallOptionsParser.Parse(commandArgs);
        var manifest = new ManifestReader().Read();
        var selectedPacks = new PackResolver().Resolve(manifest, options.RequestedPacks);
        var codingAgents = options.InstallAll ? manifest.CodingAgents : [ValidateCodingAgent(manifest, options.CodingAgent!)];

        var capabilityValidator = new CodingAgentCapabilityValidator();
        capabilityValidator.ValidateEntryModeCapabilities(manifest, codingAgents, options.EntryMode, options.InstallAll);
        capabilityValidator.ValidatePackCodingAgentCapabilities(manifest, codingAgents, selectedPacks);

        var plannedFiles = new InstallPlanner(new ContentLayout(ContentRootLocator.Find())).Collect(
            manifest,
            codingAgents,
            options.EntryMode,
            selectedPacks);
        new FileInstaller().Copy(plannedFiles, Directory.GetCurrentDirectory(), options.Force);

        var packText = PackResolver.IsDefaultPackSelection(manifest, selectedPacks)
            ? ""
            : $" and packs: {string.Join(", ", selectedPacks)}";
        Console.WriteLine($"Installed {string.Join(", ", codingAgents)} with {FormatEntryMode(options.EntryMode)} entry{packText}.");
        return 0;
    }

    private static string ValidateCodingAgent(Manifest manifest, string codingAgent)
    {
        if (manifest.CodingAgents.Contains(codingAgent, StringComparer.Ordinal))
        {
            return codingAgent;
        }

        throw new ToolException($"Unknown CodingAgent: {codingAgent}" + Environment.NewLine + $"Available CodingAgents: {string.Join(", ", manifest.CodingAgents)}");
    }

    private static string FormatEntryMode(EntryMode entryMode) =>
        entryMode.ToString().ToLowerInvariant();
}

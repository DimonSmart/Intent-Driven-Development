internal sealed class CodingAgentCapabilityValidator
{
    public void ValidateEntryModeCapabilities(Manifest manifest, IReadOnlyList<string> codingAgents, EntryMode entryMode, bool installAll)
    {
        if (entryMode != EntryMode.None)
        {
            return;
        }

        if (manifest.CodingAgentCapabilities is null)
        {
            throw new ToolException("Bundled manifest does not define codingAgentCapabilities.");
        }

        var incompatible = codingAgents
            .Where(codingAgent => !SupportsGeneratedSkills(manifest, codingAgent))
            .ToArray();

        if (incompatible.Length == 0)
        {
            return;
        }

        if (installAll)
        {
            throw new ToolException(
                $"The following CodingAgents do not support generated skills: {string.Join(", ", incompatible)}." +
                Environment.NewLine +
                "--entry none would install no entry point and no skills for those CodingAgents." +
                Environment.NewLine +
                "Use --entry minimal or install skill-capable CodingAgents explicitly.");
        }

        var codingAgent = incompatible[0];
        throw new ToolException(
            $"CodingAgent {codingAgent} does not support generated skills. --entry none would install no entry point and no skills." +
            Environment.NewLine +
            "Use --entry minimal or --entry full for this CodingAgent.");
    }

    public void ValidatePackCodingAgentCapabilities(Manifest manifest, IReadOnlyList<string> codingAgents, IReadOnlyList<string> selectedPacks)
    {
        var selectedSkills = ManifestSkillSelector.SelectedSkills(manifest, selectedPacks);
        if (selectedSkills.Count == 0)
        {
            return;
        }

        var containsManualOnlySkills = selectedSkills.Any(skill =>
            manifest.Skills.TryGetValue(skill, out var metadata) &&
            StringComparer.Ordinal.Equals(metadata.Invocation, "manual"));
        if (!containsManualOnlySkills)
        {
            return;
        }

        var manualIncompatible = codingAgents
            .Where(codingAgent => !SupportsManualOnlySkills(manifest, codingAgent))
            .ToArray();

        if (manualIncompatible.Length > 0)
        {
            var packText = selectedPacks.Count == 1
                ? $"Pack '{selectedPacks[0]}' contains"
                : $"Selected packs '{string.Join(", ", selectedPacks)}' contain";
            throw new ToolException(
                $"{packText} manual-only skills, but CodingAgent '{manualIncompatible[0]}' does not support manual-only skill invocation." +
                Environment.NewLine +
                "Manual-only skills must not be installed as auto-selectable skills." +
                Environment.NewLine +
                "Use a CodingAgent that supports manual-only skills or install core without the factory pack.");
        }
    }

    public static bool SupportsGeneratedSkills(Manifest manifest, string codingAgent)
    {
        if (manifest.CodingAgentCapabilities is null ||
            !manifest.CodingAgentCapabilities.TryGetValue(codingAgent, out var capabilities))
        {
            throw new ToolException($"Bundled manifest does not define codingAgentCapabilities for CodingAgent: {codingAgent}");
        }

        return capabilities.SupportsSkills;
    }

    private static bool SupportsManualOnlySkills(Manifest manifest, string codingAgent)
    {
        if (manifest.CodingAgentCapabilities is null ||
            !manifest.CodingAgentCapabilities.TryGetValue(codingAgent, out var capabilities))
        {
            throw new ToolException($"Bundled manifest does not define codingAgentCapabilities for CodingAgent: {codingAgent}");
        }

        return capabilities.SupportsSkills && capabilities.SupportsManualOnlySkills;
    }
}

using System.Text;

internal sealed class EntryBuilder(ContentLayout layout)
{
    public PlannedFile Build(
        Manifest manifest,
        string codingAgent,
        EntryMode entryMode,
        IReadOnlyList<string> selectedPacks)
    {
        if (!manifest.EntryPoints.TryGetValue(codingAgent, out var entryPoint))
        {
            throw new ToolException($"No entry point configured for CodingAgent: {codingAgent}");
        }

        var blocks = new List<string>
        {
            RequiredFileReader.Read(Path.Combine(layout.AdaptersRoot, codingAgent, "entry.md")),
            RequiredFileReader.Read(Path.Combine(layout.PacksRoot, "intent-driven-development.md"))
                .Replace("{{skillGuidance}}", BuildSkillGuidance(manifest, codingAgent, selectedPacks), StringComparison.Ordinal)
                .Replace("{{workflowGuidance}}", BuildWorkflowGuidance(manifest, codingAgent), StringComparison.Ordinal)
        };

        if (entryMode == EntryMode.Full)
        {
            blocks.Add(ReadCanonicalMethodology());
        }

        var content = string.Join(Environment.NewLine + Environment.NewLine, blocks.Select(block => block.Trim())) + Environment.NewLine;
        var bytes = Encoding.UTF8.GetBytes(content);
        return new PlannedFile(PathNormalizer.Normalize(entryPoint), bytes, FileHasher.Sha256(bytes));
    }

    private static string BuildSkillGuidance(Manifest manifest, string codingAgent, IReadOnlyList<string> selectedPacks)
    {
        if (!CodingAgentCapabilityValidator.SupportsGeneratedSkills(manifest, codingAgent))
        {
            return """
                This CodingAgent does not use generated IDD skills. Keep IDD work focused and
                read only the documents needed for the current task.
                """;
        }

        var selectedSkills = ManifestSkillSelector.SelectedSkills(manifest, selectedPacks)
            .OrderBy(name => name, StringComparer.Ordinal)
            .Select(name => $"- `{name}`");
        var blocks = new List<string>
        {
            "Use installed IDD skills for specific workflows:" + Environment.NewLine + string.Join(Environment.NewLine, selectedSkills),
            """
            ## IDD Workflow Routing

            Use `idd-intent-brainstorm` when product intent is unclear.
            Use `idd-intent-change` when durable product behavior must change.
            Use `idd-code-implement` for one focused behavior already covered by
            `.idd/intent/`, then use `idd-code-check-implementation`.
            Use `idd-intent-new-document` only for a new durable product area, ADR, or
            spike.

            Do not create a new spec merely because the user described a new task. Prefer
            updating the existing owning spec.
            """
        };

        if (selectedPacks.Contains("factory", StringComparer.Ordinal))
        {
            blocks.Add("""
                ## IDD Factory Routing

                Use IDD factory skills only for planned implementation orchestration,
                multi-step execution, task slicing, or factory role based work.

                - Use `idd-factory-create-work-plan` to create a temporary Factory Work Plan.
                - Use `idd-factory-execute-work-plan` to execute an explicit Factory Work Plan.
                - Use `idd-factory-review-task` after each bounded task.
                - Use `idd-factory-review-work-result` after all tasks are complete.
                - Use `idd-factory-finish-work` to summarize and clean temporary factory artifacts.

                Factory work plans are temporary execution state.
                They are not specs and must not be stored in `.idd/intent/`.
                Do not read old factory work plans unless the user explicitly provides the exact path.
                """);
        }

        return string.Join(Environment.NewLine + Environment.NewLine, blocks.Select(block => block.Trim()));
    }

    private static string BuildWorkflowGuidance(Manifest manifest, string codingAgent) =>
        CodingAgentCapabilityValidator.SupportsGeneratedSkills(manifest, codingAgent)
            ? "This file and installed IDD skills are workflow guidance.\nThey are not product specifications."
            : "This file is workflow guidance.\nIt is not a product specification.";

    private string ReadCanonicalMethodology()
    {
        var names = new[]
        {
            "intent-driven-development.md",
            "numbering.md",
            "document-types.md",
            "semantic-changes.md",
            "coding-agent-workflow.md"
        };

        return string.Join(Environment.NewLine + Environment.NewLine, names.Select(name => RequiredFileReader.Read(Path.Combine(layout.MethodologyRoot, name)).Trim()));
    }
}

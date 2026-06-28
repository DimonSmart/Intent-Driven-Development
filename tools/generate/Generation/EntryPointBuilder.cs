internal sealed class EntryPointBuilder(RepositoryLayout layout)
{
    public string Build(string adapterDir, AdapterConfig adapter)
    {
        var entry = RequiredFileReader.Read(Path.Combine(adapterDir, "entry.md"));
        var pack = BuildPack(adapter);
        var entryPoint = ContentNormalizer.NormalizeContent(ContentNormalizer.JoinBlocks(entry, pack));
        EntryPointSizeGuard.Guard(adapter.EntryPoint, entryPoint);
        return entryPoint;
    }

    private string BuildPack(AdapterConfig adapter)
    {
        var pack = RequiredFileReader.Read(Path.Combine(layout.PacksRoot, "intent-driven-development.md"));
        var skillGuidance = adapter.SupportsSkills
            ? """
              Use IDD skills for specific workflows:
              - `spec-audit`
              - `spec-brainstorm`
              - `spec-change`
              - `spec-implement`
              - `spec-import`
              - `spec-lint`
              - `spec-new-document`
              - `spec-normalize-current`
              - `spec-check-implementation`
              - `spec-update-from-implementation`

              ## IDD Workflow Routing

              When the user asks to change product behavior: use `spec-change`,
              then `spec-implement`, then `spec-check-implementation`.

              For a new feature or behavior change with unclear,
              implementation-shaped, over-specified, or likely simpler intent:
              use `spec-brainstorm` before `spec-change`. After it produces a
              confirmed specification-ready intent, use `spec-change`.

              When the user asks to implement behavior already described in
              `.specs/`: use `spec-implement`, then `spec-check-implementation`.
              Do not use `spec-brainstorm` when current specs are already clear
              and the user asks to implement them.

              When the user reports a possible bug: use
              `spec-check-implementation`; if the current spec is clear, fix
              implementation with `spec-implement`; if the desired behavior
              changes product intent, use `spec-change` first.

              When the user asks to create a new feature: use `spec-change` if
              the feature extends an existing product area. Use `spec-new-document`
              only if the feature needs a new durable product area, ADR, or
              spike.

              Do not create a new spec merely because the user described a new
              task. Prefer updating the existing owning spec.
              """
            : """
              This CodingAgent does not use generated IDD skills. Keep IDD work focused and
              read only the documents needed for the current task.
              """;
        var workflowGuidance = adapter.SupportsSkills
            ? "This file and installed IDD skills are workflow guidance.\nThey are not product specifications."
            : "This file is workflow guidance.\nIt is not a product specification.";

        return pack
            .Replace("{{skillGuidance}}", skillGuidance.Trim(), StringComparison.Ordinal)
            .Replace("{{workflowGuidance}}", workflowGuidance.Trim(), StringComparison.Ordinal);
    }
}

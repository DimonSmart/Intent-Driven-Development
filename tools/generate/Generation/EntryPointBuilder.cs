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
              Use installed IDD skills for specific workflows:
              - `idd-skip` (manual-only; never select automatically; current request only)
              - `idd-intent-audit`
              - `idd-intent-brainstorm`
              - `idd-intent-change`
              - `idd-intent-import`
              - `idd-intent-lint`
              - `idd-intent-new-document`
              - `idd-intent-normalize-current`
              - `idd-code-implement`
              - `idd-code-check-implementation`
              - `idd-code-update-intent`

              ## IDD Workflow Routing

              Use `idd-intent-brainstorm` when product intent is unclear.

              Use `idd-skip` only when the user explicitly invokes it for the current
              request. Never select `idd-skip` automatically.

              Use `idd-intent-change` when durable product behavior must change.

              Use `idd-code-implement` for one focused behavior already covered by
              `.idd/intent/`, then use `idd-code-check-implementation`.

              Use `idd-intent-new-document` only for a new durable product area,
              ADR, or spike.

              Do not create a new spec merely because the user described a new
              task. Prefer updating the existing owning spec.

              """
            : """
              This CodingAgent does not use generated IDD skills. Keep IDD work focused and
              read only the documents needed for the current task. Skip IDD only when the
              user explicitly requests it for the current request; never select skip
              automatically.
              """;
        var workflowGuidance = adapter.SupportsSkills
            ? "This file and installed IDD skills are workflow guidance.\nThey are not product specifications."
            : "This file is workflow guidance.\nIt is not a product specification.";

        return pack
            .Replace("{{skillGuidance}}", skillGuidance.Trim(), StringComparison.Ordinal)
            .Replace("{{workflowGuidance}}", workflowGuidance.Trim(), StringComparison.Ordinal);
    }
}

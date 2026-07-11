internal sealed partial class SmokeTestSuite
{
    void ExpectDurableIntentPolicy()
    {
        const string specTemplatePath = "src/canonical/project-files/intent/_templates/spec.md";
        var specTemplate = File.ReadAllText(Path.Combine(repoRoot, specTemplatePath));
        ExpectContains(specTemplate, "Durable Architecture And Constraints", specTemplatePath, "spec template");
        ExpectContains(specTemplate, "State what must be verified, not the local command", specTemplatePath, "spec template");
        ExpectDoesNotContain(specTemplate, "Architecture And Patterns", specTemplatePath, "spec template");
        ExpectDoesNotContain(specTemplate, "List checks that verify the implementation", specTemplatePath, "spec template");
        ExpectDoesNotContain(specTemplate, "Status: Current", specTemplatePath, "spec template");
        ExpectDoesNotContain(specTemplate, "Status: Superseded", specTemplatePath, "spec template");
        ExpectDoesNotContain(specTemplate, "Run dotnet build", specTemplatePath, "spec template");
        ExpectDoesNotContain(specTemplate, "Run dotnet test", specTemplatePath, "spec template");

        const string methodologyPath = "src/canonical/methodology/intent-driven-development.md";
        var methodology = File.ReadAllText(Path.Combine(repoRoot, methodologyPath));
        ExpectContains(methodology, "Durable Constraint vs Implementation Detail", methodologyPath, "methodology");
        ExpectContains(methodology, "A spec document has no lifecycle status", methodologyPath, "methodology");
        ExpectContains(methodology, "Git history is the only history of spec revisions", methodologyPath, "methodology");

        const string adrTemplatePath = "src/canonical/project-files/intent/_templates/adr.md";
        const string spikeTemplatePath = "src/canonical/project-files/intent/_templates/spike.md";
        ExpectContains(File.ReadAllText(Path.Combine(repoRoot, adrTemplatePath)), "## Status", adrTemplatePath, "ADR template");
        ExpectDoesNotContain(File.ReadAllText(Path.Combine(repoRoot, spikeTemplatePath)), "## Status", spikeTemplatePath, "spike template");

        var generatedPolicyNeedles = new Dictionary<string, string>
        {
            ["idd-intent-import"] = "State what must be verified, not the local command",
            ["idd-intent-change"] = "Keep Verification at the level of required evidence",
            ["idd-code-update-intent"] = "without internal type names",
            ["idd-intent-audit"] = "implementation leakage",
            ["idd-intent-lint"] = "Status: Current"
        };

        foreach (var (skillName, needle) in generatedPolicyNeedles)
        {
            foreach (var relativePath in GeneratedSkillPaths(skillName))
            {
                var content = File.ReadAllText(Path.Combine(repoRoot, relativePath));
                ExpectContains(content, needle, relativePath, "generated durable intent policy");
            }
        }

        foreach (var relativePath in GeneratedSkillPaths("idd-intent-lint"))
        {
            var content = File.ReadAllText(Path.Combine(repoRoot, relativePath));
            ExpectContains(content, "dotnet build", relativePath, "generated lint policy");
        }
    }
}

using System.Text.Json;
using Idd.Generation.Tests.Infrastructure;
using Xunit;

namespace Idd.Generation.Tests;

[Collection(GenerationCollection.Name)]
public sealed class IntentAuditModelContractTests(GenerationFixture fixture)
{
    private string Canonical(params string[] parts) =>
        fixture.ReadText(Path.Combine([fixture.RepoRoot, "src", "canonical", .. parts]));

    private static readonly string LegacyImplementationCheck = "idd-code-" + "check-implementation";
    private static readonly string LegacyStructureAudit = "idd-intent-" + "audit";
    private static readonly string GenericVerificationSkill = "idd-code-" + "verify";

    [Fact]
    public void Manifest_ExposesDistinctLintStructureAndDriftModel()
    {
        using var manifest = JsonDocument.Parse(Canonical("plugins", "plugin-manifest.json"));
        var skills = manifest.RootElement.GetProperty("plugins").GetProperty("idd-intent")
            .GetProperty("skills").EnumerateArray().Select(x => x.GetString()).ToArray();

        Assert.Contains("idd-intent-lint", skills);
        Assert.Contains("idd-intent-structure-audit", skills);
        Assert.Contains("idd-intent-drift-audit", skills);
        Assert.DoesNotContain(LegacyImplementationCheck, skills);
        Assert.DoesNotContain(LegacyStructureAudit, skills);
        Assert.DoesNotContain(GenericVerificationSkill, skills);
    }

    [Fact]
    public void DriftSkill_HasProjectVerificationAndEngineeringReferences()
    {
        using var manifest = JsonDocument.Parse(Canonical("plugins", "plugin-manifest.json"));
        var refs = manifest.RootElement.GetProperty("plugins").GetProperty("idd-intent")
            .GetProperty("skillReferences").EnumerateArray()
            .Where(x => x.GetProperty("skill").GetString() == "idd-intent-drift-audit")
            .Select(x => x.GetProperty("destination").GetString()).ToArray();

        Assert.Contains("project-verification.md", refs);
        Assert.Contains("engineering-guardrails.md", refs);
    }


}

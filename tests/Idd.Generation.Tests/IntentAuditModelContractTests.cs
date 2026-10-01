using System.Text.Json;
using Idd.Generation.Tests.Infrastructure;
using Xunit;

namespace Idd.Generation.Tests;

[Collection(GenerationCollection.Name)]
public sealed class IntentAuditModelContractTests(GenerationFixture fixture)
{
    private string Canonical(params string[] parts) =>
        fixture.ReadText(Path.Combine([fixture.RepoRoot, "src", "canonical", .. parts]));

    private string Repo(params string[] parts) =>
        fixture.ReadText(Path.Combine([fixture.RepoRoot, .. parts]));

    private static readonly string LegacyImplementationCheck =
        "idd-code-" + "check-implementation";

    private static readonly string LegacyStructureAudit =
        "idd-intent-" + "audit";

    private static readonly string LegacyRouteImplementationCheck =
        "implementation-" + "intent-check";

    private static readonly string LegacyRouteAudit =
        "intent-" + "audit";

    private static readonly string GenericVerificationSkill =
        "idd-code-" + "verify";

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
    public void DriftAudit_RequiresExplicitScopeAndSupportsProjectWide()
    {
        var drift = Canonical("skills", "idd-intent-drift-audit.md");

        Assert.Contains("always works with an explicitly defined audit scope", drift);
        Assert.Contains("Please specify the drift audit scope", drift);
        Assert.Contains("project-wide", drift);
        Assert.Contains("A focused scope must not be silently widened", drift);
        Assert.Contains("do not ask the user to repeat it", drift);
        Assert.Contains("intent-driven audit map", drift.ToLowerInvariant());
    }

    [Fact]
    public void DriftAudit_PreservesEvidenceDimensionsAndVocabulary()
    {
        var drift = Canonical("skills", "idd-intent-drift-audit.md");

        foreach (var value in new[]
        {
            "matches-intent",
            "implementation-drift",
            "engineering-match",
            "engineering-mismatch",
            "missing-verification",
            "missing-intent",
            "unclear-intent",
            "possible-intent-change",
            "non-goal-or-out-of-scope"
        })
            Assert.Contains(value, drift);

        foreach (var scope in new[]
        {
            "current-requirement",
            "changed-requirement",
            "preserved-requirement",
            "removed-behavior",
            "compatibility-boundary",
            "unowned-behavior"
        })
            Assert.Contains(scope, drift);

        Assert.Contains("Scope: unowned-behavior", drift);
        Assert.DoesNotContain("matches-" + "spec", drift);
        Assert.DoesNotContain("implementation-" + "mismatch", drift);
        Assert.DoesNotContain("missing-" + "spec", drift);
    }

    [Fact]
    public void ProjectWideAudit_RemainsIntentDrivenRatherThanGenericReview()
    {
        var drift = Canonical("skills", "idd-intent-drift-audit.md");

        Assert.Contains("expected observable behavior / durable contract", drift);
        Assert.Contains("implementation evidence", drift);
        Assert.Contains("verification evidence", drift);
        Assert.Contains("not a generic code", drift.ToLowerInvariant());
        Assert.Contains("durable observable implementation behavior", drift);
        Assert.Contains("private helpers", drift);
    }

    [Fact]
    public void StructureAudit_PreservesSemanticDiagnosticsAndDoesNotCompareImplementation()
    {
        var structure = Canonical("skills", "idd-intent-structure-audit.md");

        foreach (var diagnostic in new[]
        {
            "oversized specs",
            "undersized specs",
            "mixed-scope specs",
            "duplicate specs",
            "scattered shared models",
            "stale imported artifacts",
            "semantic conflicts",
            "implementation leakage",
            "over-specified architecture",
            "glossary scope creep"
        })
            Assert.Contains(diagnostic, structure);

        Assert.Contains("does not compare Product", structure);
        Assert.Contains("idd-intent-drift-audit", structure);
    }

    [Fact]
    public void Route_UsesNewAuditClassificationsAndProjectWideScope()
    {
        var route = Canonical("skills", "idd-route.md");

        Assert.Contains("intent-structure-audit", route);
        Assert.Contains("intent-drift-audit", route);
        Assert.Contains("Audit scope: focused | project-wide", route);
        Assert.Contains("Audit scope: project-wide", route);
        Assert.DoesNotContain(LegacyRouteImplementationCheck, route);
        Assert.DoesNotContain(LegacyRouteAudit, route);
    }

    [Fact]
    public void CodeImplement_PassesExplicitFocusedDriftScope()
    {
        var implementation = Canonical("skills", "idd-code-implement.md");

        Assert.Contains("focused intent drift audit", implementation);
        Assert.Contains("explicit scope derived from", implementation);
        Assert.Contains("changed implementation", implementation);
        Assert.Contains("preservation boundary", implementation);
        Assert.Contains("compatibility boundary", implementation);
        Assert.Contains("removed behavior", implementation);
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

    [Fact]
    public void CurrentRepository_DoesNotContainLegacyPublicNamesOrGenericVerificationSkill()
    {
        var ignoredSegments = new[]
        {
            $"{Path.DirectorySeparatorChar}.git{Path.DirectorySeparatorChar}",
            $"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}",
            $"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"
        };

        var forbidden = new[]
        {
            LegacyImplementationCheck,
            LegacyStructureAudit,
            GenericVerificationSkill,
            LegacyRouteImplementationCheck,
            LegacyRouteAudit
        };

        foreach (var path in Directory.EnumerateFiles(fixture.RepoRoot, "*", SearchOption.AllDirectories))
        {
            if (ignoredSegments.Any(segment => path.Contains(segment, StringComparison.OrdinalIgnoreCase)))
                continue;

            string text;
            try
            {
                text = File.ReadAllText(path);
            }
            catch
            {
                continue;
            }

            foreach (var value in forbidden)
                Assert.DoesNotContain(value, text);
        }
    }

    [Fact]
    public void SelfHostedIntent_RecordsDurableAuditModel()
    {
        var intent = Repo(".idd", "intent", "IDD-0003.spec-core-intent-and-generation.md");

        Assert.Contains("idd-intent-lint", intent);
        Assert.Contains("idd-intent-structure-audit", intent);
        Assert.Contains("idd-intent-drift-audit", intent);
        Assert.Contains("explicit scope", intent);
        Assert.Contains("project-wide", intent);
        Assert.Contains(".idd/verification.yaml", intent);
    }
}

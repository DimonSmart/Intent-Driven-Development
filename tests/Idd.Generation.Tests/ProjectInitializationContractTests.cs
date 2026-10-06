using Idd.Generation.Tests.Infrastructure;
using Xunit;

namespace Idd.Generation.Tests;

[Collection(GenerationCollection.Name)]
public sealed class ProjectInitializationContractTests(GenerationFixture fixture)
{
    private string Repo(params string[] parts) =>
        fixture.ReadText(Path.Combine([fixture.RepoRoot, .. parts]));

    [Fact]
    public void ProjectInit_CreatesIntentStateWithoutProjectPluginManifest()
    {
        var init = Repo("src", "canonical", "skills", "idd-project-init.md");

        Assert.Contains("Create only the project-owned IDD state:", init);
        Assert.Contains(".idd/intent/", init);
        Assert.DoesNotContain("\n.idd/plugins.json\n", init);
        Assert.DoesNotContain("\"plugins\":", init);
        Assert.DoesNotContain("idd-core", init);
        Assert.Contains("unused legacy file", init);
        Assert.Contains("do not read it, modify it, delete it", init);
        Assert.Contains("Do not create a replacement project manifest", init);
    }

    [Fact]
    public void ProjectInit_FactoryScopeComesFromRequestOrExistingProjectState()
    {
        var init = Repo("src", "canonical", "skills", "idd-project-init.md");

        Assert.Contains("current user request explicitly enables Factory workflows", init);
        Assert.Contains("existing Factory-specific project state", init);
        Assert.Contains(".idd/execution.yaml", init);
        Assert.Contains(".idd/factory/", init);
        Assert.Contains("Do not infer Factory enablement merely because the Coding Agent happens to have", init);
        Assert.Contains("idd-factory", init);
    }

    [Fact]
    public void ExistingProjectDocs_ShowIntentOnlyMinimalStructureAndLegacyCompatibility()
    {
        var docs = Repo("docs", "existing-project.md");

        Assert.Contains(".idd/\n  intent/\n", docs);
        Assert.DoesNotContain("  plugins.json", docs);
        Assert.Contains("unused legacy file", docs);
        Assert.Contains("does not read, modify, delete", docs);
    }

    [Fact]
    public void ProjectInitDescription_DoesNotAdvertisePluginDeclaration()
    {
        var descriptions = Repo("src", "canonical", "skills", "skill-descriptions.json");

        Assert.Contains("Initialize project-owned IDD intent state", descriptions);
        Assert.DoesNotContain("IDD plugin declaration", descriptions);
    }
}

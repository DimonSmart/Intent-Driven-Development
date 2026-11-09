using System.Text.Json;
using Idd.Factory.LiveTests.Tests;
using Xunit;

namespace Idd.Factory.LiveTests;

public sealed class FactoryEvalAgentRegistrationTests
{
    [Fact]
    public void StageProjectAgents_CopiesBothGeneratedDefinitionsUnchanged()
    {
        var root = CreateGeneratedFactory();
        try
        {
            var generated = Path.Combine(root, "generated");
            var workspace = Path.Combine(root, "workspace");
            var artifacts = Path.Combine(root, "artifacts");
            var paths = FactoryEvalAgentRegistration.StageProjectAgents(generated, workspace, artifacts);

            Assert.Equal(FactoryEvalAgentRegistration.RequiredRoles.Length, paths.Count);
            foreach (var name in FactoryEvalAgentRegistration.RequiredRoles)
            {
                Assert.Equal(File.ReadAllBytes(Path.Combine(generated, "agents", name + ".toml")),
                    File.ReadAllBytes(Path.Combine(workspace, ".codex", "agents", name + ".toml")));
            }

            using var audit = JsonDocument.Parse(File.ReadAllText(Path.Combine(artifacts, "agent-registration.json")));
            Assert.Equal("project-local", audit.RootElement.GetProperty("mode").GetString());
            Assert.Equal(FactoryEvalAgentRegistration.RequiredRoles.Length,
                audit.RootElement.GetProperty("agents").GetArrayLength());
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void StageProjectAgents_RejectsMissingNativeRoleWithoutPartialInstallation()
    {
        var root = CreateGeneratedFactory();
        try
        {
            var generated = Path.Combine(root, "generated");
            File.Delete(Path.Combine(generated, "agents", FactoryEvalAgentRegistration.RequiredRoles[1] + ".toml"));

            Assert.Throws<InvalidOperationException>(() => FactoryEvalAgentRegistration.StageProjectAgents(
                generated, Path.Combine(root, "workspace"), Path.Combine(root, "artifacts")));
            Assert.False(Directory.Exists(Path.Combine(root, "workspace", ".codex", "agents")));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void StageProjectAgents_DoesNotOverwriteConflictingRegistration()
    {
        var root = CreateGeneratedFactory();
        try
        {
            var workspace = Path.Combine(root, "workspace");
            var destination = Path.Combine(workspace, ".codex", "agents");
            Directory.CreateDirectory(destination);
            var existing = Path.Combine(destination, FactoryEvalAgentRegistration.RequiredRoles[0] + ".toml");
            File.WriteAllText(existing, "original");

            Assert.Throws<InvalidOperationException>(() => FactoryEvalAgentRegistration.StageProjectAgents(
                Path.Combine(root, "generated"), workspace, Path.Combine(root, "artifacts")));
            Assert.Equal("original", File.ReadAllText(existing));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    private static string CreateGeneratedFactory()
    {
        var root = Path.Combine(Path.GetTempPath(), "idd-factory-agent-reg-" + Guid.NewGuid().ToString("N"));
        var generated = Path.Combine(root, "generated");
        Directory.CreateDirectory(Path.Combine(generated, ".codex-plugin"));
        Directory.CreateDirectory(Path.Combine(generated, "agents"));
        File.WriteAllText(Path.Combine(generated, ".codex-plugin", "plugin.json"),
            """{"agents":"./agents/"}""");
        foreach (var name in FactoryEvalAgentRegistration.RequiredRoles)
            File.WriteAllText(Path.Combine(generated, "agents", name + ".toml"),
                "name = " + JsonSerializer.Serialize(name) + "\n" +
                "description = \"test\"\n" +
                "developer_instructions = \"generated\"\n");
        return root;
    }
}

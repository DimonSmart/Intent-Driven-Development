using Idd.Factory.Verification;

namespace Idd.Factory.Tests;

internal sealed class VerificationTestContext : IDisposable
{
    private readonly TestWorkspace workspace = new();

    public string WorkspacePath => workspace.Path;
    public string CurrentDirectory => Path.Combine(workspace.Path, ".idd", "factory", "current");

    public VerificationTestContext WithPolicy(string yaml)
    {
        workspace.Write(".idd/verification.yaml", yaml);
        return this;
    }

    public VerificationTestContext WithCheck(
        string id,
        string command,
        string context = "default",
        string? timeout = null,
        bool confirmationRequired = false)
    {
        return WithPolicy(VerificationPolicyFixture.SingleCheck(id, command, context, timeout, confirmationRequired));
    }

    public string Write(string relativePath, string content) => workspace.Write(relativePath, content);

    public VerificationEngine Engine(VerificationRuntimeHooks? hooks = null) =>
        hooks is null
            ? new VerificationEngine(workspace.Path, CurrentDirectory)
            : new VerificationEngine(workspace.Path, CurrentDirectory, hooks);

    public string EvidencePath(VerificationEvidence evidence) =>
        Path.Combine(CurrentDirectory, "verification", evidence.EvidenceId + ".json");

    public void Dispose() => workspace.Dispose();
}

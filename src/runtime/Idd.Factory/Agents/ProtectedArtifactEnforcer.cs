using Idd.Factory.Domain;

namespace Idd.Factory.Agents;

/// <summary>
/// Captures runner-owned and product-intent artifacts before a semantic worker starts.
/// If the worker mutates any protected root, the exact captured snapshot is restored before
/// the protocol violation is reported to the runtime.
/// </summary>
internal sealed class ProtectedArtifactEnforcer
{
    private readonly IReadOnlyList<ProtectedRootSnapshot> snapshots;

    private ProtectedArtifactEnforcer(IReadOnlyList<ProtectedRootSnapshot> snapshots) => this.snapshots = snapshots;

    public static ProtectedArtifactEnforcer Capture(AgentInvocation invocation)
    {
        var attemptDirectory = Path.GetDirectoryName(invocation.SemanticOutputPath)!;
        var current = Directory.GetParent(Directory.GetParent(attemptDirectory)!.FullName)!.FullName;
        var invocationPath = Path.Combine(attemptDirectory, "invocation.json");
        var policies = new[]
        {
            FilePolicy(Path.Combine(current, "state.json"), "WORKER_CHANGED_RUNNER_STATE"),
            FilePolicy(Path.Combine(current, "request.md"), "WORKER_CHANGED_RUNNER_STATE"),
            FilePolicy(Path.Combine(current, "run-context.md"), "WORKER_CHANGED_RUNNER_STATE"),
            DirectoryPolicy(Path.Combine(current, "work-items"), "WORKER_CHANGED_RUNNER_STATE"),
            DirectoryPolicy(Path.Combine(current, "clarifications"), "WORKER_CHANGED_RUNNER_STATE"),
            DirectoryPolicy(Path.Combine(invocation.Workspace, ".idd", "intent"), "WORKER_CHANGED_PRODUCT_INTENT"),
            FilePolicy(Path.Combine(invocation.Workspace, ".idd", "verification.yaml"), "WORKER_CHANGED_PRODUCT_INTENT"),
            DirectoryPolicy(Path.Combine(current, "plan-revisions"), "WORKER_CHANGED_RUNNER_STATE"),
            FilePolicy(Path.Combine(invocation.Workspace, ".idd", "factory.yaml"), "WORKER_CHANGED_FACTORY_POLICY"),
            FilePolicy(invocationPath, "WORKER_CHANGED_RUNNER_STATE")
        };
        return new(policies.Select(CaptureRoot).ToArray());
    }

    public void ValidateAndRestore()
    {
        var changed = snapshots.Where(HasChanged).ToArray();
        if (changed.Length == 0) return;

        try
        {
            foreach (var snapshot in changed) Restore(snapshot);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new AgentProtocolException(
                "PROTECTED_ARTIFACT_RECOVERY_FAILED",
                $"Worker changed protected artifacts and the authoritative snapshot could not be restored: {exception.Message}");
        }

        var first = changed[0];
        throw new AgentProtocolException(
            first.Policy.ErrorCode,
            $"Worker changed protected artifact {first.Policy.Root}; the authoritative snapshot was restored.");
    }

    private static ProtectedRootSnapshot CaptureRoot(ProtectedRootPolicy policy)
    {
        if (policy.IsDirectory)
        {
            if (File.Exists(policy.Root))
                throw new IOException($"Protected directory root is occupied by a file: {policy.Root}");
            var exists = Directory.Exists(policy.Root);
            return new(policy, exists, exists ? CaptureDirectory(policy.Root) : new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase));
        }

        if (Directory.Exists(policy.Root))
            throw new IOException($"Protected file root is occupied by a directory: {policy.Root}");
        var fileExists = File.Exists(policy.Root);
        var files = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        if (fileExists) files[string.Empty] = File.ReadAllBytes(policy.Root);
        return new(policy, fileExists, files);
    }

    private static bool HasChanged(ProtectedRootSnapshot snapshot)
    {
        var root = snapshot.Policy.Root;
        if (snapshot.Policy.IsDirectory)
        {
            if (File.Exists(root)) return true;
            var exists = Directory.Exists(root);
            if (exists != snapshot.Existed) return true;
            if (!exists) return false;
            return !Equivalent(snapshot.Files, CaptureDirectory(root));
        }

        if (Directory.Exists(root)) return true;
        var fileExists = File.Exists(root);
        if (fileExists != snapshot.Existed) return true;
        if (!fileExists) return false;
        return !File.ReadAllBytes(root).AsSpan().SequenceEqual(snapshot.Files[string.Empty]);
    }

    private static void Restore(ProtectedRootSnapshot snapshot)
    {
        RemoveCurrentRoot(snapshot.Policy.Root);
        if (!snapshot.Existed) return;

        if (!snapshot.Policy.IsDirectory)
        {
            WriteAtomically(snapshot.Policy.Root, snapshot.Files[string.Empty]);
            return;
        }

        Directory.CreateDirectory(snapshot.Policy.Root);
        foreach (var (relative, content) in snapshot.Files.OrderBy(x => x.Key, StringComparer.Ordinal))
            WriteAtomically(Path.Combine(snapshot.Policy.Root, relative), content);
    }

    private static Dictionary<string, byte[]> CaptureDirectory(string root) =>
        Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToDictionary(
                path => Path.GetRelativePath(root, path),
                File.ReadAllBytes,
                StringComparer.OrdinalIgnoreCase);

    private static bool Equivalent(IReadOnlyDictionary<string, byte[]> expected, IReadOnlyDictionary<string, byte[]> actual)
    {
        if (expected.Count != actual.Count) return false;
        foreach (var (path, content) in expected)
            if (!actual.TryGetValue(path, out var current) || !current.AsSpan().SequenceEqual(content)) return false;
        return true;
    }

    private static void RemoveCurrentRoot(string root)
    {
        if (File.Exists(root)) File.Delete(root);
        else if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
    }

    private static void WriteAtomically(string path, byte[] content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + $".restore-{Guid.NewGuid():N}.tmp";
        try
        {
            File.WriteAllBytes(temporary, content);
            File.Move(temporary, path, true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    private static ProtectedRootPolicy FilePolicy(string root, string errorCode) => new(root, false, errorCode);
    private static ProtectedRootPolicy DirectoryPolicy(string root, string errorCode) => new(root, true, errorCode);

    private sealed record ProtectedRootPolicy(string Root, bool IsDirectory, string ErrorCode);
    private sealed record ProtectedRootSnapshot(ProtectedRootPolicy Policy, bool Existed, IReadOnlyDictionary<string, byte[]> Files);
}

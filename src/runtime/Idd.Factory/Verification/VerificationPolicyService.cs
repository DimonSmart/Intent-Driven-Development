using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Idd.Factory.Verification;

internal sealed class VerificationPolicyService(string workspace)
{
    private readonly string policyPath = Path.Combine(workspace, ".idd", "verification.yaml");

    public void ValidateCheckIds(IEnumerable<string> checkIds)
    {
        var ids = checkIds.Distinct(StringComparer.Ordinal).ToArray();
        var policy = Load();
        if (policy is null)
        {
            if (ids.Length > 0)
                throw new VerificationException(
                    "UNKNOWN_VERIFICATION_CHECK",
                    "Explicit verification IDs require .idd/verification.yaml.");
            return;
        }

        var unknown = ids.Where(id => !policy.Checks.ContainsKey(id)).ToArray();
        if (unknown.Length > 0)
            throw new VerificationException(
                "UNKNOWN_VERIFICATION_CHECK",
                $"Unknown check IDs: {string.Join(", ", unknown)}.");
    }

    public VerificationPolicy? Load() =>
        File.Exists(policyPath)
            ? VerificationPolicyParser.Parse(File.ReadAllText(policyPath))
            : null;

    public async Task<VerificationPolicy?> LoadAsync(CancellationToken cancellationToken) =>
        File.Exists(policyPath)
            ? VerificationPolicyParser.Parse(await File.ReadAllTextAsync(policyPath, cancellationToken))
            : null;

    public async Task<VerificationPolicy> LoadRequiredAsync(
        string code,
        string message,
        CancellationToken cancellationToken) =>
        await LoadAsync(cancellationToken) ?? throw new VerificationException(code, message);

    public async Task<ResolvedVerificationSelection> ResolveContextAsync(
        string context,
        IEnumerable<string> changedPaths,
        CancellationToken cancellationToken)
    {
        var policy = await LoadAsync(cancellationToken);
        return policy is null
            ? new([], "not-configured")
            : new(policy.ResolveContext(context, changedPaths), PolicyHash(policy));
    }

    public static VerificationCheck GetCheck(
        VerificationPolicy policy,
        string checkId,
        string missingCode = "UNKNOWN_VERIFICATION_CHECK") =>
        policy.Checks.TryGetValue(checkId, out var check)
            ? check
            : throw new VerificationException(missingCode, $"Verification check {checkId} no longer exists.");

    public static string DefinitionHash(VerificationCheck check) =>
        Hash($"run={check.Run}\ninstructions={check.Instructions}\ntimeout={check.Timeout:c}\nconfirmation={check.ConfirmationRequired}");

    public static string PolicyHash(VerificationPolicy policy)
    {
        var canonical = new
        {
            version = 1,
            checks = policy.Checks.OrderBy(x => x.Key, StringComparer.Ordinal).Select(x => new
            {
                id = x.Key,
                run = x.Value.Run,
                instructions = x.Value.Instructions,
                timeoutTicks = x.Value.Timeout.Ticks,
                confirmationRequired = x.Value.ConfirmationRequired
            }).ToArray(),
            contexts = policy.Contexts.OrderBy(x => x.Key, StringComparer.Ordinal).Select(x => new
            {
                name = x.Key,
                hasUse = x.Value.HasUse,
                use = x.Value.Use.ToArray(),
                rules = x.Value.Rules.Select(rule => new
                {
                    paths = rule.Paths.ToArray(),
                    fallback = rule.Fallback,
                    use = rule.Use.ToArray()
                }).ToArray()
            }).ToArray()
        };
        return Hash(JsonSerializer.Serialize(canonical));
    }

    public VerificationCheck? RepositoryFallback()
    {
        if (File.Exists(Path.Combine(workspace, "scripts", "Check.ps1")))
        {
            var run = OperatingSystem.IsWindows()
                ? "& './scripts/Check.ps1'"
                : "pwsh -NoProfile -File './scripts/Check.ps1'";
            return new(run, null, TimeSpan.FromMinutes(30));
        }
        if (File.Exists(Path.Combine(workspace, "scripts", "check.sh")))
            return new("./scripts/check.sh", null, TimeSpan.FromMinutes(30), false);
        var solution = Directory.GetFiles(workspace, "*.slnx")
            .Concat(Directory.GetFiles(workspace, "*.sln"))
            .OrderBy(x => x, StringComparer.Ordinal)
            .FirstOrDefault();
        if (solution is not null)
            return new($"dotnet test '{Path.GetFileName(solution)}'", null, TimeSpan.FromMinutes(30));
        var project = Directory.GetFiles(workspace, "*.csproj", SearchOption.TopDirectoryOnly)
            .OrderBy(x => x, StringComparer.Ordinal)
            .FirstOrDefault();
        if (project is not null)
            return new(
                $"dotnet test '{Path.GetRelativePath(workspace, project).Replace('\\', '/')}'",
                null,
                TimeSpan.FromMinutes(30));
        return null;
    }

    private static string Hash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
}

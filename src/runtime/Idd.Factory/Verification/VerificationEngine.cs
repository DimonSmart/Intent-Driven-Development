namespace Idd.Factory.Verification;

public class VerificationEngine
{
    private readonly VerificationPolicyService policy;
    private readonly VerificationRunner runner;
    private readonly VerificationEvidenceStore evidenceStore;

    public VerificationEngine(string workspace, string currentDirectory)
        : this(workspace, currentDirectory, new()) { }

    internal VerificationEngine(
        string workspace,
        string currentDirectory,
        VerificationRuntimeHooks hooks)
    {
        policy = new VerificationPolicyService(workspace);
        evidenceStore = new VerificationEvidenceStore(currentDirectory, hooks);
        runner = new VerificationRunner(workspace, currentDirectory, hooks, evidenceStore);
    }

    public static VerificationEvidence Read(string json) => VerificationEvidenceStore.Read(json);

    public void ValidateCheckIds(IEnumerable<string> checkIds) => policy.ValidateCheckIds(checkIds);

    public virtual async Task<VerificationResult> RunContextAsync(
        string context,
        CancellationToken cancellationToken) =>
        await RunContextAsync(context, [], cancellationToken);

    public virtual async Task<VerificationResult> RunContextAsync(
        string context,
        IEnumerable<string> changedPaths,
        CancellationToken cancellationToken)
    {
        var loaded = await policy.LoadAsync(cancellationToken);
        return loaded is null
            ? await RunRepositoryFallbackAsync(cancellationToken)
            : await runner.RunPolicyChecksAsync(
                loaded,
                loaded.ResolveContext(context, changedPaths),
                cancellationToken);
    }

    public virtual async Task<VerificationResult> RunSubtaskAsync(
        IEnumerable<string> explicitCheckIds,
        CancellationToken cancellationToken)
    {
        var loaded = await policy.LoadAsync(cancellationToken);
        return loaded is null
            ? await RunRepositoryFallbackAsync(cancellationToken)
            : await runner.RunPolicyChecksAsync(loaded, explicitCheckIds, cancellationToken);
    }

    public async Task<VerificationResult> RunAsync(
        IEnumerable<string> checkIds,
        CancellationToken cancellationToken)
    {
        var ids = checkIds.Distinct(StringComparer.Ordinal).ToArray();
        var loaded = await policy.LoadAsync(cancellationToken);
        if (loaded is null)
        {
            if (ids.Length > 0)
                throw new VerificationException(
                    "UNKNOWN_VERIFICATION_CHECK",
                    "Explicit verification IDs require .idd/verification.yaml.");
            return new(VerificationStatus.NoChecks, []);
        }
        return await runner.RunPolicyChecksAsync(loaded, ids, cancellationToken);
    }

    public async Task<VerificationResult> RunCheckAsync(
        string checkId,
        bool confirmed,
        bool? manualPassed,
        CancellationToken cancellationToken)
    {
        var loaded = await policy.LoadAsync(cancellationToken)
            ?? throw new VerificationException(
                "UNKNOWN_VERIFICATION_CHECK",
                "Explicit verification IDs require .idd/verification.yaml.");
        if (!loaded.Checks.TryGetValue(checkId, out var check))
            throw new VerificationException(
                "UNKNOWN_VERIFICATION_CHECK",
                $"Unknown check ID {checkId}.");
        return await runner.RunChecksAsync(
            [(checkId, check)],
            confirmed,
            manualPassed,
            cancellationToken);
    }

    public async Task<VerificationResult> RunCheckAsync(
        string checkId,
        bool confirmed,
        bool? manualPassed,
        string? expectedDefinitionHash,
        string? expectedPolicyHash,
        CancellationToken cancellationToken)
    {
        var loaded = await policy.LoadAsync(cancellationToken)
            ?? throw new VerificationException(
                "VERIFICATION_POLICY_CHANGED",
                "Verification policy is no longer available.");
        ValidatePendingIdentity(loaded, checkId, expectedDefinitionHash, expectedPolicyHash);
        return await runner.RunChecksAsync(
            [(checkId, loaded.Checks[checkId])],
            confirmed,
            manualPassed,
            cancellationToken);
    }

    public async Task<VerificationResult> DeclineCheckAsync(
        string checkId,
        string? expectedDefinitionHash,
        string? expectedPolicyHash,
        CancellationToken cancellationToken)
    {
        var loaded = await policy.LoadAsync(cancellationToken)
            ?? throw new VerificationException(
                "VERIFICATION_POLICY_CHANGED",
                "Verification policy is no longer available.");
        ValidatePendingIdentity(loaded, checkId, expectedDefinitionHash, expectedPolicyHash);
        var check = loaded.Checks[checkId];
        if (!check.ConfirmationRequired)
            throw new VerificationException(
                "VERIFICATION_POLICY_CHANGED",
                $"Verification check {checkId} no longer requires confirmation.");
        var evidence = await evidenceStore.PersistManualAsync(
            checkId,
            check.Run!,
            DateTimeOffset.UtcNow,
            -1,
            "not-verified",
            "User explicitly declined confirmation.",
            cancellationToken);
        return new(VerificationStatus.Declined, [evidence], checkId, check.Run, null);
    }

    public Task<ResolvedVerificationSelection> ResolveContextAsync(
        string context,
        IEnumerable<string> changedPaths,
        CancellationToken cancellationToken) =>
        policy.ResolveContextAsync(context, changedPaths, cancellationToken);

    public async Task<string> GetCheckDefinitionHashAsync(
        string checkId,
        CancellationToken cancellationToken)
    {
        var loaded = await policy.LoadAsync(cancellationToken)
            ?? throw new VerificationException(
                "VERIFICATION_POLICY_CHANGED",
                "Verification policy is no longer available.");
        if (!loaded.Checks.TryGetValue(checkId, out var check))
            throw new VerificationException(
                "VERIFICATION_POLICY_CHANGED",
                $"Verification check {checkId} no longer exists.");
        return VerificationPolicyService.DefinitionHash(check);
    }

    private async Task<VerificationResult> RunRepositoryFallbackAsync(
        CancellationToken cancellationToken)
    {
        var fallback = policy.RepositoryFallback();
        return fallback is null
            ? new(VerificationStatus.NoChecks, [])
            : await runner.RunChecksAsync(
                [("repository-fallback", fallback)],
                cancellationToken);
    }

    private static void ValidatePendingIdentity(
        VerificationPolicy loaded,
        string checkId,
        string? expectedDefinitionHash,
        string? expectedPolicyHash)
    {
        if (expectedPolicyHash is not null
            && !string.Equals(
                expectedPolicyHash,
                VerificationPolicyService.PolicyHash(loaded),
                StringComparison.Ordinal))
        {
            throw new VerificationException(
                "VERIFICATION_POLICY_CHANGED",
                "Verification policy changed while user action was pending.");
        }

        if (!loaded.Checks.TryGetValue(checkId, out var check)
            || expectedDefinitionHash is not null
            && !string.Equals(
                expectedDefinitionHash,
                VerificationPolicyService.DefinitionHash(check),
                StringComparison.Ordinal))
        {
            throw new VerificationException(
                "VERIFICATION_POLICY_CHANGED",
                $"Verification check {checkId} changed while user action was pending.");
        }
    }
}

using System.Text.Json;
using System.Text.RegularExpressions;
using Idd.Factory.Domain;
using Idd.Factory.Verification;

namespace Idd.Factory.Finalization;

public sealed class FinalizeHandler(string workspace)
{
    public async Task<string> FinalizeAsync(FactoryState state, CancellationToken cancellationToken)
    {
        if (state.Current is not null || state.Remaining.Count != 0 || state.CurrentAttemptId is not null || state.PendingContinuation is not null || state.PendingVerificationSession is not null || state.PendingReplanTrigger is not null)
            throw new InvalidOperationException("Finalization requires quiescent linear work state.");
        if (!state.FinalVerificationPassed || state.FinalVerificationPlanRevision != state.PlanRevision) throw new InvalidOperationException("Finalization requires current strict verification.");
        if (state.FinalReview is not { Verdict: "approved", ReviewedPlanRevision: not null } review || review.ReviewedPlanRevision != state.PlanRevision) throw new InvalidOperationException("Finalization requires current approved review.");

        var current = Path.Combine(workspace, ".idd", "factory", "current");
        foreach (var reference in state.VerificationEvidenceRefs)
            _ = JsonSerializer.Deserialize<VerificationEvidence>(await File.ReadAllTextAsync(Path.Combine(current, reference), cancellationToken), FactoryJson.Options)
                ?? throw new InvalidOperationException($"Invalid verification evidence: {reference}");

        var request = await File.ReadAllTextAsync(Path.Combine(current, state.RequestPath), cancellationToken);
        var resultsRoot = Path.Combine(workspace, ".idd", "factory", "results");
        Directory.CreateDirectory(resultsRoot);
        var baseName = $"{Slug(request.Split('\n', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "factory-result")}_{DateTimeOffset.UtcNow:yyyy-MM-dd_HH-mm-ssZ}";
        var directory = Path.Combine(resultsRoot, baseName);
        for (var suffix = 2; Directory.Exists(directory); suffix++) directory = Path.Combine(resultsRoot, baseName + "-" + suffix);
        Directory.CreateDirectory(directory);

        var subject = request.Split('\n', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.Trim().TrimStart('#', ' ') ?? "Complete Factory task";
        if (subject.Length > 72) subject = subject[..72].TrimEnd();
        await File.WriteAllTextAsync(Path.Combine(directory, "commit-message.md"), $"{subject.TrimEnd('.')}\n\nPerformed by: IDD Factory\n\nResult:\n- Completed {state.Completed.Count} ordered work items\n- Passed strict integrated verification and semantic review\n", cancellationToken);

        foreach (var name in new[] { "attempts", "verification", "work-items", "plan-revisions" })
        {
            var source = Path.Combine(current, name);
            if (Directory.Exists(source)) CopyDirectory(source, Path.Combine(directory, name));
        }
        var plan = new
        {
            schemaVersion = 1,
            state.PlanRevision,
            completed = state.Completed.Select(x => new { x.Id, x.Capability, x.ContractPath, x.ResultRef, x.ChangedPaths, x.VerificationEvidenceRefs })
        };
        await File.WriteAllTextAsync(Path.Combine(directory, "completed-work.json"), JsonSerializer.Serialize(plan, FactoryJson.Options), cancellationToken);
        var result = new
        {
            schemaVersion = 3,
            state.MethodologyVersion,
            state.RuntimeVersion,
            factoryOutcome = "COMPLETED",
            state.PlanRevision,
            state.FactoryConfigurationHash,
            completedWorkCount = state.Completed.Count,
            finalReviewVerdict = review.Verdict,
            verificationStatus = "passed",
            finalReviewResultPath = review.ResultRef,
            finalVerificationPlanRevision = state.FinalVerificationPlanRevision,
            commitMessagePath = Path.GetRelativePath(workspace, Path.Combine(directory, "commit-message.md")).Replace('\\', '/'),
            planHistoryPath = Directory.Exists(Path.Combine(directory, "plan-revisions")) ? Path.GetRelativePath(workspace, Path.Combine(directory, "plan-revisions")).Replace('\\', '/') : null
        };
        await File.WriteAllTextAsync(Path.Combine(directory, "factory-result.json"), JsonSerializer.Serialize(result, FactoryJson.Options), cancellationToken);
        foreach (var entry in Directory.EnumerateFileSystemEntries(current)) { if (Directory.Exists(entry)) Directory.Delete(entry, true); else File.Delete(entry); }
        return directory;
    }

    private static string Slug(string value)
    {
        var slug = Regex.Replace(value.ToLowerInvariant(), "[^a-z0-9]+", "-").Trim('-');
        return string.IsNullOrEmpty(slug) ? "factory-result" : slug[..Math.Min(slug.Length, 40)].TrimEnd('-');
    }

    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
        {
            var target = Path.Combine(destination, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target);
        }
    }
}

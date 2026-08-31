using System.Text.Json;
using System.Text.RegularExpressions;
using Idd.Factory.Domain;
using Idd.Factory.Verification;

namespace Idd.Factory.Finalization;

public sealed class FinalizeHandler(string workspace)
{
    public async Task<string> FinalizeAsync(FactoryState state, CancellationToken cancellationToken)
    {
        if (state.FinalReview?.Verdict != "approved" || state.WorkItems.Any(x => x.Status is not WorkItemStatus.Completed and not WorkItemStatus.Superseded))
            throw new InvalidOperationException("Finalization requires approved final review and no incomplete work items.");
        var current = Path.Combine(workspace, ".idd", "factory", "current");
        var evidence = new List<VerificationEvidence>();
        foreach (var evidenceRef in state.VerificationEvidenceRefs)
            evidence.Add(JsonSerializer.Deserialize<VerificationEvidence>(await File.ReadAllTextAsync(Path.Combine(current, evidenceRef), cancellationToken), FactoryJson.Options)
                ?? throw new InvalidOperationException($"Verification evidence is invalid: {evidenceRef}"));
        // Historical failures remain audit evidence even when a repair changes path-based
        // selection. Completion and FinalVerificationPassed are the authoritative gate state.
        if (!state.FinalVerificationPassed)
            throw new InvalidOperationException("Finalization requires the current final verification gate to pass.");
        var results = Path.Combine(workspace, ".idd", "factory", "results"); Directory.CreateDirectory(results);
        var request = await File.ReadAllTextAsync(Path.Combine(current, state.RequestPath), cancellationToken);
        var slug = Slug(request.Split('\n', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "factory-result");
        var baseName = $"{slug}_{DateTimeOffset.UtcNow:yyyy-MM-dd_HH-mm-ssZ}"; var directory = Path.Combine(results, baseName); var suffix = 2;
        while (Directory.Exists(directory)) directory = Path.Combine(results, baseName + "-" + suffix++);
        Directory.CreateDirectory(directory);
        var review = JsonSerializer.Deserialize<AgentResultEnvelope>(await File.ReadAllTextAsync(Path.Combine(current, state.FinalReview.ResultRef!), cancellationToken), FactoryJson.Options);
        var message = ReadCommitMessage(review?.Payload);
        var subject = message.Subject ?? request.Split('\n', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.Trim().TrimStart('#', ' ') ?? "Complete Factory task";
        if (subject.Length > 72) subject = subject[..72].TrimEnd();
        var why = message.Why.Count > 0 ? string.Join(" ", message.Why.Take(3)) : Summarize(request);
        var bullets = message.Result.Count > 0 ? message.Result.Take(6).Select(x => "- " + x) : [$"- Completed {state.WorkItems.Count(x => x.Kind != WorkItemKind.ReviewCheckpoint)} implementation work items", "- Passed integrated Factory review and verification"];
        var commit = $"{subject.TrimEnd('.')}\n\nPerformed by: IDD Factory\n\nWhy:\n{why}\n\nResult:\n{string.Join("\n", bullets)}\n";
        var commitPath = Path.Combine(directory, "commit-message.md"); await File.WriteAllTextAsync(commitPath, commit, cancellationToken);
        var eventsSource = Path.Combine(current, "events.jsonl"); var eventsPath = Path.Combine(directory, "events.jsonl");
        if (File.Exists(eventsSource))
        {
            await File.AppendAllTextAsync(eventsSource, JsonSerializer.Serialize(new { schemaVersion = 1, timestamp = DateTimeOffset.UtcNow, runId = state.RunId, type = "run-completed", data = new { } }) + Environment.NewLine, cancellationToken);
            File.Copy(eventsSource, eventsPath);
        }
        var verificationResultDirectory = Path.Combine(directory, "verification");
        if (Directory.Exists(Path.Combine(current, "verification"))) CopyDirectory(Path.Combine(current, "verification"), verificationResultDirectory);
        var attemptsResultDirectory = Path.Combine(directory, "attempts");
        if (Directory.Exists(Path.Combine(current, "attempts"))) CopyDirectory(Path.Combine(current, "attempts"), attemptsResultDirectory);
        var decompositionResultDirectory = Path.Combine(directory, "decomposition");
        var contractsResultDirectory = Path.Combine(decompositionResultDirectory, "contracts");
        if (Directory.Exists(Path.Combine(current, "work-items"))) CopyDirectory(Path.Combine(current, "work-items"), contractsResultDirectory);
        var decompositionPath = Path.Combine(decompositionResultDirectory, "decomposition.json");
        Directory.CreateDirectory(decompositionResultDirectory);
        await File.WriteAllTextAsync(decompositionPath, JsonSerializer.Serialize(new
        {
            schemaVersion = 1,
            workItems = state.WorkItems.OrderBy(x => x.Sequence).Select(x => new
            {
                x.Id,
                x.Sequence,
                kind = x.Kind switch
                {
                    WorkItemKind.Subtask => "subtask",
                    WorkItemKind.ReviewCheckpoint => "review-checkpoint",
                    WorkItemKind.CorrectiveSubtask => "corrective-subtask",
                    _ => throw new InvalidOperationException($"Unknown work-item kind {x.Kind}.")
                },
                status = x.Status.ToString(),
                contractPath = $"contracts/{Path.GetFileName(x.ContractPath)}",
                x.Dependencies,
                x.CoveredWorkItems,
                x.VerificationCheckIds
            })
        }, FactoryJson.Options), cancellationToken);
        var result = new
        {
            schemaVersion = 1, state.MethodologyVersion, runtimeVersion = state.RuntimeVersion, workerProtocolVersion = AgentInvocation.CurrentProtocolVersion,
            factoryOutcome = "COMPLETED", subtaskCount = state.WorkItems.Count(x => x.Kind != WorkItemKind.ReviewCheckpoint),
            completedSubtaskCount = state.WorkItems.Count(x => x.Kind != WorkItemKind.ReviewCheckpoint && x.Status == WorkItemStatus.Completed),
            reviewCheckpointCount = state.WorkItems.Count(x => x.Kind == WorkItemKind.ReviewCheckpoint),
            completedReviewCheckpointCount = state.WorkItems.Count(x => x.Kind == WorkItemKind.ReviewCheckpoint && x.Status == WorkItemStatus.Completed),
            correctiveSubtaskCount = state.WorkItems.Count(x => x.Kind == WorkItemKind.CorrectiveSubtask), blockedItemCount = 0, incompleteItemCount = 0,
            finalReviewVerdict = "approved", verificationStatus = "passed", workflowName = state.WorkflowName, workflowHash = state.WorkflowHash,
            commitMessagePath = Path.GetRelativePath(workspace, commitPath).Replace('\\', '/'),
            eventLogPath = File.Exists(eventsPath) ? Path.GetRelativePath(workspace, eventsPath).Replace('\\', '/') : null,
            verificationEvidencePath = Directory.Exists(verificationResultDirectory) ? Path.GetRelativePath(workspace, verificationResultDirectory).Replace('\\', '/') : null,
            agentAttemptsPath = Directory.Exists(attemptsResultDirectory) ? Path.GetRelativePath(workspace, attemptsResultDirectory).Replace('\\', '/') : null,
            decompositionPath = Path.GetRelativePath(workspace, decompositionPath).Replace('\\', '/')
        };
        var resultPath = Path.Combine(directory, "factory-result.json"); await File.WriteAllTextAsync(resultPath, JsonSerializer.Serialize(result, FactoryJson.Options), cancellationToken);
        _ = JsonDocument.Parse(await File.ReadAllTextAsync(resultPath, cancellationToken));
        foreach (var entry in Directory.EnumerateFileSystemEntries(current))
        {
            if (Directory.Exists(entry)) Directory.Delete(entry, true); else File.Delete(entry);
        }
        return directory;
    }

    private static string Slug(string value)
    { var slug = Regex.Replace(value.ToLowerInvariant(), "[^a-z0-9]+", "-").Trim('-'); return string.IsNullOrEmpty(slug) ? "factory-result" : slug[..Math.Min(slug.Length, 40)].TrimEnd('-'); }
    private static string Summarize(string request)
    { var line = request.Split('\n', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.Trim().TrimStart('#', ' ') ?? "Implement the requested product intent."; return line.Length > 240 ? line[..240].TrimEnd() + "." : line.TrimEnd('.') + "."; }
    private static CommitMaterial ReadCommitMessage(JsonElement? payload)
    {
        if (payload is not { } value || !value.TryGetProperty("commitMessage", out var message)) return new(null, [], []);
        var subject = message.TryGetProperty("subject", out var subjectNode) ? subjectNode.GetString() : null;
        static IReadOnlyList<string> Values(JsonElement node, string name) => node.TryGetProperty(name, out var values) && values.ValueKind == JsonValueKind.Array ? values.EnumerateArray().Select(x => x.GetString()).Where(x => !string.IsNullOrWhiteSpace(x)).Cast<string>().ToArray() : [];
        return new(subject, Values(message, "why"), Values(message, "result"));
    }
    private sealed record CommitMaterial(string? Subject, IReadOnlyList<string> Why, IReadOnlyList<string> Result);
    private static void CopyDirectory(string source, string destination)
    { Directory.CreateDirectory(destination); foreach (var file in Directory.GetFiles(source, "*", SearchOption.AllDirectories)) { var target = Path.Combine(destination, Path.GetRelativePath(source, file)); Directory.CreateDirectory(Path.GetDirectoryName(target)!); File.Copy(file, target); } }
}
